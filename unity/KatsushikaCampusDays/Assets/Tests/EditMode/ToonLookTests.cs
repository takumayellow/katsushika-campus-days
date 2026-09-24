using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Tests
{
    /// <summary>
    /// ToonLook（キャラのマテリアルの役割ごとのトゥーンの値と、シェーダから写した式）を確かめる (#14, #44)。
    ///
    /// 役割: 肌と顔は柔らかい境目で赤みの陰、髪・金属・服は元の色相のまま暗くなる陰で 2 段、顔の線は常に明部。
    /// 顔の陰は法線ではなく頭の向きで決め、光が回ると明部が連続して動く。
    /// 輪郭の太さは #44 の表（画角 55°・1080p で 5 m まで約 5 px、10 m 約 2.6 px、20 m 約 1.3 px、30〜60 m で消える）。
    /// 式がシェーダと同じであることは ToonShaderTests が式の行を突き合わせて守る。
    /// </summary>
    public sealed class ToonLookTests
    {
        private static readonly ToonLook.Role[] ShadedRoles =
        {
            ToonLook.Role.Skin, ToonLook.Role.Face, ToonLook.Role.Hair, ToonLook.Role.Metal, ToonLook.Role.Cloth,
        };

        private static readonly ToonLook.Look FaceLook = ToonLook.LookOf(ToonLook.Role.Face);

        private static IEnumerable<ToonLook.Role> AllRoles()
        {
            foreach (ToonLook.Role role in System.Enum.GetValues(typeof(ToonLook.Role)))
            {
                yield return role;
            }
        }

        /// <summary>Look の値と、それが入るシェーダのプロパティ。</summary>
        private static IEnumerable<(string Property, float Value)> Values(ToonLook.Look look)
        {
            yield return ("_ShadeByNdotL", look.ShadeByNdotL);
            yield return ("_ShadeThreshold", look.ShadeThreshold);
            yield return ("_ShadeSoftness", look.ShadeSoftness);
            yield return ("_ShadeThreshold2", look.ShadeThreshold2);
            yield return ("_ShadeSoftness2", look.ShadeSoftness2);
            yield return ("_RimIntensity", look.RimIntensity);
            yield return ("_RimPower", look.RimPower);
            yield return ("_RimBacklight", look.RimBacklight);
            yield return ("_RimLightAlign", look.RimLightAlign);
            yield return ("_FaceShadow", look.FaceShadow ? 1f : 0f);
            yield return ("_FaceShadowSoftness", look.FaceShadowSoftness);
            yield return ("_FaceShadowBias", look.FaceShadowBias);
            yield return ("_HairHighlightIntensity", look.HairHighlightIntensity);
            yield return ("_HairHighlightHeight", look.HairHighlightHeight);
            yield return ("_HairHighlightWidth", look.HairHighlightWidth);
            yield return ("_HairHighlightSoftness", look.HairHighlightSoftness);
            yield return ("_HairHighlightJag", look.HairHighlightJag);
        }

        /// <summary>シェーダの ToonRamp（_ShadeByNdotL = 1）の明部の割合と 2 段目より明るい割合。</summary>
        private static (float Step1, float Step2) Ramp(ToonLook.Look look, float ndotl)
        {
            return (
                ToonLook.SmoothStep(look.ShadeThreshold - look.ShadeSoftness, look.ShadeThreshold + look.ShadeSoftness, ndotl),
                ToonLook.SmoothStep(look.ShadeThreshold2 - look.ShadeSoftness2, look.ShadeThreshold2 + look.ShadeSoftness2, ndotl));
        }

        /// <summary>色相 15° 刻み × 彩度 × 明度の格子。</summary>
        private static IEnumerable<Color> ColorGrid()
        {
            float[] saturations = { 0f, 0.1f, 0.2f, 0.4f, 0.7f, 1f };
            float[] values = { 0.2f, 0.5f, 0.8f, 1f };
            for (int hue = 0; hue < 360; hue += 15)
            {
                foreach (float saturation in saturations)
                {
                    foreach (float value in values)
                    {
                        yield return Color.HSVToRGB(hue / 360f, saturation, value);
                    }
                }
            }
        }

        private static float HueDistance(Color a, Color b)
        {
            Color.RGBToHSV(a, out float hueA, out float _, out float _);
            Color.RGBToHSV(b, out float hueB, out float _, out float _);
            return Mathf.Abs(Mathf.DeltaAngle(hueA * 360f, hueB * 360f));
        }

        private static float Saturation(Color color)
        {
            Color.RGBToHSV(color, out float _, out float saturation, out float _);
            return saturation;
        }

        [TestCase("Mirai_Hair.001", "mirai", ExpectedResult = "hair")]
        [TestCase("mirai_face", "mirai", ExpectedResult = "face")]
        [TestCase("face", "mirai", ExpectedResult = "face")]
        [TestCase(" Face ", null, ExpectedResult = "face")]
        [TestCase("botchan_cloth_kimono_kasuri_blue", "botchan", ExpectedResult = "cloth_kimono_kasuri_blue")]
        [TestCase("inari_glasses", "sora", ExpectedResult = "inari_glasses")]
        [TestCase("mirai_", "mirai", ExpectedResult = "mirai_")]
        [TestCase("", "mirai", ExpectedResult = "")]
        public string マテリアル名からBlender側の名前を取り出す(string rawName, string characterId)
        {
            return ToonLook.MaterialName(rawName, characterId);
        }

        [TestCase("outline", ToonLook.Role.Outline)]
        [TestCase("face", ToonLook.Role.Face)]
        [TestCase("eye_white", ToonLook.Role.Eye)]
        [TestCase("eye_l", ToonLook.Role.Eye)]
        [TestCase("eye_r", ToonLook.Role.Eye)]
        [TestCase("lash", ToonLook.Role.Line)]
        [TestCase("eye_rim", ToonLook.Role.Line)]
        [TestCase("brow", ToonLook.Role.Line)]
        [TestCase("skin", ToonLook.Role.Skin)]
        [TestCase("hair", ToonLook.Role.Hair)]
        [TestCase("hair_front", ToonLook.Role.Hair)]
        [TestCase("hairpin", ToonLook.Role.Cloth)]
        [TestCase("metal", ToonLook.Role.Metal)]
        [TestCase("glasses", ToonLook.Role.Metal)]
        [TestCase("cloth_blouse", ToonLook.Role.Cloth)]
        [TestCase("ribbon_red", ToonLook.Role.Cloth)]
        [TestCase("collar_white", ToonLook.Role.Cloth)]
        [TestCase("shoes_loafer", ToonLook.Role.Cloth)]
        [TestCase("", ToonLook.Role.Cloth)]
        public void 名前から役割を決める(string name, ToonLook.Role expected)
        {
            Assert.AreEqual(expected, ToonLook.RoleOf(name));
        }

        [Test]
        public void 役割ごとの値がシェーダのプロパティの範囲に入る()
        {
            Shader shader = Shader.Find(ToonLook.ShaderName);
            Assert.IsNotNull(shader, ToonLook.ShaderName + " が見つからない");
            var failures = new List<string>();
            foreach (ToonLook.Role role in AllRoles())
            {
                ToonLook.Look look = ToonLook.LookOf(role);
                if (!look.Managed)
                {
                    continue;
                }

                foreach ((string property, float value) in Values(look))
                {
                    int index = shader.FindPropertyIndex(property);
                    if (index < 0)
                    {
                        failures.Add(role + ": " + property + " がシェーダに無い");
                        continue;
                    }

                    ShaderPropertyType type = shader.GetPropertyType(index);
                    if (type == ShaderPropertyType.Range)
                    {
                        Vector2 limits = shader.GetPropertyRangeLimits(index);
                        if (value < limits.x || value > limits.y)
                        {
                            failures.Add(role + ": " + property + " = " + value + " が範囲 " + limits + " の外");
                        }
                    }
                    else if (type == ShaderPropertyType.Float)
                    {
                        if (value != 0f && value != 1f)
                        {
                            failures.Add(role + ": " + property + " = " + value + "（スイッチなので 0 か 1）");
                        }
                    }
                    else
                    {
                        failures.Add(role + ": " + property + " の型が " + type);
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 陰のある役割は明部と2段の陰がN_Lの範囲に並ぶ()
        {
            foreach (ToonLook.Role role in ShadedRoles)
            {
                ToonLook.Look look = ToonLook.LookOf(role);
                string label = role + ": ";
                Assert.IsTrue(look.Managed, label + "ToonLook が扱っていない");
                Assert.AreEqual(1f, look.ShadeByNdotL, label + "N・L の 2 段を使っていない");

                float lower1 = look.ShadeThreshold - look.ShadeSoftness;
                float upper2 = look.ShadeThreshold2 + look.ShadeSoftness2;
                Assert.Greater(lower1 - upper2, 0.1f, label + "1 段目の陰だけの幅が N・L で 0.1 に満たない");
                Assert.Less(look.ShadeThreshold + look.ShadeSoftness, 1f, label + "真正面からの光でも明部にならない");
                Assert.Greater(look.ShadeThreshold2 - look.ShadeSoftness2, -1f, label + "真後ろからの光でも 2 段目の陰にならない");

                (float lit1, float lit2) = Ramp(look, 1f);
                Assert.AreEqual(1f, lit1, 1e-6f, label + "N・L = 1 が明部でない");
                Assert.AreEqual(1f, lit2, 1e-6f, label + "N・L = 1 が明部でない");

                (float mid1, float mid2) = Ramp(look, 0.5f * (lower1 + upper2));
                Assert.AreEqual(0f, mid1, 1e-6f, label + "1 段目の陰の中ほどが明部に掛かる");
                Assert.AreEqual(1f, mid2, 1e-6f, label + "1 段目の陰の中ほどが 2 段目に掛かる");

                (float dark1, float dark2) = Ramp(look, -1f);
                Assert.AreEqual(0f, dark1, 1e-6f, label + "N・L = -1 が 2 段目の陰でない");
                Assert.AreEqual(0f, dark2, 1e-6f, label + "N・L = -1 が 2 段目の陰でない");
            }
        }

        [Test]
        public void 肌は髪と金属と服より境目が柔らかく顔の段は肌と同じ()
        {
            ToonLook.Look skin = ToonLook.LookOf(ToonLook.Role.Skin);
            foreach (ToonLook.Role role in new[] { ToonLook.Role.Hair, ToonLook.Role.Metal, ToonLook.Role.Cloth })
            {
                ToonLook.Look look = ToonLook.LookOf(role);
                Assert.Greater(skin.ShadeSoftness, look.ShadeSoftness, role + " の 1 段目の境目が肌より柔らかい");
                Assert.Greater(skin.ShadeSoftness2, look.ShadeSoftness2, role + " の 2 段目の境目が肌より柔らかい");
            }

            // 顔の頂点のうち頭の向きを焼いていないものは N・L で塗るので、首の肌と段を揃える。
            Assert.AreEqual(skin.ShadeThreshold, FaceLook.ShadeThreshold);
            Assert.AreEqual(skin.ShadeSoftness, FaceLook.ShadeSoftness);
            Assert.AreEqual(skin.ShadeThreshold2, FaceLook.ShadeThreshold2);
            Assert.AreEqual(skin.ShadeSoftness2, FaceLook.ShadeSoftness2);
        }

        [Test]
        public void 顔の陰は顔だけ髪の輪は髪だけに付く()
        {
            foreach (ToonLook.Role role in AllRoles())
            {
                ToonLook.Look look = ToonLook.LookOf(role);
                Assert.AreEqual(role == ToonLook.Role.Face, look.FaceShadow, role + " の顔の陰");
                Assert.AreEqual(role == ToonLook.Role.Hair, look.HairHighlightIntensity > 0f, role + " の髪の輪");
            }
        }

        [Test]
        public void 陰のある役割はリムと逆光のリムが付き顔の線には付かない()
        {
            foreach (ToonLook.Role role in ShadedRoles)
            {
                ToonLook.Look look = ToonLook.LookOf(role);
                Assert.Greater(look.RimIntensity, 0f, role + " にリムが無い");
                Assert.Greater(look.RimBacklight, 0f, role + " に逆光のリムが無い");
            }

            ToonLook.Look line = ToonLook.LookOf(ToonLook.Role.Line);
            Assert.AreEqual(0f, line.RimIntensity, "顔の線にリムが付く");
            Assert.AreEqual(0f, line.RimBacklight, "顔の線に逆光のリムが付く");
        }

        [Test]
        public void 顔の線はどの向きの光でも明部で描く()
        {
            ToonLook.Look line = ToonLook.LookOf(ToonLook.Role.Line);
            Assert.IsTrue(line.Managed);
            Assert.AreEqual(0f, line.ShadeByNdotL, "顔の線は half-lambert の経路で描く");

            // half-lambert（影で 0.55 倍まで下がる）は 0〜1。どこでも 1 段目・2 段目とも明部の側。
            foreach (float lambert in new[] { 0f, 0.25f, 0.5f, 1f })
            {
                Assert.AreEqual(1f, ToonLook.SmoothStep(line.ShadeThreshold - line.ShadeSoftness, line.ShadeThreshold + line.ShadeSoftness, lambert), 1e-6f);
                Assert.AreEqual(1f, ToonLook.SmoothStep(line.ShadeThreshold2 - line.ShadeSoftness, line.ShadeThreshold2 + line.ShadeSoftness, lambert), 1e-6f);
            }

            // 追加ライトは smoothstep(T, T + 4S, saturate(N・L))。
            foreach (float ndotl in new[] { 0f, 0.5f, 1f })
            {
                Assert.AreEqual(1f, ToonLook.SmoothStep(line.ShadeThreshold, line.ShadeThreshold + line.ShadeSoftness * 4f, ndotl), 1e-6f);
            }
        }

        [Test]
        public void 輪郭と目はToonLookが触らない()
        {
            Assert.IsFalse(ToonLook.LookOf(ToonLook.Role.Outline).Managed);
            Assert.IsFalse(ToonLook.LookOf(ToonLook.Role.Eye).Managed);
        }

        [Test]
        public void 服の陰は明部より暗く2段目は1段目より暗い()
        {
            var failures = new List<string>();
            foreach (Color color in ColorGrid())
            {
                Color shade = ToonLook.ClothShade(color);
                Color shade2 = ToonLook.ClothShade2(color);
                for (int c = 0; c < 3; c++)
                {
                    if (!(shade[c] < 1f) || !(shade2[c] < shade[c]))
                    {
                        failures.Add(color + ": 1 段目 " + shade + " / 2 段目 " + shade2);
                        break;
                    }
                }

                if (shade.a != 1f || shade2.a != 1f)
                {
                    failures.Add(color + ": 陰の係数の a が 1 でない");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 彩度のある色の陰の係数は元の色と同じ色相()
        {
            var failures = new List<string>();
            foreach (Color color in ColorGrid())
            {
                if (Saturation(color) < 0.2f)
                {
                    continue;
                }

                foreach (Color shade in new[] { ToonLook.ClothShade(color), ToonLook.ClothShade2(color) })
                {
                    float distance = HueDistance(color, shade);
                    if (distance > 0.5f)
                    {
                        failures.Add(color + " の陰 " + shade + " の色相が " + distance + "° ずれる");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [TestCase(1f, 1f, 1f)]
        [TestCase(0.5f, 0.5f, 0.5f)]
        [TestCase(0.99f, 0.99f, 0.98f)]
        [TestCase(0.16f, 0.16f, 0.19f)]
        public void 白や灰色の陰は青みの灰色になる(float r, float g, float b)
        {
            var color = new Color(r, g, b, 1f);
            foreach (Color shade in new[] { ToonLook.ClothShade(color), ToonLook.ClothShade2(color) })
            {
                Assert.Greater(shade.b, shade.r + 0.03f, color + " の陰 " + shade + " が青くない");
                Assert.Greater(shade.b, shade.g + 0.03f, color + " の陰 " + shade + " が青くない");
            }
        }

        [Test]
        public void 肌の陰は赤みを残して2段目ほど暗い()
        {
            Color shade = ToonLook.SkinShade;
            Color shade2 = ToonLook.SkinShade2;
            for (int c = 0; c < 3; c++)
            {
                Assert.Less(shade[c], 1f, "肌の 1 段目が明部より明るい");
                Assert.Less(shade2[c], shade[c], "肌の 2 段目が 1 段目より明るい");
            }

            foreach (Color s in new[] { shade, shade2 })
            {
                Assert.Greater(s.r, s.g, s + " が赤みを残していない");
                Assert.Greater(s.r, s.b, s + " が赤みを残していない");
            }
        }

        [Test]
        public void 髪の輪の色は髪より明るく色相を保つ()
        {
            var failures = new List<string>();
            foreach (Color color in ColorGrid())
            {
                Color highlight = ToonLook.HairHighlight(color);
                if (highlight.a != 1f)
                {
                    failures.Add(color + ": a が 1 でない");
                }

                for (int c = 0; c < 3; c++)
                {
                    if (highlight[c] < color[c] - 1e-6f)
                    {
                        failures.Add(color + ": " + highlight + " が髪より暗い");
                        break;
                    }
                }

                if (Saturation(color) >= 0.2f && HueDistance(color, highlight) > 0.5f)
                {
                    failures.Add(color + ": " + highlight + " の色相がずれる");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 顔の左右の位置は縁で正負のFaceEdgeSineになり目印より小さい()
        {
            Assert.AreEqual(ToonLook.FaceEdgeSine, ToonLook.FaceSide(0.3f, 0.2f, 0.1f), 1e-5f);
            Assert.AreEqual(-ToonLook.FaceEdgeSine, ToonLook.FaceSide(0.1f, 0.2f, 0.1f), 1e-5f);
            Assert.AreEqual(0f, ToonLook.FaceSide(0.2f, 0.2f, 0.1f), 1e-5f);
            Assert.AreEqual(ToonLook.FaceSideClamp, ToonLook.FaceSide(0.5f, 0.2f, 0.1f), 1e-6f);
            Assert.AreEqual(-ToonLook.FaceSideClamp, ToonLook.FaceSide(-0.5f, 0.2f, 0.1f), 1e-6f);
            Assert.AreEqual(0f, ToonLook.FaceSide(0.3f, 0.2f, 0f), "幅の無い顔で 0 にならない");

            Assert.AreEqual(Mathf.Sin(74f * Mathf.Deg2Rad), ToonLook.FaceEdgeSine, 0.005f, "顔の縁が左右 74° に合わない");
            Assert.LessOrEqual(ToonLook.FaceEdgeSine, ToonLook.FaceSideClamp);
            Assert.Less(ToonLook.FaceSideClamp, ToonLook.FaceFrameMarker);
            Assert.Less(ToonLook.FaceFrameMarker, 1f);

            // ビルドは接線を half に詰めることがある。詰めても顔の頂点は目印より小さく、Mikk の ±1 は 1 のまま。
            Assert.Less(Mathf.HalfToFloat(Mathf.FloatToHalf(ToonLook.FaceSideClamp)), ToonLook.FaceFrameMarker);
            Assert.AreEqual(1f, Mathf.HalfToFloat(Mathf.FloatToHalf(1f)));
        }

        /// <summary>方位は頭の正面（+Z）からキャラの右（+X）へ回る角度。返すのは光へ向かう向き。</summary>
        private static Vector3 LightFrom(float azimuthDegrees, float elevationDegrees)
        {
            float azimuth = azimuthDegrees * Mathf.Deg2Rad;
            float elevation = elevationDegrees * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Sin(azimuth) * Mathf.Cos(elevation),
                Mathf.Sin(elevation),
                Mathf.Cos(azimuth) * Mathf.Cos(elevation));
        }

        private static float Lit(Vector3 light, float side)
        {
            return Lit(light, side, FaceLook.FaceShadowBias);
        }

        private static float Lit(Vector3 light, float side, float bias)
        {
            return ToonLook.FaceLit(Vector3.forward, side, light.normalized, FaceLook.FaceShadowSoftness, bias);
        }

        /// <summary>顔の左右の位置（縁の ±0.96 まで）。</summary>
        private static IEnumerable<float> Sides(int step = 1)
        {
            for (int i = -48; i <= 48; i += step)
            {
                yield return i / 50f;
            }
        }

        [Test]
        public void 真横からの光では境目が顔の中央からバイアスぶん陰の側に来る()
        {
            Assert.AreEqual(0.5f, Lit(Vector3.right, 0f, 0f), 1e-4f);
            Assert.AreEqual(0.5f, Lit(Vector3.right, -FaceLook.FaceShadowBias), 1e-4f);
            Assert.AreEqual(0.5f, Lit(Vector3.left, FaceLook.FaceShadowBias), 1e-4f);
            Assert.AreEqual(1f, Lit(Vector3.right, 0.5f), 1e-4f, "右からの光で顔の右が明るくない");
            Assert.AreEqual(0f, Lit(Vector3.right, -0.5f), 1e-4f, "右からの光で顔の左が暗くない");
        }

        [Test]
        public void 斜め45度の光では境目が顔の左右の位置の負のsin45度に来る()
        {
            Vector3 light = LightFrom(45f, 0f);
            Assert.AreEqual(0.5f, Lit(light, -Mathf.Sqrt(0.5f), 0f), 1e-3f);
            Assert.AreEqual(1f, Lit(light, -0.6f, 0f), 1e-3f);
            Assert.AreEqual(0f, Lit(light, -0.8f, 0f), 1e-3f);
        }

        [Test]
        public void 左右を入れ替えると明暗も入れ替わる()
        {
            Vector3[] lights =
            {
                new Vector3(1f, 0f, 0f), new Vector3(1f, 0.4f, 0.6f), new Vector3(0.5f, 0.2f, -1f),
                new Vector3(0.3f, 2f, -1f), new Vector3(0.2f, 0f, 1f),
            };
            foreach (Vector3 light in lights)
            {
                var mirror = new Vector3(-light.x, light.y, light.z);
                foreach (float side in Sides())
                {
                    Assert.AreEqual(Lit(light, side), Lit(mirror, -side), 1e-5f, "光 " + light + " / 位置 " + side);
                }
            }
        }

        [Test]
        public void 正面と真上からの光では顔全体が明るく真後ろからでは全体が陰()
        {
            var bright = new List<Vector3>
            {
                new Vector3(0f, 0f, 1f), new Vector3(0f, 0.5f, 1f), new Vector3(0f, 2f, 1f), Vector3.up,
            };
            for (int azimuth = 0; azimuth < 360; azimuth += 90)
            {
                bright.Add(LightFrom(azimuth, 89.5f));
            }

            Vector3[] dark = { new Vector3(0f, 0f, -1f), new Vector3(0f, 0.5f, -1f), new Vector3(0f, 1.5f, -1f) };
            foreach (float side in Sides())
            {
                foreach (Vector3 light in bright)
                {
                    Assert.Greater(Lit(light, side), 0.999f, "光 " + light + " で位置 " + side + " が陰");
                }

                foreach (Vector3 light in dark)
                {
                    Assert.Less(Lit(light, side), 0.001f, "光 " + light + " で位置 " + side + " が明るい");
                }
            }
        }

        [Test]
        public void 正面と真後ろの光ではバイアスが効かない()
        {
            Vector3[] lights =
            {
                new Vector3(0f, 0f, 1f), new Vector3(0f, 0.5f, 1f), new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0.5f, -1f), new Vector3(0f, 3f, -1f),
            };
            foreach (Vector3 light in lights)
            {
                foreach (float side in Sides())
                {
                    Assert.AreEqual(Lit(light, side, 0f), Lit(light, side, 0.3f), 1e-6f, "光 " + light + " / 位置 " + side);
                }
            }
        }

        [Test]
        public void 光を正面から後ろへ回すと顔のどの位置も暗くなる一方()
        {
            var failures = new List<string>();
            foreach (float elevation in new[] { 0f, 30f, 55f })
            {
                foreach (float direction in new[] { 1f, -1f })
                {
                    foreach (float side in Sides())
                    {
                        float previous = float.PositiveInfinity;
                        for (int azimuth = 0; azimuth <= 180; azimuth += 2)
                        {
                            float lit = Lit(LightFrom(direction * azimuth, elevation), side);
                            if (lit > previous + 1e-5f)
                            {
                                failures.Add("仰角 " + elevation + "° 方位 " + direction * azimuth + "° 位置 " + side);
                                break;
                            }

                            previous = lit;
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 光を高くすると顔のどの位置も明るくなる一方()
        {
            var failures = new List<string>();
            for (int azimuth = 0; azimuth < 360; azimuth += 15)
            {
                foreach (float side in Sides())
                {
                    float previous = float.NegativeInfinity;
                    for (int elevation = 0; elevation <= 90; elevation += 2)
                    {
                        float lit = Lit(LightFrom(azimuth, elevation), side);
                        if (lit < previous - 1e-5f)
                        {
                            failures.Add("方位 " + azimuth + "° 仰角 " + elevation + "° 位置 " + side);
                            break;
                        }

                        previous = lit;
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// 太陽は頭上を通る（DayNightCycle）。光が顔の正面や真後ろの左右を跨いでも、明暗が片側からもう片側へ跳ばない。
        /// 境目のぼかしの傾きは最大 1.5 / (2 × 0.03) = 25 なので、0.1° 回したときの変化は 25 × 0.1° ≈ 0.044 に
        /// 光の高さで境目が動く分が乗った程度（式を写して数えると最大 0.048）。明暗が跳ぶなら 1 近く変わる。
        /// </summary>
        [Test]
        public void 光を水平に回しても顔の明暗は跳ばない()
        {
            var failures = new List<string>();
            foreach (float elevation in new[] { 0f, 30f, 60f, 70f, 80f, 88f })
            {
                foreach (float side in Sides(2))
                {
                    float previous = Lit(LightFrom(0f, elevation), side);
                    for (int step = 1; step <= 3600; step++)
                    {
                        float lit = Lit(LightFrom(step * 0.1f, elevation), side);
                        if (Mathf.Abs(lit - previous) > 0.06f)
                        {
                            failures.Add("仰角 " + elevation + "° 方位 " + step * 0.1f + "° 位置 " + side + ": " + previous + " → " + lit);
                            break;
                        }

                        previous = lit;
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 真後ろの高い光では顔の明暗が左右対称で縁から明るくなる()
        {
            foreach (float elevation in new[] { 65f, 70f, 80f, 85f })
            {
                Vector3 light = LightFrom(180f, elevation);
                foreach (float side in Sides())
                {
                    Assert.AreEqual(Lit(light, side), Lit(light, -side), 1e-5f, "仰角 " + elevation + "° 位置 " + side);
                }

                Assert.GreaterOrEqual(Lit(light, 0.96f), Lit(light, 0f) - 1e-6f, "仰角 " + elevation + "° で縁が中央より暗い");
            }

            Vector3 high = LightFrom(180f, 80f);
            Assert.Greater(Lit(high, 0.96f), 0.99f, "仰角 80° の真後ろの光で縁が明るくない");
            Assert.Less(Lit(high, 0f), 0.01f, "仰角 80° の真後ろの光で顔の中央が明るい");
        }

        [Test]
        public void 顔の明暗は頭と光を一緒に回しても変わらない()
        {
            Vector3[] lights =
            {
                new Vector3(1f, 0f, 0f), new Vector3(1f, 0.4f, 0.6f), new Vector3(0.5f, 0.2f, -1f), new Vector3(0.3f, 2f, -1f),
            };
            foreach (float yaw in new[] { 30f, 90f, 180f, 250f })
            {
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                foreach (Vector3 light in lights)
                {
                    Vector3 direction = light.normalized;
                    foreach (float side in Sides())
                    {
                        float expected = ToonLook.FaceLit(
                            Vector3.forward, side, direction, FaceLook.FaceShadowSoftness, FaceLook.FaceShadowBias);
                        float actual = ToonLook.FaceLit(
                            rotation * Vector3.forward, side, rotation * direction, FaceLook.FaceShadowSoftness, FaceLook.FaceShadowBias);
                        Assert.AreEqual(expected, actual, 1e-4f, "向き " + yaw + "° 光 " + light + " 位置 " + side);
                    }
                }
            }
        }

        private const float OutlineWidth = 0.005f;

        private static float OutlinePixels(float distance, float fov = 55f, float maxPixels = 6f, float screenHeight = 1080f)
        {
            return ToonLook.OutlinePixels(distance, OutlineWidth, 5f, 30f, 60f, maxPixels, fov, screenHeight);
        }

        [Test]
        public void 輪郭は5mまで同じ太さでその先は距離に反比例し30mから60mで消える()
        {
            float near = OutlinePixels(5f);
            Assert.AreEqual(5.19f, near, 0.01f, "画角 55°・1080p で 5 m の輪郭が約 5 px でない");
            foreach (float distance in new[] { 1f, 2f, 3f, 4f })
            {
                Assert.AreEqual(near, OutlinePixels(distance), 1e-4f, distance + " m");
            }

            Assert.AreEqual(near / 2f, OutlinePixels(10f), 1e-4f, "10 m");
            Assert.AreEqual(near / 4f, OutlinePixels(20f), 1e-4f, "20 m");
            Assert.AreEqual(near * 5f / 30f, OutlinePixels(30f), 1e-4f, "30 m");
            Assert.AreEqual(near * 5f / 45f * 0.5f, OutlinePixels(45f), 1e-4f, "45 m");
            Assert.AreEqual(0f, OutlinePixels(60f), 1e-6f, "60 m");
            Assert.AreEqual(0f, OutlinePixels(100f), 1e-6f, "100 m");
            Assert.AreEqual(0f, OutlinePixels(0f), "0 m");
        }

        [Test]
        public void 望遠で近づいても輪郭は上限の太さまで()
        {
            Assert.AreEqual(6f, OutlinePixels(3f, 20f), 1e-3f, "画角 20° の 3 m で上限の 6 px に止まらない");
            Assert.AreEqual(15.3f, OutlinePixels(3f, 20f, 0f), 0.1f, "上限なしの太さが想定と違う");
        }

        [Test]
        public void 画面が2倍の高さなら輪郭のpxも2倍()
        {
            Assert.AreEqual(2f * OutlinePixels(5f), OutlinePixels(5f, 55f, 6f, 2160f), 1e-4f);
            Assert.AreEqual(12f, OutlinePixels(3f, 20f, 6f, 2160f), 1e-3f);
        }

        private static Material NewToonMaterial()
        {
            Shader shader = Shader.Find(ToonLook.ShaderName);
            Assert.IsNotNull(shader, ToonLook.ShaderName + " が見つからない");
            return new Material(shader);
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.002f, message);
            Assert.AreEqual(expected.g, actual.g, 0.002f, message);
            Assert.AreEqual(expected.b, actual.b, 0.002f, message);
        }

        [Test]
        public void 顔に当てると頭の向きの陰と肌の陰が入り2度目は何も変えない()
        {
            Material material = NewToonMaterial();
            try
            {
                Assert.IsTrue(ToonLook.Apply(material, "face", Color.white));
                Assert.IsTrue(material.IsKeywordEnabled(ToonLook.FaceShadowKeyword), "顔の陰のキーワードが入っていない");
                Assert.AreEqual(1f, material.GetFloat(ToonLook.FaceShadowId));
                Assert.AreEqual(FaceLook.FaceShadowBias, material.GetFloat(ToonLook.FaceShadowBiasId), 1e-6f);
                AssertColor(ToonLook.SkinShade, material.GetColor(ToonLook.ShadeColorId), "1 段目の陰");
                AssertColor(ToonLook.SkinShade2, material.GetColor(ToonLook.ShadeColor2Id), "2 段目の陰");
                Assert.IsFalse(ToonLook.Apply(material, "face", Color.white), "同じ設定なのに変えたと言う");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void 顔から服に当て直すと顔の陰のキーワードが外れる()
        {
            Material material = NewToonMaterial();
            try
            {
                ToonLook.Apply(material, "face", Color.white);
                var blue = new Color(0.12f, 0.3f, 0.56f, 1f);
                Assert.IsTrue(ToonLook.Apply(material, "cloth_blouse", blue));
                Assert.IsFalse(material.IsKeywordEnabled(ToonLook.FaceShadowKeyword), "顔の陰のキーワードが残る");
                Assert.AreEqual(0f, material.GetFloat(ToonLook.FaceShadowId));
                AssertColor(ToonLook.ClothShade(blue), material.GetColor(ToonLook.ShadeColorId), "1 段目の陰");
                AssertColor(ToonLook.ClothShade2(blue), material.GetColor(ToonLook.ShadeColor2Id), "2 段目の陰");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void 髪に当てると天使の輪が入る()
        {
            Material material = NewToonMaterial();
            try
            {
                var brown = new Color(0.4f, 0.25f, 0.15f, 1f);
                Assert.IsTrue(ToonLook.Apply(material, "hair", brown));
                Assert.AreEqual(ToonLook.LookOf(ToonLook.Role.Hair).HairHighlightIntensity, material.GetFloat(ToonLook.HairHighlightIntensityId), 1e-6f);
                AssertColor(ToonLook.HairHighlight(brown), material.GetColor(ToonLook.HairHighlightColorId), "髪の輪の色");
                AssertColor(ToonLook.ClothShade(brown), material.GetColor(ToonLook.ShadeColorId), "1 段目の陰");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [TestCase("eye_l")]
        [TestCase("outline")]
        public void 目と輪郭には当てない(string name)
        {
            Material material = NewToonMaterial();
            try
            {
                float threshold = material.GetFloat(ToonLook.ShadeThresholdId);
                float byNdotL = material.GetFloat(ToonLook.ShadeByNdotLId);
                Color shade = material.GetColor(ToonLook.ShadeColorId);
                Assert.IsFalse(ToonLook.Apply(material, name, Color.red));
                Assert.AreEqual(threshold, material.GetFloat(ToonLook.ShadeThresholdId));
                Assert.AreEqual(byNdotL, material.GetFloat(ToonLook.ShadeByNdotLId));
                Assert.AreEqual(shade, material.GetColor(ToonLook.ShadeColorId));
                Assert.IsFalse(material.IsKeywordEnabled(ToonLook.FaceShadowKeyword));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void KCD_Toonでないマテリアルには当てない()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(lit, "URP の Lit が見つからない");
            var material = new Material(lit);
            try
            {
                Assert.IsFalse(ToonLook.Apply(material, "face", Color.white));
                Assert.IsFalse(ToonLook.Apply(null, "face", Color.white));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
