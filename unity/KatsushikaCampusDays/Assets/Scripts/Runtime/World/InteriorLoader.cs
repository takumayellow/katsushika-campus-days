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
    /// </summary>
    public sealed class InteriorLoader : MonoBehaviour
    {
        /// <summary>1 つの建物の屋内。</summary>
        [System.Serializable]
        public sealed class Entry
        {
            public string Id;
            public string DisplayName;
            public Transform Spawn;
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

        /// <summary>最後に外へ出た時刻。入口の再発火を抑えるのに使う。</summary>
        public float LastExitAt { get; private set; } = -999f;

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

        /// <summary>建物へ入る。プレイヤーの今の位置と向きを覚えておき、出るときに戻す。</summary>
        public void Enter(string buildingId)
        {
            Entry entry = Find(buildingId);
            if (entry == null || _busy || IsInside)
            {
                return;
            }

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null || entry.Spawn == null)
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

            StartCoroutine(Travel(player, entry.Spawn.position, entry.Spawn.eulerAngles.y, () =>
            {
                CurrentId = entry.Id;
                Minimap.Instance?.SetIndoor(entry.DisplayName, entry.EntranceWorld);
                QuestObjectiveLocator.Invalidate();
                string name = L.Get("ui.building." + entry.Id, entry.DisplayName);
                HUD.Instance?.ShowToast(L.Pick(
                    name + " の中に入った。出口は入ってきた扉。",
                    "Entered " + name + ". The way out is the door you came in through."));
            }));
        }

        /// <summary>外へ出る。</summary>
        public void Exit()
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

            StartCoroutine(Travel(player, _returnPosition, _returnYaw, () =>
            {
                CurrentId = string.Empty;
                LastExitAt = Time.time;
                Minimap.Instance?.ClearIndoor();
                QuestObjectiveLocator.Invalidate();
            }));
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
