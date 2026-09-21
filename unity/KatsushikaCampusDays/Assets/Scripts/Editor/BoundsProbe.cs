using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Campus シーンの建物ごとのワールド AABB と、入口・スポーンの位置をログへ出す。
    /// Blender 側の座標と Unity 側の座標の対応を確かめるための調査用。
    /// </summary>
    public static class BoundsProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.transform.parent == null || renderer.transform.parent.name != "Campus")
                {
                    continue;
                }

                {
                    Bounds b = renderer.bounds;
                    sb.Append("BOUNDS ").Append(renderer.name)
                        .Append(" min=").Append(b.min.ToString("F1"))
                        .Append(" max=").Append(b.max.ToString("F1")).Append('\n');
                }
            }

            foreach (string name in new[] { "Player", "Entrance_kyoso", "Entrance_lecture", "Zone_gate_main", "Label_kyoso" })
            {
                GameObject go = GameObject.Find(name);
                Transform t = go != null ? go.transform : null;
                sb.Append("POS ").Append(name).Append(' ')
                    .Append(t != null ? t.position.ToString("F1") + " yaw=" + t.eulerAngles.y.ToString("F0") : "none")
                    .Append('\n');
            }

            EditorPaths.Report(sb.ToString());
        }
    }
}
