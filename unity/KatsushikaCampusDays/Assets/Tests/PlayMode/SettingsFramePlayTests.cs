using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルの設定の枠が、描き直しのたびに大きさを合わせ直す (#187)。横持ちの指のときだけ本文を 2 列に組むので、
    /// その描き直しの中で枠が横に広がり、2 列をやめる描き直しで通常の幅に戻る。
    /// 組み方そのものは EditMode の SettingsLayoutTests が見る。ここでは SettingsView.Redraw から
    /// ResizeFrame（FitInCanvas.SetSize）を通って枠の大きさが変わるところまでの配線を、シーンに組んだままで見る。
    /// 本文が 2 列かどうかと枠の幅を組にして確かめるので、本文だけ組み直して枠が置いていかれる崩れも拾う。
    /// </summary>
    public sealed class SettingsFramePlayTests : SceneTestBase
    {
        [TearDown]
        public void ReleaseForcedMode()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
        }

        /// <summary>
        /// 横持ちの指で開くと枠が広がり、マウスの扱いに切り替えると通常の幅に戻り、指に戻すとまた広がる。
        /// 開くときの描き直しと、入力の切り替えでの描き直しの両方で、枠の大きさが変わる。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Title_SettingsFrameWidensForTouchAndFoldsBackForPointer()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Touch);
            ScreenShape.Force(false);

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            SettingsView settings = Object.FindAnyObjectByType<SettingsView>();
            Assert.IsNotNull(settings, "タイトルに SettingsView が無い");

            settings.Open(null);
            yield return PlayModeScenes.Advance(3, 0f);
            AssertTwoColumnFrame("横持ちの指で開いたとき");

            InputMode.Force(InputKind.Pointer);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(settings.IsOpen, "マウスの扱いに切り替えたら設定が閉じた");
            AssertOneColumnFrame("マウスの扱いに切り替えたとき", portrait: false);

            InputMode.Force(InputKind.Touch);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(settings.IsOpen, "指の扱いに戻したら設定が閉じた");
            AssertTwoColumnFrame("指の扱いに戻したとき");
            AssertNoErrors("設定");
        }

        /// <summary>
        /// 縦持ちの指で開いた設定を横に回すと枠が広がり、縦に戻すと 2 列をやめて通常の幅に戻る。
        /// 画面の向きの切り替えでの描き直しで、枠の大きさが変わる。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Title_SettingsFrameFollowsTheScreenRotation()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Touch);
            ScreenShape.Force(true);

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            SettingsView settings = Object.FindAnyObjectByType<SettingsView>();
            Assert.IsNotNull(settings, "タイトルに SettingsView が無い");

            settings.Open(null);
            yield return PlayModeScenes.Advance(3, 0f);
            AssertOneColumnFrame("縦持ちの指で開いたとき", portrait: true);

            ScreenShape.Force(false);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(settings.IsOpen, "画面を横に回したら設定が閉じた");
            AssertTwoColumnFrame("画面を横に回したとき");

            ScreenShape.Force(true);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(settings.IsOpen, "画面を縦に戻したら設定が閉じた");
            AssertOneColumnFrame("画面を縦に戻したとき", portrait: true);
            AssertNoErrors("設定");
        }

        /// <summary>
        /// 本文が 2 列に組まれていて、枠が Canvas に収まる範囲で横に広がっている。
        /// Canvas の幅が足りず、広げても通常の幅と変わらない画面では、広がったかを見分けられないので失敗にする。
        /// </summary>
        private static void AssertTwoColumnFrame(string when)
        {
            RectTransform panel = OpenPanel();
            float canvasWidth = CanvasWidth(panel);
            float wide = FitInCanvas.WidthToFit(SettingsView.TwoColumnFrameWidth, canvasWidth, FitInCanvas.Margin);
            float normal = FitInCanvas.WidthToFit(SettingsView.NormalFrameSize.x, canvasWidth, FitInCanvas.Margin);
            Assert.Greater(wide, normal,
                "Canvas の幅 " + canvasWidth.ToString("F0") + " では、広げた枠と通常の枠を見分けられない");

            string text = BodyOf(panel).text;
            StringAssert.StartsWith(SettingsView.TwoColumnLineHeight, text, when + ": 本文が 2 列に組まれていない");
            Assert.AreEqual(wide, panel.sizeDelta.x, 0.01f,
                when + ": 本文は 2 列なのに、枠の幅が広がっていない（Redraw から ResizeFrame が枠の大きさを変えていない）");
            Assert.AreEqual(SettingsView.NormalFrameSize.y, panel.sizeDelta.y, 0.01f, when + ": 横持ちの枠の高さが通常と違う");
        }

        /// <summary>本文が 2 列でなく、枠の幅が通常の幅（Canvas に収まる範囲）になっている。縦持ちは 2 行の組み。</summary>
        private static void AssertOneColumnFrame(string when, bool portrait)
        {
            RectTransform panel = OpenPanel();
            float canvasWidth = CanvasWidth(panel);
            float normal = FitInCanvas.WidthToFit(SettingsView.NormalFrameSize.x, canvasWidth, FitInCanvas.Margin);

            string text = BodyOf(panel).text;
            StringAssert.DoesNotContain(SettingsView.RightColumnPos, text, when + ": 本文が 2 列のまま");
            if (portrait)
            {
                StringAssert.StartsWith(SettingsView.StackedLineHeight, text, when + ": 本文が縦持ちの 2 行の組みになっていない");
            }

            Assert.AreEqual(normal, panel.sizeDelta.x, 0.01f,
                when + ": 本文は 2 列でないのに、枠の幅が通常に戻っていない（Redraw から ResizeFrame が枠の大きさを変えていない）");
            if (!portrait)
            {
                Assert.AreEqual(SettingsView.NormalFrameSize.y, panel.sizeDelta.y, 0.01f, when + ": 横持ちの枠の高さが通常と違う");
            }
        }

        /// <summary>開いている設定の枠。ResizeFrame が大きさを渡す先の FitInCanvas が付いている。</summary>
        private static RectTransform OpenPanel()
        {
            GameObject panel = GameObject.Find("Settings");
            Assert.IsNotNull(panel, "設定の枠（Settings）が開いていない");
            Assert.IsNotNull(panel.GetComponent<FitInCanvas>(), "設定の枠に FitInCanvas が無い（ResizeFrame の渡し先が無く、枠は広がらない）");
            return (RectTransform)panel.transform;
        }

        /// <summary>設定の本文の欄。SettingsView が Redraw で組んだ文字を入れる。</summary>
        private static TMP_Text BodyOf(RectTransform panel)
        {
            Transform body = panel.Find("Body/Label");
            Assert.IsNotNull(body, "設定の枠に本文の欄（Body/Label）が無い");
            TMP_Text label = body.GetComponent<TMP_Text>();
            Assert.IsNotNull(label, "設定の本文の欄に TMP_Text が無い");
            return label;
        }

        /// <summary>FitInCanvas が枠を収める先の、いちばん外の Canvas の幅。</summary>
        private static float CanvasWidth(RectTransform panel)
        {
            Canvas canvas = panel.GetComponentInParent<Canvas>(true);
            Assert.IsNotNull(canvas, "設定の枠が Canvas の下に無い");
            return ((RectTransform)canvas.rootCanvas.transform).rect.width;
        }
    }
}
