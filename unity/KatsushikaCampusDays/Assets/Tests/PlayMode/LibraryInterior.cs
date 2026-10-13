using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 図書館 (#110) の PlayMode テストが共有する道具。屋内の根、舞台のピアノ椅子、Blender の屋内座標から世界への変換、
    /// 地点を順に歩かせる操作。屋内には NavMesh を焼いていないので、プレイヤーを目的地へ向けて当たり判定ごと歩かせる。
    /// </summary>
    internal static class LibraryInterior
    {
        private const float WalkSeconds = 8f; // ゲーム内の秒（負荷で実時間が延びても変わらない）
        private const float Arrive = 0.6f;

        /// <summary>Blender の屋内座標（x, y, 床からの高さ z）を世界の位置へ移す。</summary>
        internal delegate Vector3 Point(float x, float y, float z);

        internal static Transform Find()
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.name == "Interior_library")
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>ピアノ椅子 = 鍵盤の Empty（seat_library_&lt;nn&gt;_piano）を持つ座面。番号が変わっても追従する。</summary>
        internal static Transform FindPianoStool(Transform root)
        {
            const string suffix = "_piano";
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!byName.ContainsKey(t.name))
                {
                    byName.Add(t.name, t);
                }
            }

            foreach (KeyValuePair<string, Transform> kv in byName)
            {
                if (kv.Key.StartsWith("seat_library_") && kv.Key.EndsWith(suffix))
                {
                    string stoolName = kv.Key.Substring(0, kv.Key.Length - suffix.Length);
                    if (byName.TryGetValue(stoolName, out Transform stool))
                    {
                        return stool;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Blender の屋内座標 (x, y, z) を世界へ。FBX の取り込みで x や y が反転するので、
        /// 2 つの Empty の位置と合う符号を 4 通りから選ぶ。
        /// </summary>
        internal static Point BuildMapping(Transform interior, out string error)
        {
            error = null;
            Transform stage = null;
            Transform counter = null;
            foreach (Transform t in interior.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "poi_library_hall_stage")
                {
                    stage = t;
                }
                else if (t.name == "poi_library_counter")
                {
                    counter = t;
                }
            }

            if (stage == null || counter == null)
            {
                error = "poi_library_hall_stage / poi_library_counter が無い";
                return null;
            }

            // Blender の位置: hall_stage (-1.6, 59.5, 10.75)、counter (-11.6, 25.0, 0.0)。
            foreach (float sx in new[] { 1f, -1f })
            {
                foreach (float sy in new[] { 1f, -1f })
                {
                    Vector3 a = interior.TransformPoint(new Vector3(sx * -1.6f, 10.75f, sy * 59.5f));
                    Vector3 b = interior.TransformPoint(new Vector3(sx * -11.6f, 0f, sy * 25.0f));
                    if ((a - stage.position).magnitude < 0.05f && (b - counter.position).magnitude < 0.05f)
                    {
                        float fx = sx;
                        float fy = sy;
                        return (x, y, z) => interior.TransformPoint(new Vector3(fx * x, z, fy * y));
                    }
                }
            }

            error = "屋内の座標の向きが合わない: stage " + stage.position.ToString("F2") + " counter "
                + counter.position.ToString("F2");
            return null;
        }

        internal static IEnumerator Settled()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
        }

        /// <summary>地点を順に歩く。着けなければ記録し、その地点へ置いて続ける（残りの区間も調べるため）。</summary>
        internal static IEnumerator WalkAll(PlayerController player, List<string> bad, string label, Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 target = points[i];
                player.SetAutopilot(target, player.RunSpeed);
                float elapsed = 0f;
                while (player.AutopilotActive && elapsed < WalkSeconds)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                }

                player.ClearAutopilot();
                Vector3 flat = player.transform.position - target;
                flat.y = 0f;
                if (flat.magnitude > Arrive)
                {
                    bad.Add(label + " の " + (i + 1) + " 番目の地点に着けない（残り " + flat.magnitude.ToString("F2")
                        + " m、いま " + player.transform.position.ToString("F2") + "、目標 " + target.ToString("F2") + "）");
                    player.Teleport(target, 0f);
                    yield return Settled();
                }
            }
        }
    }
}
