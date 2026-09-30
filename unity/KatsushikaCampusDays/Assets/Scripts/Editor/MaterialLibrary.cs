using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Editor
{
    /// <summary>
    /// FBX に入っているマテリアル名から URP Lit / KCD Toon のマテリアルを起こして使い回す。
    /// 名前は Blender 側の命名規約（DESIGN.md 2.2 / 3.1）と同じものを前提にする。
    /// </summary>
    public static class MaterialLibrary
    {
        public const string CampusFolder = "Assets/Materials/Campus";
        public const string CharacterFolder = "Assets/Materials/Characters";

        /// <summary>キャラクターの輪郭線（KCD/Toon の Outline パス）の幅。</summary>
        public const float CharacterOutlineWidth = 0.005f;

        /// <summary>
        /// キャラの輪郭線の殻を、法線の向きでなく画面の面に沿って押し出す（KCD/Toon の _OutlineScreenFlat）。
        /// 服に沿った薄い物（リボンの輪・セーラー襟）は、法線の向きのまま押すと殻の裏が奥の服の後ろへ回り、
        /// 輪郭が物から離れた弧や、襟の V の底で交差した線になる (#47)。キャンパスは法線の向きのまま。
        /// </summary>
        public const float CharacterOutlineScreenFlat = 1f;

        /// <summary>
        /// キャラの陰の境目を画面の 1 画素にする（KCD/Toon の _ShadeCrisp）。境目の幅が固定だと、後頭部のような
        /// 大きな丸みで境目が数十画素にぼけ、陰が輪郭の無い斑になる (#47)。キャンパスは固定幅のまま。
        /// </summary>
        public const float CharacterShadeCrisp = 1f;

        /// <summary>
        /// キャラの陰の境目（KCD/Toon の _ShadeThreshold。half-lambert の値）。0.5 で光に対して 90° の所に境目が来る
        /// （GGXrd と同じ）。キャンパスの既定 0.1 は 143° で、陰は光の真裏の 37° の範囲だけになる。キャラでは、後ろから
        /// 見ると後頭部や背中に楕円の陰が浮き、前から見るとほとんど陰が無い (#47)。顔と目は顔の見た目
        /// （<see cref="ApplyFaceLook"/>）が決めるので、ここでは変えない。
        /// </summary>
        public const float CharacterShadeThreshold = 0.5f;

        /// <summary>
        /// キャラの環境光を法線によらない一定の色にする割合（KCD/Toon の _FlatAmbient）。1 で真上と真下の SH の平均だけを
        /// 使う（MToon と同じ）。法線ごとの SH のままだと、陰の中に空の向きの淡い明るみが残り、後ろ髪や紺の襟の陰が
        /// 一色にならない (#47)。顔・目・輪郭（反転ハル）にも付ける。キャンパスは 0 のまま。
        /// </summary>
        public const float CharacterFlatAmbient = 1f;

        /// <summary>
        /// キャラのリムライトの強さ（KCD/Toon の _RimIntensity）。0 で切る。リムは (1 − N・V)^4 で縁ほど白くなるぼかしで、
        /// 陰の中にも足されるので、後ろ髪・紺の襟・黒い靴下の縁が白く光り、塗り分けが段階状に崩れる (#47)。
        /// 輪郭の抜けは輪郭線が受け持つ。目は <see cref="ApplyFaceLook"/> が常に 0 にする。キャンパスは 0.35 のまま。
        /// </summary>
        public const float CharacterRimIntensity = 0f;

        /// <summary>
        /// 天使の輪（KCD/Toon の _HAIRRING_ON）を描く材質 (#47)。輪の座標は Blender が髪の頂点の UV の 2 枚目に書く
        /// （blender/kcd_chara/hair.py の RING_MATERIAL と ring_coords）。
        /// </summary>
        public const string HairRingMaterial = "hair";

        /// <summary>天使の輪の色。髪の色を <see cref="HairRingHighlight"/> へ寄せる割合。</summary>
        public const float HairRingLighten = 0.38f;

        private static readonly Color HairRingHighlight = Color.white;

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string ToonShader = "KCD/Toon";

        /// <summary>キャンパス側のマテリアル名 → 基本色（RGB hex）。</summary>
        private static readonly Dictionary<string, string> CampusColors = new Dictionary<string, string>
        {
            { "asphalt", "3C3C3C" },
            { "brick_red", "654F44" },
            { "concrete_light", "D8D6D0" },
            { "concrete_grey", "B4B2AC" },
            { "concrete_dark", "8A8884" },
            { "glass_clear", "9FC4D8" },
            { "glass_dark", "2E3A42" },
            { "grass", "6FA84A" },
            { "grass_dark", "4E7F34" },
            { "louver_white", "EDEDE8" },
            { "metal_white", "E2E2DE" },
            { "metal_grey", "9A9A96" },
            { "roof_grey", "6E6E6A" },
            { "sand", "D8C89A" },
            { "sign_familymart_blue", "0068B7" },
            { "sign_familymart_green", "009944" },
            { "sign_familymart_white", "FFFFFF" },
            { "sign_starbucks_green", "006241" },
            { "stone_light", "CFCCC4" },
            { "stone_dark", "A8A49B" },
            { "water", "8FB8C8" },
            { "white", "F5F5F2" },
            { "wood", "9A6B3F" },
            // モール北側の花壇（#56。blender/kcd_lib/mats.py と同じ色）
            { "bed_soil", "5A4331" },
            { "flower_leaf", "3E7A2E" },
            { "flower_red", "D9434E" },
            { "flower_yellow", "F2C84B" },
            { "flower_white", "F4F1EA" },
            { "flower_pink", "E98FB0" },
            // パンジー・ビオラの紫とマリーゴールドの橙（#58）
            { "flower_purple", "7456C8" },
            { "flower_orange", "F28C28" },
            // 葉は理科大グリーン #00843D 基準の 4 段（blender/kcd_lib/mats.py と同じ）。
            // 名前が leaf* なので foliage 判定（KCD/Toon + _ShadeColor 0.62 + アウトライン 0）と
            // CampusStage.IsFoliageMaterial（コライダ除外）はそのまま効く。
            // 木の葉のマテリアルは leaf 1 つで、4 段の色は Blender が頂点カラーに焼く（VertexColored, #51）。
            { "leaf_dark", "0A6B38" },
            { "leaf", "0C8C45" },
            { "leaf_light", "4CAE5B" },
            { "leaf_top", "8ECB63" },
            { "trunk", "6B4A2F" },
            // 入口の看板・外構小物（blender/kcd_lib/mats.py の PALETTE と同じ色）。
            // 以前はここに無く、B0B0AC の灰色で .mat が作られていた。
            { "tus_green", "00843D" },
            { "sign_plate", "DEDEDB" },
            { "vending_red", "C21A17" },
            { "vending_blue", "0F54B8" },
            { "bin_green", "296638" },
            { "bike_frame", "2E2E33" },
            { "bike_tire", "0F0F0F" },
            // 隠しアイテムの宝石（#65）。collectibles.json の rarity ごとの色で、少し光らせて遠くから見つけやすくする。
            { "gem_common", "E8DCC0" },
            { "gem_uncommon", "5FCF8E" },
            { "gem_rare", "58A8F0" },
            { "gem_legendary", "F2B93B" }
        };

        /// <summary>宝石の自己発光の強さ（基本色に掛ける）。</summary>
        private const float GemEmission = 0.55f;

        /// <summary>
        /// 服・髪などの名前に含まれる色語 → 色。palette.json が無いキャラだけの予備 (#55)。
        /// 色の正は Blender の kcd_chara/mats.py で、ふだんは palette.json 経由で届く。
        /// </summary>
        private static readonly Dictionary<string, string> ClothColors = new Dictionary<string, string>
        {
            { "green", "00843D" },
            { "navy", "1E2A4A" },
            { "black", "1A1A1A" },
            { "white", "F7F7F2" },
            { "red", "C0392B" },
            { "blue", "2E5FA3" },
            { "purple", "6A3FA0" },
            { "brown", "7A4A2A" },
            { "grey", "9A9A96" },
            { "pink", "E8A0B4" },
            { "yellow", "E8C24A" },
            { "cream", "F0E2C8" },
            { "orange", "D2782E" }
        };

        /// <summary>キャラ id → { 髪, 瞳 } の色（DESIGN.md 2 の外見表）。</summary>
        private static readonly Dictionary<string, string[]> CharacterTones = new Dictionary<string, string[]>
        {
            { "mirai", new[] { "C9A27E", "4FD1D9" } },
            { "botchan", new[] { "2B2B33", "4A3A2A" } },
            { "madonna", new[] { "8B5A2B", "8A4FA0" } },
            { "inari", new[] { "241F26", "B03040" } },
            { "kaname", new[] { "8B5A2B", "6B4A2F" } },
            { "sora", new[] { "9FD8E8", "3FA0C0" } },
            { "prof", new[] { "D8D8D0", "4A4A4A" } }
        };

        /// <summary>作らずに探すだけ。取り込みの最中は CreateAsset を呼べないので使い分ける。</summary>
        public static Material FindCampus(string rawName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(CampusFolder + "/" + Normalize(rawName) + ".mat");
        }

        /// <summary>作らずに探すだけ。キャラクター側。</summary>
        public static Material FindCharacter(string characterId, string rawName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                CharacterFolder + "/" + characterId + "_" + Normalize(rawName) + ".mat");
        }

        /// <summary>キャンパス／樹木の FBX マテリアルを 1 つ用意する。</summary>
        public static Material EnsureCampus(string rawName)
        {
            string name = Normalize(rawName);
            string path = CampusFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                Repaint(material, name, CampusSurfaces.LoadTiling());
                return material;
            }

            bool foliage = name.StartsWith("leaf") || name == "trunk";
            material = new Material(Shader.Find(foliage ? ToonShader : LitShader));
            Color color = Parse(CampusColors.TryGetValue(name, out string hex) ? hex : "B0B0AC");

            if (InteriorPalette.TryGetGlass(name, out string glassHex, out float glassAlpha))
            {
                MakeTransparent(material, glassAlpha, Parse(glassHex));
                material.SetFloat("_Smoothness", 0.95f);
            }
            else if (name.StartsWith("glass"))
            {
                MakeTransparent(material, name == "glass_clear" ? 0.32f : 0.72f, color);
                material.SetFloat("_Smoothness", 0.92f);
            }
            else if (name.StartsWith("gem_"))
            {
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", 0.9f);
                material.SetFloat("_Metallic", 0f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * GemEmission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else if (!CampusColors.ContainsKey(name) && InteriorPalette.TryGetSurface(name, out InteriorPalette.Surface surface))
            {
                material.SetColor("_BaseColor", Parse(surface.Hex));
                material.SetFloat("_Smoothness", surface.Smoothness);
                material.SetFloat("_Metallic", surface.Metallic);
                if (InteriorPalette.TryGetEmission(name, out Color emission))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            else if (name == "water")
            {
                // 屋外の水面は専用のシェーダ KCD/Water (#58)。透け・映り込み・さざ波の値はシェーダの既定値が正。
                material.SetColor("_BaseColor", color);
                WaterMaterial.Apply(material);
            }
            else
            {
                material.SetColor("_BaseColor", color);
                if (foliage)
                {
                    material.SetColor("_ShadeColor", color * 0.62f);
                    material.SetFloat("_OutlineWidth", 0f);
                    if (VertexColored.Contains(name))
                    {
                        ApplyVertexColor(material);
                    }
                }
                else
                {
                    material.SetFloat("_Smoothness", Smoothness(name));
                    material.SetFloat("_Metallic", name.StartsWith("metal") ? 0.8f : 0f);
                }
            }

            CampusSurfaces.Apply(material, name, CampusSurfaces.LoadTiling(), out bool textured);
            if (textured)
            {
                material.SetColor(BaseColorId, Color.white);
            }

            Save(material, path);
            return material;
        }

        /// <summary>
        /// すでにある .mat の色を <see cref="CampusColors"/> の宣言へ合わせ直す (#51)。
        ///
        /// 以前はここで「あれば、そのまま返す」で終わっていた。そのため CampusColors を直しても
        /// 一度でも .mat が出来ていれば二度と反映されず、木の葉と幹の色は 2026-09-22 に焼かれた
        /// 古い値のまま固まっていた（leaf は芝と見分けが付かない #6FA84A 系、幹は明るい灰褐色 #A09385）。
        /// 「葉の色を直した」はずの変更がゲームに一度も届いていなかった。
        ///
        /// CampusColors に載っていない名前（屋内のパレット由来など）は、手で調整した値を
        /// 上書きしてしまわないよう触らない。
        ///
        /// 面のテクスチャ（<see cref="CampusSurfaces"/>）もここで合わせる。画像を貼った面は
        /// 画像の平均色が宣言の色なので、_BaseColor は白にする。直したら true。
        /// </summary>
        private static bool Repaint(Material material, string name, Dictionary<string, float> tiling)
        {
            if (VertexColored.Contains(name))
            {
                if (!ApplyVertexColor(material))
                {
                    return false;
                }

                EditorUtility.SetDirty(material);
                return true;
            }

            if (!CampusColors.TryGetValue(name, out string hex))
            {
                return false;
            }

            Color declared = Parse(hex);
            bool changed = CampusSurfaces.Apply(material, name, tiling, out bool textured);
            Color target = textured ? Color.white : declared;
            if (material.HasProperty(BaseColorId) && !Same(material.GetColor(BaseColorId), target))
            {
                // ガラスと水は半透明なので、アルファは今の値のまま残す
                target.a = material.GetColor(BaseColorId).a;
                material.SetColor(BaseColorId, target);
                if (material.HasProperty(ShadeColorId))
                {
                    material.SetColor(ShadeColorId, declared * 0.62f);
                }

                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        /// <summary>
        /// キャンパスの .mat をすべて CampusColors の色と面のテクスチャに合わせ直し、直した数を返す (#59)。
        ///
        /// 一度差し替えた FBX は埋め込みのマテリアルを返さなくなるので（<see cref="RepaintCharacters"/> と
        /// 同じ事情）、ResolveMaterials 経由の <see cref="EnsureCampus"/> には既存の .mat がほとんど来ない。
        /// ここでは FBX を通さず、フォルダの .mat を直接引く。
        /// </summary>
        public static int RepaintCampus()
        {
            if (!AssetDatabase.IsValidFolder(CampusFolder))
            {
                return 0;
            }

            Dictionary<string, float> tiling = CampusSurfaces.LoadTiling();
            int repainted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { CampusFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && Repaint(material, Path.GetFileNameWithoutExtension(path), tiling))
                {
                    repainted++;
                }
            }

            if (repainted > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return repainted;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int VertexColorId = Shader.PropertyToID("_VertexColor");
        private static readonly int OutlineScreenFlatId = Shader.PropertyToID("_OutlineScreenFlat");
        private static readonly int ShadeCrispId = Shader.PropertyToID("_ShadeCrisp");
        private static readonly int ShadeThresholdId = Shader.PropertyToID("_ShadeThreshold");
        private static readonly int FlatAmbientId = Shader.PropertyToID("_FlatAmbient");
        private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");
        private const string VertexColorKeyword = "_VERTEXCOLOR_ON";

        /// <summary>
        /// 色を FBX の頂点カラーで持つマテリアル (#51)。木の葉は 1 本を幹と葉の 2 マテリアルにし、
        /// 葉の明暗（上面が明るく、内側と下が暗い）は面ごとの頂点カラーで出す。
        /// 頂点カラーの色は blender/kcd_lib/mats.py の leaf_dark / leaf / leaf_light / leaf_top
        /// （CampusColors の同名の色と同じ）から作る。
        /// </summary>
        private static readonly HashSet<string> VertexColored = new HashSet<string> { "leaf" };

        /// <summary>
        /// 頂点カラーの色をそのまま出すため、_BaseColor は白、陰は 0.62 の灰色にする。
        /// 葉の陰を「葉の色 × 0.62」にしていたころは、シェーダが albedo にもう一度それを掛けるので
        /// 陰の側が葉の色の 2 乗 × 0.62 まで沈んでいた。
        /// </summary>
        private static readonly Color VertexColorShade = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>頂点カラーを使う設定にする。変えたら true。</summary>
        private static bool ApplyVertexColor(Material material)
        {
            if (!material.HasProperty(VertexColorId))
            {
                return false;
            }

            bool same = material.GetFloat(VertexColorId) == 1f
                && material.IsKeywordEnabled(VertexColorKeyword)
                && Same(material.GetColor(BaseColorId), Color.white)
                && Same(material.GetColor(ShadeColorId), VertexColorShade);
            if (same)
            {
                return false;
            }

            material.SetFloat(VertexColorId, 1f);
            material.EnableKeyword(VertexColorKeyword);
            material.SetColor(BaseColorId, Color.white);
            material.SetColor(ShadeColorId, VertexColorShade);
            return true;
        }

        /// <summary>色が実質同じか。8 bit に戻したとき同じ値なら同じとみなす。</summary>
        private static bool Same(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f
                && Mathf.Abs(a.g - b.g) < 0.002f
                && Mathf.Abs(a.b - b.b) < 0.002f;
        }

        /// <summary>キャラクター FBX のマテリアルを 1 つ用意する。顔だけテクスチャを貼る。</summary>
        public static Material EnsureCharacter(string characterId, string rawName, Texture2D face)
        {
            string name = Normalize(rawName);
            string path = CharacterFolder + "/" + characterId + "_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Dictionary<string, Color> palette = LoadCharacterPalette(characterId);
            Dictionary<string, Pattern> patterns = LoadCharacterPatterns(characterId);
            if (material != null)
            {
                RepaintCharacter(material, name, palette, patterns);
                return material;
            }

            material = new Material(Shader.Find(ToonShader));
            CharacterTones.TryGetValue(characterId, out string[] tones);
            Color color = palette.TryGetValue(name, out Color declared) ? declared : CharacterColor(name, tones);

            SetCharacterColor(material, color);
            ApplyPattern(material, patterns.TryGetValue(name, out Pattern pattern) ? pattern : null, color);
            ApplyHairRing(material, name, color);
            material.SetFloat("_OutlineWidth", CharacterOutlineWidth);
            ApplyOutlineScreenFlat(material);
            ApplyShadeCrisp(material);
            ApplyShadeThreshold(material, name);
            ApplyFlatAmbient(material);
            ApplyRim(material);

            // 顔・目・スカートの面が内向きに出力されたキャラもあるので、両面描画にする（シェーダ側で法線を裏返す）。
            material.SetFloat("_Cull", 0f);

            if (name == "outline")
            {
                // Blender 側の反転ハル。輪郭色で塗りつぶし、片面描画のまま置く。
                var outline = new Color(0.12f, 0.10f, 0.14f);
                material.SetColor("_BaseColor", outline);
                material.SetColor("_ShadeColor", outline);
                material.SetColor("_ShadeColor2", outline);
                material.SetFloat("_RimIntensity", 0f);
                material.SetFloat("_OutlineWidth", 0f);
                material.SetFloat("_Cull", 2f);
            }

            ApplyFaceLook(material, name, face, FacesOutward(characterId));

            if (IsFaceDetail(name))
            {
                material.SetFloat("_OutlineWidth", 0f);
            }

            if (name == "metal")
            {
                material.SetFloat("_SpecularIntensity", 0.8f);
            }

            Save(material, path);
            return material;
        }

        private static readonly Color ShadeTint = new Color(0.45f, 0.42f, 0.58f);

        /// <summary>
        /// 陰の色（KCD/Toon の _ShadeColor）。シェーダは陰を「地の色 × この色」で塗るので、これは掛ける色になる。
        /// 地の色をそのまま紫へ寄せると、暗い色ほど掛ける色も暗くなり、紺のスカートや襟の陰が黒につぶれる (#47)。
        /// 地の色を最も明るい成分で割って色味だけにしてから紫へ寄せる。白の陰は前と同じ。
        /// </summary>
        internal static Color ShadeOf(Color color)
        {
            float brightest = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            Color hue = brightest > 0f
                ? new Color(color.r / brightest, color.g / brightest, color.b / brightest)
                : Color.white;
            return Color.Lerp(hue, ShadeTint, 0.42f);
        }

        private static void SetCharacterColor(Material material, Color color)
        {
            material.SetColor("_BaseColor", color);
            material.SetColor("_ShadeColor", ShadeOf(color));
            material.SetColor("_ShadeColor2", Color.Lerp(color, new Color(0.30f, 0.28f, 0.44f), 0.55f));
        }

        /// <summary>
        /// すでにある .mat の色を Blender の palette.json に合わせ直す (#55)。
        ///
        /// 以前は .mat があれば中身を見ずに返していたうえ、色は名前の部分一致で決めていた
        /// （cloth_kimono_kasuri_blue は "blue" が入るのでベタの #2E5FA3、まつ毛・眉は辞書に
        /// 無いので既定のベージュ）。Blender のプレビューを見て色を決めても、ゲームには
        /// 別の色が出ていた。campus 側の <see cref="Repaint"/> (#51) のキャラ版。
        ///
        /// 顔テクスチャの 4 枚と輪郭は palette.json に載らない（載っていても触らない）。
        /// </summary>
        private static bool RepaintCharacter(
            Material material, string name, Dictionary<string, Color> palette, Dictionary<string, Pattern> patterns)
        {
            if (IsFaceTextured(name) || name == "outline" || !palette.TryGetValue(name, out Color declared))
            {
                return false;
            }

            if (!material.HasProperty(BaseColorId))
            {
                return false;
            }

            patterns.TryGetValue(name, out Pattern pattern);
            bool changed = false;
            if (pattern == null && (!Same(material.GetColor(BaseColorId), declared)
                || !Same(material.GetColor(ShadeColorId), ShadeOf(declared))))
            {
                SetCharacterColor(material, declared);
                changed = true;
            }

            if (IsFaceDetail(name) && !Mathf.Approximately(material.GetFloat("_OutlineWidth"), 0f))
            {
                material.SetFloat("_OutlineWidth", 0f);
                changed = true;
            }

            changed |= ApplyOutlineScreenFlat(material);
            changed |= ApplyShadeCrisp(material);
            changed |= ApplyShadeThreshold(material, name);
            changed |= ApplyFlatAmbient(material);
            changed |= ApplyRim(material);
            changed |= ApplyPattern(material, pattern, declared);
            changed |= ApplyHairRing(material, name, declared);
            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        /// <summary>
        /// 絣・ハート柄などの和柄 (#55)。Blender は Generated 座標 × <see cref="Scale"/> で <see cref="Texture"/> を
        /// ボックス投影し、画像の色をそのまま服の色にしている（kcd_chara/mats.py の make_material, uv=False）。
        /// </summary>
        public sealed class Pattern
        {
            public Texture2D Texture;
            public float Scale;
        }

        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int PatternMapId = Shader.PropertyToID("_PatternMap");
        private static readonly int PatternScaleId = Shader.PropertyToID("_PatternScale");
        private static readonly int PatternBlendId = Shader.PropertyToID("_PatternBlend");

        /// <summary>ボックス投影の継ぎ目のぼかし。Blender の projection_blend（kcd_chara/mats.py）と同じ値。</summary>
        private const float PatternBlend = 0.25f;
        private const string PatternKeyword = "_PATTERN_ON";

        /// <summary>
        /// 柄を貼る（pattern が null なら外す）。変えたら true。
        /// 画像の色がそのまま出るように _BaseColor は白にし、陰の色は柄の平均色（palette.json の hex）から作る。
        /// </summary>
        private static bool ApplyPattern(Material material, Pattern pattern, Color mean)
        {
            if (!material.HasProperty(PatternId))
            {
                return false;
            }

            if (pattern == null)
            {
                if (material.GetFloat(PatternId) == 0f && !material.IsKeywordEnabled(PatternKeyword)
                    && material.GetTexture(PatternMapId) == null)
                {
                    return false;
                }

                material.SetFloat(PatternId, 0f);
                material.DisableKeyword(PatternKeyword);
                material.SetTexture(PatternMapId, null);
                SetCharacterColor(material, mean);
                return true;
            }

            bool same = material.GetFloat(PatternId) == 1f
                && material.IsKeywordEnabled(PatternKeyword)
                && material.GetTexture(PatternMapId) == pattern.Texture
                && Mathf.Approximately(material.GetFloat(PatternScaleId), pattern.Scale)
                && Mathf.Approximately(material.GetFloat(PatternBlendId), PatternBlend)
                && Same(material.GetColor(BaseColorId), Color.white)
                && Same(material.GetColor(ShadeColorId), ShadeOf(mean));
            if (same)
            {
                return false;
            }

            SetCharacterColor(material, mean);
            material.SetColor(BaseColorId, Color.white);
            material.SetFloat(PatternId, 1f);
            material.EnableKeyword(PatternKeyword);
            material.SetTexture(PatternMapId, pattern.Texture);
            material.SetFloat(PatternScaleId, pattern.Scale);
            material.SetFloat(PatternBlendId, PatternBlend);
            return true;
        }

        /// <summary>
        /// palette.json から和柄を引く。{マテリアル名: 柄}。
        ///
        /// 柄の画像は Blender が FBX の隣に &lt;柄&gt;.png で書く（kasuri.png など）。palette.json に
        /// "pattern" があればその名前で、無ければマテリアル名の語（cloth_kimono_kasuri_blue なら kasuri）で
        /// 同じフォルダの png を探す。倍率は "pattern_scale"（Blender の params の pattern_scale と同じ値）。
        /// 倍率の無い柄は、でたらめな大きさで貼るより平均色のままにしておく。
        /// </summary>
        public static Dictionary<string, Pattern> LoadCharacterPatterns(string characterId)
        {
            var patterns = new Dictionary<string, Pattern>();
            string folder = EditorPaths.CharactersFolder + "/" + characterId;
            foreach (PaletteEntry entry in LoadPaletteEntries(characterId))
            {
                if (string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }

                Texture2D texture = null;
                if (!string.IsNullOrEmpty(entry.pattern))
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + entry.pattern + ".png");
                }
                else
                {
                    foreach (string word in Normalize(entry.name).Split('_'))
                    {
                        if (word != "face")
                        {
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + word + ".png");
                        }

                        if (texture != null)
                        {
                            break;
                        }
                    }
                }

                if (texture == null)
                {
                    if (!string.IsNullOrEmpty(entry.pattern))
                    {
                        Debug.LogWarning("[KCD] " + characterId + "/palette.json の " + entry.name + " の柄 "
                            + entry.pattern + ".png が " + folder + " に無いので、柄を貼らずに平均色で塗る");
                    }

                    continue;
                }

                if (entry.pattern_scale <= 0f)
                {
                    Debug.LogWarning("[KCD] " + characterId + "/palette.json の " + entry.name
                        + " に pattern_scale が無いので、柄を貼らずに平均色で塗る");
                    continue;
                }

                patterns[Normalize(entry.name)] = new Pattern { Texture = texture, Scale = entry.pattern_scale };
            }

            return patterns;
        }

        /// <summary>
        /// 全キャラの .mat を palette.json の色に塗り直し、塗り直した数を返す (#55)。
        ///
        /// 一度差し替えた FBX は、埋め込みのマテリアルを LoadAllAssetsAtPath で返さなくなる
        /// （外部の .mat に置き換わっている）。そのため ResolveMaterials 経由の
        /// <see cref="EnsureCharacter"/> には既存の .mat がほとんど来ない。
        /// ここでは FBX を通さず、palette.json の名前から .mat を直接引く。
        /// 顔と目（palette.json に載らない）は <c>CharacterImporter.RefreshFaceTextures</c> が
        /// <see cref="ApplyFaceLook"/> で塗り直す。SceneBuilder は両方を呼ぶ。
        /// </summary>
        public static int RepaintCharacters()
        {
            if (!AssetDatabase.IsValidFolder(EditorPaths.CharactersFolder))
            {
                return 0;
            }

            int repainted = 0;
            foreach (string folder in AssetDatabase.GetSubFolders(EditorPaths.CharactersFolder))
            {
                string characterId = Path.GetFileName(folder);
                Dictionary<string, Color> palette = LoadCharacterPalette(characterId);
                Dictionary<string, Pattern> patterns = LoadCharacterPatterns(characterId);
                foreach (string name in palette.Keys)
                {
                    string path = CharacterFolder + "/" + characterId + "_" + name + ".mat";
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null && RepaintCharacter(material, name, palette, patterns))
                    {
                        repainted++;
                    }
                }

                // 輪郭（反転ハル）は palette.json に載らないので、環境光とリムだけ揃える。
                Material outline = AssetDatabase.LoadAssetAtPath<Material>(
                    CharacterFolder + "/" + characterId + "_outline.mat");
                if (outline != null && (ApplyFlatAmbient(outline) | ApplyRim(outline)))
                {
                    EditorUtility.SetDirty(outline);
                    repainted++;
                }
            }

            if (repainted > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return repainted;
        }

        [System.Serializable]
        private sealed class PaletteEntry
        {
            public string name;
            public string hex;
            public string pattern;
            public float pattern_scale;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public PaletteEntry[] materials;
            public bool outward_faces;
        }

        /// <summary>
        /// Blender（build_characters.write_palette）が FBX の隣に書く色表を読む。無ければ null。
        /// FBX が運ぶのはマテリアル名だけなので、色はこのファイルで受け取る。
        /// </summary>
        private static PaletteFile LoadPaletteFile(string characterId)
        {
            string path = Path.Combine(EditorPaths.CharactersFolder, characterId, "palette.json");
            return File.Exists(path) ? JsonUtility.FromJson<PaletteFile>(File.ReadAllText(path)) : null;
        }

        private static PaletteEntry[] LoadPaletteEntries(string characterId)
        {
            return LoadPaletteFile(characterId)?.materials ?? new PaletteEntry[0];
        }

        /// <summary>
        /// 顔の面が外向きに出力されたキャラか（palette.json の outward_faces）。
        /// 輪郭線は法線の向きへ押し出した殻の裏面なので、面が外向きのときだけ顔にも線が出せる。
        /// </summary>
        public static bool FacesOutward(string characterId)
        {
            return LoadPaletteFile(characterId)?.outward_faces ?? false;
        }

        /// <summary>palette.json の色 {マテリアル名: 色}。和柄の色は柄の平均色。</summary>
        public static Dictionary<string, Color> LoadCharacterPalette(string characterId)
        {
            var palette = new Dictionary<string, Color>();
            foreach (PaletteEntry entry in LoadPaletteEntries(characterId))
            {
                if (!string.IsNullOrEmpty(entry.name) && ColorUtility.TryParseHtmlString("#" + entry.hex, out Color color))
                {
                    palette[Normalize(entry.name)] = color;
                }
            }

            return palette;
        }

        /// <summary>顔テクスチャ（face.png）を共有する Blender 側のマテリアル名。</summary>
        public static readonly string[] FaceTexturedNames = { "face", "eye_white", "eye_l", "eye_r" };

        private static readonly Color FaceShade = new Color(0.86f, 0.78f, 0.82f);
        private static readonly Color FaceShade2 = new Color(0.74f, 0.66f, 0.72f);

        private static bool IsFaceTextured(string name)
        {
            return System.Array.IndexOf(FaceTexturedNames, name) >= 0;
        }

        /// <summary>
        /// 輪郭線を付けない顔のパーツの材質（目・まつ毛・二重線・眉）。どれも顔の表面すれすれに貼った
        /// 薄い板か細い帯で、殻を押し出すと帯の縁がぎざぎざの黒い線になる。
        /// Blender の輪郭（kcd_chara/outline.py の SKIP_PARTS）もこれらのパーツを除いている。
        /// SKIP_PARTS にある鼻と口は face 材質に載るので、線の有無は <see cref="ApplyFaceLook"/> が決める。
        /// </summary>
        private static bool IsFaceDetail(string name)
        {
            return name.StartsWith("eye") || name == "lash" || name == "brow";
        }

        /// <summary>
        /// 顔まわりの見た目を材質に適用する。既に同じ状態なら false。
        /// Blender 側は face / eye_white / eye_l / eye_r の 4 面に同じ face.png（虹彩・ハイライト・
        /// まつ毛を描いた画像）を平面投影で貼っている。Unity でも同じ 4 面にテクスチャを付け、
        /// ベース色は白にして画像の色をそのまま出す（単色で塗ると目が「水色の円板」になる）。
        /// 目はテクスチャに描いたハイライトを見せたいので、リムと陰影を切って常に明部で描く。
        /// 顔のリムは服と同じ <see cref="CharacterRimIntensity"/>。環境光は顔も目も服と同じく平らにする。
        /// outline が true なら顔（目は除く）に服と同じ幅の輪郭線を付け、横顔の鼻・口・顎に線を出す。
        /// </summary>
        public static bool ApplyFaceLook(Material material, string rawName, Texture2D face, bool outline)
        {
            string name = Normalize(rawName);
            if (material == null || face == null || !IsFaceTextured(name))
            {
                return false;
            }

            bool eye = name != "face";
            float width = outline && !eye ? CharacterOutlineWidth : 0f;
            float rim = eye ? 0f : CharacterRimIntensity;
            float threshold = eye ? -1f : material.GetFloat("_ShadeThreshold");
            float threshold2 = eye ? -1f : material.GetFloat("_ShadeThreshold2");

            bool same = material.GetTexture("_BaseMap") == face
                && material.GetColor("_BaseColor") == Color.white
                && material.GetColor("_ShadeColor") == FaceShade
                && material.GetColor("_ShadeColor2") == FaceShade2
                && Mathf.Approximately(material.GetFloat("_OutlineWidth"), width)
                && Mathf.Approximately(material.GetFloat("_RimIntensity"), rim)
                && Mathf.Approximately(material.GetFloat("_ShadeThreshold"), threshold)
                && Mathf.Approximately(material.GetFloat("_ShadeThreshold2"), threshold2)
                && !NeedsOutlineScreenFlat(material)
                && !NeedsShadeCrisp(material)
                && !NeedsFlatAmbient(material);
            if (same)
            {
                return false;
            }

            material.SetTexture("_BaseMap", face);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_ShadeColor", FaceShade);
            material.SetColor("_ShadeColor2", FaceShade2);
            material.SetFloat("_OutlineWidth", width);
            material.SetFloat("_RimIntensity", rim);
            material.SetFloat("_ShadeThreshold", threshold);
            material.SetFloat("_ShadeThreshold2", threshold2);
            ApplyOutlineScreenFlat(material);
            ApplyShadeCrisp(material);
            ApplyFlatAmbient(material);
            return true;
        }

        private static bool NeedsOutlineScreenFlat(Material material)
        {
            return material.HasProperty(OutlineScreenFlatId)
                && !Mathf.Approximately(material.GetFloat(OutlineScreenFlatId), CharacterOutlineScreenFlat);
        }

        /// <summary>
        /// キャラの材質に <see cref="CharacterOutlineScreenFlat"/> を付ける。変えたら true。
        /// Blender の反転ハル（outline）は幅 0 なので、付けても何も変わらない。
        /// </summary>
        private static bool ApplyOutlineScreenFlat(Material material)
        {
            if (!NeedsOutlineScreenFlat(material))
            {
                return false;
            }

            material.SetFloat(OutlineScreenFlatId, CharacterOutlineScreenFlat);
            return true;
        }

        private static bool NeedsShadeCrisp(Material material)
        {
            return material.HasProperty(ShadeCrispId)
                && !Mathf.Approximately(material.GetFloat(ShadeCrispId), CharacterShadeCrisp);
        }

        /// <summary>キャラの材質に <see cref="CharacterShadeCrisp"/> を付ける。変えたら true。</summary>
        private static bool ApplyShadeCrisp(Material material)
        {
            if (!NeedsShadeCrisp(material))
            {
                return false;
            }

            material.SetFloat(ShadeCrispId, CharacterShadeCrisp);
            return true;
        }

        private static bool NeedsFlatAmbient(Material material)
        {
            return material.HasProperty(FlatAmbientId)
                && !Mathf.Approximately(material.GetFloat(FlatAmbientId), CharacterFlatAmbient);
        }

        /// <summary>キャラの材質に <see cref="CharacterFlatAmbient"/> を付ける。変えたら true。</summary>
        private static bool ApplyFlatAmbient(Material material)
        {
            if (!NeedsFlatAmbient(material))
            {
                return false;
            }

            material.SetFloat(FlatAmbientId, CharacterFlatAmbient);
            return true;
        }

        private static bool NeedsRim(Material material)
        {
            return material.HasProperty(RimIntensityId)
                && !Mathf.Approximately(material.GetFloat(RimIntensityId), CharacterRimIntensity);
        }

        /// <summary>キャラの材質のリムを <see cref="CharacterRimIntensity"/> にする。変えたら true。</summary>
        private static bool ApplyRim(Material material)
        {
            if (!NeedsRim(material))
            {
                return false;
            }

            material.SetFloat(RimIntensityId, CharacterRimIntensity);
            return true;
        }

        /// <summary>
        /// キャラの材質の陰の境目を <see cref="CharacterShadeThreshold"/> にする。顔と目と輪郭（反転ハル）は変えない。
        /// 変えたら true。
        /// </summary>
        private static bool ApplyShadeThreshold(Material material, string name)
        {
            if (IsFaceTextured(name) || name == "outline" || !material.HasProperty(ShadeThresholdId)
                || Mathf.Approximately(material.GetFloat(ShadeThresholdId), CharacterShadeThreshold))
            {
                return false;
            }

            material.SetFloat(ShadeThresholdId, CharacterShadeThreshold);
            return true;
        }

        private static readonly int HairRingId = Shader.PropertyToID("_HairRing");
        private static readonly int HairRingColorId = Shader.PropertyToID("_HairRingColor");
        private const string HairRingKeyword = "_HAIRRING_ON";

        /// <summary>天使の輪の色。髪の色を白へ <see cref="HairRingLighten"/> だけ寄せる。</summary>
        public static Color HairRingColor(Color hair)
        {
            Color ring = Color.Lerp(hair, HairRingHighlight, HairRingLighten);
            ring.a = 1f;
            return ring;
        }

        /// <summary>
        /// 髪の材質に天使の輪を付ける（ほかの材質は何もしない）。変えたら true。
        /// hair は髪の色（palette.json の hex）で、輪の色はそこから作る。
        /// </summary>
        public static bool ApplyHairRing(Material material, string name, Color hair)
        {
            if (name != HairRingMaterial || !material.HasProperty(HairRingId))
            {
                return false;
            }

            Color ring = HairRingColor(hair);
            bool same = material.GetFloat(HairRingId) == 1f
                && material.IsKeywordEnabled(HairRingKeyword)
                && Same(material.GetColor(HairRingColorId), ring);
            if (same)
            {
                return false;
            }

            material.SetFloat(HairRingId, 1f);
            material.EnableKeyword(HairRingKeyword);
            material.SetColor(HairRingColorId, ring);
            return true;
        }

        private static Color CharacterColor(string name, string[] tones)
        {
            string hair = tones != null ? tones[0] : "6B4A2F";
            string eye = tones != null ? tones[1] : "4A4A4A";

            if (name == "skin" || name == "face")
            {
                return Parse("FFE2D2");
            }

            if (name == "hair")
            {
                return Parse(hair);
            }

            if (name == "eye_white")
            {
                return Parse("FBFBFB");
            }

            if (name.StartsWith("eye"))
            {
                return Parse(eye);
            }

            foreach (KeyValuePair<string, string> entry in ClothColors)
            {
                if (name.Contains(entry.Key))
                {
                    return Parse(entry.Value);
                }
            }

            if (name == "metal")
            {
                return Parse("C8C8C0");
            }

            if (name.StartsWith("shoes") || name.StartsWith("bag"))
            {
                return Parse("5A3A24");
            }

            return Parse("E8E4DC");
        }

        private static float Smoothness(string name)
        {
            if (name.StartsWith("metal") || name.StartsWith("sign"))
            {
                return 0.62f;
            }

            if (name == "asphalt" || name.StartsWith("grass") || name == "sand")
            {
                return 0.08f;
            }

            return 0.22f;
        }

        private static void MakeTransparent(Material material, float alpha, Color color)
        {
            color.a = alpha;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Color Parse(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.magenta;
        }

        private static string Normalize(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "default";
            }

            string name = rawName.Trim().ToLowerInvariant();
            int dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static void Save(Material material, string path)
        {
            EditorPaths.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateAsset(material, path);
        }
    }
}
