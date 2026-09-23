using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 裏エンド「寮でぐーたら」(#41) の数値と純関数。
    /// Unity を起動せずに確かめられるよう、幾何と文面はすべて DormRoute / DormEnding / DormEntrance の
    /// 静的メソッドに切り出してある。ここは EditMode なので KCD.Runtime しか見えない
    /// （RouteStage / DormStage は Editor アセンブリなので、そちらの数値はここで押さえた定数に頼る）。
    /// </summary>
    public sealed class DormEndingTests
    {
        private static readonly Rect Campus = WorldBounds.DefaultArea;

        // 壁の寸法。WorldBoundsStage（Editor）の同名の定数と同じ値。
        private const float WallThickness = 2f;
        private const float WallBottom = -2f;
        private const float WallTop = 6f;

        // ---- 区画と門の幾何 ----

        [Test]
        public void DefaultAnnex_HoldsTheDormAndTheRoute()
        {
            Rect annex = DormRoute.DefaultAnnex;

            // 寮の footprint（route.json dormitory.bbox）x -474.92..-448.11 / z 180.35..225.43。
            Assert.Less(annex.xMin, -474.92f, "寮の西端が区画からはみ出す");
            Assert.Greater(annex.xMax, -448.11f, "寮の東端が区画からはみ出す");
            Assert.Less(annex.yMin, 180.35f, "寮の南端が区画からはみ出す");
            Assert.Greater(annex.yMax, 225.43f, "寮の北端が区画からはみ出す");

            // 経路が西の壁を抜けてから寮に着くまで（x -451.79 まで / z 175.57..243.02）。
            Assert.Less(annex.xMin, -451.79f, "経路の西端が区画からはみ出す");
            Assert.Less(annex.yMin, 175.57f, "経路の南端が区画からはみ出す");
            Assert.Greater(annex.yMax, 243.02f, "経路の北端が区画からはみ出す");
        }

        [Test]
        public void DefaultAnnex_TouchesTheCampusWall()
        {
            // 区画の東端とキャンパスの西端がずれると、門の脇の角に隙間ができて落ちられる。
            Assert.IsTrue(DormRoute.AnnexTouchesCampus(Campus, DormRoute.DefaultAnnex));
        }

        [Test]
        public void DefaultGate_FitsTheWallAndOpensIntoTheAnnex()
        {
            Assert.IsTrue(DormRoute.GateFitsWall(Campus, DormRoute.DefaultGateZ, DormRoute.DefaultGateHalfWidth),
                "門が西の壁の南北からはみ出している");
            Assert.IsTrue(
                DormRoute.GateOpensIntoAnnex(DormRoute.DefaultAnnex, DormRoute.DefaultGateZ,
                    DormRoute.DefaultGateHalfWidth),
                "門の先が増築区画の外を向いている");
        }

        /// <summary>build_route.py が route.fbx と一緒に書き出す寸法。</summary>
        private static string RouteSidecarPath =>
            Path.Combine(Application.dataPath, "Models", "Campus", "route.json");

        /// <summary>折れ線が x = wallX を横切る z。横切らなければ NaN。</summary>
        private static float CrossingZ(List<object> points, float wallX)
        {
            Vector2 prev = ToPoint(points[0]);
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 cur = ToPoint(points[i]);
                if ((prev.x - wallX) * (cur.x - wallX) <= 0f && !Mathf.Approximately(prev.x, cur.x))
                {
                    float t = (wallX - prev.x) / (cur.x - prev.x);
                    if (t >= 0f && t <= 1f)
                    {
                        return Mathf.Lerp(prev.y, cur.y, t);
                    }
                }

                prev = cur;
            }

            return float.NaN;
        }

        private static Vector2 ToPoint(object raw)
        {
            var pair = raw as List<object>;
            Assert.IsNotNull(pair, "経路の点が [x, z] の配列になっていない");
            Assert.AreEqual(2, pair.Count, "経路の点が 2 つの数になっていない");
            return new Vector2(System.Convert.ToSingle(pair[0]), System.Convert.ToSingle(pair[1]));
        }

        [Test]
        public void DefaultGate_SitsWhereTheRouteCrossesTheWall()
        {
            // ここは以前 DefaultGateZ（239.83）を定数 239.83 と比べていた。DefaultGateZ を
            // どんな値に変えても必ず通る恒真の検査で、門と経路がずれても気づけなかった。
            // route.json に入っている経路そのものを読み、x = キャンパスの西端 を横切る z を
            // 自分で求めて、門がそこを跨いでいるかを見る。
            Assert.IsTrue(File.Exists(RouteSidecarPath),
                RouteSidecarPath + " が無い。blender/build_route.py を流し直すこと");

            string text = File.ReadAllText(RouteSidecarPath);
            var node = MiniJson.Deserialize(text) as Dictionary<string, object>;
            Assert.IsNotNull(node, "route.json がオブジェクトとして読めない");

            var notes = MiniJson.GetObject(node, "notes");
            Assert.IsNotNull(notes, "route.json に notes が無い");

            List<object> points = MiniJson.GetArray(notes, "route_nw_gate");
            Assert.IsNotNull(points, "route.json に notes.route_nw_gate が無い");
            Assert.Greater(points.Count, 1, "経路の点が足りない");

            float crossing = CrossingZ(points, Campus.xMin);
            Assert.IsFalse(float.IsNaN(crossing),
                "経路が x = " + Campus.xMin + " を横切っていない（門を開ける場所が決まらない）");

            // 既定値（sidecar が読めないときに使う数）で跨げていること。
            Assert.Less(DormRoute.DefaultGateZ - DormRoute.DefaultGateHalfWidth, crossing,
                "門の南端が経路の横断点より北にある");
            Assert.Greater(DormRoute.DefaultGateZ + DormRoute.DefaultGateHalfWidth, crossing,
                "門の北端が経路の横断点より南にある");

            // 実際に使う値（sidecar 優先）でも同じこと。
            DormRoute.Info info = DormRoute.FromJson(text);
            Assert.AreEqual(crossing, info.GateZ, 0.02f, "sidecar の gate_z が経路の横断点とずれている");
            Assert.Less(info.GateZ - info.GateHalfWidth, crossing);
            Assert.Greater(info.GateZ + info.GateHalfWidth, crossing);
        }

        [Test]
        public void WatchArea_CoversBothTheCampusAndTheAnnex()
        {
            Rect area = DormRoute.WatchArea(Campus, DormRoute.DefaultAnnex);

            Assert.AreEqual(DormRoute.DefaultAnnexXMin, area.xMin, 0.001f, "西へ広がっていない");
            Assert.AreEqual(Campus.xMax, area.xMax, 0.001f, "東は変わらない");
            Assert.AreEqual(Campus.yMin, area.yMin, 0.001f, "南は変わらない");
            Assert.AreEqual(Campus.yMax, area.yMax, 0.001f, "北は変わらない");

            // 門の外（寮の玄関）と寮の重心が見張りの範囲に入っている。入っていないと引き戻される。
            Assert.IsTrue(WorldBounds.IsWithin(new Vector3(DormRoute.DefaultDoorX, 0f, DormRoute.DefaultDoorZ), area));
            Assert.IsTrue(
                WorldBounds.IsWithin(new Vector3(DormRoute.DefaultCentroidX, 0f, DormRoute.DefaultCentroidZ), area));
        }

        [Test]
        public void WatchArea_StillExcludesTheInteriorSlots()
        {
            // 屋内は x = 1200 以降、寮の屋内は x = 4000。見張りの範囲に入ると安全な位置として覚えてしまう。
            Rect area = DormRoute.WatchArea(Campus, DormRoute.DefaultAnnex);
            Assert.Less(area.xMax, 1200f);
        }

        [Test]
        public void SplitWestWall_LeavesAGapExactlyAtTheGate()
        {
            Assert.IsTrue(DormRoute.SplitWestWall(Campus, DormRoute.DefaultGateZ, DormRoute.DefaultGateHalfWidth,
                WallThickness, WallBottom, WallTop,
                out DormRoute.WallSlab south, out DormRoute.WallSlab north));

            float gateMin = DormRoute.DefaultGateZ - DormRoute.DefaultGateHalfWidth;
            float gateMax = DormRoute.DefaultGateZ + DormRoute.DefaultGateHalfWidth;

            // 2 枚とも x はもとの Wall_West と同じ（内側の面が矩形の縁に来る）。
            float expectedX = Campus.xMin - WallThickness * 0.5f;
            Assert.AreEqual(expectedX, south.Center.x, 0.001f);
            Assert.AreEqual(expectedX, north.Center.x, 0.001f);

            // 南の壁は矩形の南端から門の南端まで、北の壁は門の北端から矩形の北端まで。
            Assert.AreEqual(Campus.yMin, south.Center.z - south.Size.z * 0.5f, 0.001f);
            Assert.AreEqual(gateMin, south.Center.z + south.Size.z * 0.5f, 0.001f);
            Assert.AreEqual(gateMax, north.Center.z - north.Size.z * 0.5f, 0.001f);
            Assert.AreEqual(Campus.yMax, north.Center.z + north.Size.z * 0.5f, 0.001f);

            // 開いた幅は門の幅ちょうど。
            Assert.AreEqual(DormRoute.DefaultGateHalfWidth * 2f,
                (north.Center.z - north.Size.z * 0.5f) - (south.Center.z + south.Size.z * 0.5f), 0.001f);

            // 高さはもとの壁と同じ。
            Assert.AreEqual(WallTop - WallBottom, south.Size.y, 0.001f);
            Assert.AreEqual((WallTop + WallBottom) * 0.5f, south.Center.y, 0.001f);
        }

        [Test]
        public void SplitWestWall_RefusesAGateThatFallsOffTheWall()
        {
            Assert.IsFalse(DormRoute.SplitWestWall(Campus, Campus.yMax + 10f, 12f,
                WallThickness, WallBottom, WallTop, out _, out _), "壁の北の外の門");
            Assert.IsFalse(DormRoute.SplitWestWall(Campus, 0f, 0f,
                WallThickness, WallBottom, WallTop, out _, out _), "幅 0 の門");
        }

        [Test]
        public void AnnexWalls_CloseThreeSidesAndOverlapTheCampusWall()
        {
            Rect annex = DormRoute.DefaultAnnex;
            DormRoute.WallSlab[] slabs = DormRoute.AnnexWalls(annex, WallThickness, WallBottom, WallTop);

            Assert.AreEqual(3, slabs.Length, "東はキャンパスの西の壁が兼ねるので 3 枚");
            for (int i = 0; i < slabs.Length; i++)
            {
                Assert.AreEqual(DormRoute.AnnexWallNames[i], slabs[i].Name);
                Assert.AreEqual(WallTop - WallBottom, slabs[i].Size.y, 0.001f);
            }

            // 南北の壁は西へ厚さぶん伸びて西の壁の角を覆い、東は区画の東端でぴったり止まる。
            // 東へも伸ばすと、キャンパスの内側へ厚さぶん（2 m）はみ出して、増築区画からは
            // 見えない当たり判定の角柱がキャンパスの中に 2 本立つ。
            foreach (int i in new[] { 1, 2 })
            {
                DormRoute.WallSlab slab = slabs[i];
                float eastEnd = slab.Center.x + slab.Size.x * 0.5f;
                float westEnd = slab.Center.x - slab.Size.x * 0.5f;
                Assert.AreEqual(annex.xMax, eastEnd, 0.001f, slab.Name + " の東端が区画の東端と違う");
                Assert.AreEqual(annex.xMin - WallThickness, westEnd, 0.001f,
                    slab.Name + " の西端が西の壁の外面まで届いていない");
                Assert.LessOrEqual(eastEnd, Campus.xMin + 0.001f,
                    slab.Name + " がキャンパスの内側へはみ出している");
            }

            // 角に隙間はできない。Wall_West は内側の面が Campus.xMin に来るので
            // x = Campus.xMin - 厚さ .. Campus.xMin を占め、南北の壁の東端と必ず重なる。
            DormRoute.WallSlab north = slabs[1];
            Assert.GreaterOrEqual(north.Center.x + north.Size.x * 0.5f, Campus.xMin - WallThickness,
                "北の壁がキャンパスの西の壁まで届いていない");

            // 西の壁は区画の西端のすぐ外。
            DormRoute.WallSlab west = slabs[0];
            Assert.AreEqual(annex.xMin, west.Center.x + west.Size.x * 0.5f, 0.001f);
        }

        [Test]
        public void AnnexWalls_LeaveTheDormAndTheDoorInside()
        {
            Rect annex = DormRoute.DefaultAnnex;
            var door = new Vector2(DormRoute.DefaultDoorX, DormRoute.DefaultDoorZ);

            // 玄関のトリガー箱（外へ 1.6 m）ぶんの余裕を見ても壁に埋まらない。
            Assert.IsTrue(DormRoute.Contains(annex, door, 3f), "玄関が壁に近すぎる");
        }

        [Test]
        public void OutwardFromBearing_PointsSouthSouthEastForTheDormDoor()
        {
            // route.json の facing_bearing 159.5 度（南南東）。0 度が +z で時計回り。
            Vector3 outward = DormRoute.OutwardFromBearing(DormRoute.DefaultDoorBearing);

            Assert.AreEqual(1f, outward.magnitude, 0.001f, "単位ベクトルでない");
            Assert.AreEqual(0f, outward.y, 0.001f, "水平でない");
            Assert.Greater(outward.x, 0f, "東へ向いていない");
            Assert.Less(outward.z, 0f, "南へ向いていない");

            // 真北・真東も確かめる。
            Assert.AreEqual(Vector3.forward.z, DormRoute.OutwardFromBearing(0f).z, 0.001f);
            Assert.AreEqual(Vector3.right.x, DormRoute.OutwardFromBearing(90f).x, 0.001f);
        }

        // ---- sidecar（route.json）の読み取り ----

        [Test]
        public void FromJson_FallsBackToTheMeasuredDefaults()
        {
            foreach (string text in new[] { null, string.Empty, "{}", "これは JSON ではない" })
            {
                DormRoute.Info info = DormRoute.FromJson(text);
                Assert.AreEqual(DormRoute.DefaultDoorX, info.Door.x, 0.001f, "入力: " + (text ?? "null"));
                Assert.AreEqual(DormRoute.DefaultGateZ, info.GateZ, 0.001f, "入力: " + (text ?? "null"));
                Assert.AreEqual(DormRoute.DefaultAnnexXMin, info.Annex.xMin, 0.001f, "入力: " + (text ?? "null"));
            }
        }

        [Test]
        public void FromJson_TakesTheKeysItFinds()
        {
            const string text = "{\"door\":[-450.0,190.0],\"door_bearing\":150.0,"
                + "\"centroid\":[-460.0,200.0],\"gate_z\":235.0,\"gate_half_width\":10.0,"
                + "\"annex\":[-480.0,160.0,-340.0,260.0]}";

            DormRoute.Info info = DormRoute.FromJson(text);

            Assert.AreEqual(-450f, info.Door.x, 0.001f);
            Assert.AreEqual(190f, info.Door.y, 0.001f);
            Assert.AreEqual(150f, info.DoorBearing, 0.001f);
            Assert.AreEqual(235f, info.GateZ, 0.001f);
            Assert.AreEqual(10f, info.GateHalfWidth, 0.001f);
            Assert.AreEqual(-480f, info.Annex.xMin, 0.001f);
            Assert.AreEqual(260f, info.Annex.yMax, 0.001f);
        }

        [Test]
        public void FromJson_RejectsValuesThatWouldBreakTheAnnex()
        {
            // 玄関が区画の外。丸ごと捨てて既定値に戻す（中途半端に混ぜない）。
            DormRoute.Info info = DormRoute.FromJson("{\"door\":[0.0,0.0]}");
            Assert.AreEqual(DormRoute.DefaultDoorX, info.Door.x, 0.001f);

            // 門が区画の南北の外。
            info = DormRoute.FromJson("{\"gate_z\":300.0}");
            Assert.AreEqual(DormRoute.DefaultGateZ, info.GateZ, 0.001f);
        }

        [Test]
        public void Default_IsPlausible()
        {
            Assert.IsTrue(DormRoute.IsPlausible(DormRoute.Default));
        }

        // ---- 入口 ----

        [Test]
        public void CanEnter_WaitsAfterComingBackOut()
        {
            Assert.IsFalse(DormEntrance.CanEnter(true, 999f), "屋内にいる間は入れない");
            Assert.IsFalse(DormEntrance.CanEnter(false, 0f), "出た直後は入れない");
            Assert.IsFalse(DormEntrance.CanEnter(false, DormEntrance.ReenterGuard - 0.1f));
            Assert.IsTrue(DormEntrance.CanEnter(false, DormEntrance.ReenterGuard));
        }

        [Test]
        public void ReenterGuard_MatchesTheOtherEntrances()
        {
            // EntranceTrigger は LastExitAt から 3 秒待つ。片方だけ短いと寮の出口で反復横跳びできる。
            Assert.AreEqual(3f, DormEntrance.ReenterGuard, 0.001f);
        }

        [Test]
        public void InWalkInZone_OnlyJustOutsideTheDoor()
        {
            float half = DormEntrance.WalkInHalfWidth;

            Assert.IsTrue(DormEntrance.InWalkInZone(new Vector3(0f, 0f, 0.5f), half), "扉の正面");
            Assert.IsTrue(DormEntrance.InWalkInZone(new Vector3(half, 0f, 0f), half), "開口の端");
            Assert.IsFalse(DormEntrance.InWalkInZone(new Vector3(0f, 0f, 2f), half), "まだ遠い");
            Assert.IsFalse(DormEntrance.InWalkInZone(new Vector3(half + 0.1f, 0f, 0f), half), "開口の外");
            Assert.IsFalse(DormEntrance.InWalkInZone(Vector3.zero, 0f), "半幅 0 なら歩き入りを切る");
        }

        [Test]
        public void FacesDoor_IgnoresPassersBy()
        {
            Vector3 outward = DormRoute.OutwardFromBearing(DormRoute.DefaultDoorBearing);

            Assert.IsTrue(DormEntrance.FacesDoor(-outward, outward, 0.5f), "扉に正対");
            Assert.IsFalse(DormEntrance.FacesDoor(outward, outward, 0.5f), "背を向けている");
            Assert.IsFalse(DormEntrance.FacesDoor(Vector3.Cross(outward, Vector3.up), outward, 0.5f), "横切るだけ");
            Assert.IsFalse(DormEntrance.FacesDoor(Vector3.zero, outward, 0.5f), "向きが無い");

            // 上下の傾きは見ない（坂の上から近づいても入れる）。
            Assert.IsTrue(DormEntrance.FacesDoor(-outward + Vector3.up * 5f, outward, 0.5f));
        }

        [Test]
        public void InwardYaw_LooksIntoTheBuilding()
        {
            // 外向きが真南（方位 180 度）なら、中へ向く yaw は 0 度（+z）。
            Vector3 outward = DormRoute.OutwardFromBearing(180f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(DormEntrance.InwardYaw(outward), 0f), 0.01f);

            // 玄関の向き（159.5 度）なら中へは -20.5 度。
            outward = DormRoute.OutwardFromBearing(DormRoute.DefaultDoorBearing);
            Assert.AreEqual(0f,
                Mathf.DeltaAngle(DormEntrance.InwardYaw(outward), DormRoute.DefaultDoorBearing - 180f), 0.01f);
        }

        // ---- 裏エンドの画面 ----

        [Test]
        public void ShouldPlay_NeedsTheFlagAndPlaysOnlyOnce()
        {
            Assert.IsFalse(DormEnding.ShouldPlay(false, false), "フラグが立つ前");
            Assert.IsTrue(DormEnding.ShouldPlay(false, true), "フラグが立った");
            // フラグは一度立つと消えないので、出したあとに掛けなおす掛け金が要る。
            Assert.IsFalse(DormEnding.ShouldPlay(true, true), "二度目は出さない");
        }

        [Test]
        public void ShouldReturnToTitle_OnlyOnTheEnterThatClosesTheResult()
        {
            // 結果を出している最中の決定だけがタイトルへ戻る。
            Assert.IsTrue(DormEnding.ShouldReturnToTitle(true, DormEnding.InputGuardSeconds, true));

            // 出していないのに戻ると、暗転の途中やシーン破棄の片付け（OnDisable）で
            // タイトルを読み直してしまう (#53)。
            Assert.IsFalse(DormEnding.ShouldReturnToTitle(false, 99f, true), "出していないのに戻る");

            // 押していないフレームでは何も起きない。
            Assert.IsFalse(DormEnding.ShouldReturnToTitle(true, 99f, false), "押していないのに戻る");
        }

        [Test]
        public void ShouldReturnToTitle_ThrowsAwayTheEnterThatEndedTheConversation()
        {
            // 会話を送った Enter がそのまま結果画面を閉じると、読む間もなくタイトルへ飛ぶ。
            Assert.IsFalse(DormEnding.ShouldReturnToTitle(true, 0f, true), "出した瞬間");
            Assert.IsFalse(DormEnding.ShouldReturnToTitle(true, DormEnding.InputGuardSeconds - 0.01f, true));
            Assert.IsTrue(DormEnding.ShouldReturnToTitle(true, DormEnding.InputGuardSeconds, true), "境目は受け付ける");

            // 入力よけは時間を止めていても進む秒数（unscaledTime）で数える。
            // timeScale 0 のまま scaled で数えると永遠に閉じられない。
            Assert.Greater(DormEnding.InputGuardSeconds, 0f);
        }

        [Test]
        public void ChoiceText_PromisesWhatCloseActuallyDoes()
        {
            // 案内は「Enter でタイトルへ」。Close が GameManager.ReturnToTitle を呼ぶのと食い違わないこと。
            // 食い違っていたのが #53 の入口（案内だけタイトル、実装はその場に留まる）。
            Assert.IsTrue(DormEnding.ChoiceText(false).Contains("タイトル"), DormEnding.ChoiceText(false));
            Assert.IsTrue(DormEnding.ChoiceText(true).ToLowerInvariant().Contains("title"),
                DormEnding.ChoiceText(true));
        }

        [Test]
        public void FormatClock_ShowsTwoDigitsAndWrapsAtMidnight()
        {
            Assert.AreEqual("08:30", DormEnding.FormatClock(8.5f));
            Assert.AreEqual("00:00", DormEnding.FormatClock(0f));
            Assert.AreEqual("09:00", DormEnding.FormatClock(9f));
            Assert.AreEqual("14:37", DormEnding.FormatClock(14f + 37f / 60f));
            Assert.AreEqual("23:59", DormEnding.FormatClock(23f + 59f / 60f));
            Assert.AreEqual("00:00", DormEnding.FormatClock(24f), "24 時は 0 時");
            Assert.AreEqual("23:00", DormEnding.FormatClock(-1f), "負の時刻も一日ぶん回す");
            Assert.AreEqual("00:00", DormEnding.FormatClock(float.NaN), "NaN でも落ちない");
        }

        [Test]
        public void BodyText_ShowsTheClockAndNoOtherNumber()
        {
            const float hours = 14f + 37f / 60f;

            foreach (bool english in new[] { false, true })
            {
                string body = DormEnding.BodyText(hours, english);
                Assert.IsTrue(body.Contains("14:37"), "時刻が出ていない（english=" + english + "）");

                // 探索率・棟数・所持数のような「集計した数」は出さない。この一日は数えないから。
                string stripped = body.Replace("14:37", string.Empty).Replace("<size=200%>", string.Empty)
                    .Replace("<size=85%>", string.Empty).Replace("</size>", string.Empty);
                foreach (char c in stripped)
                {
                    Assert.IsFalse(char.IsDigit(c), "時刻以外の数字が出ている: " + body);
                }
            }
        }

        [Test]
        public void Texts_DifferBetweenJapaneseAndEnglish()
        {
            Assert.AreNotEqual(DormEnding.HeadingText(false), DormEnding.HeadingText(true));
            Assert.AreNotEqual(DormEnding.ChoiceText(false), DormEnding.ChoiceText(true));
            Assert.IsFalse(string.IsNullOrEmpty(DormEnding.HeadingText(true)));
            Assert.IsFalse(string.IsNullOrEmpty(DormEnding.ChoiceText(true)));
        }

        [Test]
        public void PanelSitsAboveItsOwnFade()
        {
            // 暗転（101）とパネル（102）の重ね順が同じだと、どちらが手前かは生成順まかせになる。
            Assert.Greater(DormEnding.PanelSortingOrder, DormEnding.SortingOrder);
            Assert.Greater(DormEnding.SortingOrder, 100, "InteriorLoader の暗転より手前");
        }

        // ---- 会話データ ----

        private static string DialoguePath =>
            Path.Combine(Application.dataPath, "Data", "Dialogue", DormRoute.DialogueId + ".json");

        [Test]
        public void DormHeadDialogue_SetsTheFlagExactlyOnce()
        {
            var node = MiniJson.Deserialize(File.ReadAllText(DialoguePath)) as Dictionary<string, object>;
            Assert.IsNotNull(node, DialoguePath + " はオブジェクトとして読めない");

            DialogueData data = DialogueData.FromJson(node);
            Assert.AreEqual(DormRoute.DialogueId, data.Id);

            int flagged = 0;
            bool hasFallback = false;
            for (int i = 0; i < data.Topics.Count; i++)
            {
                DialogueTopic topic = data.Topics[i];
                Assert.Greater(topic.Lines.Count, 0, topic.Id + " に行が無い");

                if (topic.SetsFlag == DormRoute.FlagId)
                {
                    flagged++;
                    Assert.IsTrue(topic.Once, "裏エンドを呼ぶ話題は once でないと何度も呼ばれる");
                }

                if (!topic.Once && string.IsNullOrEmpty(topic.RequiresActiveQuest)
                    && string.IsNullOrEmpty(topic.RequiresCompletedQuest))
                {
                    hasFallback = true;
                }
            }

            Assert.AreEqual(1, flagged, DormRoute.FlagId + " を立てる話題はちょうど 1 つ");
            Assert.IsTrue(hasFallback, "話題を使い切ったあとの受け皿が無い");
        }

        [Test]
        public void DormHeadDialogue_WarnsBeforeTheEnding()
        {
            var node = MiniJson.Deserialize(File.ReadAllText(DialoguePath)) as Dictionary<string, object>;
            DialogueData data = DialogueData.FromJson(node);

            // 1 回目に選ばれる話題はフラグを立てない（いきなり一日が終わらない）。
            DialogueTopic first = DialogueSystem.SelectTopic(data, null, new List<string>());
            Assert.IsNotNull(first, "最初に選べる話題が無い");
            Assert.AreNotEqual(DormRoute.FlagId, first.SetsFlag, "初対面でいきなり裏エンドに入る");

            // 2 回目にフラグが立つ。
            var spent = new List<string> { data.Id + "/" + first.Id };
            DialogueTopic second = DialogueSystem.SelectTopic(data, null, spent);
            Assert.IsNotNull(second, "2 回目に選べる話題が無い");
            Assert.AreEqual(DormRoute.FlagId, second.SetsFlag, "2 回目でフラグが立たない");
        }

        [Test]
        public void DormHeadDialogue_DoesNotTouchQuests()
        {
            var node = MiniJson.Deserialize(File.ReadAllText(DialoguePath)) as Dictionary<string, object>;
            DialogueData data = DialogueData.FromJson(node);

            // 寮は本編の集計に混ぜない。クエストを始めたり条件にしたりしない。
            for (int i = 0; i < data.Topics.Count; i++)
            {
                DialogueTopic topic = data.Topics[i];
                Assert.IsTrue(string.IsNullOrEmpty(topic.StartsQuest), topic.Id + " がクエストを始めている");
                Assert.IsTrue(string.IsNullOrEmpty(topic.RequiresActiveQuest), topic.Id + " がクエストを見ている");
                Assert.IsTrue(string.IsNullOrEmpty(topic.RequiresCompletedQuest), topic.Id + " がクエストを見ている");
            }
        }

        [Test]
        public void DormIsNotCountedAsOneOfTheNineBuildings()
        {
            // Assets/Data/Ending/result.json の totals.buildings は 9（本編の屋内の数）。
            // 寮を DayStats.NoteEnter に混ぜると 10/9 になってしまう。
            string path = Path.Combine(Application.dataPath, "Data", "Ending", "result.json");
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");

            var totals = MiniJson.GetObject(node, "totals");
            Assert.IsNotNull(totals, "result.json に totals が無い");
            Assert.AreEqual(9, MiniJson.GetInt(totals, "buildings"), "屋内の数が変わったら寮の扱いを見直すこと");
        }
    }
}
