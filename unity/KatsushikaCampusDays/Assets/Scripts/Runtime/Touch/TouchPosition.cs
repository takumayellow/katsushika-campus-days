using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 指で遊ぶボタンが出ている間だけ、枠を別の場所に置く (#187)。右下には「ジャンプ」「話す」のボタンが出るので、
    /// 画面の下に置いた知らせはボタンの上へ逃がす。会話やメニューでボタンが隠れている間と、
    /// キーボード・マウスに戻したときは元の場所に置く（上げたままだと、ポーズの枠越しに一覧の行に重なる）。
    /// 縦長の画面ではボタンが画面の幅の大半を占めるので、ボタンの上へ逃がす場所を別に持つ。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TouchPosition : MonoBehaviour
    {
        [SerializeField] private Vector2 _touchPosition;
        [SerializeField] private Vector2 _portraitTouchPosition;

        private RectTransform _rect;
        private Vector2 _pointerPosition;

        /// <summary>指のときの anchoredPosition。SceneBuilder から差し込む。</summary>
        public void Bind(Vector2 touchPosition)
        {
            Bind(touchPosition, touchPosition);
        }

        /// <summary>指のときの anchoredPosition を、横長と縦長の画面で別に差し込む。</summary>
        public void Bind(Vector2 touchPosition, Vector2 portraitTouchPosition)
        {
            _touchPosition = touchPosition;
            _portraitTouchPosition = portraitTouchPosition;
        }

        /// <summary>指で遊ぶボタンが出ているか（<see cref="TouchHud.ShowsPlayControls"/>）に合う場所。</summary>
        public static Vector2 PositionFor(bool buttonsShown, Vector2 pointerPosition, Vector2 touchPosition)
        {
            return buttonsShown ? touchPosition : pointerPosition;
        }

        /// <summary>ボタンが出ているかと画面の向きに合う場所。</summary>
        public static Vector2 PositionFor(bool buttonsShown, bool portrait, Vector2 pointerPosition,
            Vector2 touchPosition, Vector2 portraitTouchPosition)
        {
            return PositionFor(buttonsShown, pointerPosition, portrait ? portraitTouchPosition : touchPosition);
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _pointerPosition = _rect.anchoredPosition;
        }

        private void OnEnable()
        {
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        private void Sync()
        {
            bool buttonsShown = TouchHud.ShowsPlayControls(InputMode.IsTouch, KCDInput.GameplayBlocked);
            Vector2 position = PositionFor(
                buttonsShown, ScreenShape.IsPortrait, _pointerPosition, _touchPosition, _portraitTouchPosition);
            if (_rect.anchoredPosition != position)
            {
                _rect.anchoredPosition = position;
            }
        }
    }
}
