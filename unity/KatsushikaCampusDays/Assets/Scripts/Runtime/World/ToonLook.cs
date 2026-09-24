using UnityEngine;

namespace KCD
{
    /// <summary>
    /// キャラクターの KCD/Toon マテリアルに、役割（肌・顔・髪・服・金属・顔の線）ごとの陰の付け方を入れる (#14)。
    ///
    /// 役割はマテリアル名で決める（Blender 側の命名規約 DESIGN.md 2.2 / 3.1 と同じ名前）。
    /// 陰の色は albedo に掛ける係数なので、肌は赤みのある陰、服は元の色相のまま暗くなる色にする。
    /// 顔は法線ではなく頭の向きで陰を決める（CharacterImporter が顔の頂点に頭の向きを焼く）。
    ///
    /// エディタの MaterialLibrary が .mat を作るとき・塗り直すときに使い、テストが .mat と突き合わせる。
    /// シェーダの顔の陰と輪郭の太さの式も C# に写してあり、テストが #44 の表と比べる。
    /// </summary>
    public static class ToonLook
    {
        public const string ShaderName = "KCD/Toon";
        public const string FaceShadowKeyword = "_FACE_SHADOW_ON";

        /// <summary>
        /// 頭の向きを焼いた頂点の目印。Mikk の tangent.w は ±1 なので、|w| がこれより小さい頂点を顔とみなす
        /// （シェーダの HasFaceFrame と同じ値）。
        /// </summary>
        public const float FaceFrameMarker = 0.995f;

        /// <summary>
        /// 顔の縁の左右の位置。Blender の顔の面は正面から左右 74° まで（kcd_chara/body.py）なので、
        /// 頭の水平の断面を円とみなすと縁は sin 74° = 0.96 に来る。
        /// </summary>
        public const float FaceEdgeSine = 0.96f;

        /// <summary>焼く左右の位置の上限。目印の 0.995 と区別がつくように、縁でもこれ以下に抑える。</summary>
        public const float FaceSideClamp = 0.97f;

        /// <summary>陰の付け方を決めるマテリアルの役割。</summary>
        public enum Role
        {
            /// <summary>Blender 側の外形ハル。描かない（CharacterImporter が止める）。</summary>
            Outline,
            /// <summary>顔の面。頭の向きで陰を決める。</summary>
            Face,
            /// <summary>白目と瞳。顔テクスチャのハイライトを見せるため、常に明部で描く（MaterialLibrary.ApplyFaceLook）。</summary>
            Eye,
            /// <summary>まつ毛・目の縁・眉。顔の線なので陰を付けない。</summary>
            Line,
            Skin,
            Hair,
            Metal,
            Cloth,
        }

        /// <summary>役割ごとのトゥーンの値。シェーダの同名のプロパティに入る。</summary>
        public struct Look
        {
            /// <summary>false の役割（輪郭・目）はここでは触らない。</summary>
            public bool Managed;
            public float ShadeByNdotL;
            public float ShadeThreshold;
            public float ShadeSoftness;
            public float ShadeThreshold2;
            public float ShadeSoftness2;
            public float RimIntensity;
            public float RimPower;
            public float RimBacklight;
            public float RimLightAlign;
            public bool FaceShadow;
            public float FaceShadowSoftness;
            public float FaceShadowBias;
            public float HairHighlightIntensity;
            public float HairHighlightHeight;
            public float HairHighlightWidth;
            public float HairHighlightSoftness;
            public float HairHighlightJag;
        }

        /// <summary>肌と顔の 1 段目の陰（albedo に掛ける）。赤みを残して暗くする。</summary>
        public static readonly Color SkinShade = new Color(0.95f, 0.83f, 0.82f, 1f);

        /// <summary>肌と顔の 2 段目の陰。</summary>
        public static readonly Color SkinShade2 = new Color(0.85f, 0.69f, 0.70f, 1f);

