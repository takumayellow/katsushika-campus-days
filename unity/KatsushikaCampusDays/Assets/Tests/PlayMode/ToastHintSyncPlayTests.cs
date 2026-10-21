using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 知らせ（トースト）と写真モードの案内が、指のボタンが出ているか・画面の向き・右上の列に合わせて、実際に枠の位置と大きさを
    /// 書き換え、状況が戻れば元に戻ること (#187)。置き方の表（<see cref="ToastLayout.SlotFor"/>、<see cref="PhotoHintLayout.SlotFor"/>）
    /// は EditMode で見ているが、<c>Sync</c> が表のとおりに枠へ書いているかは、動かさないと分からない。
    /// シーンは読まない。Canvas を WorldSpace にして大きさを自分で決めるので、Game ビューの縦横に左右されない。
    /// 人物の頭に合わせてトーストの下端を上げるところは、実際のキャンパスで <see cref="ToastPlacementPlayTests"/> が見る。
    /// </summary>
    public sealed class ToastHintSyncPlayTests
    {
        /// <summary>右上の列の代わりの枠の幅。横の指のトーストの幅は、この枠の左端の手前で止まる。</summary>
        private const float ColumnWidth = 384f;

        /// <summary>縦長の画面でのトーストの高さ。UIFactory が FitInCanvas に渡す値と同じ（Editor の定数なので、ここに写す）。</summary>
        private const float PortraitToastHeight = 100f;

        private const float Tolerance = 1e-3f;

        private static readonly Vector2 LandscapeCanvas = TouchCanvasScale.TouchReference;
        private static readonly Vector2 PortraitCanvas = new Vector2(TouchCanvasScale.PortraitWidth, 1280f);

        private GameObject _canvas;
        private RectTransform _column;

        [SetUp]
        public void CreateCanvas()
        {
            _canvas = new GameObject("ToastHintSyncPlayTests", typeof(Canvas));
            // WorldSpace なら大きさ（sizeDelta）を自分で決められる。Screen や CanvasScaler に左右されない。
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Resize(LandscapeCanvas);

            _column = new GameObject("Column", typeof(RectTransform)).GetComponent<RectTransform>();
            _column.SetParent(_canvas.transform, false);
            _column.anchorMin = Vector2.one;
            _column.anchorMax = Vector2.one;
            _column.pivot = Vector2.one;
            _column.anchoredPosition = Vector2.zero;
            _column.sizeDelta = new Vector2(ColumnWidth, 100f);

            // 前のテストのシーンが封鎖を掛けたままだと、「ボタンが出ていない」場面を作れない。
            KCDInput.ClearAllBlocks();
            InputMode.Force(InputKind.Pointer);
            ScreenShape.Force(false);
            KCDInput.PhotoMode = false;
        }

        [TearDown]
        public void Release()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            KCDInput.PhotoMode = false;
            KCDInput.Unblock(this);
            Object.Destroy(_canvas);
        }

        [UnityTest]
        public IEnumerator Toast_IsPlacedForEachSituationAndBackAgain()
        {
            RectTransform toast = BuildToast();
            float besideColumn = WidthBesideColumn(ColumnWidth);
            Assert.Less(besideColumn, ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch).Size.x,
                "列の手前で止めた幅が元の幅より狭くないと、狭めたかどうかを見分けられない");

            yield return Situate(InputKind.Pointer, false, false, false);
            AssertPointer(toast, "キーボード・マウス");

            yield return Situate(InputKind.Touch, false, false, false);
            AssertLandscapeTouch(toast, besideColumn, "横持ちの指");

            yield return Situate(InputKind.Touch, false, true, false);
            AssertPointer(toast, "指でメニューなどを開いてボタンが隠れている間");

            yield return Situate(InputKind.Touch, false, true, true);
            AssertLandscapeTouch(toast, besideColumn, "指の写真モード（ボタンが出ている）");

            yield return Situate(InputKind.Touch, true, false, false);
            AssertPortrait(toast, "縦持ちの指");

            yield return Situate(InputKind.Touch, false, false, false);
            AssertLandscapeTouch(toast, besideColumn, "縦持ちから横持ちへ戻した");

            yield return Situate(InputKind.Pointer, false, false, false);
            AssertPointer(toast, "指からキーボード・マウスへ戻した");
        }

        [UnityTest]
        public IEnumerator Toast_WidthFollowsTheRightColumnOnlyWhileTheFingerButtonsAreShown()
        {
            RectTransform toast = BuildToast();
            yield return Situate(InputKind.Touch, false, false, false);
            AssertLandscapeTouch(toast, WidthBesideColumn(ColumnWidth), "列の幅が " + ColumnWidth);

            const float Narrow = 200f;
            SetColumnWidth(Narrow);
            yield return Settle();
            AssertLandscapeTouch(toast, WidthBesideColumn(Narrow), "列を " + Narrow + " に狭めた");

            SetColumnWidth(ColumnWidth);
            yield return Settle();
            AssertLandscapeTouch(toast, WidthBesideColumn(ColumnWidth), "列を元の幅に戻した");

            yield return Situate(InputKind.Pointer, false, false, false);
            SetColumnWidth(Narrow);
            yield return Settle();
            AssertPointer(toast, "キーボード・マウスでは列が狭くなっても幅を変えない");
        }

        [UnityTest]
        public IEnumerator PhotoHint_IsPlacedForEachSituationAndBackAgain()
        {
            RectTransform hint = BuildHint();

            yield return Situate(InputKind.Pointer, false, true, true);
            AssertHint(hint, PhotoHintLayout.Shape.Pointer, "キーボード・マウスの写真モード");

            yield return Situate(InputKind.Touch, false, true, true);
            AssertHint(hint, PhotoHintLayout.Shape.LandscapeTouch, "横持ちの指の写真モード");

            yield return Situate(InputKind.Touch, true, true, true);
            AssertHint(hint, PhotoHintLayout.Shape.Portrait, "縦持ちの指の写真モード");

            yield return Situate(InputKind.Touch, false, true, true);
            AssertHint(hint, PhotoHintLayout.Shape.LandscapeTouch, "縦持ちから横持ちへ戻した");

            yield return Situate(InputKind.Pointer, false, true, true);
            AssertHint(hint, PhotoHintLayout.Shape.Pointer, "指からキーボード・マウスへ戻した");
        }

        /// <summary>入力・画面の向き・封鎖・写真モードを揃え、枠が追いつくまで待つ。</summary>
        private IEnumerator Situate(InputKind input, bool portrait, bool blocked, bool photoMode)
        {
            InputMode.Force(input);
            ScreenShape.Force(portrait);
            Resize(portrait ? PortraitCanvas : LandscapeCanvas);
            if (blocked)
            {
                KCDInput.Block(this);
            }
            else
            {
                KCDInput.Unblock(this);
            }

            KCDInput.PhotoMode = photoMode;
            Assert.AreEqual(blocked, KCDInput.GameplayBlocked, "封鎖の状態が揃わない。ほかの画面が封鎖を掛けたままになっていないか");
            yield return Settle();
        }

        /// <summary>Sync（Update）と FitInCanvas（LateUpdate）が書き終わるまで。次のフレームで揃うが、余裕を見て 3 フレーム。</summary>
        private static IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        private Vector2 CanvasSize => ((RectTransform)_canvas.transform).sizeDelta;

        private void Resize(Vector2 size)
        {
            ((RectTransform)_canvas.transform).sizeDelta = size;
        }

        private void SetColumnWidth(float width)
        {
            _column.sizeDelta = new Vector2(width, _column.sizeDelta.y);
        }

        /// <summary>横の指のトーストの幅。真ん中に置いた枠の右端が、右の列の左端から <see cref="FitInCanvas.Margin"/> 手前で止まる。</summary>
        private float WidthBesideColumn(float columnWidth)
        {
            float room = (CanvasSize.x / 2f - columnWidth - FitInCanvas.Margin) * 2f;
            return Mathf.Min(ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch).Size.x, room);
        }

        /// <summary>SceneBuilder（UIFactory.BuildToast）と同じ組み方。置き場所は PC の表で組み、Sync が書き換える。</summary>
        private RectTransform BuildToast()
        {
            ToastLayout.Slot pointer = ToastLayout.SlotFor(ToastLayout.Shape.Pointer);
            RectTransform rect = NewInactiveRect("Toast", pointer.Anchor, ToastLayout.Pivot, pointer.Position, pointer.Size);
            rect.gameObject.AddComponent<FitInCanvas>().BindReflow(PortraitToastHeight);
            rect.gameObject.AddComponent<ToastLayout>().Bind(_column);
            rect.gameObject.SetActive(true);
            return rect;
        }

        /// <summary>SceneBuilder（UIFactory.BuildPhotoOverlay）と同じ組み方。</summary>
        private RectTransform BuildHint()
        {
            PhotoHintLayout.Slot pointer = PhotoHintLayout.SlotFor(PhotoHintLayout.Shape.Pointer);
            RectTransform rect = NewInactiveRect("PhotoHint", pointer.Anchor, pointer.Anchor, pointer.Position, pointer.Size);
            rect.gameObject.AddComponent<FitInCanvas>().BindReflow(PhotoHintLayout.SlotFor(PhotoHintLayout.Shape.Portrait).Size.y);
            rect.gameObject.AddComponent<PhotoHintLayout>();
            rect.gameObject.SetActive(true);
            return rect;
        }

        /// <summary>Awake が走る前に部品を足し終えるよう、非アクティブで作る。</summary>
        private RectTransform NewInactiveRect(string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.SetActive(false);
            var rect = (RectTransform)host.transform;
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private void AssertPointer(RectTransform toast, string where)
        {
            ToastLayout.Slot slot = ToastLayout.SlotFor(ToastLayout.Shape.Pointer);
            var size = new Vector2(FitInCanvas.WidthToFit(slot.Size.x, CanvasSize.x, FitInCanvas.Margin), slot.Size.y);
            AssertPlaced(toast, slot.Anchor, ToastLayout.Pivot, slot.Position, size, where);
        }

        private void AssertLandscapeTouch(RectTransform toast, float width, string where)
        {
            ToastLayout.Slot slot = ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch);

            // 下端（アンカーの y）は人物の頭の高さで決まる。人物が居なければ下限、居れば下限から上限の間。
            // 前のテストのシーンが残っていると人物が居ることがあるので、頭の高さには立ち入らず範囲だけを見る
            // （頭に合わせた高さは ToastPlacementPlayTests と ToastLayoutTests が見る）。
            float bottom = toast.anchorMin.y;
            float ceiling = ToastLayout.BottomFor(1f, CanvasSize.y);
            Assert.GreaterOrEqual(bottom, slot.Anchor.y - Tolerance, where + " の下端が下限より低い");
            Assert.LessOrEqual(bottom, ceiling + Tolerance, where + " の下端がクエストの追跡にかかる高さ");

            var size = new Vector2(width, slot.Size.y);
            AssertPlaced(toast, new Vector2(slot.Anchor.x, bottom), ToastLayout.Pivot, slot.Position, size, where);
        }

        private void AssertPortrait(RectTransform toast, string where)
        {
            ToastLayout.Slot slot = ToastLayout.SlotFor(ToastLayout.Shape.Portrait);
            var size = new Vector2(FitInCanvas.WidthToFit(slot.Size.x, CanvasSize.x, FitInCanvas.Margin), PortraitToastHeight);
            AssertPlaced(toast, slot.Anchor, ToastLayout.Pivot, slot.Position, size, where);
        }

        private void AssertHint(RectTransform hint, PhotoHintLayout.Shape shape, string where)
        {
            PhotoHintLayout.Slot slot = PhotoHintLayout.SlotFor(shape);
            var size = new Vector2(FitInCanvas.WidthToFit(slot.Size.x, CanvasSize.x, FitInCanvas.Margin), slot.Size.y);
            AssertPlaced(hint, slot.Anchor, slot.Anchor, slot.Position, size, where);
        }

        private static void AssertPlaced(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, string where)
        {
            AssertVector(anchor, rect.anchorMin, where + " のアンカー（最小）");
            AssertVector(anchor, rect.anchorMax, where + " のアンカー（最大）");
            AssertVector(pivot, rect.pivot, where + " のピボット");
            AssertVector(position, rect.anchoredPosition, where + " の位置");
            AssertVector(size, rect.sizeDelta, where + " の大きさ");
        }

        private static void AssertVector(Vector2 expected, Vector2 actual, string what)
        {
            string detail = what + "（期待 " + expected + "、実際 " + actual + "）";
            Assert.AreEqual(expected.x, actual.x, Tolerance, detail + " の x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, detail + " の y");
        }
    }
}
