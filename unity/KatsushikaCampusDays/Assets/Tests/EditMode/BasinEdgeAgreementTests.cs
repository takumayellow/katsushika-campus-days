using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 堀の水際の辺（縁石と見えない壁を立てる所）は、Blender 側（blender/kcd_lib/site.py の basin_edges）と
    /// Unity 側（CampusStage.BasinEdges）の 2 か所で計算している (#68)。両方が同じ辺を出すことを守る。
    ///
    /// blender/tests/fixtures/basin_edges.json は basin_edges の答え（blender/tests/basin_fixture.py が書き、
    /// pytest が今の site.py の答えと同じかを見る）。ここでは同じ矩形を CampusStage.BasinEdges に通し、
    /// 同じ辺が同じ順で出るかを見る。縁石の端を延ばすか（ext0 / ext1）は Blender 側だけが持つので比べない。
    ///
    /// このアセンブリ（KCD.Tests.EditMode）は KCD.Editor を参照していないので、CampusStage は
    /// リフレクションで呼ぶ。
    /// </summary>
    public sealed class BasinEdgeAgreementTests
    {
        private const string FixturePath = "blender/tests/fixtures/basin_edges.json";
        private const string Regenerate = "（python blender/tests/basin_fixture.py で作り直す）";
        private const float Tolerance = 1e-4f;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private readonly struct Edge
        {
            public readonly char Axis;
            public readonly float C;
            public readonly float T0;
            public readonly float T1;
            public readonly int Out;

            public Edge(char axis, float c, float t0, float t1, int outSide)
            {
                Axis = axis;
                C = c;
                T0 = t0;
                T1 = t1;
                Out = outSide;
            }

            public override string ToString()
            {
                return Axis + "=" + C.ToString("0.#####") + " t[" + T0.ToString("0.#####") + ", "
                    + T1.ToString("0.#####") + "] out " + (Out > 0 ? "+1" : "-1");
            }
        }

        private static Type CampusStageType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KCD.Editor")
                {
                    continue;
                }

                Type type = assembly.GetType("KCD.Editor.CampusStage");
                Assert.IsNotNull(type, "KCD.Editor に CampusStage が無い（名前を変えたらこのテストも直す）");
                return type;
            }

            Assert.Fail("KCD.Editor アセンブリが読み込まれていない");
            return null;
        }

        private static List<Dictionary<string, object>> LoadCases()
        {
            string path = Path.Combine(RepoRoot, FixturePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), path + " が無い" + Regenerate);
            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, FixturePath + " を JSON として読めない");

            var cases = new List<Dictionary<string, object>>();
            foreach (object item in MiniJson.GetArray(root, "cases"))
            {
                var c = item as Dictionary<string, object>;
                Assert.IsNotNull(c, FixturePath + " の cases の要素がオブジェクトでない");
                cases.Add(c);
            }

            Assert.Greater(cases.Count, 0, FixturePath + " の cases が空");
            return cases;
        }

        private static float ToFloat(object value, string what)
        {
            Assert.IsInstanceOf<double>(value, what + " が数値でない");
            return (float)(double)value;
        }

        private static Vector4[] Rects(Dictionary<string, object> c, string name)
        {
            List<object> rects = MiniJson.GetArray(c, "rects");
            Assert.Greater(rects.Count, 0, name + ": rects が空");
            var result = new Vector4[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i] as List<object>;
                Assert.IsNotNull(r, name + ": rects[" + i + "] が配列でない");
                Assert.AreEqual(4, r.Count, name + ": rects[" + i + "] が (u0, v0, u1, v1) でない");
                string what = name + ": rects[" + i + "]";
                result[i] = new Vector4(ToFloat(r[0], what), ToFloat(r[1], what), ToFloat(r[2], what), ToFloat(r[3], what));
            }

            return result;
        }

        private static List<Edge> FixtureEdges(Dictionary<string, object> c, string name)
        {
            var edges = new List<Edge>();
            List<object> items = MiniJson.GetArray(c, "edges");
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i] as Dictionary<string, object>;
                Assert.IsNotNull(e, name + ": edges[" + i + "] がオブジェクトでない");
                string axis = MiniJson.GetString(e, "axis");
                Assert.IsTrue(axis == "u" || axis == "v", name + ": edges[" + i + "] の axis が u / v でない: " + axis);
                string what = name + ": edges[" + i + "]";
                edges.Add(new Edge(axis[0], ToFloat(e["c"], what), ToFloat(e["t0"], what), ToFloat(e["t1"], what),
                    MiniJson.GetInt(e, "out")));
            }

            return edges;
        }

        /// <summary>CampusStage.BasinEdges(rects) の結果。</summary>
        private static List<Edge> UnityEdges(Type stage, Vector4[] rects)
        {
            MethodInfo method = stage.GetMethod("BasinEdges", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "CampusStage.BasinEdges が見つからない");
            var list = method.Invoke(null, new object[] { rects }) as IList;
            Assert.IsNotNull(list, "CampusStage.BasinEdges の戻り値がリストでない");

            Type edgeType = stage.GetNestedType("BasinEdge", BindingFlags.Public);
            Assert.IsNotNull(edgeType, "CampusStage.BasinEdge が見つからない");
            FieldInfo axis = Field(edgeType, "Axis");
            FieldInfo c = Field(edgeType, "C");
            FieldInfo t0 = Field(edgeType, "T0");
            FieldInfo t1 = Field(edgeType, "T1");
            FieldInfo outSide = Field(edgeType, "Out");

            var edges = new List<Edge>();
            foreach (object e in list)
            {
                edges.Add(new Edge((char)axis.GetValue(e), (float)c.GetValue(e), (float)t0.GetValue(e),
                    (float)t1.GetValue(e), (int)outSide.GetValue(e)));
            }

            return edges;
        }

        private static FieldInfo Field(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, "CampusStage.BasinEdge." + name + " が見つからない");
            return field;
        }

        private static string Describe(List<Edge> edges)
        {
            var text = new StringBuilder();
            foreach (Edge e in edges)
            {
                text.Append("\n    ").Append(e);
            }

            return text.ToString();
        }

        [Test]
        public void BasinEdges_MatchSitePyOnEveryFixtureCase()
        {
            Type stage = CampusStageType();
            List<Dictionary<string, object>> cases = LoadCases();
            foreach (Dictionary<string, object> c in cases)
            {
                string name = MiniJson.GetString(c, "name", "?");
                List<Edge> python = FixtureEdges(c, name);
                List<Edge> unity = UnityEdges(stage, Rects(c, name));
                string both = "\n  site.py:" + Describe(python) + "\n  CampusStage:" + Describe(unity);

                Assert.AreEqual(python.Count, unity.Count, name + ": 水際の辺の数が違う" + both);
                for (int k = 0; k < python.Count; k++)
                {
                    Edge p = python[k];
                    Edge u = unity[k];
                    string at = name + ": " + k + " 本目の辺が違う" + both;
                    Assert.AreEqual(p.Axis, u.Axis, at);
                    Assert.AreEqual(p.Out, u.Out, at);
                    Assert.AreEqual(p.C, u.C, Tolerance, at);
                    Assert.AreEqual(p.T0, u.T0, Tolerance, at);
                    Assert.AreEqual(p.T1, u.T1, Tolerance, at);
                }
            }
        }

        [Test]
        public void Fixture_BasinsCase_IsCampusStageBasins()
        {
            Type stage = CampusStageType();
            FieldInfo field = stage.GetField("Basins", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "CampusStage.Basins が見つからない");
            var basins = field.GetValue(null) as Vector4[];
            Assert.IsNotNull(basins, "CampusStage.Basins が Vector4[] でない");

            Dictionary<string, object> real = LoadCases().Find(c => MiniJson.GetString(c, "name") == "basins");
            Assert.IsNotNull(real, FixturePath + " に basins（site.BASINS）の組が無い" + Regenerate);
            Vector4[] rects = Rects(real, "basins");

            Assert.AreEqual(basins.Length, rects.Length, "水盤の矩形の数が fixture と CampusStage.Basins で違う" + Regenerate);
            for (int i = 0; i < rects.Length; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Assert.AreEqual(rects[i][k], basins[i][k], Tolerance,
                        "fixture の basins[" + i + "][" + k + "] と CampusStage.Basins[" + i + "] が違う" + Regenerate);
                }
            }
        }

        [Test]
        public void Fixture_CoversSharedEdges()
        {
            // 接する辺を引く所（L 字の継ぎ目・一部だけ接する辺・許容差ぎりぎり）を含んでいないと、
            // 上の突き合わせは 4 辺そのままの矩形しか見ないことになる。
            int trimmed = 0;
            List<Dictionary<string, object>> cases = LoadCases();
            foreach (Dictionary<string, object> c in cases)
            {
                string name = MiniJson.GetString(c, "name", "?");
                if (FixtureEdges(c, name).Count != Rects(c, name).Length * 4)
                {
                    trimmed++;
                }
            }

            Assert.GreaterOrEqual(cases.Count, 5, FixturePath + " の組が少ない");
            Assert.GreaterOrEqual(trimmed, 3, FixturePath + " に隣と接する形の組が少ない");
        }
    }
}
