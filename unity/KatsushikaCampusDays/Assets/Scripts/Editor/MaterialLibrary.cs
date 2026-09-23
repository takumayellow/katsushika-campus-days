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

        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string ToonShader = "KCD/Toon";

        /// <summary>キャンパス側のマテリアル名 → 基本色（RGB hex）。</summary>
        private static readonly Dictionary<string, string> CampusColors = new Dictionary<string, string>
        {
            { "asphalt", "3C3C3C" },
            { "brick_red", "8E3B2F" },
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
            // 葉は理科大グリーン #00843D 基準の 4 段（blender/kcd_lib/mats.py と同じ）。
            // 名前が leaf* なので foliage 判定（KCD/Toon + _ShadeColor 0.62 + アウトライン 0）と
            // CampusStage.IsFoliageMaterial（コライダ除外）はそのまま効く。
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
            { "bike_tire", "0F0F0F" }
        };

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
                Repaint(material, name);
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
                MakeTransparent(material, 0.58f, color);
                material.SetFloat("_Smoothness", 0.96f);
            }
            else
            {
                material.SetColor("_BaseColor", color);
                if (foliage)
                {
                    material.SetColor("_ShadeColor", color * 0.62f);
                    material.SetFloat("_OutlineWidth", 0f);
                }
                else
                {
                    material.SetFloat("_Smoothness", Smoothness(name));
                    material.SetFloat("_Metallic", name.StartsWith("metal") ? 0.8f : 0f);
                }
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
        /// </summary>
        private static void Repaint(Material material, string name)
        {
            if (!CampusColors.TryGetValue(name, out string hex))
            {
                return;
            }

            Color declared = Parse(hex);
            if (!material.HasProperty(BaseColorId) || Same(material.GetColor(BaseColorId), declared))
            {
                return;
            }

            material.SetColor(BaseColorId, declared);
            if (material.HasProperty(ShadeColorId))
            {
                material.SetColor(ShadeColorId, declared * 0.62f);
            }

            EditorUtility.SetDirty(material);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");

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
            material.SetFloat("_OutlineWidth", 0.005f);

            // 顔・目・スカートの面は内向きに出力されているので、両面描画にする（シェーダ側で法線を裏返す）。
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

            ApplyFaceLook(material, name, face);

            if (name.StartsWith("eye"))
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

        private static Color ShadeOf(Color color)
        {
            return Color.Lerp(color, new Color(0.45f, 0.42f, 0.58f), 0.42f);
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
            if (pattern == null && !Same(material.GetColor(BaseColorId), declared))
            {
                SetCharacterColor(material, declared);
                changed = true;
            }

            changed |= ApplyPattern(material, pattern, declared);
            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        /// <summary>
        /// 絣・矢絣などの和柄 (#55)。Blender は Generated 座標 × <see cref="Scale"/> で <see cref="Texture"/> を
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
        }

        /// <summary>
        /// Blender（build_characters.write_palette）が FBX の隣に書く色表を読む。無ければ空。
        /// FBX が運ぶのはマテリアル名だけなので、色はこのファイルで受け取る。
        /// </summary>
        private static PaletteEntry[] LoadPaletteEntries(string characterId)
        {
            string path = Path.Combine(EditorPaths.CharactersFolder, characterId, "palette.json");
            if (!File.Exists(path))
            {
                return new PaletteEntry[0];
            }

            PaletteFile file = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(path));
            return file?.materials ?? new PaletteEntry[0];
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
        /// 顔まわりの見た目を材質に適用する。既に同じ状態なら false。
        /// Blender 側は face / eye_white / eye_l / eye_r の 4 面に同じ face.png（虹彩・ハイライト・
        /// まつ毛を描いた画像）を平面投影で貼っている。Unity でも同じ 4 面にテクスチャを付け、
        /// ベース色は白にして画像の色をそのまま出す（単色で塗ると目が「水色の円板」になる）。
        /// 目はテクスチャに描いたハイライトを見せたいので、リムと陰影を切って常に明部で描く。
        /// </summary>
        public static bool ApplyFaceLook(Material material, string rawName, Texture2D face)
        {
            string name = Normalize(rawName);
            if (material == null || face == null || !IsFaceTextured(name))
            {
                return false;
            }

            bool eye = name != "face";
            float rim = eye ? 0f : material.GetFloat("_RimIntensity");
            float threshold = eye ? -1f : material.GetFloat("_ShadeThreshold");
            float threshold2 = eye ? -1f : material.GetFloat("_ShadeThreshold2");

            bool same = material.GetTexture("_BaseMap") == face
                && material.GetColor("_BaseColor") == Color.white
                && material.GetColor("_ShadeColor") == FaceShade
                && material.GetColor("_ShadeColor2") == FaceShade2
                && Mathf.Approximately(material.GetFloat("_OutlineWidth"), 0f)
                && Mathf.Approximately(material.GetFloat("_RimIntensity"), rim)
                && Mathf.Approximately(material.GetFloat("_ShadeThreshold"), threshold)
                && Mathf.Approximately(material.GetFloat("_ShadeThreshold2"), threshold2);
            if (same)
            {
                return false;
            }

            material.SetTexture("_BaseMap", face);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_ShadeColor", FaceShade);
            material.SetColor("_ShadeColor2", FaceShade2);
            material.SetFloat("_OutlineWidth", 0f);
            material.SetFloat("_RimIntensity", rim);
            material.SetFloat("_ShadeThreshold", threshold);
            material.SetFloat("_ShadeThreshold2", threshold2);
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
