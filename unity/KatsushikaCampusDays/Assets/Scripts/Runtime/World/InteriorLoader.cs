using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 建物の出入り。屋内モデルはキャンパスの遠くに並べてあるので、シーンを切り替えずに
    /// 暗転してワープするだけで済む。入口を踏むと Enter、屋内の exit を踏むと Exit。
    /// 登録（どの建物がどこにあるか）は SceneBuilder がコードで張る。
    /// 扉が 2 つある建物（講義棟の玄関と南東の玄関, <see cref="BuildingDoors"/>）では、入った扉の内側に立ち、
    /// 出るときは踏んだ出口の扉の外へ戻る。
    /// </summary>
    public sealed class InteriorLoader : MonoBehaviour
    {
        /// <summary>
        /// 建物の扉 1 つ。屋内で入った直後に立つ所と、その扉から出たときに立つ外の所。
        /// 外の所は扉の外面から <see cref="OutsideStep"/> m 外で、建物に背を向ける。
        /// </summary>
        [System.Serializable]
        public sealed class Door
        {
            /// <summary>扉の id（玄関は建物の id と同じ, BuildingDoors）。</summary>
            public string DoorId;

            /// <summary>この扉から入った直後に立つ所。</summary>
            public Transform Spawn;

            /// <summary>外の所が分かっているか（キャンパスに Entrance_&lt;扉の id&gt; があったか）。</summary>
            public bool HasOutside;

            /// <summary>この扉から出たときに立つ外の所。</summary>
            public Vector3 OutsidePosition;

            /// <summary>外の所で向く向き（建物の外向き）。</summary>
            public float OutsideYaw;
        }

        /// <summary>扉から出たときに立つ所の、扉の外面からの距離（m）。入るときに覚える戻り先と同じ 2 m。</summary>
        public const float OutsideStep = 2f;

        /// <summary>1 つの建物の屋内。</summary>
        [System.Serializable]
        public sealed class Entry
        {
            public string Id;
            public string DisplayName;

            /// <summary>玄関から入った直後に立つ所。扉ごとの立ち位置が無いときもここに立つ。</summary>
            public Transform Spawn;

            /// <summary>
            /// 扉ごとの立ち位置と外の所（玄関を含む）。階段で別の建物から移ってきたときは、玄関の外の所を戻り先にする（<see cref="MoveAcross"/>）。
            /// 空でもよく、そのときは入ったときに覚えた戻り先へ出る。
            /// </summary>
            public List<Door> Doors = new List<Door>();

            /// <summary>
            /// キャンパス側の入口の位置。ステージが書き込むが、いまは読む側がない
            /// （目的地の案内は QuestObjectiveLocator が入口と InteriorExit を直接探す）。
            /// </summary>
            public Vector3 EntranceWorld;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [SerializeField] private float _fadeSeconds = 0.35f;

        private CanvasGroup _fade;
        private bool _busy;
        private Vector3 _returnPosition;
        private float _returnYaw;

        /// <summary>現在のシーンの出入り係。</summary>
        public static InteriorLoader Instance { get; private set; }

        /// <summary>今いる建物の id。外なら空。</summary>
        public string CurrentId { get; private set; } = string.Empty;

        /// <summary>建物の中にいるか。</summary>
        public bool IsInside => !string.IsNullOrEmpty(CurrentId);

        /// <summary>ミニマップの見出しに出している階（#275）。階の無い建物と外では空。</summary>
        public string CurrentFloor { get; private set; } = string.Empty;

        /// <summary>最後に外へ出た時刻。入口の再発火を抑えるのに使う。</summary>
        public float LastExitAt { get; private set; } = -999f;

        /// <summary>入ったときに覚えた戻り先。建物の中でセーブするとき一緒に書く (#61)。</summary>
        public Vector3 ReturnPosition => _returnPosition;

        /// <summary>戻り先で向く向き。</summary>
        public float ReturnYaw => _returnYaw;

        /// <summary>登録されている建物の屋内。開発者用ワープメニューの一覧に使う。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>出入りの暗転の途中か。</summary>
        public bool IsBusy => _busy;

        /// <summary>SceneBuilder から。</summary>
        public void Register(Entry entry)
        {
            _entries.Add(entry);
        }

        /// <summary>この建物に屋内があるか。</summary>
        public bool HasInterior(string buildingId)
        {
            return Find(buildingId) != null;
        }

        private void Awake()
        {
            Instance = this;
            _fade = BuildFadeOverlay();
        }

        /// <summary>
        /// 暗転の途中で止められた（シーンの切り替え・オブジェクトの無効化）ときは、コルーチンも止まるので
        /// 自分の封鎖と暗転をここで外す。外さないと操作が止まったままになる (#40)。
        /// </summary>
        private void OnDisable()
        {
            KCDInput.Unblock(this);
            if (_busy && _fade != null)
            {
                _fade.alpha = 0f;
            }

            _busy = false;
        }

        private void OnDestroy()
        {
            KCDInput.Unblock(this);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 建物へ入る。プレイヤーの今の位置と向きを覚えておき、出るときに戻す。
        /// doorId を渡すとその扉の内側に立つ（省くか登録の無い扉なら玄関の内側）。
        /// </summary>
        public void Enter(string buildingId, string doorId = null)
        {
            Entry entry = Find(buildingId);
            if (entry == null || _busy || IsInside)
            {
                return;
            }

            Door door = FindDoor(entry, doorId);
            Transform spawn = door != null && door.Spawn != null ? door.Spawn : entry.Spawn;
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null || spawn == null)
            {
                Debug.LogWarning("[KCD] 屋内へ入れません: " + buildingId);
                return;
            }

            // 出るときは入った地点から 2 m 手前（外側）に、建物へ背を向けて立つ。
            Vector3 back = -player.transform.forward;
            back.y = 0f;
            back.Normalize();
            _returnPosition = player.transform.position + back * 2f;
            _returnYaw = player.transform.eulerAngles.y + 180f;

            StartCoroutine(Travel(player, spawn.position, spawn.eulerAngles.y, () =>
            {
                MarkInside(entry);

                // 書くのは暗転が明けて封鎖が外れてから（AutoSave）。ここでは頼むだけ (#61)。
                AutoSave.Request();
                string name = L.Get("ui.building." + entry.Id, entry.DisplayName);
                HUD.Instance?.ShowToast(entry.Doors.Count > 1
                    ? L.Pick(name + " の中に入った。どの玄関からも外へ出られる。",
                        "Entered " + name + ". You can leave through any of its entrances.")
                    : L.Pick(name + " の中に入った。出口は入ってきた扉。",
                        "Entered " + name + ". The way out is the door you came in through."));
            }));
        }

        /// <summary>
        /// 外へ出る。doorId は踏んだ出口の扉。入ったときと別の扉から出たら、その扉の外 2 m に建物へ背を向けて立つ。
        /// 同じ扉から出たとき・扉を省いたときは、入ったときに覚えた戻り先へ戻る。
        /// </summary>
        public void Exit(string doorId = null)
        {
            if (!IsInside || _busy)
            {
                return;
            }

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                return;
            }

            Vector3 position = _returnPosition;
            float yaw = _returnYaw;
            Entry entry = Find(CurrentId);
            if (entry != null && LeavesByOtherDoor(entry.Doors, doorId, _returnPosition, out Door door))
            {
                position = door.OutsidePosition;
                yaw = door.OutsideYaw;
            }

            StartCoroutine(Travel(player, position, yaw, () =>
            {
                MarkOutside();
                AutoSave.Request();
            }));
        }

        /// <summary>
        /// 同じ建物の中で別の階へ移る（エレベーター, #275）。入ったときに覚えた戻り先はそのまま。
        /// 見出しの階は InteriorFloorLights が足の高さから <see cref="ShowFloor"/> で付ける。
        /// 外にいるとき・暗転の途中は何もせず false。
        /// </summary>
        public bool MoveWithin(Vector3 position, float yaw)
        {
            Entry entry = Find(CurrentId);
            if (entry == null || _busy)
            {
                return false;
            }

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                return false;
            }

            StartCoroutine(Travel(player, position + Vector3.up * 0.15f, yaw, AutoSave.Request));
            return true;
        }

        /// <summary>
        /// 階段で別の建物の屋内へ移る（第2研究棟の西の端の階段と食堂の 1F・2F, #303）。着いたらその建物の中にいることにし、
        /// 出たときの戻り先をその建物の玄関の外にする（玄関の外の所が分からなければ戻り先はそのまま）。
        /// 玄関から入ったときと同じく、入ったことをクエストと 1 日の記録（DayStats）に知らせる。
        /// 同じ建物なら <see cref="MoveWithin"/> と同じ。外にいるとき・屋内の無い建物・暗転の途中は何もせず false。
        /// </summary>
        public bool MoveAcross(string buildingId, Vector3 position, float yaw)
        {
            if (buildingId == CurrentId)
            {
                return MoveWithin(position, yaw);
            }

            Entry to = Find(buildingId);
            if (Find(CurrentId) == null || to == null || _busy)
            {
                return false;
            }

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                return false;
            }

            // 玄関から入ったとき（EntranceTrigger）と同じく、その建物に入ったことをクエストと 1 日の記録に知らせる。
            GameManager.Instance.Quests?.ReportEnter(to.Id);
            DayStats.NoteEnter(to.Id);
            StartCoroutine(Travel(player, position + Vector3.up * 0.15f, yaw, () =>
            {
                Door main = FindDoor(to, to.Id);
                if (main != null && main.HasOutside)
                {
                    _returnPosition = main.OutsidePosition;
                    _returnYaw = main.OutsideYaw;
                }

                MarkInside(to);
                AutoSave.Request();
            }));
            return true;
        }

        /// <summary>
        /// ミニマップの見出しを「建物名 階」にする（#275）。入ったとき・エレベーターで着いたとき・
        /// セーブを読んで戻ったときのどれでも、InteriorFloorLights がいる階に合わせて呼ぶ。外では何もしない。
        /// </summary>
        public void ShowFloor(string floor)
        {
            Entry entry = Find(CurrentId);
            if (entry == null)
            {
                return;
            }

            CurrentFloor = floor ?? string.Empty;
            Minimap.Instance?.SetIndoor(string.IsNullOrEmpty(CurrentFloor)
                ? entry.DisplayName
                : entry.DisplayName + " " + CurrentFloor);
        }

        /// <summary>
        /// セーブを読んで建物の中に戻すとき (#61)。プレイヤーを動かすのは呼び出し側で、ここは
        /// 「どの建物の中にいて、出たらどこへ戻るか」とミニマップ・目的地の表示だけをそろえる。
        /// 暗転の途中なら打ち切る。その建物の屋内がこのシーンに無ければ何もせず false。
        /// </summary>
        public bool RestoreInside(string buildingId, Vector3 returnPosition, float returnYaw)
        {
            Entry entry = Find(buildingId);
            if (entry == null)
            {
                return false;
            }

            CancelTravel();
            _returnPosition = returnPosition;
            _returnYaw = returnYaw;
            MarkInside(entry);
            return true;
        }

        /// <summary>
        /// セーブを読んで外に立たせるとき。建物の中にいたら、中にいる扱いをやめる（ワープはしない）。
        /// これをしないと、外に立っているのに屋内の照明・ミニマップ・BGM のまま残る。
        /// 出た直後の自動セーブは入口の 2 m 手前に立っているので、出たときと同じく 3 秒は入口を閉じておく。
        /// </summary>
        public void RestoreOutside()
        {
            CancelTravel();
            if (IsInside)
            {
                MarkOutside();
            }
            else
            {
                LastExitAt = Time.time;
            }
        }

        private void MarkInside(Entry entry)
        {
            CurrentId = entry.Id;
            CurrentFloor = string.Empty;
            Minimap.Instance?.SetIndoor(entry.DisplayName);
            QuestObjectiveLocator.Invalidate();
        }

        private void MarkOutside()
        {
            CurrentId = string.Empty;
            CurrentFloor = string.Empty;
            LastExitAt = Time.time;
            Minimap.Instance?.ClearIndoor();
            QuestObjectiveLocator.Invalidate();
        }

        /// <summary>出入りの暗転を途中で打ち切る。封鎖と暗転を外す（OnDisable と同じ後始末）。</summary>
        private void CancelTravel()
        {
            if (!_busy)
            {
                return;
            }

            StopAllCoroutines();
            KCDInput.Unblock(this);
            if (_fade != null)
            {
                _fade.alpha = 0f;
            }

            _busy = false;
        }

        /// <summary>
        /// 出口の扉 exitDoorId が、入ってきた扉と別の扉か。入ってきた扉は覚えておかず、戻り先（入った扉の外 2 m）に
        /// いちばん近い扉の外の所で決める。こうすればセーブから建物の中に戻ったときも、セーブの形を変えずに同じ答えになる。
        /// 別の扉なら true で、door にその扉を返す。扉の登録が 1 つ以下・出口の扉が登録に無い・外の所が分からないときは false。
        /// </summary>
        public static bool LeavesByOtherDoor(IReadOnlyList<Door> doors, string exitDoorId, Vector3 returnPosition,
            out Door door)
        {
            door = null;
            if (doors == null || doors.Count < 2 || string.IsNullOrEmpty(exitDoorId))
            {
                return false;
            }

            Door exit = null;
            Door nearest = null;
            float best = float.MaxValue;
            foreach (Door each in doors)
            {
                if (each == null || !each.HasOutside)
                {
                    continue;
                }

                if (each.DoorId == exitDoorId)
                {
                    exit = each;
                }

                Vector3 d = each.OutsidePosition - returnPosition;
                d.y = 0f;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    nearest = each;
                }
            }

            if (exit == null || exit == nearest)
            {
                return false;
            }

            door = exit;
            return true;
        }

        private static Door FindDoor(Entry entry, string doorId)
        {
            if (entry.Doors == null || string.IsNullOrEmpty(doorId))
            {
                return null;
            }

            foreach (Door door in entry.Doors)
            {
                if (door != null && door.DoorId == doorId)
                {
                    return door;
                }
            }

            return null;
        }

        private Entry Find(string buildingId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == buildingId)
                {
                    return _entries[i];
                }
            }

            return null;
        }

        private IEnumerator Travel(PlayerController player, Vector3 position, float yaw, System.Action arrived)
        {
            _busy = true;

            // 自分の封鎖だけを掛けて外す。暗転中に開いた会話・ポーズ・落下からの復帰の封鎖は残す (#40)。
            KCDInput.Block(this);
            AudioManager.Instance?.PlaySe("door_open");

            yield return Fade(1f);

            player.Teleport(position, yaw);
            Physics.SyncTransforms();
            arrived?.Invoke();
            CameraRig.SnapBehind(player.transform);
            AudioManager.Instance?.PlaySe("door_close", 0.8f);

            yield return null;
            yield return Fade(0f);

            KCDInput.Unblock(this);
            _busy = false;
        }

        private IEnumerator Fade(float target)
        {
            if (_fade == null)
            {
                yield break;
            }

            float start = _fade.alpha;
            float elapsed = 0f;
            while (elapsed < _fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _fade.alpha = Mathf.Lerp(start, target, elapsed / _fadeSeconds);
                yield return null;
            }

            _fade.alpha = target;
        }

        /// <summary>暗転用の真っ黒な板。HUD より手前に出す。</summary>
        private CanvasGroup BuildFadeOverlay()
        {
            var go = new GameObject("FadeCanvas");
            go.transform.SetParent(transform, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var panel = new GameObject("Black", typeof(RectTransform));
            panel.transform.SetParent(go.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = panel.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return group;
        }
    }
}
