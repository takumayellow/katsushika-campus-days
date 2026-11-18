using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>エリア外に落ちたときに戻す判定（#40）。</summary>
    public sealed class WorldBoundsTests
    {
        private static readonly Rect Area = WorldBounds.DefaultArea;
        private const float KillY = WorldBounds.DefaultKillY;

        [Test]
        public void DefaultArea_CoversCampusAndStaysOnOuterGround()
        {
            // campus.json の境界 x -173..199 / z -167..101 を包み、地面（±350 m）の内側に収まる。
            Assert.LessOrEqual(Area.xMin, -173f);
            Assert.GreaterOrEqual(Area.xMax, 199f);
            Assert.LessOrEqual(Area.yMin, -167f);
            Assert.GreaterOrEqual(Area.yMax, 101f);
            Assert.Greater(Area.xMin, -350f);
            Assert.Less(Area.xMax, 350f);
            Assert.Greater(Area.yMin, -350f);
            Assert.Less(Area.yMax, 350f);
        }

        [Test]
        public void DefaultArea_ExcludesInteriorSlots()
        {
            // 屋内は x = 1200 m 以降に並ぶ。範囲に入ると屋内の位置を安全な位置として覚えてしまう。
            Assert.Less(Area.xMax, 1200f);
        }

        [Test]
        public void IsWithin_UsesXAndZOnly()
        {
            Assert.IsTrue(WorldBounds.IsWithin(new Vector3(0f, 0f, 0f), Area));
            Assert.IsTrue(WorldBounds.IsWithin(new Vector3(100f, -100f, -150f), Area), "y は見ない");
            Assert.IsTrue(WorldBounds.IsWithin(new Vector3(339f, 0f, -339f), Area));
            Assert.IsFalse(WorldBounds.IsWithin(new Vector3(341f, 0f, 0f), Area), "東の外");
            Assert.IsFalse(WorldBounds.IsWithin(new Vector3(-341f, 0f, 0f), Area), "西の外");
            Assert.IsFalse(WorldBounds.IsWithin(new Vector3(0f, 0f, 341f), Area), "北の外");
            Assert.IsFalse(WorldBounds.IsWithin(new Vector3(0f, 0f, -341f), Area), "南の外");
        }

        [Test]
        public void ShouldRecover_StaysQuietOnCampus()
        {
            Assert.IsFalse(WorldBounds.ShouldRecover(new Vector3(13f, 0.2f, -33f), Area, KillY, false));
            Assert.IsFalse(WorldBounds.ShouldRecover(new Vector3(-170f, 5f, 100f), Area, KillY, false), "屋上や高台でも戻さない");
            Assert.IsFalse(WorldBounds.ShouldRecover(new Vector3(0f, -5f, 0f), Area, KillY, false), "killY より上なら戻さない");
        }

        [Test]
        public void ShouldRecover_WhenFallenBelowKillY()
        {
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(0f, KillY - 0.01f, 0f), Area, KillY, false));
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(0f, -500f, 0f), Area, KillY, false));
        }

        [Test]
        public void ShouldRecover_WhenOutsideTheArea()
        {
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(345f, 0f, 0f), Area, KillY, false));
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(0f, 0f, -400f), Area, KillY, false));
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(500f, 0f, 500f), Area, KillY, false), "地面の外の空中");
        }

        [Test]
        public void ShouldRecover_NeverWhileInside()
        {
            // 屋内は x ≥ 1200 m にあるので矩形の外だが、屋内にいる間は戻さない。
            Assert.IsFalse(WorldBounds.ShouldRecover(new Vector3(1250f, 0f, 10f), Area, KillY, true));
            Assert.IsFalse(WorldBounds.ShouldRecover(new Vector3(1250f, -100f, 10f), Area, KillY, true));
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(1250f, 0f, 10f), Area, KillY, false),
                "屋内の座標なのに屋外扱い（ロードで状態がずれた）ならキャンパスへ戻す");
        }

        [Test]
        public void ShouldRecover_NaNPositionIsRecovered()
        {
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(float.NaN, 0f, 0f), Area, KillY, false));
            Assert.IsTrue(WorldBounds.ShouldRecover(new Vector3(0f, float.NaN, 0f), Area, KillY, false));
        }

        [Test]
        public void ShouldRecover_FollowsTheAreaWhenWidened()
        {
            // 隠しエンドで範囲を広げたら、同じ場所でも戻さなくなる。
            var wide = new Rect(-800f, -800f, 1600f, 1600f);
            var road = new Vector3(500f, 0f, -300f);
            Assert.IsTrue(WorldBounds.ShouldRecover(road, Area, KillY, false));
            Assert.IsFalse(WorldBounds.ShouldRecover(road, wide, KillY, false));
        }

        [Test]
        public void IsSafe_RequiresGroundOrSeat()
        {
            var spot = new Vector3(13f, 0.2f, -33f);
            Assert.IsTrue(WorldBounds.IsSafe(spot, Area, true, false, false), "接地");
            Assert.IsTrue(WorldBounds.IsSafe(spot, Area, false, true, false), "着席中も覚えてよい");
            Assert.IsFalse(WorldBounds.IsSafe(spot, Area, false, false, false), "ジャンプ中や落下中は覚えない");
        }

        [Test]
        public void IsSafe_RejectsLowOutsideOrIndoor()
        {
            Assert.IsFalse(WorldBounds.IsSafe(new Vector3(0f, WorldBounds.SafeMinY - 0.1f, 0f), Area, true, false, false),
                "地面より深い所は覚えない");
            Assert.IsFalse(WorldBounds.IsSafe(new Vector3(345f, 0f, 0f), Area, true, false, false), "範囲の外");
            Assert.IsFalse(WorldBounds.IsSafe(new Vector3(1250f, 0f, 10f), Area, true, false, true), "屋内");
            Assert.IsFalse(WorldBounds.IsSafe(new Vector3(0f, 0f, 0f), Area, true, false, true), "屋内フラグが立っていれば覚えない");
        }

        [Test]
        public void IsSafe_PositionNeverTriggersRecovery()
        {
            // 安全な位置へ戻した直後にまた発動すると、暗転が繰り返される。
            Assert.Greater(WorldBounds.SafeMinY, KillY);
            Vector3[] spots =
            {
                new Vector3(0f, WorldBounds.SafeMinY + 0.001f, 0f),
                new Vector3(339.9f, 0f, 339.9f),
                new Vector3(-340f, 0f, -340f),
                new Vector3(-173f, 12f, 101f)
            };

            foreach (Vector3 spot in spots)
            {
                if (WorldBounds.IsSafe(spot, Area, true, false, false))
                {
                    Assert.IsFalse(WorldBounds.ShouldRecover(spot, Area, KillY, false), spot.ToString());
                }
            }
        }

        [Test]
        public void ReturnPoint_PrefersLastSafeThenStartThenCenter()
        {
            var safe = new Vector3(20f, 0.2f, -40f);
            var start = new Vector3(5f, 0.15f, 5f);

            Assert.AreEqual(safe, WorldBounds.ReturnPoint(true, safe, true, start, Area, KillY));
            Assert.AreEqual(start, WorldBounds.ReturnPoint(false, safe, true, start, Area, KillY), "安全な位置がまだ無い");

            Vector3 center = WorldBounds.ReturnPoint(false, safe, false, start, Area, KillY);
            Assert.AreEqual(Area.center.x, center.x, 1e-4f);
            Assert.AreEqual(Area.center.y, center.z, 1e-4f);
            Assert.IsFalse(WorldBounds.ShouldRecover(center, Area, KillY, false));
        }

        [Test]
        public void ReturnPoint_SkipsPointsThatWouldTriggerAgain()
        {
            var badSafe = new Vector3(400f, 0f, 0f);
            var badStart = new Vector3(0f, -100f, 0f);
            var start = new Vector3(5f, 0.15f, 5f);

            Assert.AreEqual(start, WorldBounds.ReturnPoint(true, badSafe, true, start, Area, KillY));
            Vector3 fallback = WorldBounds.ReturnPoint(true, badSafe, true, badStart, Area, KillY);
            Assert.IsFalse(WorldBounds.ShouldRecover(fallback, Area, KillY, false));
        }

        [Test]
        public void ReturnYaw_FacesBackTowardTheReturnPoint()
        {
            // 東の外（+x）で落ちて、西（-x）にある安全な位置へ戻ったら、さらに西（内側）を向く。
            Assert.AreEqual(-90f, WorldBounds.ReturnYaw(new Vector3(345f, -20f, 0f), new Vector3(330f, 0f, 0f), 0f), 1e-3f);
            // 北の外で落ちたら南（180°）を向く。
            Assert.AreEqual(180f, Mathf.Abs(WorldBounds.ReturnYaw(new Vector3(0f, -20f, 345f), new Vector3(0f, 0f, 300f), 0f)), 1e-3f);
            // 真下に落ちただけ（水平のずれがほぼ無い）なら元の向きのまま。
            Assert.AreEqual(42f, WorldBounds.ReturnYaw(new Vector3(1f, -20f, 1f), new Vector3(1.05f, 0f, 1f), 42f), 1e-3f);
        }

        [Test]
        public void NextOffCampus_FlipsOnlyBeyondTheMarginAroundTheWestWall()
        {
            float wall = -WorldBounds.HalfExtent;
            float m = WorldBounds.GateMargin;

            Assert.IsTrue(WorldBounds.NextOffCampus(false, wall - m - 0.1f), "門の外へ出た");
            Assert.IsFalse(WorldBounds.NextOffCampus(true, wall + m + 0.1f), "門の内へ戻った");
            Assert.IsFalse(WorldBounds.NextOffCampus(false, wall), "壁の上では前のまま（内）");
            Assert.IsTrue(WorldBounds.NextOffCampus(true, wall), "壁の上では前のまま（外）");
            Assert.IsFalse(WorldBounds.NextOffCampus(false, 0f), "キャンパスの真ん中");
            Assert.IsTrue(WorldBounds.NextOffCampus(false, DormRoute.DefaultDoorX), "寮の玄関は門の外");
            Assert.IsTrue(WorldBounds.NextOffCampus(true, float.NaN), "NaN は前のまま");
        }

        [Test]
        public void ShouldAnnounceGate_OnlyWhenWalkedAcross()
        {
            Assert.IsTrue(WorldBounds.ShouldAnnounceGate(true, false, true, 0.2f), "歩いて出た");
            Assert.IsTrue(WorldBounds.ShouldAnnounceGate(true, true, false, 0.2f), "歩いて戻った");
            Assert.IsFalse(WorldBounds.ShouldAnnounceGate(true, false, false, 0.2f), "変わっていない");
            Assert.IsFalse(WorldBounds.ShouldAnnounceGate(false, false, true, 0f), "最初の判定（ロード直後）");
            Assert.IsFalse(WorldBounds.ShouldAnnounceGate(true, false, true, WorldBounds.TeleportDistance + 1f),
                "移された（屋内から出た・戻し）");
        }

        [Test]
        public void GateToasts_HaveTextInEveryDictionary()
        {
            foreach (string key in new[] { "ui.hud.left_campus", "ui.hud.back_on_campus" })
            {
                foreach (string lang in new[] { "ja", "en" })
                {
                    string path = System.IO.Path.Combine(Application.dataPath, "Data", "Localization", lang + ".json");
                    StringAssert.Contains("\"" + key + "\":", System.IO.File.ReadAllText(path), lang + " に " + key);
                }
            }
        }
    }
}
