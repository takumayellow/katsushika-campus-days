using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内の窓の外の遠景 (#60) の、シーンに依らない決まりごと。
    /// 近景 ext_* の見分け方、今いる建物のドームだけを描く判定、時刻ごとの明るさ（夕方・夜）を守る。
    /// </summary>
    public sealed class InteriorBackdropTests
    {
        private const float Tolerance = 1e-4f;

        private static float Luminance(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, Tolerance, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, Tolerance, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, Tolerance, message + " (b)");
        }

        // --- 近景 ext_* -------------------------------------------------------------------

        [TestCase("ext_library")]
        [TestCase("ext_library_trees")]
        [TestCase("EXT_gym")]
        [TestCase("Ext_dorm_trees")]
        public void 近景の名前はext_で始まる(string name)
        {
            Assert.IsTrue(InteriorBackdrop.IsExteriorDressing(name), name);
        }

        [TestCase("wall_library")]
        [TestCase("floor_library")]
        [TestCase("furn_library_03_stacks")]
        [TestCase("text_panel")]
        [TestCase("sign_ext_library")]
        [TestCase("ext")]
        [TestCase("")]
        [TestCase(null)]
        public void 屋内のメッシュは近景とみなさない(string name)
        {
            Assert.IsFalse(InteriorBackdrop.IsExteriorDressing(name), name ?? "null");
        }

        // --- どのドームを描くか -----------------------------------------------------------------

        [Test]
        public void 出入り係が無いエディタでは全部描く()
        {
            Assert.IsTrue(InteriorBackdrop.VisibleFor(false, null, "library"));
            Assert.IsTrue(InteriorBackdrop.VisibleFor(false, "gym", "library"));
        }

        [Test]
        public void 今いる建物のドームだけを描く()
        {
            Assert.IsTrue(InteriorBackdrop.VisibleFor(true, "library", "library"));
            Assert.IsFalse(InteriorBackdrop.VisibleFor(true, "gym", "library"), "別の建物の中");
            Assert.IsFalse(InteriorBackdrop.VisibleFor(true, string.Empty, "library"), "キャンパス（屋外）");
            Assert.IsFalse(InteriorBackdrop.VisibleFor(true, null, "library"), "キャンパス（屋外）");
        }

        // --- 時刻の明るさ ---------------------------------------------------------------------

        [TestCase(7.5f)]
        [TestCase(8.5f)]
        [TestCase(12f)]
        [TestCase(15.5f)]
        public void 昼は撮った画のまま(float hours)
        {
            AssertColor(Color.white, InteriorBackdrop.TintAt(hours), hours + " 時");
        }

        [TestCase(0f)]
        [TestCase(2f)]
        [TestCase(4.5f)]
        [TestCase(19.5f)]
        [TestCase(22f)]
        [TestCase(23.9f)]
        public void 夜は暗い青に沈める(float hours)
        {
            Color tint = InteriorBackdrop.TintAt(hours);
            AssertColor(InteriorBackdrop.NightTint, tint, hours + " 時");
            Assert.Less(Luminance(tint), 0.3f, "夜の窓の外が明るすぎる");
            Assert.Greater(tint.b, tint.r, "夜は青みが勝つ");
        }

        [Test]
        public void 夕方は橙に寄る()
        {
            Color dusk = InteriorBackdrop.TintAt(18f);
            AssertColor(InteriorBackdrop.DuskTint, dusk, "18 時");
            Assert.Greater(dusk.r, dusk.g, "夕方は赤が勝つ");
            Assert.Greater(dusk.g, dusk.b, "夕方は青が弱い");
            Assert.Less(Luminance(dusk), Luminance(Color.white), "夕方は昼より暗い");
            Assert.Greater(Luminance(dusk), Luminance(InteriorBackdrop.NightTint), "夕方は夜より明るい");
        }

        [Test]
        public void 夕方から夜へ少しずつ暗くなる()
        {
            float previous = Luminance(InteriorBackdrop.TintAt(15.5f));
            for (float h = 15.6f; h <= 19.5f; h += 0.1f)
            {
                float now = Luminance(InteriorBackdrop.TintAt(h));
                Assert.LessOrEqual(now, previous + Tolerance, h.ToString("F1") + " 時で明るく戻っている");
                previous = now;
            }
        }

        [Test]
        public void 夜明けに少しずつ明るくなる()
        {
            float previous = Luminance(InteriorBackdrop.TintAt(4.5f));
            for (float h = 4.6f; h <= 7.5f; h += 0.1f)
            {
                float now = Luminance(InteriorBackdrop.TintAt(h));
                Assert.GreaterOrEqual(now, previous - Tolerance, h.ToString("F1") + " 時で暗く戻っている");
                previous = now;
            }
        }

        [Test]
        public void 時刻が飛ばずにつながる()
        {
            // DayNightCycle は 1 日を 12 分で回すので、1 フレームで進むのは 0.01 時にも満たない。
            // 0.01 時ごとの色の差が小さければ、窓の外の明るさがぱっと切り替わることはない。
            Color previous = InteriorBackdrop.TintAt(0f);
            for (int step = 1; step <= 2400; step++)
            {
                Color now = InteriorBackdrop.TintAt(step * 0.01f);
                float jump = Mathf.Max(Mathf.Abs(now.r - previous.r),
                    Mathf.Max(Mathf.Abs(now.g - previous.g), Mathf.Abs(now.b - previous.b)));
                Assert.Less(jump, 0.01f, (step * 0.01f).ToString("F2") + " 時で色が飛ぶ");
                previous = now;
            }
        }

        [Test]
        public void 二十四時で折り返す()
        {
            AssertColor(InteriorBackdrop.TintAt(12f), InteriorBackdrop.TintAt(36f), "36 時 = 12 時");
            AssertColor(InteriorBackdrop.TintAt(23f), InteriorBackdrop.TintAt(-1f), "-1 時 = 23 時");
            AssertColor(InteriorBackdrop.TintAt(0f), InteriorBackdrop.TintAt(24f), "24 時 = 0 時");
        }
    }
}
