using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 隠しストーリー「はじまりの春の宝探し」（q_secret_spring_hunt）の舞台を置く (#175)。
    ///
    /// 図書館 2F 回廊の西の壁際、書架の列の北の空いた所に、屋上のドームへ上がる階段室の入口を置く。
    /// 外周は全面ガラスのカーテンウォールなので、扉は壁に直接付けず、壁際に立てた白い壁の箱（コア）の正面に付ける。
    /// 扉を調べると dome_door の会話（扉の中の手紙）が出る。
    /// 鍵 c_dome_key は同じ 2F の poi_library_gallery に CollectibleStage が置く。
    /// 屋内の poi を使うので InteriorStage.Build のあとに呼ぶ。
    /// </summary>
    public static class SecretStoryStage
    {
        public const string DoorName = "Talker_dome_door";

        /// <summary>扉の会話データの id。クエストの talk 先でもある。</summary>
        public const string DoorNpcId = "dome_door";

        public const string CoreName = "DomeStairCore";

        private const string Building = "library";
        private const string GalleryPoi = "poi_library_gallery";

        /// <summary>
        /// 屋内の座標からワールドへの写し方を解くのに使う、回廊の poi 以外の 2 点。回廊の poi と一直線に並ばない。
        /// </summary>
        private static readonly string[] FramePois = { "poi_library_stacks", "poi_library_counter" };

        /// <summary>
        /// poi_library_gallery（回廊の書架の列のあいだ）から見たコアの位置（屋内の座標 (x, z)、m）。
        /// 屋内の座標はサイドカーの empties と同じで、Blender の (x, y) を Unity の (x, z) に読んだもの。
        /// 西の壁は poi の 6.0 m 西、書架の北端は 9.0 m 北、閲覧机は 33 m 北（plan_library.py の _gallery）。
        /// コアは書架の北端から 4 m 北の壁際に立てる。x は壁を探す目安で、実際は見つけた壁の面に付ける。
        /// </summary>
        private static readonly Vector2 CoreFromPoi = new Vector2(-6.0f, 13.0f);

        /// <summary>壁を探すときに、壁の目安から部屋の内側へ戻る距離（m）。</summary>
        private const float WallProbeBack = 1.5f;

        /// <summary>壁を探す光線の高さ（m、床から）。</summary>
        private const float WallProbeHeight = 1.5f;

        /// <summary>見つけた壁の面が目安からこれ以上ずれていたら、壁ではないとみなす（m）。</summary>
        private const float WallTolerance = 0.6f;

        /// <summary>コアの幅のうち、壁を探す光線を飛ばす所（中心からの距離、m）。</summary>
        private static readonly float[] WallProbeOffsets = { -1.0f, -0.5f, 0f, 0.5f, 1.0f };

        /// <summary>コアの背中をガラスの面より奥へ入れる深さ（m）。サッシの縦枠をコアの中に隠す。</summary>
        private const float CoreSink = 0.05f;

        /// <summary>コアの幅（壁に沿って）と、ガラスの面から手前への奥行き（m）。</summary>
        private const float CoreWidth = 2.4f;
        private const float CoreDepth = 1.1f;

        /// <summary>天井が見つからなかったときのコアの高さ（m）。2F の天井は床から 3.2 m（plan_library.py）。</summary>
        private const float DefaultCoreHeight = 3.2f;

        /// <summary>扉の面をコアからどれだけ浮かせるか（m）。</summary>
        private const float DoorGap = 0.01f;

        private const float DoorWidth = 0.9f;
        private const float DoorHeight = 1.9f;

        /// <summary>扉に近づいて E が出る距離（m、水平）。</summary>
        private const float DoorRange = 2.0f;

        public static void Build(Transform root)
        {
            Transform interiors = root.Find("Interiors");
            Transform interior = interiors != null
                ? interiors.Find(QuestObjectiveLocator.InteriorPrefix + Building)
                : null;
            Transform poi = interior != null ? FindDeep(interior, GalleryPoi) : null;
            if (poi == null)
            {
                EditorPaths.Report("ドームの扉を置けません: " + GalleryPoi + " が見つからない");
                return;
            }

            if (!SolveFrame(interior, out Vector3 axisX, out Vector3 axisZ, out string error))
            {
                EditorPaths.Report("ドームの扉を置けません: " + error);
                return;
            }

            Vector3 guess = CollectibleStage.IndoorFloor(
                poi.position + axisX * CoreFromPoi.x + axisZ * CoreFromPoi.y);
            // 西の壁の面は屋内の +x（部屋の内側）を向く。
            Vector3 inward = axisX.normalized;
            Vector3 back = OnWall(guess, inward, axisZ.normalized, out bool snapped, out string probe);
            if (!snapped)
            {
                EditorPaths.Report("ドームの扉: 回廊の西の壁が見つからないので、計算した位置に置きます "
                    + back + "（" + probe + "）");
            }

            float height = CoreHeight(back + inward * (CoreDepth * 0.5f));
            Transform core = BuildCore(interior, back, inward, height);
            BuildDoor(core);

            EditorPaths.Report("ドームの扉を図書館 2F の西の壁際に置きました " + back
                + "（コアの高さ " + height.ToString("0.00") + " m" + (snapped ? "" : "、壁は未確認") + "）");
        }

        /// <summary>
        /// 屋内の座標の +x と +z が、ワールドでそれぞれどちらを向くか（1 m あたり）。
        /// FBX の取り込みの軸と InteriorStage の回し方に頼らず、サイドカーとシーンの両方にある 3 つの poi から解く。
        /// </summary>
        private static bool SolveFrame(Transform interior, out Vector3 axisX, out Vector3 axisZ, out string error)
        {
            axisX = Vector3.right;
            axisZ = Vector3.forward;
            Dictionary<string, object> empties = ReadEmpties(out error);
            if (empties == null)
            {
                return false;
            }

            var names = new[] { GalleryPoi, FramePois[0], FramePois[1] };
            var local = new Vector2[names.Length];
            var world = new Vector2[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                Dictionary<string, object> point = MiniJson.GetObject(empties, names[i]);
                Transform found = FindDeep(interior, names[i]);
                if (point == null || found == null)
                {
                    error = names[i] + " がサイドカーかシーンに無い";
                    return false;
                }

                local[i] = new Vector2(MiniJson.GetFloat(point, "x"), MiniJson.GetFloat(point, "z"));
                world[i] = new Vector2(found.position.x, found.position.z);
            }

            // 屋内の差 d と世界の差 e から、2×2 の写し A（A·d = e）を解く。A の列が屋内の +x と +z。
            Vector2 d1 = local[1] - local[0];
            Vector2 d2 = local[2] - local[0];
            Vector2 e1 = world[1] - world[0];
            Vector2 e2 = world[2] - world[0];
            float det = d1.x * d2.y - d2.x * d1.y;
            if (Mathf.Abs(det) < 1f)
            {
                error = "poi が一直線に近く並んでいて向きを解けない";
                return false;
            }

            Vector2 colX = (e1 * d2.y - e2 * d1.y) / det;
            Vector2 colZ = (e2 * d1.x - e1 * d2.x) / det;
            axisX = new Vector3(colX.x, 0f, colX.y);
            axisZ = new Vector3(colZ.x, 0f, colZ.y);

            // 屋内は縮めずに回す（か裏返す）だけなので、どちらも長さ 1 で直交する。外れていれば poi の取り違えを疑う。
            if (Mathf.Abs(axisX.magnitude - 1f) > 0.05f || Mathf.Abs(axisZ.magnitude - 1f) > 0.05f
                || Mathf.Abs(Vector3.Dot(axisX, axisZ)) > 0.05f)
            {
                error = "poi から解いた向きが回転になっていない（+x " + axisX + "、+z " + axisZ + "）";
                return false;
            }

            return true;
        }

        /// <summary>サイドカー（Models/Interiors/library.json）の empties。読めなければ null と理由。</summary>
        private static Dictionary<string, object> ReadEmpties(out string error)
        {
            error = null;
            string path = InteriorStage.ModelsFolder + "/" + Building + ".json";
            string absolute = EditorPaths.Absolute(path);
            if (!File.Exists(absolute))
            {
                error = "屋内の配置情報が無い: " + path;
                return null;
            }

            var root = MiniJson.Deserialize(File.ReadAllText(absolute)) as Dictionary<string, object>;
            Dictionary<string, object> empties = root != null ? MiniJson.GetObject(root, "empties") : null;
            if (empties == null)
            {
                error = "屋内の配置情報に empties が無い: " + path;
            }

            return empties;
        }

        /// <summary>
        /// 壁の目安 guess から部屋の内側へ <see cref="WallProbeBack"/> m 戻った所から、コアの幅に並べた光線を壁へ飛ばす。
        /// 光線ごとにいちばん手前の壁らしい面を取り、そのうちいちばん奥（サッシの縦枠ではなくガラスの面）を壁とする。
        /// 返すのはその面の上の、guess と同じ床の高さの点。どれも当たらないか、目安から
        /// <see cref="WallTolerance"/> m 以上ずれていれば guess のまま（snapped = false）。
        /// probe には見つからなかったときの手がかり（当たった光線の数と、壁までの距離）を入れる。
        /// </summary>
        private static Vector3 OnWall(Vector3 guess, Vector3 inward, Vector3 along, out bool snapped, out string probe)
        {
            Physics.SyncTransforms();
            int hitRays = 0;
            float farthest = -1f;
            for (int k = 0; k < WallProbeOffsets.Length; k++)
            {
                Vector3 origin = guess + along * WallProbeOffsets[k] + inward * WallProbeBack
                    + Vector3.up * WallProbeHeight;
                float nearest = NearestFacing(origin, -inward, WallProbeBack + 1.0f, inward);
                if (nearest < 0f)
                {
                    continue;
                }

                hitRays++;
                farthest = Mathf.Max(farthest, nearest);
            }

            probe = "壁に当たった光線 " + hitRays + "/" + WallProbeOffsets.Length + " 本、壁までの距離 "
                + (farthest >= 0f ? farthest.ToString("0.00") + " m" : "なし");
            snapped = farthest >= 0f && Mathf.Abs(farthest - WallProbeBack) < WallTolerance;
            return snapped ? guess + inward * (WallProbeBack - farthest) : guess;
        }

        /// <summary>
        /// origin から direction へ飛ばした光線が当たる、facing を向いた面（内積 0.9 以上）のうちいちばん手前までの距離。
        /// 手すりのガラスや書架の側面のように向きの違う面は使わない。無ければ -1。
        /// </summary>
        private static float NearestFacing(Vector3 origin, Vector3 direction, float length, Vector3 facing)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, length, ~0, QueryTriggerInteraction.Ignore);
            float nearest = -1f;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Vector3.Dot(hits[i].normal, facing) < 0.9f)
                {
                    continue;
                }

                if (nearest < 0f || hits[i].distance < nearest)
                {
                    nearest = hits[i].distance;
                }
            }

            return nearest;
        }

        /// <summary>floor の真上の天井までの高さ。見つからないか、ありえない高さなら <see cref="DefaultCoreHeight"/>。</summary>
        private static float CoreHeight(Vector3 floor)
        {
            const float lift = 1.0f;
            float nearest = NearestFacing(floor + Vector3.up * lift, Vector3.up, 5f, Vector3.down);
            float height = nearest < 0f ? DefaultCoreHeight : nearest + lift;
            return height >= 2.6f && height <= 4.5f ? height : DefaultCoreHeight;
        }

        /// <summary>
        /// 壁際に立てる白い壁の箱。背中を壁の面 back に合わせ（少し奥へ入れて縦枠を隠す）、正面は部屋の内側を向く。
        /// 歩いて抜けられないよう当たり判定を残し、屋内の壁と同じ Building のレイヤーにする。
        /// </summary>
        private static Transform BuildCore(Transform interior, Vector3 back, Vector3 inward, float height)
        {
            var core = new GameObject(CoreName);
            core.transform.SetParent(interior, true);
            core.transform.SetPositionAndRotation(back, Quaternion.LookRotation(inward));

            float depth = CoreDepth + CoreSink;
            GameObject body = AddCube(core.transform, "body",
                new Vector3(0f, height * 0.5f, CoreDepth - depth * 0.5f), new Vector3(CoreWidth, height, depth),
                MaterialLibrary.EnsureCampus("wall_white"));
            body.AddComponent<BoxCollider>();
            SetLayer(body, "Building");
            return core.transform;
        }

        /// <summary>コアの正面の真ん中に付ける扉。調べられるように、触れる判定の箱と NPCTalker を付ける。</summary>
        private static void BuildDoor(Transform core)
        {
            var door = new GameObject(DoorName);
            SetLayer(door, "Interactable");
            door.transform.SetParent(core, false);
            door.transform.localPosition = new Vector3(0f, 0f, CoreDepth + DoorGap);
            door.transform.localRotation = Quaternion.identity;

            AddParts(door.transform);

            // InteractionPrompt は Interactable と NPC のレイヤーの当たり判定を球で探す。扉の前の薄い見えない箱。
            var box = door.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, DoorHeight * 0.5f, 0.1f);
            box.size = new Vector3(DoorWidth + 0.1f, DoorHeight, 0.2f);

            NPCTalker talker = door.AddComponent<NPCTalker>();
            talker.NpcId = DoorNpcId;
            talker.DisplayName = "ドームの扉";
            talker.PromptKey = "ui.interact.inspect";
            talker.PromptLabel = "調べる";
            talker.TurnSpeed = 0f;
            talker.InteractionRange = DoorRange;
        }

        /// <summary>木の扉と金属の枠、真鍮色の丸いノブ。当たり判定は外す（触れる判定は親の箱）。</summary>
        private static void AddParts(Transform door)
        {
            Material wood = MaterialLibrary.EnsureCampus("wood");
            Material metal = MaterialLibrary.EnsureCampus("metal_grey");

            AddCube(door, "panel", new Vector3(0f, DoorHeight * 0.5f, 0.03f),
                new Vector3(DoorWidth, DoorHeight, 0.06f), wood);
            AddCube(door, "frame_top", new Vector3(0f, DoorHeight + 0.04f, 0.03f),
                new Vector3(DoorWidth + 0.16f, 0.08f, 0.08f), metal);
            AddCube(door, "frame_left", new Vector3(-(DoorWidth * 0.5f + 0.04f), DoorHeight * 0.5f, 0.03f),
                new Vector3(0.08f, DoorHeight, 0.08f), metal);
            AddCube(door, "frame_right", new Vector3(DoorWidth * 0.5f + 0.04f, DoorHeight * 0.5f, 0.03f),
                new Vector3(0.08f, DoorHeight, 0.08f), metal);
            // 鍵穴の付いた板。ノブの下に小さく
            AddCube(door, "keyplate", new Vector3(DoorWidth * 0.5f - 0.12f, DoorHeight * 0.5f - 0.12f, 0.065f),
                new Vector3(0.06f, 0.12f, 0.01f), metal);

            GameObject knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "knob";
            Object.DestroyImmediate(knob.GetComponent<Collider>());
            knob.transform.SetParent(door, false);
            knob.transform.localPosition = new Vector3(DoorWidth * 0.5f - 0.12f, DoorHeight * 0.5f, 0.1f);
            knob.transform.localScale = Vector3.one * 0.07f;
            knob.GetComponent<MeshRenderer>().sharedMaterial = metal;
        }

        /// <summary>当たり判定を外した箱。要る所は呼んだ側で付け直す。</summary>
        private static GameObject AddCube(Transform parent, string name, Vector3 position, Vector3 size,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
        }
    }
}
