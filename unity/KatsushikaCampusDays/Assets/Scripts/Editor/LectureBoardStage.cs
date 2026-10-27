using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// サブクエスト「黒板の 1 バイト」（q_sq_hello_world）の板書と、板を調べる判定を置く (#389)。
    ///
    /// 講義棟の 203・302・402・502・602 の黒板の右の端に、教授が残した 0 と 1 の 8 けたをチョークの色で貼る。
    /// 7F の廊下の白板には HELLO, と、これまでの人が書き足した WORLD の並びを貼り、クエストを終えたら
    /// 自分の WORLD の行が出る（<see cref="QuestDoneReveal"/>）。
    /// 板の位置と向きは屋内モデルの empty（board_lecture_&lt;室&gt; が板の面の中心、_f がその 1 m 手前）から取る
    /// （blender/kcd_interior/plan_lecture_rooms.py の _board）。
    /// 文字のマテリアルはフォントの既定マテリアルを共有させる（CampusProps.SignFaces と同じ理由, #64）。
    /// 屋内を使うので <see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class LectureBoardStage
    {
        public const string InteriorName = "Interior_lecture";
        public const string EmptyPrefix = "board_lecture_";
        public const string FrontSuffix = "_f";
        public const string TalkerPrefix = "Talker_board_";
        public const string QuestId = "q_sq_hello_world";

        /// <summary>白板の、自分が書き足す行の名前。クエストを終えるまで隠す。</summary>
        public const string MineName = "BoardText_mine";

        /// <summary>
        /// 板書のある板と、その文字。8 けたの並びと次の板は、会話 board_&lt;室&gt;.json の板の行（written）と同じにしておく
        /// （案内の言い回しは板に収まるよう縮めてある）。
        /// </summary>
        public static readonly (string Key, string Chalk)[] Boards =
        {
            ("203", "01001000\nA = 01000001\n(1/5)  → 302"),
            ("302", "01000101\n(2/5)  → 402"),
            ("402", "01001100\n(3/5)  → 502"),
            ("502", "01001100\n(4/5)  → 602"),
            ("602", "01001111\n(5/5)  → 7F"),
        };

        public const string HallKey = "7F";

        private const string HallText = "HELLO,\nWORLD   world!   WORLD\nＷｏｒｌｄ   WORLD.\nWORLD ―― そら";
        private const string MineText = "WORLD";

        /// <summary>黒板（幅 7.2 m）のうち板書を貼る所。見る人から右へずらした枠の中心（m）。</summary>
        private const float ChalkSideOffset = 2.2f;

        private static readonly Vector2 ChalkFrame = new Vector2(2.4f, 1.0f);

        /// <summary>白板（2.4 x 1.2 m）の枠。上に既にある行、下に自分の行。</summary>
        private static readonly Vector2 HallFrame = new Vector2(2.2f, 0.8f);

        private static readonly Vector2 MineFrame = new Vector2(2.2f, 0.24f);
        private const float HallTextLift = 0.12f;
        private const float MineDrop = 0.44f;

        /// <summary>板の面から文字を浮かせる距離（m）。同じ奥行きだと面と文字がちらつく。</summary>
        private const float FaceGap = 0.01f;

        private static readonly Color32 Chalk = new Color32(236, 238, 228, 255);
        private static readonly Color32 Marker = new Color32(28, 40, 86, 255);
        private static readonly Color32 MineMarker = new Color32(170, 36, 36, 255);

        /// <summary>板を調べる判定の箱（板に沿った幅・高さ・板から手前への奥行き、m）と、E が出る距離。</summary>
        private static readonly Vector3 TriggerSize = new Vector3(2.6f, 1.4f, 0.6f);

        private const float InspectRange = 2.5f;

        public static void Build(Transform root)
        {
            Transform interior = Find(root, InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(InteriorName + " が無いので、黒板の板書は置きませんでした。");
                return;
            }

            TMP_FontAsset font = FontLibrary.Ensure();
            if (font == null)
            {
                EditorPaths.Report("日本語フォントが無いので、黒板の板書は置きませんでした。");
                return;
            }

            int placed = 0;
            foreach ((string key, string chalk) in Boards)
            {
                if (TryFrame(interior, key, out Vector3 face, out Vector3 facing))
                {
                    Vector3 center = face + ViewerRight(facing) * ChalkSideOffset;
                    Transform board = Group(interior, key, center, facing);
                    AddText(board, "BoardText", font, chalk, Vector3.zero, ChalkFrame, Chalk);
                    AddTalker(board, key, "黒板");
                    placed++;
                }
            }

            if (TryFrame(interior, HallKey, out Vector3 hall, out Vector3 hallFacing))
            {
                Transform board = Group(interior, HallKey, hall, hallFacing);
                AddText(board, "BoardText", font, HallText, Vector3.up * HallTextLift, HallFrame, Marker);
                GameObject mine = AddText(board, MineName, font, MineText, Vector3.down * MineDrop, MineFrame, MineMarker);
                mine.SetActive(false);
                QuestDoneReveal reveal = board.gameObject.AddComponent<QuestDoneReveal>();
                reveal.QuestId = QuestId;
                reveal.Target = mine;
                AddTalker(board, HallKey, "白板");
                placed++;
            }

            EditorPaths.Report("講義棟の板書を " + placed + "/" + (Boards.Length + 1) + " 枚に置きました。");
        }

        /// <summary>板の面の中心と、板が向く向き（水平）。empty が無ければ false。</summary>
        internal static bool TryFrame(Transform interior, string key, out Vector3 face, out Vector3 facing)
        {
            face = Vector3.zero;
            facing = Vector3.forward;
            Transform board = Find(interior, EmptyPrefix + key);
            Transform front = Find(interior, EmptyPrefix + key + FrontSuffix);
            if (board == null || front == null)
            {
                EditorPaths.Report(EmptyPrefix + key + " か " + FrontSuffix + " が無いので、この板の板書は置きませんでした。");
                return false;
            }

            Vector3 toFront = front.position - board.position;
            toFront.y = 0f;
            if (toFront.sqrMagnitude < 0.01f)
            {
                EditorPaths.Report(EmptyPrefix + key + " の向きが解けないので、この板の板書は置きませんでした。");
                return false;
            }

            face = board.position;
            facing = toFront.normalized;
            return true;
        }

        /// <summary>板の前に立って板を見る人の右。</summary>
        public static Vector3 ViewerRight(Vector3 facing)
        {
            return Vector3.Cross(Vector3.up, -facing).normalized;
        }

        /// <summary>板書と判定をまとめる親。原点は板の面の上、+Z は板の手前（部屋の側）。</summary>
        private static Transform Group(Transform interior, string key, Vector3 center, Vector3 facing)
        {
            var go = new GameObject(TalkerPrefix + key);
            go.transform.SetParent(interior, true);
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            return go.transform;
        }

        /// <summary>板と平行な文字。TextMeshPro はローカルの −Z の側から読めるので、+Z を板の奥へ向ける。</summary>
        private static GameObject AddText(Transform board, string name, TMP_FontAsset font, string body,
            Vector3 offset, Vector2 frame, Color32 ink)
        {
            var go = new GameObject(name);
            go.transform.SetParent(board, false);
            go.transform.localPosition = offset + Vector3.forward * FaceGap;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.alignment = TextAlignmentOptions.Center;
            text.color = ink;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.8f;
            text.fontSizeMax = 3.2f;
            text.fontSize = 3.2f;
            text.rectTransform.sizeDelta = frame;
            text.text = body;
            return go;
        }

        /// <summary>
        /// 板を調べる判定。InteractionPrompt は Interactable のレイヤーの当たり判定を球で探すので、板の手前に薄い見えない箱を置く。
        /// 会話 id は board_&lt;室&gt;（クエストの talk 先でもある）。
        /// </summary>
        private static void AddTalker(Transform board, string key, string displayName)
        {
            var box = board.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0f, TriggerSize.z * 0.5f);
            box.size = TriggerSize;

            NPCTalker talker = board.gameObject.AddComponent<NPCTalker>();
            talker.NpcId = "board_" + key;
            talker.DisplayName = displayName;
            talker.PromptKey = "ui.interact.inspect";
            talker.PromptLabel = "調べる";
            talker.TurnSpeed = 0f;
            talker.InteractionRange = InspectRange;
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
