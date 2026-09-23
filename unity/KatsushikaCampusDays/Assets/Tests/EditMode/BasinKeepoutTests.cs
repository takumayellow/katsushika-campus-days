using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 水盤（図書館南の池）に入れないこと (#46) と、背景ビルの当たり判定の約束ごと (#50) を守る。
    ///
    /// 同じ数字が Blender 側（blender/kcd_lib/site.py）と Unity 側（Assets/Scripts/Editor の
    /// CampusStage.cs / SeatFactory.cs）の両方に書いてある。片方だけ直すと、見た目は変わらないのに
    /// プレイヤーが水に入れるようになる —— それを静かに通さないための突き合わせ。
    ///
    /// EditMode テストのアセンブリ（KCD.Tests.EditMode.asmdef）は KCD.Runtime しか参照していないので、
    /// Editor のクラスは呼べない。そこでソースを読んで数値を取り出して比べる。
    /// </summary>
    public sealed class BasinKeepoutTests
    {
        /// <summary>ベンチの背もたれの天端（m）。SeatFactory.CreateBench の Back（中心 0.72 + 高さ 0.36 の半分）。</summary>
        private const float BenchBackTopY = 0.90f;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static string ReadRepo(string relative)
        {
            string path = Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), path + " が無い");
            return File.ReadAllText(path);
        }

        private static string ReadAsset(params string[] parts)
        {
            string path = Path.Combine(Application.dataPath, Path.Combine(parts));
            Assert.IsTrue(File.Exists(path), path + " が無い");
            return File.ReadAllText(path);
        }

        private static string CampusStageSource => ReadAsset("Scripts", "Editor", "CampusStage.cs");

        private static float Number(string source, string pattern, string what)
        {
            Match m = Regex.Match(source, pattern, RegexOptions.Multiline);
            Assert.IsTrue(m.Success, what + " を読み取れない（書き方を変えたらこのテストも直す）: " + pattern);
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>site.py の「NAME = 1.23」。</summary>
        private static float PyFloat(string source, string name)
        {
            return Number(source, "^" + name + @"\s*=\s*(-?\d+(?:\.\d+)?)", name);
        }

        /// <summary>site.py の「NAME = (1.0, 2.0)」。</summary>
        private static Vector2 PyPair(string source, string name)
        {
            const string num = @"(-?\d+(?:\.\d+)?)";
            Match m = Regex.Match(source, "^" + name + @"\s*=\s*\(\s*" + num + @"\s*,\s*" + num + @"\s*\)",
                RegexOptions.Multiline);
            Assert.IsTrue(m.Success, name + " を読み取れない");
            return new Vector2(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        /// <summary>C# の「public const float Name = 1.23f;」。</summary>
        private static float CsConst(string source, string name)
        {
            return Number(source, @"public const float " + name + @"\s*=\s*(-?\d+(?:\.\d+)?)f?\s*;", name);
        }

        [Test]
        public void SitePyAndCampusStage_AgreeOnTheBasin()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;

            Vector2 u = PyPair(py, "BASIN_U");
            Vector2 v = PyPair(py, "BASIN_V");
            Assert.AreEqual(u.x, CsConst(cs, "BasinU0"), 1e-4f, "BASIN_U[0] と BasinU0 が違う");
            Assert.AreEqual(u.y, CsConst(cs, "BasinU1"), 1e-4f, "BASIN_U[1] と BasinU1 が違う");
            Assert.AreEqual(v.x, CsConst(cs, "BasinV0"), 1e-4f, "BASIN_V[0] と BasinV0 が違う");
            Assert.AreEqual(v.y, CsConst(cs, "BasinV1"), 1e-4f, "BASIN_V[1] と BasinV1 が違う");

            Assert.AreEqual(PyFloat(py, "Z_BASIN_RIM"), CsConst(cs, "BasinRimTopY"), 1e-4f, "縁石の天端が違う");
            Assert.AreEqual(PyFloat(py, "Z_WATER"), CsConst(cs, "BasinWaterY"), 1e-4f, "水面の高さが違う");

            float rim = Number(py, @"outer\s*=\s*geom\.offset_polygon\(inner,\s*(-?\d+(?:\.\d+)?)\)", "縁石の幅");
            Assert.AreEqual(rim, CsConst(cs, "BasinRimWidth"), 1e-4f, "縁石の幅が違う");

            // 水盤の底は水面より下、かつ一番高い地面（モールの舗装）より上（透けて見える底を石にする）。
            float floor = PyFloat(py, "Z_BASIN_FLOOR");
            Assert.Less(floor, PyFloat(py, "Z_WATER"), "水盤の底が水面より上にある");
            Assert.Greater(floor, PyFloat(py, "Z_MALL"), "水盤の底より舗装のほうが高いと、水越しに芝が見える");
        }

        [Test]
        public void BasinWall_IsTooTallToClimbOrJumpOver()
        {
            string cs = CampusStageSource;
            string player = ReadAsset("Scripts", "Runtime", "Player", "PlayerController.cs");

            float step = Number(player, @"_stepOffset\s*=\s*(-?\d+(?:\.\d+)?)f", "stepOffset");
            float jump = Number(player, @"_jumpHeight\s*=\s*(-?\d+(?:\.\d+)?)f", "ジャンプの高さ");
            float climb = step + jump;

            float top = CsConst(cs, "BasinWallTopY");
            float rim = CsConst(cs, "BasinRimTopY");

            // 水際で一番高い足場は縁石の天端（0.36 m）。Blender 実測で 4 辺 344 点 x 18 オフセットを
            // 調べても、壁の外 0.25..4.5 m にこれより高い面は無かった。
            Assert.Greater(top - rim, climb,
                "縁石（" + rim + " m）から step+jump = " + climb + " m で越えられてしまう");
            Assert.Greater(top - BenchBackTopY, climb,
                "ベンチの背もたれ（" + BenchBackTopY + " m）から越えられてしまう");

            Assert.Less(CsConst(cs, "BasinWallBottomY"), 0f, "壁の下端は地面より下まで伸ばす");

            // 壁は水際を中心に立てる。縁石へ食い込むのは厚みの半分だけで、腰かける幅を残す。
            float thickness = CsConst(cs, "BasinWallThickness");
            Assert.Less(thickness * 0.5f, CsConst(cs, "BasinRimWidth") * 0.5f,
                "壁が太すぎて縁石に腰かけられない");
        }

        [Test]
        public void BasinNavVolume_CoversTheRim_ButLeavesTheDeckWalkable()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;

            float margin = CsConst(cs, "BasinNavMargin");
            Assert.Greater(margin, CsConst(cs, "BasinRimWidth"),
                "縁石の上に NavMesh が残ると、NPC が縁石に上がってそのまま水へ降りる");

            float navTop = CsConst(cs, "BasinWallBottomY") + CsConst(cs, "BasinNavHeight");
            Assert.Greater(navTop, CsConst(cs, "BasinRimTopY"), "除外する箱が縁石の天端より低い");

            // 水盤の北の石張りデッキ（build_basin の deck）は歩けるまま残す。
            Vector2 v = PyPair(py, "BASIN_V");
            float deckStart = v.y + Number(py,
                @"frame\.rect\(BASIN_U\[0\],\s*BASIN_V\[1\]\s*\+\s*(\d+(?:\.\d+)?)", "デッキの南端");
            Assert.Greater(deckStart, v.y + margin,
                "NavMesh の除外がデッキまで届くと、池の北側を NPC が通れなくなる");
        }

        [Test]
        public void PondBenches_StandOnTheDeck_FacingTheWater()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;
            string seats = ReadAsset("Scripts", "Editor", "SeatFactory.cs");

            Vector2 u = PyPair(py, "BASIN_U");
            Vector2 v = PyPair(py, "BASIN_V");
            float deckStart = v.y + Number(py,
                @"frame\.rect\(BASIN_U\[0\],\s*BASIN_V\[1\]\s*\+\s*(\d+(?:\.\d+)?)", "デッキの南端");
            float deckEnd = PyFloat(py, "MALL_V") - PyFloat(py, "MALL_HW");

            const string num = @"(-?\d+(?:\.\d+)?)";
            MatchCollection spots = Regex.Matches(seats,
                @"new BenchSpot\(""(\w+)"",\s*" + num + @"f,\s*" + num + @"f,\s*" + num + @"f,\s*" + num + @"f\)");
            Assert.Greater(spots.Count, 0, "SeatFactory の屋外ベンチ表を読み取れない");

            int pond = 0;
            foreach (Match spot in spots)
            {
                string id = spot.Groups[1].Value;
                float bu = float.Parse(spot.Groups[2].Value, CultureInfo.InvariantCulture);
                float bv = float.Parse(spot.Groups[3].Value, CultureInfo.InvariantCulture);
                float faceV = float.Parse(spot.Groups[5].Value, CultureInfo.InvariantCulture);

                bool insideBasin = bu >= u.x && bu <= u.y && bv >= v.x && bv <= v.y;
                Assert.IsFalse(insideBasin, "ベンチ " + id + " が水盤の内側（水の中）に立っている");

                if (!id.StartsWith("pond"))
                {
                    continue;
                }

                pond++;
                Assert.GreaterOrEqual(bv, deckStart, "ベンチ " + id + " がデッキより南（＝縁石や水の上）にある");
                Assert.LessOrEqual(bv, deckEnd, "ベンチ " + id + " がデッキより北（＝モールの上）にある");
                Assert.GreaterOrEqual(bu, u.x, "ベンチ " + id + " が水盤より西にはみ出している");
                Assert.LessOrEqual(bu, u.y, "ベンチ " + id + " が水盤より東にはみ出している");
                Assert.Less(faceV, bv, "ベンチ " + id + " が水の方を向いていない");

                // 背もたれから跳んでも壁を越えられない距離に置く（水際まで 3.4 m）。
                Assert.Greater(bv - v.y, CsConst(cs, "BasinRimWidth"),
                    "ベンチ " + id + " が縁石に近すぎる");
            }

            Assert.AreEqual(2, pond, "池のベンチは 2 脚");
        }

        [Test]
        public void WaterMesh_IsNamedSoThatCampusStageSkipsIt()
        {
            StringAssert.Contains("MeshBuilder(\"site_water\")", ReadRepo("blender/build_campus.py"),
                "build_campus.py が水面を site_water という名前で出していない");

            string cs = CampusStageSource;
            StringAssert.Contains("Contains(\"water\")", cs,
                "CampusStage.IsWaterMesh が名前で水面を見分けていない");
            StringAssert.Contains("IsWaterMesh(id)", cs, "DressCampus が水面を仕分けていない");
            Assert.IsTrue(Regex.IsMatch(cs, @"IsWaterMesh\(id\)\)\s*\{[\s\S]*?DestroyImmediate\(water\)"),
                "水面の MeshCollider を消していない");
            Assert.IsTrue(Regex.IsMatch(cs, @"IsWaterMesh\(id\)\)\s*\{[\s\S]*?Ignore\(go\);"),
                "水面を NavMesh から外していない");
        }

        [Test]
        public void BackgroundBuildings_GetCollidersWithoutGlass_AndStayOutOfNavMesh()
        {
            string glass = Regex.Match(ReadRepo("blender/kcd_lib/buildings.py"),
                @"def _bg_windows\([^)]*glass\s*=\s*""(glass\w*)""").Groups[1].Value;
            Assert.IsNotEmpty(glass, "背景ビルの窓のマテリアル名を読み取れない");

            string cs = CampusStageSource;
            StringAssert.Contains("StartsWith(\"glass\")", cs,
                "背景ビルの当たり判定から " + glass + " の板を落としていない");
            StringAssert.Contains("IsBackgroundNonSolidMaterial", cs, "背景ビル用の除外条件が無い");
            Assert.IsTrue(Regex.IsMatch(cs, @"backgroundBuilding\)\s*\{[\s\S]*?Ignore\(go\);"),
                "背景ビルを NavMesh から外していない（屋根の上を NPC が歩く）");
        }
    }
}
