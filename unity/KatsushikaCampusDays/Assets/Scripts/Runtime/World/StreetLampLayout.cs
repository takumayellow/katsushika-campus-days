using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// キャンパスの照明柱の灯具の位置を、site_furniture のメッシュの頂点から拾う (#8)。
    ///
    /// site_furniture はベンチと照明柱をまとめた 1 つのメッシュ（blender/kcd_lib/site.py の
    /// build_street_furniture）。ベンチは高さ 0.92 m まで、照明柱は高さ 4 m の柱の上に灯具
    /// （0.55 × 0.30 m の箱と、その下面の 0.45 × 0.22 m の発光面）が載る。メッシュの一番低い頂点より
    /// <see cref="MinHeadHeight"/> 高い頂点だけを残し、水平に <see cref="ClusterRadius"/> 以内のものを
    /// 1 本の灯具にまとめる。灯具どうしはモールの両側で 10 m、並びで 20 m 離れているので混ざらない。
    /// </summary>
    public static class StreetLampLayout
    {
        /// <summary>灯具とみなす高さ（メッシュの一番低い頂点から、m）。ベンチの背（0.92 m）より十分高い。</summary>
        public const float MinHeadHeight = 3f;

        /// <summary>1 本の灯具にまとめる水平の距離（m）。灯具の箱は差し渡し 0.63 m。</summary>
        public const float ClusterRadius = 1f;

        /// <summary>
        /// ワールド座標の頂点から灯具の位置を返す。位置は灯具の頂点の水平の平均と、一番低い頂点の高さ
        /// （発光面の高さ）。x、z の順に並べて返すので、同じメッシュからは毎回同じ順になる。
        /// </summary>
        public static List<Vector3> FindLamps(IReadOnlyList<Vector3> worldVertices)
        {
            var lamps = new List<Vector3>();
            if (worldVertices == null || worldVertices.Count == 0)
            {
                return lamps;
            }

            float bottom = float.MaxValue;
            for (int i = 0; i < worldVertices.Count; i++)
            {
                bottom = Mathf.Min(bottom, worldVertices[i].y);
            }

            float threshold = bottom + MinHeadHeight;
            float radiusSqr = ClusterRadius * ClusterRadius;
            var seeds = new List<Vector2>();
            var sums = new List<Vector2>();
            var counts = new List<int>();
            var lows = new List<float>();

            for (int i = 0; i < worldVertices.Count; i++)
            {
                Vector3 v = worldVertices[i];
                if (v.y < threshold)
                {
                    continue;
                }

                var flat = new Vector2(v.x, v.z);
                int found = -1;
                for (int c = 0; c < seeds.Count; c++)
                {
                    if ((seeds[c] - flat).sqrMagnitude <= radiusSqr)
                    {
                        found = c;
                        break;
                    }
                }

                if (found < 0)
                {
                    seeds.Add(flat);
                    sums.Add(flat);
                    counts.Add(1);
                    lows.Add(v.y);
                    continue;
                }

                sums[found] += flat;
                counts[found] += 1;
                lows[found] = Mathf.Min(lows[found], v.y);
            }

            for (int c = 0; c < seeds.Count; c++)
            {
                Vector2 center = sums[c] / counts[c];
                lamps.Add(new Vector3(center.x, lows[c], center.y));
            }

            lamps.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            return lamps;
        }
    }
}
