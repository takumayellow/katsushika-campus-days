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
            { "leaf", "6FA84A" },
            { "leaf_light", "8FC663" },
            { "trunk", "6B4A2F" }
        };

        /// <summary>服・髪などの名前に含まれる色語 → 色。キャラ差分はここで吸収する。</summary>
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

        /// <summary>キャラクター FBX のマテリアルを 1 つ用意する。顔だけテクスチャを貼る。</summary>
        public static Material EnsureCharacter(string characterId, string rawName, Texture2D face)
        {
            string name = Normalize(rawName);
            string path = CharacterFolder + "/" + characterId + "_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find(ToonShader));
            CharacterTones.TryGetValue(characterId, out string[] tones);
            Color color = CharacterColor(name, tones);

            material.SetColor("_BaseColor", color);
            material.SetColor("_ShadeColor", Color.Lerp(color, new Color(0.45f, 0.42f, 0.58f), 0.42f));
            material.SetColor("_ShadeColor2", Color.Lerp(color, new Color(0.30f, 0.28f, 0.44f), 0.55f));
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
