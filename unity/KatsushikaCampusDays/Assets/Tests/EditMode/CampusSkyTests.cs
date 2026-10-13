using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// キャンパスの空・霧・遠くの地面がつながっていること (#8, #40)。
    ///
    /// 上空 120 m から見ると、霧の先に Procedural の skybox の「地面色」（灰色がかった茶色）が帯になって見えていた。
    /// 空を KCD/Sky にし、その地平線より下を霧と同じ色で塗り、地面（OuterGround）を霧の終わりより先まで敷く。
    /// RenderSettings はアクティブなシーンのものしか読めないので、霧と skybox は Campus.unity の本文から読む。
    /// </summary>
    public sealed class CampusSkyTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string SkyShaderPath = "Assets/Shaders/KCD_Sky.shader";
        private const string SkyShaderName = "KCD/Sky";

        private static string _sceneText;

        private static string SceneText
        {
            get
            {
                if (_sceneText == null)
                {
                    string path = Path.Combine(Application.dataPath, "Scenes", "Campus.unity");
                    Assert.IsTrue(File.Exists(path), path + " が無い");
                    _sceneText = File.ReadAllText(path);
                }

                return _sceneText;
            }
        }

        private static string RenderSettingsBlock
        {
            get
            {
                Match match = Regex.Match(SceneText, @"^RenderSettings:\s*$(.*?)^---", RegexOptions.Multiline | RegexOptions.Singleline);
                Assert.IsTrue(match.Success, ScenePath + " に RenderSettings が無い");
                return match.Groups[1].Value;
            }
        }

        private static float ReadFloat(string block, string key)
        {
            Match match = Regex.Match(block, @"^\s*" + key + @":\s*([-\d.eE+]+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, ScenePath + " の RenderSettings に " + key + " が無い");
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static Color ReadColor(string block, string key)
        {
            Match match = Regex.Match(block,
                @"^\s*" + key + @":\s*\{r:\s*([-\d.eE+]+),\s*g:\s*([-\d.eE+]+),\s*b:\s*([-\d.eE+]+),\s*a:\s*([-\d.eE+]+)\}",
                RegexOptions.Multiline);
            Assert.IsTrue(match.Success, ScenePath + " の RenderSettings に " + key + " が無い");
            return new Color(
                float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        private static Material SceneSkybox()
        {
            Match match = Regex.Match(RenderSettingsBlock,
                @"^\s*m_SkyboxMaterial:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32})", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, ScenePath + " に skybox のマテリアルが無い");

            string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
            Assert.That(path, Does.StartWith("Assets/"),
                ScenePath + " の skybox がプロジェクトのアセットでない（組み込みの Default-Skybox のまま）");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(material, path + " を Material として読めない");
            return material;
        }

        private static float FogEnd => ReadFloat(RenderSettingsBlock, "m_LinearFogEnd");

        private static void AssertSameColor(Color expected, Color actual, string what)
        {
            const float Tolerance = 0.005f;
            Assert.AreEqual(expected.r, actual.r, Tolerance, what + " の r");
            Assert.AreEqual(expected.g, actual.g, Tolerance, what + " の g");
            Assert.AreEqual(expected.b, actual.b, Tolerance, what + " の b");
        }

        [Test]
        public void SkyShader_Compiles()
        {
            Shader shader = Shader.Find(SkyShaderName);
            Assert.IsNotNull(shader, SkyShaderName + " が見つからない");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), SkyShaderName + " のコンパイルに失敗している");
        }

        [Test]
        public void SkyShader_StaysLightForWebGL()
        {
            // WebGL2 の GPU で重くしない約束。テクスチャを読まず、ループを持たない。
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Shaders", "KCD_Sky.shader"));
            string code = Regex.Replace(text, @"//[^\n]*", string.Empty);
            StringAssert.DoesNotMatch(@"\bfor\s*\(", code, SkyShaderPath + " にループがある");
            StringAssert.DoesNotMatch(@"\bwhile\s*\(", code, SkyShaderPath + " にループがある");
            StringAssert.DoesNotMatch(@"SAMPLE_TEXTURE|tex2D|texCUBE|\.Sample\s*\(", code,
                SkyShaderPath + " がテクスチャを読んでいる");
            StringAssert.DoesNotMatch(@"\b(TEXTURE2D|TEXTURECUBE)\s*\(", code, SkyShaderPath + " がテクスチャを持っている");
        }

        [Test]
        public void Campus_SkyboxUsesTheSkyShader()
        {
            Material sky = SceneSkybox();
            Assert.IsNotNull(sky.shader, sky.name + " にシェーダが無い");
            Assert.AreEqual(SkyShaderName, sky.shader.name, sky.name + " のシェーダが " + SkyShaderName + " でない");
        }

        [Test]
        public void Campus_FogMatchesTheSkyBelowTheHorizon()
        {
            // 空の地平線より下（far clip の先）と、霧に溶けた地面が同じ色なら、上空から見ても帯が出ない (#40)。
            string block = RenderSettingsBlock;
            Assert.AreEqual(1f, ReadFloat(block, "m_Fog"), "霧が切れている");
            Assert.AreEqual(1f, ReadFloat(block, "m_FogMode"), "霧が Linear でない");

            Color fog = ReadColor(block, "m_FogColor");
            Material sky = SceneSkybox();
            AssertSameColor(fog, sky.GetColor("_HorizonColor"), "空の地平線の色と霧の色");
        }

        [Test]
        public void Campus_StartsWithTheMorningPalette()
        {
            // シーンに焼いた霧と環境光が、ゲーム開始 8:30 の配色と同じこと。違うと入った瞬間に色が跳ぶ。
            SkyState morning = SkyPalette.Evaluate(DayRestart.DayStartHour);
            string block = RenderSettingsBlock;

            Assert.AreEqual(1f, ReadFloat(block, "m_AmbientMode"), "環境光が Trilight でない");
            AssertSameColor(morning.Fog, ReadColor(block, "m_FogColor"), "霧の色");
            AssertSameColor(morning.AmbientSky, ReadColor(block, "m_AmbientSkyColor"), "上からの環境光");
            AssertSameColor(morning.AmbientEquator, ReadColor(block, "m_AmbientEquatorColor"), "横からの環境光");
            AssertSameColor(morning.AmbientGround, ReadColor(block, "m_AmbientGroundColor"), "下からの環境光");
            AssertSameColor(morning.Zenith, SceneSkybox().GetColor("_ZenithColor"), "空の天頂の色");
        }

        [Test]
        public void Campus_CamerasSeeAtLeastToTheFogEnd()
        {
            // far clip が霧の終わりより手前だと、霧に溶けきる前の地面がそこで切れて空が見える。
            float fogEnd = FogEnd;
            MatchCollection clips = Regex.Matches(SceneText,
                @"^\s*(?:far clip plane|FarClipPlane):\s*([-\d.eE+]+)\s*$", RegexOptions.Multiline);
            Assert.Greater(clips.Count, 0, ScenePath + " にカメラが無い");
            foreach (Match clip in clips)
            {
                float far = float.Parse(clip.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.GreaterOrEqual(far, fogEnd, "far clip " + far + " m が霧の終わり " + fogEnd + " m より手前");
            }
        }

        [Test]
        public void Campus_GroundReachesPastTheFogEndFromAnywhereWalkable()
        {
            float fogEnd = FogEnd;
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Renderer ground = null;
                WorldBounds bounds = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.name == "OuterGround")
                        {
                            ground = renderer;
                        }
                    }

                    if (bounds == null)
                    {
                        bounds = root.GetComponentInChildren<WorldBounds>(true);
                    }
                }

                Assert.IsNotNull(ground, ScenePath + " に OuterGround が無い");
                Assert.IsNotNull(bounds, ScenePath + " に WorldBounds が無い");
                Assert.IsTrue(ground.enabled && ground.gameObject.activeInHierarchy, "OuterGround が描かれない");

                // 歩ける矩形のどの端に立っても、霧の終わりまで地面が続くこと。
                Bounds world = ground.bounds;
                Rect area = bounds.Area;
                float margin = Mathf.Min(
                    Mathf.Min(area.xMin - world.min.x, world.max.x - area.xMax),
                    Mathf.Min(area.yMin - world.min.z, world.max.z - area.yMax));
                Assert.GreaterOrEqual(margin, fogEnd,
                    "歩ける範囲 " + area + " の端から OuterGround の端まで " + margin + " m しかない（霧の終わり " + fogEnd + " m）");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
