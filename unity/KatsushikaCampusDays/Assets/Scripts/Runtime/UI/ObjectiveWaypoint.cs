using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 追跡中クエストの目的地を画面の上に出す印 (#163)。見えていれば目的地の上に輪と距離を、
    /// 画面の外や背後にあれば画面の端に向きの矢印と距離を出す。
    /// ミニマップは屋内の地点（キャンパスから離れた屋内の模型の中）を出せないので、屋内ではこれが唯一の案内になる。
    /// 屋内にいて目的地が外にあるときは、その建物の出口を指す。
    /// </summary>
    public sealed class ObjectiveWaypoint : MonoBehaviour
    {
        /// <summary>目的地の印を、地点の位置からどれだけ上に出すか (m)。人の頭の少し上。</summary>
        private const float RaiseMeters = 2.1f;

        /// <summary>これより近ければ着いたものとして出さない (m)。</summary>
        private const float HideWithinMeters = 2f;

        /// <summary>画面の端に出すとき、端からどれだけ内側に置くか（Canvas の単位）。</summary>
        private const float EdgeMargin = 72f;

        /// <summary>見えている目的地の印を上下に揺らす幅（Canvas の単位）と速さ（rad/s）。</summary>
        private const float BobAmplitude = 6f;
        private const float BobSpeed = 3f;

        [SerializeField] private RectTransform _area;
        [SerializeField] private RectTransform _marker;
        [SerializeField] private Image _ring;
        [SerializeField] private RectTransform _arrow;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Transform _target;

        private Camera _camera;

        /// <summary>画面の上の置き場。Position は画面中央からのずれ（Canvas の単位）、Angle は向き（度、右が 0・上が 90）。</summary>
        public struct Placement
        {
            public bool OnScreen;
            public Vector2 Position;
            public float Angle;
        }

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(RectTransform area, RectTransform marker, Image ring, RectTransform arrow, TMP_Text label,
            Transform target)
        {
            _area = area;
            _marker = marker;
            _ring = ring;
            _arrow = arrow;
            _label = label;
            _target = target;
        }

        /// <summary>
        /// ビューポート座標（x, y は 0..1、z はカメラからの奥行き）を、画面の上の置き場にする。
        /// 画面の内側（端から margin 以上内側）で、かつカメラの前にあれば、その位置にそのまま置く。
        /// そうでなければ、画面中央から目的地の向きに伸ばした線が画面の端（margin の内側）と交わる所に置く。
        /// 背後にある点は、ビューポート座標が上下左右とも反転して返るので、向きを反転してから端へ寄せる。
        /// </summary>
        public static Placement Place(Vector3 viewport, Vector2 areaSize, float margin)
        {
            var offset = new Vector2((viewport.x - 0.5f) * areaSize.x, (viewport.y - 0.5f) * areaSize.y);
            bool behind = viewport.z < 0f;
            if (behind)
            {
                offset = -offset;
            }

            var limit = new Vector2(Mathf.Max(1f, areaSize.x * 0.5f - margin), Mathf.Max(1f, areaSize.y * 0.5f - margin));
            if (!behind && Mathf.Abs(offset.x) <= limit.x && Mathf.Abs(offset.y) <= limit.y)
            {
                return new Placement { OnScreen = true, Position = offset, Angle = -90f };
            }

            // 真後ろで向きが決まらないときは下を指す（振り返れ、の意味）。
            if (offset.sqrMagnitude < 1e-6f)
            {
                offset = Vector2.down;
            }

            float scaleX = Mathf.Abs(offset.x) > 1e-6f ? limit.x / Mathf.Abs(offset.x) : float.MaxValue;
            float scaleY = Mathf.Abs(offset.y) > 1e-6f ? limit.y / Mathf.Abs(offset.y) : float.MaxValue;
            return new Placement
            {
                OnScreen = false,
                Position = offset * Mathf.Min(scaleX, scaleY),
                Angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg
            };
        }

        private void LateUpdate()
        {
            if (!TryGoal(out Vector3 goal, out bool toExit))
            {
                Hide();
                return;
            }

            Vector3 delta = goal - _target.position;
            delta.y = 0f;
            float meters = delta.magnitude;
            if (meters < HideWithinMeters)
            {
                Hide();
                return;
            }

            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    Hide();
                    return;
                }
            }

            Vector3 viewport = _camera.WorldToViewportPoint(goal + Vector3.up * RaiseMeters);
            Placement placement = Place(viewport, _area.rect.size, EdgeMargin);

            float bob = placement.OnScreen ? BobAmplitude * Mathf.Sin(Time.unscaledTime * BobSpeed) : 0f;
            _marker.anchoredPosition = placement.Position + new Vector2(0f, bob);
            _marker.gameObject.SetActive(true);

            if (_ring != null)
            {
                _ring.enabled = placement.OnScreen;
            }

            if (_arrow != null)
            {
                _arrow.gameObject.SetActive(!placement.OnScreen);
                // 矢印の絵は上向き（Angle = 90）なので、90 度引いて回す。
                _arrow.localRotation = Quaternion.Euler(0f, 0f, placement.Angle - 90f);
            }

            if (_label != null)
            {
                string distance = L.Format("ui.waypoint.distance", Mathf.RoundToInt(meters));
                _label.text = toExit ? L.Get("ui.waypoint.exit", "出口") + "  " + distance : distance;
            }
        }

        /// <summary>
        /// 今指す先。追跡中のステップが無い・目的地が見つからない・会話や写真で HUD を隠しているときは false。
        /// 屋内にいて目的地がキャンパスにあるなら、その建物の出口を指す（toExit = true）。
        /// </summary>
        private bool TryGoal(out Vector3 goal, out bool toExit)
        {
            goal = Vector3.zero;
            toExit = false;
            if (_target == null || _area == null || _marker == null || KCDInput.PhotoMode)
            {
                return false;
            }

            QuestStep step = GameManager.Instance != null ? GameManager.Instance.Quests?.TrackedQuest?.CurrentStep : null;
            if (step == null || !QuestObjectiveLocator.TryLocate(step, _target.position, out goal))
            {
                return false;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null || !loader.IsInside || Minimap.Instance == null || !Minimap.Instance.IsOnMap(goal))
            {
                return true;
            }

            toExit = true;
            return QuestObjectiveLocator.TryLocateExit(loader.CurrentId, _target.position, out goal);
        }

        private void Hide()
        {
            if (_marker != null && _marker.gameObject.activeSelf)
            {
                _marker.gameObject.SetActive(false);
            }
        }
    }
}
