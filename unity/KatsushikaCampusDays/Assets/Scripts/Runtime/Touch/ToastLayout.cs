using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 知らせ（トースト）の置き場所を、指のボタンが出ているかと画面の向きに合わせる (#187)。PC は画面の下の真ん中。
    /// 指で遊ぶ横長の画面では下の真ん中に自分の人物が立つので、枠の下端を画面の高さの 0.56 に置いて頭の上に出し、
    /// 幅は右上の列（ミニマップ・その下の名前・時計）の手前までに狭める。縦長の画面では「話す」の案内の上に置く。
    /// メニューを開いてボタンが隠れている間は、PC と同じ下の真ん中に戻す（上げたままだと、ポーズの枠越しに一覧の行に重なる）。
    /// 枠の置き方はここの表が唯一の出どころで、シーンを組むときも PC の表を使う。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ToastLayout : MonoBehaviour
    {
        /// <summary>画面の種類。</summary>
        public enum Shape
        {
            Pointer,
            LandscapeTouch,
            Portrait
        }

        /// <summary>どの置き方でも、枠の下端の真ん中をアンカーに合わせる。</summary>
        public static readonly Vector2 Pivot = new Vector2(0.5f, 0f);

        /// <summary>
        /// 枠の置き方。Anchor はアンカー、Position はそこからのずれ。Size は狭める前の大きさ
        /// （横の指では <see cref="LandscapeWidth"/>、縦長の画面では <see cref="FitInCanvas"/> が狭める）。
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
            // Pointer。1920×1080 で組んだ元の置き方。
            new Slot(new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1080f, 60f)),
            // LandscapeTouch。横長ではカメラの縦の画角が決まっているので、人物はどの縦横比でも同じ高さ
            // （頭は上から 0.47）に立つ。下端を 0.56 に置くと頭の上に出て、左上のクエストの追跡（縦横比 2.85 まで）とも、
            // 「話す」の案内（下から 190〜254）の上 24 とも重ならない。
            new Slot(new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(1080f, 60f)),
            // Portrait。「話す」の案内（下から 380〜444）の上に 24 空ける。幅と高さは FitInCanvas が 768×100 にする。
            new Slot(new Vector2(0.5f, 0f), new Vector2(0f, 468f), new Vector2(1080f, 60f))
        };

        private static readonly Vector3[] Corners = new Vector3[4];

        [SerializeField] private RectTransform[] _rightColumn = new RectTransform[0];

        private RectTransform _rect;
        private FitInCanvas _fit;
        private Shape _shape;
        private float _width;
        private bool _placed;

        /// <summary>横の指のときに手前で止まる右上の枠。SceneBuilder から差し込む。</summary>
        public void Bind(params RectTransform[] rightColumn)
        {
            _rightColumn = rightColumn;
        }

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

        /// <summary>
        /// 横の指のときの枠の幅。真ん中に置いた枠の右端が、右の列の左端（columnLeft、親の真ん中から）の
        /// <see cref="FitInCanvas.Margin"/> 手前で止まる。列が真ん中に届く（Canvas の大きさが決まっていない）ときは元の幅。
        /// </summary>
        public static float LandscapeWidth(float columnLeft)
        {
            float width = SlotFor(Shape.LandscapeTouch).Size.x;
            float half = columnLeft - FitInCanvas.Margin;
            return half <= 0f ? width : Mathf.Min(width, half * 2f);
        }

        /// <summary>
        /// column の枠（置いた後の位置・大きさ・倍率）のいちばん左の x。parent の真ん中から、parent の単位で測る。
        /// null は飛ばし、1 つも無ければ parent の右端。
        /// </summary>
        public static float ColumnLeft(RectTransform parent, IEnumerable<RectTransform> column)
        {
            Rect area = parent.rect;
            float left = area.xMax;
            foreach (RectTransform rect in column)
            {
                if (rect == null)
                {
                    continue;
                }

                rect.GetWorldCorners(Corners);
                foreach (Vector3 corner in Corners)
                {
                    left = Mathf.Min(left, parent.InverseTransformPoint(corner).x);
                }
            }

            return left - area.center.x;
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
            // 撮影の瞬間にボタンを隠しても、枠は動かさない（撮ったあとの知らせが跳ねる）。
            bool buttonsShown = TouchHud.LayoutFor(InputMode.IsTouch, KCDInput.GameplayBlocked, KCDInput.PhotoMode, false)
                                != TouchHud.Layout.Hidden;
            Shape shape = ShapeFor(buttonsShown, ScreenShape.IsPortrait);
            Slot slot = SlotFor(shape);

            // ミニマップと時計は KeepPointerSize が入力に合わせて縮めるので、毎フレーム置いた後の大きさから幅を出す。
            float width = shape == Shape.LandscapeTouch
                ? LandscapeWidth(ColumnLeft((RectTransform)_rect.parent, _rightColumn))
                : slot.Size.x;
            if (_placed && shape == _shape && Mathf.Approximately(width, _width))
            {
                return;
            }

            var size = new Vector2(width, slot.Size.y);
            _rect.anchorMin = slot.Anchor;
            _rect.anchorMax = slot.Anchor;
            _rect.pivot = Pivot;
            _rect.anchoredPosition = slot.Position;
            _rect.sizeDelta = size;
            if (_fit != null)
            {
                _fit.SetSize(size);
            }

            _shape = shape;
            _width = width;
            _placed = true;
        }
    }
}