        public static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        public static readonly int ShadeColor2Id = Shader.PropertyToID("_ShadeColor2");
        public static readonly int ShadeByNdotLId = Shader.PropertyToID("_ShadeByNdotL");
        public static readonly int ShadeThresholdId = Shader.PropertyToID("_ShadeThreshold");
        public static readonly int ShadeSoftnessId = Shader.PropertyToID("_ShadeSoftness");
        public static readonly int ShadeThreshold2Id = Shader.PropertyToID("_ShadeThreshold2");
        public static readonly int ShadeSoftness2Id = Shader.PropertyToID("_ShadeSoftness2");
        public static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");
        public static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        public static readonly int RimBacklightId = Shader.PropertyToID("_RimBacklight");
        public static readonly int RimLightAlignId = Shader.PropertyToID("_RimLightAlign");
        public static readonly int FaceShadowId = Shader.PropertyToID("_FaceShadow");
        public static readonly int FaceShadowSoftnessId = Shader.PropertyToID("_FaceShadowSoftness");
        public static readonly int FaceShadowBiasId = Shader.PropertyToID("_FaceShadowBias");
        public static readonly int HairHighlightColorId = Shader.PropertyToID("_HairHighlightColor");
        public static readonly int HairHighlightIntensityId = Shader.PropertyToID("_HairHighlightIntensity");
        public static readonly int HairHighlightHeightId = Shader.PropertyToID("_HairHighlightHeight");
        public static readonly int HairHighlightWidthId = Shader.PropertyToID("_HairHighlightWidth");
        public static readonly int HairHighlightSoftnessId = Shader.PropertyToID("_HairHighlightSoftness");
        public static readonly int HairHighlightJagId = Shader.PropertyToID("_HairHighlightJag");

        private const float ColorTolerance = 0.002f;
        private const float FloatTolerance = 1e-5f;

        /// <summary>
        /// FBX / .mat のマテリアル名から、Blender 側の名前（cloth_blouse など）を取り出す。
        /// 小文字にし、Blender の重複番号（.001）と先頭の「&lt;キャラ id&gt;_」を落とす。
        /// </summary>
        public static string MaterialName(string rawName, string characterId)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            string name = rawName.Trim().ToLowerInvariant();
            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                name = name.Substring(0, dot);
            }

            if (!string.IsNullOrEmpty(characterId))
            {
                string prefix = characterId.ToLowerInvariant() + "_";
                if (name.StartsWith(prefix, System.StringComparison.Ordinal) && name.Length > prefix.Length)
                {
                    name = name.Substring(prefix.Length);
                }
            }

