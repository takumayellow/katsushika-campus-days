using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KCD.Editor
{
    /// <summary>
    /// 日本語の UI 用に TMP フォントアセットを 1 つ用意する。
    /// OS のフォントをプロジェクトへ複製し、動的アトラスのフォントアセットを作る。
    /// </summary>
    public static class FontLibrary
    {
        public const string FontFolder = "Assets/Fonts";
        public const string FontAssetPath = "Assets/Fonts/KCD_JP.asset";

        /// <summary>建物の名札が共有する縁取り付きマテリアル。</summary>
        public const string SignMaterialPath = "Assets/Fonts/KCD_JP - Sign Outline.mat";

        private const string TmpSettingsPath = TmpEssentials.SettingsPath;

        /// <summary>OS 側のフォント候補。再配布できる OFL のものを先に置く。</summary>
        private static readonly string[] Candidates =
        {
            "NotoSansJP-VF.ttf",
            "BIZ-UDGothicR.ttc",
            "YuGothM.ttc",
            "meiryo.ttc",
            "msgothic.ttc"
        };

        private static TMP_FontAsset _cached;

        /// <summary>日本語フォントアセットを取得する。無ければ生成する。見つからなければ null。</summary>
        public static TMP_FontAsset Ensure()
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (!TmpEssentials.Ensure())
            {
                EditorPaths.Report("TMP の設定が無いためフォント生成を飛ばします。");
                return null;
            }

            _cached = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (_cached != null)
            {
                Register(_cached);
                return _cached;
            }

            Font font = EnsureSourceFont();
            if (font == null)
            {
                EditorPaths.Report("日本語フォントが見つかりません。TMP 既定フォントのままにします。");
                return null;
            }

            _cached = TMP_FontAsset.CreateFontAsset(
                font, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);

            if (_cached == null)
            {
                EditorPaths.Report("フォントアセットを生成できません: " + font.name);
                return null;
            }

            _cached.name = "KCD_JP";
            AssetDatabase.CreateAsset(_cached, FontAssetPath);

            if (_cached.atlasTextures != null && _cached.atlasTextures.Length > 0)
            {
                _cached.atlasTextures[0].name = "KCD_JP Atlas";
                AssetDatabase.AddObjectToAsset(_cached.atlasTextures[0], _cached);
            }

            if (_cached.material != null)
            {
                _cached.material.name = "KCD_JP Material";
                AssetDatabase.AddObjectToAsset(_cached.material, _cached);
            }

            _cached.TryAddCharacters(CollectCharacters());
            EditorUtility.SetDirty(_cached);
            AssetDatabase.SaveAssets();
            Register(_cached);
            EditorPaths.Report("日本語フォントアセットを生成: " + FontAssetPath + " (" + font.name + ")");
            return _cached;
        }

        /// <summary>
        /// フォントの既定マテリアルを写した縁取り付きマテリアルを 1 枚だけ用意して返す（#64）。
        /// TextMeshPro の outlineWidth / outlineColor は編集時に renderer.material を呼び、
        /// 名札 1 枚ごとにマテリアルを複製してシーンへ埋め込むので、名札はこの 1 枚を共有する。
        /// 毎回フォント側の値を写し直すので、アトラスを作り直しても古い値が残らない。
        /// </summary>
        public static Material EnsureSignMaterial(TMP_FontAsset fontAsset, float outlineWidth, Color32 outlineColor)
        {
            if (fontAsset == null || fontAsset.material == null)
            {
                return null;
            }

            Material source = fontAsset.material;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(SignMaterialPath);
            if (material == null)
            {
                EditorPaths.EnsureFolder(FontFolder);
                material = new Material(source) { name = Path.GetFileNameWithoutExtension(SignMaterialPath) };
                AssetDatabase.CreateAsset(material, SignMaterialPath);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            // Mobile/Distance Field は OUTLINE_ON が無いと縁を描かない（shader_feature）。
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            ShaderUtilities.UpdateShaderRatios(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>TMP の既定フォントに設定して、明示指定を忘れても日本語が出るようにする。</summary>
        private static void Register(TMP_FontAsset fontAsset)
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null || TMP_Settings.defaultFontAsset == fontAsset)
            {
                return;
            }

            TMP_Settings.defaultFontAsset = fontAsset;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>OS のフォントをプロジェクトへ複製し、Font として読み込む。</summary>
        private static Font EnsureSourceFont()
        {
            EditorPaths.EnsureFolder(FontFolder);
            string windowsFonts = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "Fonts");

            foreach (string candidate in Candidates)
            {
                string source = Path.Combine(windowsFonts, candidate);
                if (!File.Exists(source))
                {
                    continue;
                }

                string assetPath = FontFolder + "/" + candidate;
                string destination = EditorPaths.ProjectRelative(assetPath);

                if (!File.Exists(destination))
                {
                    File.Copy(source, destination, false);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }

                Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                if (font != null)
                {
                    return font;
                }
            }

            return null;
        }

        /// <summary>Data 配下の JSON と UI の定型文から、先に焼いておく文字を集める。</summary>
        private static string CollectCharacters()
        {
            HashSet<char> characters = new HashSet<char>();
            const string baseline =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
                " 　:：/／%.,、。()（）「」『』・-ー〜!?！？+＋…♪→←↑↓" +
                "葛飾キャンパスデイズはじめるつづきからおわる操作説明設定" +
                "クエスト進行中達成所持品時刻日目曜午前午後" +
                "話す入る調べる拾う受け取る戻る決定選択キャラクター" +
                "東京理科大学未来図書館研究棟講義棟教育センター体育館温室実験棟" +
                "並木道広場食堂売店中庭池噴水正門新宿みらい公園";

            foreach (char c in baseline)
            {
                characters.Add(c);
            }

            string dataFolder = EditorPaths.ProjectRelative("Assets/Data");
            if (Directory.Exists(dataFolder))
            {
                foreach (string path in Directory.GetFiles(dataFolder, "*.json", SearchOption.AllDirectories))
                {
                    foreach (char c in File.ReadAllText(path, Encoding.UTF8))
                    {
                        if (!char.IsControl(c))
                        {
                            characters.Add(c);
                        }
                    }
                }
            }

            StringBuilder builder = new StringBuilder(characters.Count);
            foreach (char c in characters)
            {
                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
