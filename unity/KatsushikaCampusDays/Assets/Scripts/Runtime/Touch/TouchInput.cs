using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace KCD
{
    /// <summary>
    /// 指の入力のうち、仮想のゲームパッドに書かないもの（視点・拡大縮小・走るの切り替え）の受け口 (#187)。
    /// 視点となぞりは UI のイベント（<see cref="UnityEngine.EventSystems.EventSystem"/> の Update）でためておき、
    /// そのフレームで最初に読まれたときに 1 フレーム分として確定させる。読む側より後にイベントが来たフレームの分は、
    /// 次のフレームに回るだけで失われない。
    /// </summary>
    public static class TouchInput
    {
        /// <summary>
        /// 画面の高さ 1 つ分なぞったときの視点の量（<see cref="KCDInput.Look"/> の単位）。
        /// 視点の感度（CinemachineInputBridge の 2.2）を掛けて約 100 度になる。
        /// </summary>
        public const float LookPerScreenHeight = 45f;

        /// <summary>
        /// 2 本指の間隔を e 倍に広げたときの拡大の量（<see cref="KCDInput.Zoom"/> の単位）。
        /// ズームの感度（CinemachineInputBridge の 3.5）を掛けて、カメラの距離の倍率がおよそ指の間隔の逆数になる。
        /// </summary>
        public const float ZoomPerPinchLog = 1f / 3.5f;

        private static Vector2 _pendingLook;
        private static float _pendingZoom;
        private static Vector2 _look;
        private static float _zoom;
        private static int _committedFrame = -1;

        /// <summary>走るの切り替え。止まると歩きに戻す（<see cref="NextSprint"/>）。</summary>
        public static bool Sprint { get; set; }

        /// <summary>このフレームの視点の量。</summary>
        public static Vector2 Look => LookAt(Time.frameCount);

        /// <summary>このフレームの拡大の量。広げると正（寄る）。</summary>
        public static float Zoom => ZoomAt(Time.frameCount);

        /// <summary>frame の視点の量。<see cref="Look"/> の中身で、フレームを決めて試すために分けてある。</summary>
        public static Vector2 LookAt(int frame)
        {
            Commit(frame);
            return _look;
        }

        /// <summary>frame の拡大の量。<see cref="Zoom"/> の中身。</summary>
        public static float ZoomAt(int frame)
        {
            Commit(frame);
            return _zoom;
        }

        /// <summary>なぞった量（画素）を視点の量にする。上へなぞると正（マウスを上へ動かしたのと同じ向き）。</summary>
        public static Vector2 DragToLook(Vector2 pixels, float screenHeight)
        {
            return screenHeight <= 0f ? Vector2.zero : pixels / screenHeight * LookPerScreenHeight;
        }

        /// <summary>2 本指の間隔が before から after に変わったときの拡大の量。</summary>
        public static float PinchToZoom(float before, float after)
        {
            return before <= 0f || after <= 0f ? 0f : Mathf.Log(after / before) * ZoomPerPinchLog;
        }

        /// <summary>
        /// 走るの次の状態。動いていたのが止まったら歩きに戻す。止まったまま切り替えたときは、
        /// 歩き出してから止まるまで走る。
        /// </summary>
        public static bool NextSprint(bool on, bool wasMoving, bool isMoving)
        {
            return on && !(wasMoving && !isMoving);
        }

        /// <summary>1 本指でなぞった量をためる。</summary>
        public static void AddDrag(Vector2 pixels, float screenHeight)
        {
            _pendingLook += DragToLook(pixels, screenHeight);
        }

        /// <summary>2 本指の間隔の変化をためる。</summary>
        public static void AddPinch(float before, float after)
        {
            _pendingZoom += PinchToZoom(before, after);
        }

        /// <summary>
        /// 指で遊んでいる間に届いたマウスのポインタか。ブラウザは指で叩くと互換のマウスのイベントも出し、
        /// UI の入力モジュールはそれを指とは別のポインタとして配るので、受けると 1 回の操作が 2 回に数えられる。
        /// </summary>
        public static bool IsEmulatedMouse(PointerEventData eventData)
        {
            return eventData is ExtendedPointerEventData extended &&
                   extended.pointerType == UIPointerType.MouseOrPen &&
                   InputMode.IsTouch;
        }

        /// <summary>ためた量と走るを捨てる。操作 UI を隠したとき・シーンを替えたときに呼ぶ。</summary>
        public static void Clear()
        {
            _pendingLook = Vector2.zero;
            _pendingZoom = 0f;
            _look = Vector2.zero;
            _zoom = 0f;
            Sprint = false;
        }

        /// <summary>
        /// frame の分を確定させる。前のフレームに読まれていなかった（会話中などで視点を読まない）なら、
        /// その間にたまった分は捨てる。読み始めた瞬間にまとめて回ると、カメラが跳ぶ。
        /// </summary>
        private static void Commit(int frame)
        {
            if (_committedFrame == frame)
            {
                return;
            }

            bool continuous = _committedFrame == frame - 1;
            _committedFrame = frame;
            _look = continuous ? _pendingLook : Vector2.zero;
            _zoom = continuous ? _pendingZoom : 0f;
            _pendingLook = Vector2.zero;
            _pendingZoom = 0f;
        }

        /// <summary>ドメインを読み直さない再生でも、前回の再生の分を持ち越さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
            _committedFrame = -1;
        }
    }
}
