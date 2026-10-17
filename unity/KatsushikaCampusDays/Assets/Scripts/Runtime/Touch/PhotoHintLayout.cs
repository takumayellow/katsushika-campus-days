using UnityEngine;

namespace KCD
{
    /// <summary>
    /// フォトモードの操作案内の置き場所を、指のボタンが出ているかと画面の向きに合わせる (#187)。
    /// PC は画面の下の真ん中。指で遊ぶ横長の画面では下の真ん中に自分の人物が立つので、写真モードの間は何も出ない
    /// 左上に置く（上の「戻る」「案内」のボタンと、左のスティックの丸より手前で止まる）。縦長の画面では上端の真ん中
    /// （上のボタンの並びより上）に置く。撮影の瞬間にボタンを隠しても動かさない。
    /// 枠の置き方はここの表が唯一の出どころで、シーンを組むときも PC の表を使う。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PhotoHintLayout : MonoBehaviour
    {
        /// <summary>画面の種類。</summary>
        public enum Shape
        {
            Pointer,
            LandscapeTouch,
            Portrait
        }

        /// <summary>
        /// 枠の置き方。Anchor はアンカーとピボットの両方に使い（枠のその点を Canvas のその点に合わせる）、
        /// Position はそこからのずれ。Size は <see cref="FitInCanvas"/> が狭める前の大きさ。
        /// </summary>
        public readonly struct Slot
        {
            public readonly Vector2 Anchor;
            public readonly Vector2 Position;
            public readonly Vector2 Size;

            public Slot(Vector2 anchor, Vector2 position, Vector2 size)
            {
                Anchor = anchor;
                Position = position;
                Size = size;
            }
        }

        private static readonly Slot[] Slots =
        {
            // Pointer。1920×1080 で組んだ元の置き方。2 行が入る高さ。
            new Slot(new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(1180f, 96f)),
            // LandscapeTouch。左上から。指の文の 3 行が 26 のまま入る大きさ。右端（左から 760）は、どの横長の画面でも
            // 上の「戻る」と「案内」のボタンより左で止まる（いちばん厳しい縦横比 1 の画面で、ボタンの左端は 770）。
            new Slot(new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(736f, 144f)),
            // Portrait。上端の真ん中。幅は FitInCanvas が Canvas の幅（800）に合わせて 768 に狭める。
            // 高さは 3 行（指の文と、幅に収まらず折り返したキーボード・ゲームパッドの文）が入る大きさ。
            new Slot(new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1180f, 140f))
        };

        private RectTransform _rect;
        private FitInCanvas _fit;
        private Shape _shape;
        private bool _placed;

        /// <summary>指のボタンが出ているか（<see cref="TouchHud.LayoutFor"/>）と画面の向きから決まる画面の種類。</summary>
        public static Shape ShapeFor(bool buttonsShown, bool portrait)
        {
            return !buttonsShown ? Shape.Pointer : portrait ? Shape.Portrait : Shape.LandscapeTouch;
        }

        /// <summary>shape の画面での枠の置き方。</summary>
        public static Slot SlotFor(Shape shape)
        {
            return Slots[(int)shape];
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _fit = GetComponent<FitInCanvas>();
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
            // 撮影の瞬間にボタンを隠しても、枠は動かさない（撮ったあとに案内が跳ねる）。
            bool buttonsShown = TouchHud.LayoutFor(InputMode.IsTouch, KCDInput.GameplayBlocked, KCDInput.PhotoMode, false)
                                != TouchHud.Layout.Hidden;
            Shape shape = ShapeFor(buttonsShown, ScreenShape.IsPortrait);
            if (_placed && shape == _shape)
            {
                return;
            }

            Slot slot = SlotFor(shape);
            _rect.anchorMin = slot.Anchor;
            _rect.anchorMax = slot.Anchor;
            _rect.pivot = slot.Anchor;
            _rect.anchoredPosition = slot.Position;
            _rect.sizeDelta = slot.Size;
            if (_fit != null)
            {
                _fit.SetSize(slot.Size);
            }

            _shape = shape;
            _placed = true;
        }
    }
}
