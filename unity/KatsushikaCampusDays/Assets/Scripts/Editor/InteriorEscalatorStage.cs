using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 屋内のエスカレーター。FBX の Empty esc_&lt;id&gt;_&lt;区間&gt;_lo（下の端）と esc_&lt;id&gt;_&lt;区間&gt;_hi（上の端）を
    /// 結ぶ斜めの板を当たり判定にし、乗ったプレイヤーを運ぶ Escalator を付ける。2 点は段鼻の線と段の奥の線の
    /// 中間の線の両端（blender/kcd_interior/plan_lecture_esc.py）。段の部品 escsteps_&lt;id&gt;_&lt;区間&gt; があれば、
    /// それを 1 段ぶんずつ動かして流れて見せる。段の部品には当たり判定を付けない（<see cref="IsMovingSteps"/>）。
    /// </summary>
    public static class InteriorEscalatorStage
    {
        /// <summary>段の部品のメッシュ名の頭。</summary>
        public const string StepsPrefix = "escsteps_";

        /// <summary>段の高さ（m）。plan_lecture_dims.ESC_RISE と同じ。段の数は上下の端の高さの差をこれで割って決める。</summary>
        public const float StepRise = 0.2f;

        /// <summary>動く段の部品か（名前が escsteps_ で始まる。大文字小文字は見ない）。当たり判定と静的フラグを付けない。</summary>
        public static bool IsMovingSteps(string objectName)
        {
            return objectName != null
                && objectName.StartsWith(StepsPrefix, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>エスカレーターを置く。置いた区間の数を返す（無ければ 0）。</summary>
        public static int Build(Transform interior, string id)
        {
            Dictionary<string, Transform[]> ends = CollectEnds(interior, id);
            if (ends.Count == 0)
            {
                return 0;
            }

            var group = new GameObject("Escalators");
            group.transform.SetParent(interior, false);
            int built = 0;
            var names = new List<string>(ends.Keys);
            names.Sort(System.StringComparer.Ordinal);
            foreach (string flight in names)
            {
                Transform[] pair = ends[flight];
                if (pair[0] == null || pair[1] == null)
                {
                    EditorPaths.Report("屋内 " + id + " のエスカレーター " + flight + " は _lo と _hi の片方しかありません。");
                    continue;
                }

                if (pair[1].position.y - pair[0].position.y < StepRise * 0.5f)
                {
                    EditorPaths.Report("屋内 " + id + " のエスカレーター " + flight + " は _hi が _lo より上にありません。");
                    continue;
                }

                AddFlight(group.transform, flight, pair[0].position, pair[1].position,
                    FindSteps(interior, id, flight));
                built++;
            }

            return built;
        }

        /// <summary>esc_&lt;id&gt;_&lt;区間&gt;_lo / _hi を区間ごとに集める（[0] が下の端、[1] が上の端）。</summary>
        private static Dictionary<string, Transform[]> CollectEnds(Transform interior, string id)
        {
            string prefix = "esc_" + id + "_";
            var ends = new Dictionary<string, Transform[]>();
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (!name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                int slot = name.EndsWith("_lo", System.StringComparison.Ordinal) ? 0
                    : name.EndsWith("_hi", System.StringComparison.Ordinal) ? 1 : -1;
                if (slot < 0)
                {
                    continue;
                }

                string flight = name.Substring(prefix.Length, name.Length - prefix.Length - 3);
                if (flight.Length == 0)
                {
                    continue;
                }

                if (!ends.TryGetValue(flight, out Transform[] pair))
                {
                    pair = new Transform[2];
                    ends[flight] = pair;
                }

                pair[slot] = child;
            }

            return ends;
        }

        /// <summary>区間の段の部品。名前が escsteps_&lt;id&gt;_&lt;区間&gt; のメッシュ（無ければ null）。</summary>
        private static Transform FindSteps(Transform interior, string id, string flight)
        {
            string wanted = StepsPrefix + id + "_" + flight;
            foreach (MeshFilter filter in interior.GetComponentsInChildren<MeshFilter>(true))
            {
                if (string.Equals(filter.gameObject.name, wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    return filter.transform;
                }
            }

            return null;
        }

        /// <summary>1 区間の Escalator と、2 点を結ぶ斜めの板の当たり判定。</summary>
        private static void AddFlight(Transform group, string flight, Vector3 bottom, Vector3 top, Transform steps)
        {
            var go = new GameObject("Escalator_" + flight);
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(bottom, Quaternion.identity);
            int count = Mathf.Max(1, Mathf.RoundToInt((top.y - bottom.y) / StepRise));
            Escalator escalator = go.AddComponent<Escalator>();
            escalator.Configure(go.transform.InverseTransformVector(top - bottom), count, steps);
            int ground = LayerMask.NameToLayer("Ground");
            if (ground < 0)
            {
                EditorPaths.Report("Ground レイヤーが無いので、エスカレーター " + flight + " の板に乗っても運ばれません。");
            }

            escalator.AddRamp(ground);
        }
    }
}
