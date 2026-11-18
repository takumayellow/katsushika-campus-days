using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Campus シーン（屋外）の全レンダラの三角形のワールド面積を、マテリアルごとに足して表にする (#416)。
    /// 写真の質感をどの材質から張るかを、面積の大きい順に決めるための調査用。
    ///
    /// 実行: -executeMethod KCD.Editor.SurfaceAreaProbe.Run [-areaOut &lt;出力 .md のパス&gt;]
    /// 出力の既定は docs/progress/416/area.md（リポジトリの根から）。
    /// </summary>
    public static class SurfaceAreaProbe
    {
        private const string DefaultOut = "../../docs/progress/416/area.md";

        /// <summary>屋内（InteriorStage が置く）のルート。屋外の面ではないので数えない。</summary>
        public const string InteriorRoot = "Interiors";

        public static void Run()
        {
            EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            Dictionary<string, double> areas = SumAreas(Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude));
            Dictionary<string, float> tiling = CampusSurfaces.LoadTiling();

            string outPath = Path.GetFullPath(EditorPaths.ReadArgument("-areaOut", Path.Combine(Application.dataPath, "..", DefaultOut)));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, Format(areas, tiling), new UTF8Encoding(false));
            EditorPaths.Report("面積の表を書いた: " + outPath + "（" + areas.Count + " 材質）");
        }

        /// <summary>{マテリアル名: 面積 m²}。サブメッシュ i はレンダラの i 番目のマテリアルに数える。</summary>
        public static Dictionary<string, double> SumAreas(IEnumerable<MeshRenderer> renderers)
        {
            var areas = new Dictionary<string, double>();
            foreach (MeshRenderer renderer in renderers)
            {
                if (IsUnder(renderer.transform, InteriorRoot))
                {
                    continue;
                }

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    if (materials[sub] == null)
                    {
                        continue;
                    }

                    string name = materials[sub].name;
                    areas.TryGetValue(name, out double sum);
                    areas[name] = sum + SubmeshArea(mesh.GetTriangles(sub), vertices, toWorld);
                }
            }

            return areas;
        }

        /// <summary>自分か親のどれかが <paramref name="rootName"/> という名前なら true。</summary>
        public static bool IsUnder(Transform transform, string rootName)
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                if (t.name == rootName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>三角形の並びのワールド面積（m²）。</summary>
        public static double SubmeshArea(int[] triangles, Vector3[] vertices, Matrix4x4 toWorld)
        {
            double area = 0.0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[t]]);
                Vector3 b = toWorld.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                Vector3 c = toWorld.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                area += 0.5 * Vector3.Cross(b - a, c - a).magnitude;
            }

            return area;
        }

        private static string Format(Dictionary<string, double> areas, Dictionary<string, float> tiling)
        {
            double total = areas.Values.Sum();
            var sb = new StringBuilder();
            sb.Append("# Campus シーンの材質ごとの面積 (#416)\n\n");
            sb.Append("`SurfaceAreaProbe.Run`（`Assets/Scripts/Editor/SurfaceAreaProbe.cs`）が、Campus シーンの有効な MeshRenderer（屋内の `Interiors` の下は除く）の三角形のワールド面積を、サブメッシュのマテリアルごとに足した値。")
                .Append("裏表の 2 面や、地面に隠れた面も数える。「写真」は `tiling.json` にあり、すでに写真の質感を張ってある材質。\n\n");
            sb.Append("| 順位 | 材質 | 面積 m² | 割合 | 写真 |\n|---:|---|---:|---:|:---:|\n");
            int rank = 1;
            foreach (KeyValuePair<string, double> row in areas.OrderByDescending(kv => kv.Value))
            {
                sb.Append("| ").Append(rank++).Append(" | `").Append(row.Key).Append("` | ")
                    .Append(row.Value.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append((100.0 * row.Value / total).ToString("F1", CultureInfo.InvariantCulture)).Append("% | ")
                    .Append(tiling.ContainsKey(row.Key) ? "済" : "").Append(" |\n");
            }

            sb.Append("\n合計 ").Append(total.ToString("N0", CultureInfo.InvariantCulture)).Append(" m²、")
                .Append(areas.Count).Append(" 材質。\n");
            return sb.ToString();
        }
    }
}