            return name;
        }

        /// <summary>Blender 側のマテリアル名から役割を決める。知らない名前は服として扱う。</summary>
        public static Role RoleOf(string name)
        {
            switch (name)
            {
                case "outline":
                    return Role.Outline;
                case "face":
                    return Role.Face;
                case "eye_white":
                case "eye_l":
                case "eye_r":
                    return Role.Eye;
                case "lash":
                case "eye_rim":
                case "brow":
                    return Role.Line;
                case "skin":
                    return Role.Skin;
                case "metal":
                case "glasses":
                    return Role.Metal;
                case "hair":
                    return Role.Hair;
            }

            if (!string.IsNullOrEmpty(name) && name.StartsWith("hair_", System.StringComparison.Ordinal))
            {
                return Role.Hair;
            }

            return Role.Cloth;
        }

        /// <summary>役割ごとの値。閾値は N・L（-1〜1）で、陰の段は明部 → 1 段目 → 2 段目の順に暗くなる。</summary>
        public static Look LookOf(Role role)
        {
            switch (role)
            {
                case Role.Skin:
                    // 肌は光を少し回り込ませ（閾値を負に）、境目を柔らかくする。
                    return Base(-0.05f, 0.06f, -0.45f, 0.12f, 0.25f);
                case Role.Face:
                {
                    // 顔は頭の向きで 1 段だけ。頭の向きを焼いていない頂点のために肌と同じ段も入れておく。
                    Look look = Base(-0.05f, 0.06f, -0.45f, 0.12f, 0.15f);
                    look.FaceShadow = true;
                    look.FaceShadowSoftness = 0.03f;
                    look.FaceShadowBias = 0.1f;
                    return look;
                }
                case Role.Hair:
                {
                    // 髪は境目をくっきりさせ、天使の輪を乗せる。
                    Look look = Base(0.1f, 0.02f, -0.3f, 0.06f, 0.2f);
                    look.HairHighlightIntensity = 0.5f;
                    return look;
                }
                case Role.Metal:
                {
                    // 金属と眼鏡は硬い境目。スペキュラは MaterialLibrary の値のまま。
                    Look look = Base(0.1f, 0.015f, -0.2f, 0.03f, 0.3f);
                    look.RimBacklight = 1.0f;
                    look.RimLightAlign = 0.5f;
                    return look;
                }
                case Role.Line:
                {
                    // 顔の線は陰で潰れると目元が読めなくなるので、常に明部（閾値 -1）でリムも付けない。
                    Look look = Base(-1f, 0.03f, -1f, 0.08f, 0f);
                    look.ShadeByNdotL = 0f;
                    look.RimBacklight = 0f;
                    look.RimLightAlign = 0f;
                    return look;
                }
                case Role.Cloth:
                    return Base(0.05f, 0.03f, -0.35f, 0.08f, 0.2f);
                default:
                    return new Look { Managed = false };
            }
        }

        private static Look Base(float threshold, float softness, float threshold2, float softness2, float rim)
        {
            return new Look
            {
                Managed = true,
                ShadeByNdotL = 1f,
                ShadeThreshold = threshold,
                ShadeSoftness = softness,
                ShadeThreshold2 = threshold2,
                ShadeSoftness2 = softness2,
                RimIntensity = rim,
                RimPower = 4f,
                RimBacklight = 1.5f,
                RimLightAlign = 0.6f,
                FaceShadow = false,
                FaceShadowSoftness = 0.04f,
                FaceShadowBias = 0f,
                HairHighlightIntensity = 0f,
                HairHighlightHeight = 1f,
                HairHighlightWidth = 0.05f,
                HairHighlightSoftness = 0.02f,
                HairHighlightJag = 0.02f,
            };
        }

        /// <summary>服・髪・金属の 1 段目の陰。元の色相を保ったまま、彩度を少し残して暗くする係数。</summary>
        public static Color ClothShade(Color color)
        {
            return HueShade(color, 0.45f, 0.78f);
        }

        /// <summary>服・髪・金属の 2 段目の陰。</summary>
        public static Color ClothShade2(Color color)
        {
            return HueShade(color, 0.6f, 0.58f);
        }

        /// <summary>
        /// albedo に掛ける陰の係数を、元の色と同じ色相で作る。掛けると色相はほぼそのまま、明度が value 倍になる。
        /// 白や灰色のように彩度の低い色は色相が決まらないので、青みの灰色（アニメの陰の定番）に寄せる。
        /// </summary>
        private static Color HueShade(Color color, float saturationScale, float value)
        {
            Color.RGBToHSV(color, out float h, out float s, out float _);
            Color cool = Color.HSVToRGB(0.68f, 0.12f, value);
            Color own = Color.HSVToRGB(h, s * saturationScale, value);
            Color shade = Color.Lerp(cool, own, Mathf.Clamp01(s / 0.2f));
            shade.a = 1f;
            return shade;
        }

        /// <summary>髪の天使の輪の色。albedo に掛けずにそのまま出すので、髪の色を白へ寄せた色にする。</summary>
        public static Color HairHighlight(Color hair)
        {
            Color color = Color.Lerp(hair, Color.white, 0.55f);
            color.a = 1f;
            return color;
        }

        /// <summary>
        /// 役割に合わせてマテリアルを設定する。何か変えたら true（同じなら何もしない）。
        /// color は palette.json の色（和柄なら柄の平均色）。服・髪・金属の陰の色はこの色から作る。
        /// KCD/Toon でないマテリアルと、輪郭・目のマテリアルは触らない。
        /// </summary>
        public static bool Apply(Material material, string name, Color color)
        {
            if (material == null || material.shader == null || material.shader.name != ShaderName)
            {
                return false;
            }

            Role role = RoleOf(name);
            Look look = LookOf(role);
            if (!look.Managed)
            {
                return false;
            }

            bool changed = false;
            changed |= SetFloat(material, ShadeByNdotLId, look.ShadeByNdotL);
            changed |= SetFloat(material, ShadeThresholdId, look.ShadeThreshold);
            changed |= SetFloat(material, ShadeSoftnessId, look.ShadeSoftness);
            changed |= SetFloat(material, ShadeThreshold2Id, look.ShadeThreshold2);
            changed |= SetFloat(material, ShadeSoftness2Id, look.ShadeSoftness2);
            changed |= SetFloat(material, RimIntensityId, look.RimIntensity);
            changed |= SetFloat(material, RimPowerId, look.RimPower);
            changed |= SetFloat(material, RimBacklightId, look.RimBacklight);
            changed |= SetFloat(material, RimLightAlignId, look.RimLightAlign);
            changed |= SetToggle(material, FaceShadowId, FaceShadowKeyword, look.FaceShadow);
            changed |= SetFloat(material, FaceShadowSoftnessId, look.FaceShadowSoftness);
            changed |= SetFloat(material, FaceShadowBiasId, look.FaceShadowBias);
            changed |= SetFloat(material, HairHighlightIntensityId, look.HairHighlightIntensity);
            changed |= SetFloat(material, HairHighlightHeightId, look.HairHighlightHeight);
            changed |= SetFloat(material, HairHighlightWidthId, look.HairHighlightWidth);
            changed |= SetFloat(material, HairHighlightSoftnessId, look.HairHighlightSoftness);
            changed |= SetFloat(material, HairHighlightJagId, look.HairHighlightJag);

            switch (role)
            {
                case Role.Skin:
                case Role.Face:
                    changed |= SetColor(material, ShadeColorId, SkinShade);
                    changed |= SetColor(material, ShadeColor2Id, SkinShade2);
                    break;
                case Role.Hair:
                case Role.Metal:
                case Role.Cloth:
                    changed |= SetColor(material, ShadeColorId, ClothShade(color));
                    changed |= SetColor(material, ShadeColor2Id, ClothShade2(color));
                    break;
            }

            if (role == Role.Hair)
            {
                changed |= SetColor(material, HairHighlightColorId, HairHighlight(color));
            }

            return changed;
        }

        private static bool SetFloat(Material material, int id, float value)
        {
            if (!material.HasProperty(id) || Mathf.Abs(material.GetFloat(id) - value) < FloatTolerance)
            {
                return false;
            }

            material.SetFloat(id, value);
            return true;
        }

        private static bool SetColor(Material material, int id, Color value)
        {
            if (!material.HasProperty(id))
            {
                return false;
            }

            Color current = material.GetColor(id);
            if (Mathf.Abs(current.r - value.r) < ColorTolerance
                && Mathf.Abs(current.g - value.g) < ColorTolerance
                && Mathf.Abs(current.b - value.b) < ColorTolerance
                && Mathf.Abs(current.a - value.a) < ColorTolerance)
            {
                return false;
            }

            material.SetColor(id, value);
            return true;
        }

        /// <summary>[Toggle] のプロパティとキーワードを揃える。</summary>
        private static bool SetToggle(Material material, int id, string keyword, bool on)
        {
            if (!material.HasProperty(id))
            {
                return false;
            }

            bool changed = SetFloat(material, id, on ? 1f : 0f);
            if (material.IsKeywordEnabled(keyword) != on)
            {
                if (on)
                {
                    material.EnableKeyword(keyword);
                }
                else
                {
                    material.DisableKeyword(keyword);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 顔の頂点に焼く左右の位置。center / halfWidth は顔の面の左右の中心と半幅（モデルの根元の空間）。
        /// 縁で <see cref="FaceEdgeSine"/>、はみ出しても <see cref="FaceSideClamp"/> までにする。
        /// </summary>
        public static float FaceSide(float x, float center, float halfWidth)
        {
            if (halfWidth <= 1e-6f)
            {
                return 0f;
            }

            return Mathf.Clamp((x - center) / halfWidth * FaceEdgeSine, -FaceSideClamp, FaceSideClamp);
        }

        /// <summary>
        /// シェーダの FaceLit と同じ式（顔の明部の割合、0〜1。影の減衰は掛けない）。
        /// headForward は頭の前方向、faceSide は焼いた左右の位置（キャラの右が +）、
        /// lightDirection は光へ向かう向き（URP の Light.direction と同じ向き）。すべてワールド空間。
        /// </summary>
        public static float FaceLit(Vector3 headForward, float faceSide, Vector3 lightDirection, float softness, float bias)
        {
            var forward = new Vector2(headForward.x, headForward.z);
            forward *= 1f / Mathf.Sqrt(Mathf.Max(Vector2.Dot(forward, forward), 1e-6f));
            var right = new Vector2(forward.y, -forward.x);
            var lightH = new Vector2(lightDirection.x, lightDirection.z);
            float lengthH = lightH.magnitude;
            var lightN = lightH / Mathf.Max(lengthH, 1e-4f);
            float front = Vector2.Dot(forward, lightN);
            float side = Vector2.Dot(right, lightN);
            float u = faceSide;
            float facing = u * side + Mathf.Sqrt(Mathf.Clamp01(1f - u * u)) * front;
            float threshold = Mathf.Lerp(-1f - 2f * softness, -bias * (1f - Mathf.Abs(front)), Mathf.Clamp01(lengthH * 2f));
            return SmoothStep(threshold - softness, threshold + softness, facing);
        }

        /// <summary>
        /// シェーダの Outline パスと同じ式で、カメラの正面 distance m にある輪郭の画面上の太さ（px）を返す。
        /// 画面の高さ screenHeight px、縦の視野角 verticalFovDegrees のカメラを仮定する。
        /// </summary>
        public static float OutlinePixels(
            float distance,
            float width,
            float nearDistance,
            float fadeStart,
            float fadeEnd,
            float maxPixels,
            float verticalFovDegrees,
            float screenHeight = 1080f)
        {
            if (distance <= 0f)
            {
                return 0f;
            }

            float p11 = 1f / Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float worldWidth = width * Mathf.Clamp(distance, 0.5f, nearDistance);
            if (maxPixels > 0f)
            {
                worldWidth = Mathf.Min(worldWidth, maxPixels * 2f * distance / (1080f * p11));
            }

            float fade = 1f - SmoothStep(fadeStart, fadeEnd, distance);
            return worldWidth * fade * screenHeight * p11 / (2f * distance);
        }

        /// <summary>HLSL の smoothstep と同じ（Mathf.SmoothStep は引数の意味が違う）。</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
