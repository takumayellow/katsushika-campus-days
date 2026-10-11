using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// NPC が花壇やベンチの上を歩かないことを守る (#164)。
    ///
    /// NPC は NavMesh の上を歩くので、焼いた NavMesh が小物の天面に乗っていると、プレイヤーは上がれない所を NPC だけが歩く。
    /// NavMesh の段差（agentClimb）はプレイヤーの stepOffset 以下にそろえ、小物（site_furniture・site_props_*）は
    /// CampusStage が Not Walkable で焼く。
    /// </summary>
    public sealed class CampusPropNavMeshTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>この高さ（m, 舗装から）より上の上向きの面を小物の天面とみなす。縁石の低い段や舗装の継ぎ目は数えない。</summary>
        private const float RaisedTop = 0.2f;

        /// <summary>天面のすぐそばに NavMesh があるとみなす上下の幅（m）。下の地面の NavMesh は数えない。</summary>
        private const float OnTopTolerance = 0.15f;

        /// <summary>天面の点を間引く格子の幅（m）。花壇は花の三角形が多いので、同じ格子の点は 1 つだけ調べる。</summary>
        private const float SampleGrid = 0.5f;

        private Scene _scene;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void NavMesh_の段差はプレイヤーが上がれる高さ以下()
        {
            string player = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Runtime", "Player", "PlayerController.cs"));
            string site = File.ReadAllText(Path.Combine(RepoRoot, "blender", "kcd_lib", "site.py"));
            float step = Number(player, @"_stepOffset\s*=\s*(-?\d+(?:\.\d+)?)f", "stepOffset");
            float bedTop = Number(site, @"^BED_TOP\s*=\s*(-?\d+(?:\.\d+)?)", "BED_TOP");

            float climb = NavMesh.GetSettingsByID(0).agentClimb;
            Assert.LessOrEqual(climb, step, "NPC がプレイヤーの上がれない段を上がる");
            Assert.Less(climb, bedTop, "NPC が花壇の縁石に上がる");
        }

        [Test]
        public void 小物の天面に_NavMesh_が無い()
        {
            MethodInfo isPropMesh = CampusStageType().GetMethod("IsPropMesh", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(isPropMesh, "CampusStage.IsPropMesh が無い（名前を変えたらこのテストも直す）");

            var filters = new List<MeshFilter>();
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null && (bool)isPropMesh.Invoke(null, new object[] { filter.name }))
                    {
                        filters.Add(filter);
                    }
                }
            }

            Assert.IsTrue(filters.Exists(f => f.name == "site_props_beds"), ScenePath + " に site_props_beds が無い");

            var bad = new List<string>();
            int sampled = 0;
            foreach (MeshFilter filter in filters)
            {
                int hits = 0;
                Vector3 first = Vector3.zero;
                foreach (Vector3 top in RaisedTops(filter))
                {
                    sampled++;
                    if (NavMesh.SamplePosition(top, out NavMeshHit hit, OnTopTolerance, NavMesh.AllAreas)
                        && Mathf.Abs(hit.position.y - top.y) < OnTopTolerance)
                    {
                        if (hits++ == 0)
                        {
                            first = top;
                        }
                    }
                }

                if (hits > 0)
                {
                    bad.Add(string.Format("{0}: 天面 {1} か所に NavMesh がある（例 {2}）", filter.name, hits, first));
                }
            }

            Assert.Greater(sampled, 0, "小物の天面を 1 つも見つけられない");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>舗装から RaisedTop より上にある上向きの三角形の重心を、SampleGrid の格子で 1 つずつ返す。</summary>
        private static IEnumerable<Vector3> RaisedTops(MeshFilter filter)
        {
            Mesh mesh = filter.sharedMesh;
            Transform t = filter.transform;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var seen = new HashSet<Vector3Int>();
            // 小物はどれも舗装に置いてあるので、メッシュのいちばん低い所を舗装の高さとみなす
            float floor = float.MaxValue;
            foreach (Vector3 v in vertices)
            {
                floor = Mathf.Min(floor, t.TransformPoint(v).y);
            }

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(vertices[triangles[i]]);
                Vector3 b = t.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = t.TransformPoint(vertices[triangles[i + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-10f || normal.normalized.y < 0.7f)
                {
                    continue;
                }

                Vector3 center = (a + b + c) / 3f;
                if (center.y < floor + RaisedTop)
                {
                    continue;
                }

                var cell = new Vector3Int(
                    Mathf.FloorToInt(center.x / SampleGrid),
                    Mathf.FloorToInt(center.y / SampleGrid),
                    Mathf.FloorToInt(center.z / SampleGrid));
                if (seen.Add(cell))
                {
                    yield return center;
                }
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

        private static float Number(string source, string pattern, string what)
        {
            Match m = Regex.Match(source, pattern, RegexOptions.Multiline);
            Assert.IsTrue(m.Success, what + " を読み取れない（書き方を変えたらこのテストも直す）: " + pattern);
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
