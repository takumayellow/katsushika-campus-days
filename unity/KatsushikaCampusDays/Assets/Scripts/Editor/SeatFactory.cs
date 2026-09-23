using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 座れるベンチ。屋外は FBX に椅子メッシュが無いのでプリミティブで組み、SeatInteractable を付ける。
    /// 屋内は FBX のソファ・ベンチ・ラウンジチェア（seat_ Empty）に座る操作を付ける。
    /// </summary>
    public static class SeatFactory
    {
        private const float SeatHeight = 0.45f;

        /// <summary>
        /// 屋内の座れる家具に被せる見えない壁の高さ。座面（0.38〜0.46 m）は CharacterController の
        /// stepOffset（0.4 m）とほぼ同じなので、メッシュの当たり判定だけだと段差として上ってしまう。
        /// ジャンプ（1.1 m）+ stepOffset でも越えない高さにする。
        /// </summary>
        private const float BlockerHeight = 1.9f;

        /// <summary>屋外ベンチ 1 脚ぶん。座標は CampusProps のローカル軸 (u, v)。</summary>
        private readonly struct BenchSpot
        {
            public BenchSpot(string id, float u, float v, float faceU, float faceV)
            {
                Id = id;
                U = u;
                V = v;
                FaceU = faceU;
                FaceV = faceV;
            }

            public string Id { get; }

            public float U { get; }

            public float V { get; }

            public float FaceU { get; }

            public float FaceV { get; }
        }

        /// <summary>
        /// 屋外ベンチの置き場所と向く先（CampusProps のローカル座標 u, v）。
        ///
        /// pond_a / pond_b は図書館を囲む堀の東岸の芝生（u = -46）に置き、堀と図書館の方（-u）を向ける。
        /// 堀の東の帯は site.py の BASINS[1]（u -59.5..-50）で、縁石の外端は u = -48.4。
        /// 水際までは 4 m あるので、背もたれ（天端 0.90 m）に乗っても見えない壁
        /// （天端 2.6 m）は越えられない。
        /// </summary>
        private static readonly BenchSpot[] CampusBenches =
        {
            new BenchSpot("mall_a", 65f, -27.6f, 65f, -24f),
            new BenchSpot("mall_b", 73f, -27.6f, 73f, -24f),
            new BenchSpot("pond_a", -46f, -40f, -50f, -40f),
            new BenchSpot("pond_b", -46f, -55f, -50f, -55f),
        };

        /// <summary>屋外ベンチ。位置は CampusProps のローカル座標系（u, v）。</summary>
        public static int PlaceCampus(Transform parent)
        {
            var group = new GameObject("Benches");
            group.transform.SetParent(parent, false);

            int count = 0;
            foreach (BenchSpot spot in CampusBenches)
            {
                count += CreateAt(group.transform, spot.Id, CampusProps.Local(spot.U, spot.V),
                    CampusProps.Local(spot.FaceU, spot.FaceV));
            }

            return count;
        }

        /// <summary>
        /// 屋内の座れる家具。FBX の seat_&lt;id&gt;_&lt;nn&gt; Empty（Blender の Ctx.flush_seats が書く）ごとに、
        /// E で座る操作と、上に乗り上げないための見えない壁を付ける。
        /// 座れる家具が 1 つも無い棟（食堂など）は、これまでどおり POI の脇にベンチを置く。
        /// </summary>
        public static int PlaceInterior(Transform interior, string id)
        {
            string prefix = "seat_" + id + "_";
            var empties = new Dictionary<string, Transform>();
            var seats = new List<string>();
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (!name.StartsWith(prefix) || empties.ContainsKey(name))
                {
                    continue;
                }

                empties.Add(name, child);
                // seat_<id>_<nn> が家具 1 つ。_f / _s / _a<k> はその付属。
                if (IsDigits(name.Substring(prefix.Length)))
                {
                    seats.Add(name);
                }
            }

            if (seats.Count == 0)
            {
                return PlacePoiBenches(interior, id);
            }

            seats.Sort(System.StringComparer.Ordinal);
            var group = new GameObject("Seats");
            group.transform.SetParent(interior, false);
            int count = 0;
            foreach (string name in seats)
            {
                if (CreateInteriorSeat(group.transform, name, empties))
                {
                    count++;
                }
                else
                {
                    EditorPaths.Report("屋内 " + id + " の " + name + " は _f / _s / _a0 が欠けているので座れません。");
                }
            }

            return count;
        }

        /// <summary>
        /// FBX の家具 1 つぶん。Empty はワールド座標で読む（屋内は 180° 回して置くので、向きも座標から出す）。
        ///   seat_&lt;id&gt;_&lt;nn&gt;    家具の外形の中心（床の高さ）
        ///   ..._f               正面の辺の中点（中心からの向きが座ったときの正面）
        ///   ..._s               側面の辺の中点（中心からの距離が幅の半分）
        ///   ..._a&lt;k&gt;            座る位置（床の高さ）
        /// </summary>
        private static bool CreateInteriorSeat(Transform parent, string name, Dictionary<string, Transform> empties)
        {
            if (!empties.TryGetValue(name, out Transform centre)
                || !empties.TryGetValue(name + "_f", out Transform front)
                || !empties.TryGetValue(name + "_s", out Transform side)
                || !empties.ContainsKey(name + "_a0"))
            {
                return false;
            }

            Vector3 origin = centre.position;
            Vector3 forward = front.position - origin;
            forward.y = 0f;
            Vector3 lateral = side.position - origin;
            lateral.y = 0f;
            float halfDepth = forward.magnitude;
            float halfWidth = lateral.magnitude;
            if (halfDepth < 0.05f || halfWidth < 0.05f)
            {
                return false;
            }

            var seat = new GameObject("Seat_" + name.Substring("seat_".Length));
            seat.transform.SetParent(parent, false);
            seat.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward / halfDepth, Vector3.up));

            // 座る位置。向きは家具の正面にそろえる（SitAt は anchor.forward を見る）。
            var anchors = new List<Transform>();
            for (int k = 0; empties.TryGetValue(name + "_a" + k, out Transform point); k++)
            {
                anchors.Add(Anchor(seat.transform, "Anchor" + k, seat.transform.InverseTransformPoint(point.position)));
            }

            // 乗り上げ防止の見えない壁。家具の外形いっぱいに立てる。
            // Default レイヤーのままにして、カメラ（Ground / Building だけを見る）には当たらないようにする。
            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(seat.transform, false);
            BoxCollider solid = blocker.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, BlockerHeight * 0.5f, 0f);
            solid.size = new Vector3(halfWidth * 2f, BlockerHeight, halfDepth * 2f);

            // 座る操作の判定は正面の辺の前に置く。幅の広いソファでも端から座れるよう、反応距離を幅に合わせる。
            var trigger = new GameObject("SeatTrigger");
            trigger.transform.SetParent(seat.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 0.8f, halfDepth);
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                trigger.layer = layer;
            }

            BoxCollider box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(halfWidth * 2f + 0.4f, 1.6f, 1.2f);

            SeatInteractable interactable = trigger.AddComponent<SeatInteractable>();
            interactable.InteractionRange = Mathf.Max(2.2f, halfWidth + 1.2f);
            interactable.Anchors = anchors.ToArray();
            return true;
        }

        private static bool IsDigits(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>座れる家具の無い棟のベンチ。poi_&lt;id&gt;_&lt;name&gt; の脇に、POI の方を向けて置く。</summary>
        private static int PlacePoiBenches(Transform interior, string id)
        {
            string[] names = { "lounge", "reading", "foyer", "bleachers", "cafe", "hall" };
            int count = 0;
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                foreach (string name in names)
                {
                    if (child.name != "poi_" + id + "_" + name)
                    {
                        continue;
                    }

                    Vector3 side = child.right * 1.6f;
                    Vector3 position = child.position + side;
                    position.y = child.position.y;
                    Vector3 target = child.position;
                    target.y = position.y;
                    CreateBench(interior, id + "_" + name, position, YawTowards(position, target));
                    count++;
                }
            }

            return count;
        }

        private static int CreateAt(Transform parent, string id, Vector2 xz, Vector2 faceXz)
        {
            Vector3 position = CampusProps.Ground(xz);
            Vector3 target = CampusProps.Ground(faceXz);
            target.y = position.y;
            CreateBench(parent, id, position, YawTowards(position, target));
            return 1;
        }

        private static float YawTowards(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            return delta.sqrMagnitude < 0.001f ? 0f : Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        }

        /// <summary>2 人掛けのベンチ。+Z が正面。</summary>
        public static GameObject CreateBench(Transform parent, string id, Vector3 position, float yaw)
        {
            var bench = new GameObject("Bench_" + id);
            bench.transform.SetParent(parent, false);
            bench.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            Material wood = MaterialLibrary.EnsureCampus("wood");
            Material metal = MaterialLibrary.EnsureCampus("metal_grey");

            Part(bench.transform, "Seat", new Vector3(0f, SeatHeight, 0f), Quaternion.identity,
                new Vector3(1.7f, 0.07f, 0.42f), wood);
            Part(bench.transform, "Back", new Vector3(0f, 0.72f, -0.19f), Quaternion.Euler(-8f, 0f, 0f),
                new Vector3(1.7f, 0.36f, 0.05f), wood);
            Part(bench.transform, "LegL", new Vector3(-0.7f, SeatHeight * 0.5f, 0f), Quaternion.identity,
                new Vector3(0.05f, SeatHeight, 0.38f), metal);
            Part(bench.transform, "LegR", new Vector3(0.7f, SeatHeight * 0.5f, 0f), Quaternion.identity,
                new Vector3(0.05f, SeatHeight, 0.38f), metal);

            var trigger = new GameObject("SeatTrigger");
            trigger.transform.SetParent(bench.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 0.8f, 0.3f);
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                trigger.layer = layer;
            }

            BoxCollider box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.2f, 1.6f, 2.2f);

            SeatInteractable seat = trigger.AddComponent<SeatInteractable>();
            seat.InteractionRange = 2.2f;
            seat.Anchors = new[]
            {
                Anchor(bench.transform, "AnchorL", new Vector3(-0.42f, 0f, 0.02f)),
                Anchor(bench.transform, "AnchorR", new Vector3(0.42f, 0f, 0.02f))
            };

            return bench;
        }

        private static Transform Anchor(Transform parent, string name, Vector3 local)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = local;
            anchor.transform.localRotation = Quaternion.identity;
            return anchor.transform;
        }

        private static void Part(
            Transform parent, string name, Vector3 local, Quaternion rotation, Vector3 size, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = local;
            part.transform.localRotation = rotation;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
