using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 歩ける範囲の見張り。キャンパスの地面（OuterGround）は ±350 m までしかなく、その外は床が無い。
    /// 範囲の縁には WorldBoundsStage が見えない壁を立てるので普段はそこで止まる。
    /// それでも外へ出たり、地面の継ぎ目から下へ落ちたりしたら、暗転して最後に立っていた安全な位置へ戻す（#40）。
    /// 建物の中（x ≥ 1200 m に並べた屋内）にいる間は見ない。
    /// </summary>
    public sealed class WorldBounds : MonoBehaviour
    {
        /// <summary>
        /// 歩ける範囲の半幅（m）。壁と見張りの矩形はどちらもこの値から作る。
        /// 隠しエンド「学校をサボる」でキャンパスの外の道を歩けるようにするときは、ここを広げる（地面も広げること）。
        /// </summary>
        public const float HalfExtent = 340f;

        /// <summary>これより下に落ちたら戻す（m）。キャンパスの地面は y = -0.3 前後。</summary>
        public const float DefaultKillY = -15f;

        /// <summary>安全な位置として覚えてよい高さの下限（m）。</summary>
        public const float SafeMinY = -2f;

        /// <summary>安全な位置を覚え直す間隔（秒）。</summary>
        public const float SampleInterval = 0.5f;

        /// <summary>戻した直後に地面へ食い込まないよう少し持ち上げる量（m）。</summary>
        public const float ReturnLift = 0.1f;

        public const string ToastKey = "ui.hud.returned_to_campus";
        public const string ToastFallback = "道に迷ったので、キャンパスに戻ってきた";

        /// <summary>既定の範囲。xz 平面の矩形で、Rect の x がワールド x（東）、y がワールド z（北）。</summary>
        public static Rect DefaultArea => new Rect(-HalfExtent, -HalfExtent, HalfExtent * 2f, HalfExtent * 2f);

        [SerializeField] private Rect _area = DefaultArea;
        [SerializeField] private float _killY = DefaultKillY;
        [SerializeField] private float _fadeSeconds = 0.35f;

        private PlayerController _player;
        private CanvasGroup _fade;
        private bool _recovering;
        private bool _hasSafe;
        private Vector3 _safePosition;
        private bool _hasStart;
        private Vector3 _startPosition;
        private float _sampleTimer;

        /// <summary>見張っている範囲（xz）。</summary>
        public Rect Area => _area;

        /// <summary>これより下は落下とみなす高さ。</summary>
        public float KillY => _killY;

        /// <summary>いま戻している最中か。</summary>
        public bool IsRecovering => _recovering;

        /// <summary>xz が範囲の中か。y は見ない。</summary>
        public static bool IsWithin(Vector3 position, Rect area)
        {
            return area.Contains(new Vector2(position.x, position.z));
        }

        /// <summary>
        /// 戻すべきか。y が killY より下、または範囲の外なら true。建物の中では常に false。
        /// NaN の座標は範囲に入らないので戻す側になる。
        /// </summary>
        public static bool ShouldRecover(Vector3 position, Rect area, float killY, bool inside)
        {
            if (inside)
            {
                return false;
            }

            return !(position.y >= killY) || !IsWithin(position, area);
        }

        /// <summary>
        /// 最後の安全な位置として覚えてよいか。接地（または着席）していて、y が SafeMinY より上で、範囲の中。
        /// 建物の中の位置は覚えない（外へ戻す先にならない）。
        /// </summary>
        public static bool IsSafe(Vector3 position, Rect area, bool grounded, bool sitting, bool inside)
        {
            if (inside || !(grounded || sitting))
            {
                return false;
            }

            return position.y > SafeMinY && IsWithin(position, area);
        }

        /// <summary>
        /// 戻し先。最後の安全な位置 → 起動時の位置 → 範囲の中心 の順に、戻してもまた発動しないものを使う。
        /// </summary>
        public static Vector3 ReturnPoint(bool hasSafe, Vector3 safe, bool hasStart, Vector3 start, Rect area, float killY)
        {
            if (hasSafe && !ShouldRecover(safe, area, killY, false))
            {
                return safe;
            }

            if (hasStart && !ShouldRecover(start, area, killY, false))
            {
                return start;
            }

            return new Vector3(area.center.x, 1f, area.center.y);
        }

        /// <summary>戻したあとに向く角度。いた場所から戻し先へ向かう向き（＝範囲の内側）。近すぎれば fallback。</summary>
        public static float ReturnYaw(Vector3 from, Vector3 to, float fallbackYaw)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            if (!(delta.sqrMagnitude > 0.01f))
            {
                return fallbackYaw;
            }

            return Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        }

        private void Awake()
        {
            _fade = BuildFadeOverlay();

            // シーンに置いたままの位置（スポーン）を起動時の位置として覚える。ロードで動く前に取る。
            FindPlayer();
        }

        private void OnDisable()
        {
            // 暗転の途中で止まったら、入力の封鎖と暗幕を残さない。
            KCDInput.Unblock(this);
            if (_recovering)
            {
                _recovering = false;
                if (_fade != null)
                {
                    _fade.alpha = 0f;
                }
            }
        }

        private void Update()
        {
            if (_recovering || FindPlayer() == null)
            {
                return;
            }

            // 会話・ポーズ・写真モード・建物の出入りなど、ほかの画面が封鎖している間は見張らない。
            // その最中に戻し始めると、戻し終えたときにその画面の封鎖まで外してしまう。落ちていれば、封鎖が外れてから戻す。
            if (KCDInput.GameplayBlocked)
            {
                return;
            }

            bool inside = InteriorLoader.Instance != null && InteriorLoader.Instance.IsInside;
            Vector3 position = _player.transform.position;

            if (!_player.IsSitting && ShouldRecover(position, _area, _killY, inside))
            {
                StartCoroutine(Recover(position));
                return;
            }

            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer > 0f)
            {
                return;
            }

            _sampleTimer = SampleInterval;
            if (IsSafe(position, _area, _player.IsGrounded, _player.IsSitting, inside))
            {
                _safePosition = position;
                _hasSafe = true;
            }
        }

        private PlayerController FindPlayer()
        {
            if (_player != null)
            {
                return _player;
            }

            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                _player = tagged.GetComponent<PlayerController>();
            }

            if (_player != null && !_hasStart)
            {
                _startPosition = _player.transform.position;
                _hasStart = true;
            }

            return _player;
        }

        private IEnumerator Recover(Vector3 lostAt)
        {
            _recovering = true;

            // 自分の封鎖だけを掛けて外す。暗転の途中で開いた Esc のポーズ（設定を含む）や Tab のクエストログの封鎖は
            // その画面のものなので、戻し終えても残る（ポーズ中に F5/F9 や写真モードが効かない, #40）。
            KCDInput.Block(this);

            yield return Fade(1f);

            if (_player != null)
            {
                Vector3 target = ReturnPoint(_hasSafe, _safePosition, _hasStart, _startPosition, _area, _killY);
                float yaw = ReturnYaw(lostAt, target, _player.transform.eulerAngles.y);
                _player.Teleport(target + Vector3.up * ReturnLift, yaw);
                Physics.SyncTransforms();
                CameraRig.SnapBehind(_player.transform);
            }

            yield return null;
            yield return Fade(0f);

            HUD.Instance?.ShowToast(L.Get(ToastKey, ToastFallback));
            KCDInput.Unblock(this);
            _sampleTimer = SampleInterval;
            _recovering = false;
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

        /// <summary>暗転用の真っ黒な板。InteriorLoader のものと同じ作り。</summary>
        private CanvasGroup BuildFadeOverlay()
        {
            var go = new GameObject("BoundsFadeCanvas");
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
