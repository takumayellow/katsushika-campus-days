using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 指で遊ぶボタンが出ている間だけ、枠を別の場所に置く (#187)。右下には「ジャンプ」「話す」のボタンが出るので、
    /// 画面の下に置いた知らせはボタンの上へ逃がす。会話やメニューでボタンが隠れている間と、
    /// キーボード・マウスに戻したときは元の場所に置く（上げたままだと、ポーズの枠越しに一覧の行に重なる）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TouchPosition : MonoBehaviour
    {
        [SerializeField] private Vector2 _touchPosition;

        private RectTransform _rect;
        private Vector2 _pointerPosition;

        /// <summary>指のときの anchoredPosition。SceneBuilder から差し込む。</summary>
        public void Bind(Vector2 touchPosition)
        {
            _touchPosition = touchPosition;
        }

        /// <summary>指で遊ぶボタンが出ているか（<see cref="TouchHud.ShowsPlayControls"/>）に合う場所。</summary>
        public static Vector2 PositionFor(bool buttonsShown, Vector2 pointerPosition, Vector2 touchPosition)
        {
            return buttonsShown ? touchPosition : pointerPosition;
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
            Vector2 position = PositionFor(buttonsShown, _pointerPosition, _touchPosition);
            if (_rect.anchoredPosition != position)
            {
                _rect.anchoredPosition = position;
            }
        }
    }
}
