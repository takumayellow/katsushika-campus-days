using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// サブクエスト「306 の居残り」（q_sq_first_program）の TA と、最前列の PC を調べる判定を置く (#392)。
    ///
    /// 306 は講義棟 3F の PC ルーム（blender/kcd_interior/plan_lecture_rooms.py の _type_b）。東の壁に白板、
    /// その 1.3 m 手前に教員の机、2.2 m 手前から PC の机の列が並ぶ。位置は白板の empty（board_lecture_306 と _f）から取り、
    /// TA は教員の机の脇に、PC の判定は最前列の机（白板の中心から見る人の右へ 1.6 m の列）に置く。
    /// 屋内の当たり判定を使うので <see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class LectureTaStage
    {
        public const string InteriorName = "Interior_lecture";
        public const string RoomKey = "306";

        /// <summary>TA の会話 id（Dialogue/ta_306.json）。見た目は男子学生を借りる（院生）。</summary>
        public const string TaDialogueId = "ta_306";

        public const string TaBodyId = "student_m";
        public const string TaName = "TA";

        /// <summary>PC の会話 id（Dialogue/pc_306.json）と判定の名前。</summary>
        public const string PcDialogueId = "pc_306";

        public const string PcTalkerName = "Talker_pc_306";

        /// <summary>白板の面の中心から床まで（_type_b の whiteboard の z + 1.5）。</summary>
        private const float BoardCenterHeight = 1.5f;

        /// <summary>TA の立ち位置: 白板の面から手前へ（教員の机の中心と同じ）、見る人の左へ（机の端の外）。</summary>
        private const float TaFront = 1.3f;

        private const float TaLeft = 1.4f;

        /// <summary>PC の判定: 白板の面から手前へ（最前列の机の座る側の縁）、見る人の右へ（z 41.1 の机）。</summary>
        private const float PcFront = 2.7f;

        private const float PcRight = 1.6f;

        /// <summary>判定の箱（机に沿った幅・高さ・奥行き、m）と、その中心の床からの高さ、E が出る距離。</summary>
        private static readonly Vector3 PcTriggerSize = new Vector3(1.4f, 1.0f, 0.9f);

        private const float PcTriggerLift = 0.9f;
        private const float InspectRange = 2.5f;

        public static void Build(Transform root)
        {
            Transform interior = Find(root, InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(InteriorName + " が無いので、306 の TA と PC は置きませんでした。");
                return;
            }

            if (!LectureBoardStage.TryFrame(interior, RoomKey, out Vector3 face, out Vector3 facing))
            {
                return;
            }

            Physics.SyncTransforms();
            Vector3 floor = face + Vector3.down * BoardCenterHeight;
            Vector3 right = LectureBoardStage.ViewerRight(facing);

            PlaceTa(interior, CollectibleStage.IndoorFloor(floor + facing * TaFront - right * TaLeft), facing);
            PlacePc(interior, CollectibleStage.IndoorFloor(floor + facing * PcFront + right * PcRight), facing);
        }

        /// <summary>TA。部屋（PC の机の列）を向いて立ち、動かない（屋内に NavMesh は無い）。</summary>
        private static void PlaceTa(Transform interior, Vector3 position, Vector3 facing)
        {
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            GameObject npc = ActorFactory.CreateNpc(interior, TaBodyId, TaName, position, yaw, 0f);
            npc.name = "NPC_" + TaDialogueId;

            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = TaDialogueId;
                talker.DisplayName = TaName;
            }

            NPCWander wander = npc.GetComponent<NPCWander>();
            if (wander != null)
            {
                Object.DestroyImmediate(wander);
            }

            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                Object.DestroyImmediate(agent);
            }

            CampusStage.Ignore(npc);
            EditorPaths.Report("306 に TA を置きました: " + position.ToString("F1") + " 向き " + yaw.ToString("F0") + "°");
        }

        /// <summary>
        /// 最前列の PC を調べる判定。見えない箱を机の上に置く（LectureBoardStage の板の判定と同じ作り）。
        /// 原点は机の座る側の縁の床、+Z は白板の手前（部屋の奥）の向き。
        /// </summary>
        private static void PlacePc(Transform interior, Vector3 position, Vector3 facing)
        {
            var go = new GameObject(PcTalkerName);
            go.transform.SetParent(interior, true);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, PcTriggerLift, 0f);
            box.size = PcTriggerSize;

            NPCTalker talker = go.AddComponent<NPCTalker>();
            talker.NpcId = PcDialogueId;
            talker.DisplayName = "PC";
            talker.PromptKey = "ui.interact.inspect";
            talker.PromptLabel = "調べる";
            talker.TurnSpeed = 0f;
            talker.InteractionRange = InspectRange;
            EditorPaths.Report("306 の最前列に PC の判定を置きました: " + position.ToString("F1"));
        }

        private static Transform Find(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
