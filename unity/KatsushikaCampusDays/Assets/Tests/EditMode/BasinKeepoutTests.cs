using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 水盤（図書館を囲む堀、#46 / #56）に入れないことと、背景ビルの当たり判定の約束ごと (#50) を守る。
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

        /// <summary>C# の「public const float Name = 1.23f;」。</summary>
        private static float CsConst(string source, string name)
        {
            return Number(source, @"public const float " + name + @"\s*=\s*(-?\d+(?:\.\d+)?)f?\s*;", name);
        }

        private const string Num = @"(-?\d+(?:\.\d+)?)";

        /// <summary>site.py の「BASINS = [(u0, v0, u1, v1), ...]」。</summary>
        private static List<Vector4> PyBasins(string py)
        {
            Match list = Regex.Match(py, @"^BASINS\s*=\s*\[([\s\S]*?)\]", RegexOptions.Multiline);
            Assert.IsTrue(list.Success, "site.py の BASINS を読み取れない");
            return Rects(list.Groups[1].Value,
                @"\(\s*" + Num + @"\s*,\s*" + Num + @"\s*,\s*" + Num + @"\s*,\s*" + Num + @"\s*\)");
        }

        /// <summary>CampusStage.cs の「Basins = { new Vector4(u0, v0, u1, v1), ... };」。</summary>
        private static List<Vector4> CsBasins(string cs)
        {
            Match list = Regex.Match(cs, @"Vector4\[\]\s*Basins\s*=\s*\{([\s\S]*?)\};");
            Assert.IsTrue(list.Success, "CampusStage.Basins を読み取れない");
            return Rects(list.Groups[1].Value,
                @"new Vector4\(\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*\)");
        }

        private static List<Vector4> Rects(string body, string pattern)
        {
            var rects = new List<Vector4>();
            foreach (Match m in Regex.Matches(body, pattern))
            {
                rects.Add(new Vector4(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value)));
            }

            Assert.Greater(rects.Count, 0, "水盤の矩形が 1 つも読めない");
            return rects;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        [Test]
        public void SitePyAndCampusStage_AgreeOnTheBasin()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;

            List<Vector4> blender = PyBasins(py);
            List<Vector4> unity = CsBasins(cs);
            Assert.AreEqual(blender.Count, unity.Count, "水盤の矩形の数が site.py と CampusStage.cs で違う");
            for (int i = 0; i < blender.Count; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Assert.AreEqual(blender[i][k], unity[i][k], 1e-4f,
                        "BASINS[" + i + "][" + k + "] と Basins[" + i + "] が違う");
                }

                Assert.Less(blender[i].x, blender[i].z, "BASINS[" + i + "] の u0 < u1 になっていない");
                Assert.Less(blender[i].y, blender[i].w, "BASINS[" + i + "] の v0 < v1 になっていない");
            }

            Assert.AreEqual(PyFloat(py, "BASIN_RIM"), CsConst(cs, "BasinRimWidth"), 1e-4f, "縁石の幅が違う");
            Assert.AreEqual(PyFloat(py, "Z_BASIN_RIM"), CsConst(cs, "BasinRimTopY"), 1e-4f, "縁石の天端が違う");
            Assert.AreEqual(PyFloat(py, "Z_WATER"), CsConst(cs, "BasinWaterY"), 1e-4f, "水面の高さが違う");

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
        public void BasinNavVolume_CoversTheRim_ButLeavesTheMallWalkable()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;

            float margin = CsConst(cs, "BasinNavMargin");
            Assert.Greater(margin, CsConst(cs, "BasinRimWidth"),
                "縁石の上に NavMesh が残ると、NPC が縁石に上がってそのまま水へ降りる");

            float navTop = CsConst(cs, "BasinWallBottomY") + CsConst(cs, "BasinNavHeight");
            Assert.Greater(navTop, CsConst(cs, "BasinRimTopY"), "除外する箱が縁石の天端より低い");

            // 東の堀はモールで途切れる。除外する箱がモールの舗装に深く掛かると、モールを NPC が通れなくなる。
            float mallV = PyFloat(py, "MALL_V");
            float mallHw = PyFloat(py, "MALL_HW");
            float mall0 = mallV - mallHw;
            float mall1 = mallV + mallHw;
            float u0 = PyFloat(py, "MALL_U0");
            float u1 = PyFloat(py, "MALL_U1");
            foreach (Vector4 r in PyBasins(py))
            {
                bool acrossMall = r.x - margin < u1 && r.z + margin > u0;
                if (!acrossMall)
                {
                    continue;
                }

                float overlap = Mathf.Min(r.w + margin, mall1) - Mathf.Max(r.y - margin, mall0);
                Assert.LessOrEqual(overlap, 0.2f,
                    "水盤 " + r + " の NavMesh の除外がモールに " + overlap + " m 掛かる");
            }
        }

        [Test]
        public void PondBenches_StandOnTheEastBank_FacingTheWater()
        {
            string py = ReadRepo("blender/kcd_lib/site.py");
            string cs = CampusStageSource;
            string seats = ReadAsset("Scripts", "Editor", "SeatFactory.cs");

            List<Vector4> basins = PyBasins(py);
            float rimWidth = CsConst(cs, "BasinRimWidth");

            MatchCollection spots = Regex.Matches(seats,
                @"new BenchSpot\(""(\w+)"",\s*" + Num + @"f,\s*" + Num + @"f,\s*" + Num + @"f,\s*" + Num + @"f\)");
            Assert.Greater(spots.Count, 0, "SeatFactory の屋外ベンチ表を読み取れない");

            int pond = 0;
            foreach (Match spot in spots)
            {
                string id = spot.Groups[1].Value;
                float bu = F(spot.Groups[2].Value);
                float bv = F(spot.Groups[3].Value);
                float faceU = F(spot.Groups[4].Value);
                float faceV = F(spot.Groups[5].Value);

                foreach (Vector4 r in basins)
                {
                    bool onWaterOrRim = bu >= r.x - rimWidth && bu <= r.z + rimWidth
                        && bv >= r.y - rimWidth && bv <= r.w + rimWidth;
                    Assert.IsFalse(onWaterOrRim, "ベンチ " + id + " が水盤 " + r + " の水か縁石の上に立っている");
                }

                if (!id.StartsWith("pond"))
                {
                    continue;
                }

                pond++;

                // 東岸の芝生から堀（-u）に正対する。
                Assert.Less(faceU, bu, "ベンチ " + id + " が堀の方（-u）を向いていない");
                Assert.AreEqual(bv, faceV, 1e-4f, "ベンチ " + id + " が堀に正対していない");
                List<Vector4> ahead = basins.FindAll(r => bv >= r.y && bv <= r.w && r.z < bu);
                Assert.AreEqual(1, ahead.Count, "ベンチ " + id + " の正面（-u）に堀が無い");

                // 背もたれから跳んでも壁を越えられない距離に置く（水際まで 4 m）。
                Assert.Greater(bu - ahead[0].z, rimWidth + 1f, "ベンチ " + id + " が縁石に近すぎる");
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
