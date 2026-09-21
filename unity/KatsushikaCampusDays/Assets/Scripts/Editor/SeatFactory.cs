using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 座れるベンチ。FBX に椅子メッシュが無いのでプリミティブで組み、SeatInteractable を付ける。
    /// 屋外はモールと図書館の水盤、屋内はラウンジ等の POI の脇に置く。
    /// </summary>
    public static class SeatFactory
    {
        private const float SeatHeight = 0.45f;

        /// <summary>屋外ベンチ。位置は CampusProps のローカル座標系（u, v）。</summary>
        public static int PlaceCampus(Transform parent)
        {
            var group = new GameObject("Benches");
            group.transform.SetParent(parent, false);

            int count = 0;
            count += CreateAt(group.transform, "mall_a", CampusProps.Local(65f, -27.6f), CampusProps.Local(65f, -24f));
            count += CreateAt(group.transform, "mall_b", CampusProps.Local(73f, -27.6f), CampusProps.Local(73f, -24f));
            count += CreateAt(group.transform, "pond_a", CampusProps.Local(-42f, -37f), CampusProps.Local(-30f, -31.6f));
            count += CreateAt(group.transform, "pond_b", CampusProps.Local(-18f, -37f), CampusProps.Local(-30f, -31.6f));
            return count;
        }

        /// <summary>屋内ベンチ。poi_&lt;id&gt;_&lt;name&gt; の脇に、POI の方を向けて置く。</summary>
        public static int PlaceInterior(Transform interior, string id)
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
