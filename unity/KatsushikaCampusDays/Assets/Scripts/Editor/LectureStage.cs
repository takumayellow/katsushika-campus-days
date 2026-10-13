using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// 講義棟の大ホールの教卓に立つ教授 (#176)。クエスト「はじめての講義」の最後に話しかける相手。
    /// 外の教授（prof）と同じ人なので見た目は prof を借り、会話 id だけ <see cref="DialogueId"/> にする
    /// （講義のあとの話題を外の教授の話題と混ぜないため）。
    /// 屋内の当たり判定を使って床に立たせるので、<see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class LectureStage
    {
        /// <summary>会話データの id（Dialogue/lecture.json）。</summary>
        public const string DialogueId = "lecture";

        /// <summary>見た目を借りる NPC。</summary>
        public const string BodyId = "prof";

        /// <summary>表示名。</summary>
        public const string NpcName = "教授";

        /// <summary>屋内の名前（InteriorStage が "Interior_" + id で置く）。</summary>
        public const string InteriorName = "Interior_lecture";

        /// <summary>教卓の位置の empty。</summary>
        public const string StageEmpty = "poi_lecture_hall_stage";

        /// <summary>客席の empty。教授はこちらを向く。</summary>
        public const string SeatsEmpty = "poi_lecture_hall_seats";

        /// <summary>教授を置く。屋内か教卓の empty が無ければ何もしない（屋内モデルが無いビルドでも壊れない）。</summary>
        public static GameObject Build(Transform root)
        {
            Transform interior = Find(root, InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(InteriorName + " が無いので、講義の教授は置きませんでした。");
                return null;
            }

            Transform stage = Find(interior, StageEmpty);
            if (stage == null)
            {
                EditorPaths.Report(StageEmpty + " が無いので、講義の教授は置きませんでした。");
                return null;
            }

            Physics.SyncTransforms();
            Vector3 position = CollectibleStage.IndoorFloor(stage.position);

            Transform seats = Find(interior, SeatsEmpty);
            float yaw = stage.eulerAngles.y;
            if (seats != null)
            {
                Vector3 toSeats = seats.position - position;
                toSeats.y = 0f;
                if (toSeats.sqrMagnitude > 0.01f)
                {
                    yaw = Mathf.Atan2(toSeats.x, toSeats.z) * Mathf.Rad2Deg;
                }
            }

            GameObject npc = ActorFactory.CreateNpc(interior, BodyId, NpcName, position, yaw, 0f);
            npc.name = "NPC_" + DialogueId;

            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = DialogueId;
                talker.DisplayName = NpcName;
            }

            // 教卓から動かさない。屋内に NavMesh は無い（DormStage.PlaceDormHead と同じ）。
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
            EditorPaths.Report("講義棟の教卓に教授を置きました: " + position.ToString("F1")
                + " 向き " + yaw.ToString("F0") + "°");
            return npc;
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
