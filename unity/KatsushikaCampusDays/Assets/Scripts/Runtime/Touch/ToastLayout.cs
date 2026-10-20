using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 知らせ（トースト）の置き場所を、指のボタンが出ているかと画面の向きに合わせる (#187)。PC は画面の下の真ん中。
    /// 指で遊ぶ横長の画面では下の真ん中に自分の人物が立つので、枠の下端を画面の高さの 0.56 以上に置いて頭の上に出す。
    /// カメラを寄せると頭は画面の上へ上がるので、下端も頭に合わせて上げる（<see cref="BottomFor"/>）。
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
            // （カメラが遠いとき、頭は上から 0.47）に立つ。下端の 0.56 はその頭の上に出て、左上のクエストの追跡
            // （縦横比 2.85 まで）とも、「話す」の案内（下から 190〜254）の上 24 とも重ならない下限。
            // カメラを寄せて頭が上がったときは、Sync が BottomFor でこれより上げる。
            new Slot(new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(1080f, 60f)),
            // Portrait。「話す」の案内（下から 380〜444）の上に 24 空ける。幅と高さは FitInCanvas が 768×100 にする。
            new Slot(new Vector2(0.5f, 0f), new Vector2(0f, 468f), new Vector2(1080f, 60f))
        };

        /// <summary>左上のクエストの追跡の下端（Canvas の上から）。シーンの QuestTracker の位置 40 + 高さ 150。</summary>
        public const float TrackerBottom = 190f;

        /// <summary>
        /// 頭の上端と枠の下端の間に空ける高さ（画面の高さに対する割合）。カメラが遠いとき（頭の上端は下から 0.533）に、
        /// 下端がちょうど下限の 0.56 になる。
        /// </summary>
        public const float HeadGap = 0.027f;

        /// <summary>下端を上げる刻み（画面の高さに対する割合）。カメラの細かい動きで枠が震えないようにする。</summary>
        public const float Step = 0.01f;

        /// <summary>人物が見つからないとき、探し直す間隔（秒）。</summary>
        private const float SearchInterval = 1f;

        private static readonly Vector3[] Corners = new Vector3[4];

        [SerializeField] private RectTransform[] _rightColumn = new RectTransform[0];

        private RectTransform _rect;
        private FitInCanvas _fit;
        private Shape _shape;
        private float _width;
        private float _bottom;
        private bool _placed;
        private PlayerAppearance _player;
        private Transform _aim;
        private float _nextSearchAt;

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
        /// 横の指のときの枠の下端（アンカーの y、画面の高さに対する割合）。頭の上端 headTop（画面の下から）に
        /// <see cref="HeadGap"/> を足した高さを <see cref="Step"/> 刻みに切り上げ、下限（スロットの 0.56）と、
        /// クエストの追跡にかからない上限の間に収める。縦横比が大きいほど Canvas の高さ canvasHeight（Canvas の単位）が縮み、
        /// 追跡の占める割合が増えて上限が下がる。上限が下限を割るときと、頭が測れない（NaN）ときは下限。
        /// </summary>
        public static float BottomFor(float headTop, float canvasHeight)
        {
            Slot slot = SlotFor(Shape.LandscapeTouch);
            float floor = slot.Anchor.y;
            if (float.IsNaN(headTop) || !(canvasHeight > 0f))
            {
                return floor;
            }

            float ceiling = Mathf.Max(floor, 1f - (TrackerBottom + slot.Size.y) / canvasHeight);
            float steps = Mathf.Ceil((headTop + HeadGap - floor) / Step - 1e-3f);
            return Mathf.Clamp(floor + steps * Step, floor, ceiling);
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
            var parent = (RectTransform)_rect.parent;

            // ミニマップと時計は KeepPointerSize が入力に合わせて縮めるので、毎フレーム置いた後の大きさから幅を出す。
            // 下端はカメラの寄りに合わせて頭の上に出す。横の指のときだけで、縦長と PC の置き方は動かさない。
            bool landscape = shape == Shape.LandscapeTouch;
            float width = landscape ? LandscapeWidth(ColumnLeft(parent, _rightColumn)) : slot.Size.x;
            float bottom = landscape ? BottomFor(MeasureHeadTop(), parent.rect.height) : slot.Anchor.y;
            if (_placed && shape == _shape && Mathf.Approximately(width, _width) && Mathf.Approximately(bottom, _bottom))
            {
                return;
            }

            var size = new Vector2(width, slot.Size.y);
            var anchor = new Vector2(slot.Anchor.x, bottom);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.pivot = Pivot;
            _rect.anchoredPosition = slot.Position;
            _rect.sizeDelta = size;
            if (_fit != null)
            {
                _fit.SetSize(size);
            }

            _shape = shape;
            _width = width;
            _bottom = bottom;
            _placed = true;
        }

        /// <summary>
        /// 人物の頭の上端の高さ（画面の下から、画面の高さに対する割合）。カメラは注視点（CameraAim）を画面の真ん中に写すので、
        /// 注視点から頭の上端までの高さだけを実際のカメラで写して測る。人物ごと動くジャンプやカメラの遅れで、枠が跳ねない。
        /// 人物かカメラが無いとき、カメラの後ろに回ったときは NaN。
        /// </summary>
        private float MeasureHeadTop()
        {
            Camera camera = Camera.main;
            if (camera == null || !FindPlayer())
            {
                return float.NaN;
            }

            Vector3 aim = camera.WorldToViewportPoint(_aim.position);
            Vector3 top = camera.WorldToViewportPoint(_aim.position + Vector3.up * PlayerAppearance.HeadAboveAim(_player.BodyHeight));
            return aim.z > 0f && top.z > 0f ? 0.5f + (top.y - aim.y) : float.NaN;
        }

        /// <summary>自分の人物とその注視点を掴む。居ない画面では <see cref="SearchInterval"/> に 1 回だけ探す。</summary>
        private bool FindPlayer()
        {
            if (_player != null && _aim != null)
            {
                return true;
            }

            if (Time.unscaledTime < _nextSearchAt)
            {
                return false;
            }

            _nextSearchAt = Time.unscaledTime + SearchInterval;
            _player = FindAnyObjectByType<PlayerAppearance>();
            _aim = _player != null ? _player.transform.Find(PlayerAppearance.AimName) : null;
            return _aim != null;
        }
    }
}
