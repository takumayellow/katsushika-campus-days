using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときの操作 UI (#187) が、仮想のゲームパッドを通して <see cref="KCDInput"/> に届くこと。
    /// OnScreenControl は書いた値を次の入力の更新で反映するので、1 フレーム進めてから読む。
    /// </summary>
    public sealed class TouchControlsPlayTests : SceneTestBase
    {
        private const float Range = 90f;

        [TearDown]
        public void ReleaseForcedMode()
        {
            InputMode.Force(null);
            TouchInput.Clear();
            KCDInput.PhotoMode = false;
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator StickAndJump_ReachKCDInputThroughTheVirtualGamepad()
        {
            yield return LoadBareStage();
            InputMode.Force(InputKind.Touch);
            var canvas = new GameObject("TouchControlsPlayTests", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                TouchStick stick = AddStick(canvas.transform);
                OnScreenButton jump = AddButton(canvas.transform, "<Gamepad>/buttonSouth");
                yield return null;

                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                PointerEventData finger = Finger(1, center);
                stick.OnPointerDown(finger);
                finger.position = center + new Vector2(Range, 0f);
                stick.OnDrag(finger);
                Assert.IsTrue(stick.Held);
                Assert.AreEqual(1f, stick.Value.x, 0.001f, "いっぱいに倒しても 1 にならない");

                yield return null;
                Assert.AreEqual(1f, KCDInput.Move.x, 0.01f, "スティックを右へ倒しても右へ歩かない");
                Assert.AreEqual(0f, KCDInput.Move.y, 0.01f);

                jump.OnPointerDown(Finger(2, center));
                yield return null;
                Assert.IsTrue(KCDInput.JumpPressed, "ジャンプのボタンを押しても跳ばない");

                jump.OnPointerUp(Finger(2, center));
                stick.OnPointerUp(finger);
                Assert.IsFalse(stick.Held);
                yield return null;
                Assert.AreEqual(Vector2.zero, KCDInput.Move, "スティックを離しても歩き続ける");
            }
            finally
            {
                Object.Destroy(canvas);
            }
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Stick_AnotherFingerDoesNotTakeOver()
        {
            yield return LoadBareStage();
            InputMode.Force(InputKind.Touch);
            var canvas = new GameObject("TouchControlsPlayTests", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                TouchStick stick = AddStick(canvas.transform);
                yield return null;

                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                PointerEventData first = Finger(1, center);
                stick.OnPointerDown(first);
                first.position = center + new Vector2(0f, Range * 0.5f);
                stick.OnDrag(first);

                PointerEventData second = Finger(2, center + new Vector2(-Range, 0f));
                stick.OnPointerDown(second);
                stick.OnDrag(second);
                stick.OnPointerUp(second);

                Assert.IsTrue(stick.Held, "2 本目の指を離したら、1 本目で倒しているスティックが戻った");
                Assert.AreEqual(0.5f, stick.Value.y, 0.001f, "2 本目の指でスティックの中心が動いた");
            }
            finally
            {
                Object.Destroy(canvas);
            }
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator LookArea_OneFingerTurnsTwoFingersZoomAndAThirdIsIgnored()
        {
            yield return LoadBareStage();
            InputMode.Force(InputKind.Touch);
            var host = new GameObject("TouchLookAreaForTest", typeof(RectTransform));
            try
            {
                TouchLookArea look = host.AddComponent<TouchLookArea>();
                yield return null;

                // 1 本指: 画面の高さの 1 割を上へなぞると、次のフレームに 1 割分だけ視点が回る。
                // このフレームで一度読んでおく。読みが途切れると、次のフレームでなぞりの量を捨てる。
                _ = TouchInput.Look;
                PointerEventData first = Finger(1, new Vector2(100f, 100f));
                look.OnPointerDown(first);
                first.position += new Vector2(0f, Screen.height * 0.1f);
                look.OnDrag(first);
                yield return null;
                Assert.AreEqual(TouchInput.LookPerScreenHeight * 0.1f, TouchInput.Look.y, 0.01f,
                    "1 本指でなぞっても視点が回らない");
                Assert.AreEqual(0f, TouchInput.Zoom, 0.0001f);

                // 2 本指: 間隔を 2 倍に広げると寄る。視点は回さない。
                PointerEventData second = Finger(2, first.position + new Vector2(200f, 0f));
                look.OnPointerDown(second);
                Assert.AreEqual(2, look.FingerCount);
                second.position += new Vector2(200f, 0f);
                look.OnDrag(second);
                yield return null;
                Assert.AreEqual(TouchInput.PinchToZoom(200f, 400f), TouchInput.Zoom, 0.0001f,
                    "2 本指で広げても寄らない");
                Assert.AreEqual(Vector2.zero, TouchInput.Look, "つまんでいる間に視点が回った");

                // 3 本目は見ない。
                PointerEventData third = Finger(3, new Vector2(50f, 50f));
                look.OnPointerDown(third);
                Assert.AreEqual(2, look.FingerCount, "3 本目の指を数えた");
                third.position += new Vector2(0f, 300f);
                look.OnDrag(third);
                yield return null;
                Assert.AreEqual(Vector2.zero, TouchInput.Look, "3 本目の指のなぞりで視点が回った");
                Assert.AreEqual(0f, TouchInput.Zoom, 0.0001f);

                // 1 本目を離すと、残った 2 本目のなぞりで視点が回る。
                look.OnPointerUp(first);
                Assert.AreEqual(1, look.FingerCount);
                second.position += new Vector2(Screen.height * 0.1f, 0f);
                look.OnDrag(second);
                yield return null;
                Assert.AreEqual(TouchInput.LookPerScreenHeight * 0.1f, TouchInput.Look.x, 0.01f,
                    "指を 1 本離したあと、残った指でなぞっても視点が回らない");
                look.OnPointerUp(second);
                Assert.AreEqual(0, look.FingerCount);
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        /// <summary>
        /// キャンパスの肩越しカメラは、1 本指で縦になぞると縦にだけ回って距離を変えず、2 本指で間隔を広げると寄り、狭めると戻る。
        /// Cinemachine は軌道の縦の軸と半径の軸に同じ縦の目安（Hint）を付けるので、目安で軸を選ぶと縦のなぞりで距離が変わる。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_VerticalDragKeepsTheCameraDistanceAndPinchingMovesIt()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Touch);

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsFalse(KCDInput.GameplayBlocked, "入場の直後なのに操作が止まっている");

            var player = Object.FindAnyObjectByType<PlayerAppearance>();
            Assert.IsNotNull(player, "キャンパスに自分の人物が居ない");
            Transform aim = player.transform.Find(PlayerAppearance.AimName);
            Assert.IsNotNull(aim, "カメラの注視点が無い");
            Transform camera = Camera.main.transform;
            var settle = new CameraSettle(camera, aim);
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "入場の直後のカメラが落ち着かない");
            float start = settle.Distance;
            Vector3 startForward = camera.forward;

            // 画面の高さの 2% を上へなぞると、縦の軸が 0.02 × 45 × 2.2 × 感度の倍率 度だけ回る（倍率 1 で約 2 度）。
            // シーンのブリッジは _invertVertical を立ててある。設定画面で上下を反転していなければ（CameraPrefs.VerticalSign が 1）、
            // 上へなぞると縦の軸の値が下がり、カメラが下がって見上げる（仰角が増える）。マウスを上へ動かしたときと同じ向き。
            // 軌道の中心は注視点から横へずらしてあり、カメラは注視点を見るので、向きは軸と同じ角度だけは回らない。
            // 縦の向きは、符号と見込みの半分を超えることだけを見る。
            float expected = 0.02f * TouchInput.LookPerScreenHeight * CameraMath.LookSensitivity *
                             CameraPrefs.SensitivityScale * CameraPrefs.VerticalSign;
            TouchInput.AddDrag(new Vector2(0f, Screen.height * 0.02f), Screen.height);
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "1 本指で上へなぞったあと、カメラが落ち着かない");
            float pitch = Mathf.DeltaAngle(Pitch(startForward), Pitch(camera.forward));
            float yaw = Mathf.DeltaAngle(Yaw(startForward), Yaw(camera.forward));
            string turned = "（仰角の変化 " + pitch.ToString("F2") + " 度、方位の変化 " + yaw.ToString("F2") +
                            " 度、見込み " + expected.ToString("F2") + " 度）";
            Assert.Greater(pitch * Mathf.Sign(expected), Mathf.Abs(expected) * 0.5f,
                "1 本指で上へなぞっても、カメラが見込みの向きに縦に回らない" + turned);
            Assert.Less(Mathf.Abs(yaw), Mathf.Abs(expected) * 0.25f, "1 本指で縦になぞったのに、カメラが横に回った" + turned);

            // 軌道の中心が横へずれている分、縦に回すと注視点との距離もわずかに変わる（1% に届かない）。
            // 縦のなぞりで半径の倍率まで動くと距離は何割も変わるので、1% まで許せば見分けられる。
            Assert.AreEqual(start, settle.Distance, start * 0.01f, "1 本指で上へなぞったらカメラの距離が変わった");

            TouchInput.AddDrag(new Vector2(0f, -Screen.height * 0.02f), Screen.height);
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "1 本指で下へなぞったあと、カメラが落ち着かない");
            Assert.AreEqual(start, settle.Distance, start * 0.01f, "上下に同じだけなぞったのにカメラの距離が元に戻らない");

            // 2 本指の間隔を 2 倍に広げると半径の倍率が半分になり、元の間隔に狭めると戻る。
            // 注視点との距離は、軌道の中心が横へずれている分だけ半径の倍率どおりには縮まない。8 割を切れば寄ったとみなす。
            TouchInput.AddPinch(1f, 2f);
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "2 本指で広げたあと、カメラが落ち着かない");
            Assert.Less(settle.Distance, start * 0.8f, "2 本指で広げてもカメラが寄らない");
            TouchInput.AddPinch(2f, 1f);
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "2 本指で狭めたあと、カメラが落ち着かない");
            Assert.AreEqual(start, settle.Distance, start * 0.01f, "2 本指で狭めてもカメラが元の距離に戻らない");
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// キャンパスの操作 UI は指のときだけ動く。マウスのときは仮想のゲームパッドも作らない
        /// （作ると <c>Gamepad.current</c> を取り、PC で本物のゲームパッドを使う人の入力を邪魔する）。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_TouchControlsRunOnlyWhilePlayingWithFingers()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Pointer);

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);

            Assert.IsNotNull(Object.FindAnyObjectByType<TouchHud>(), "キャンパスにタッチの操作 UI が無い");
            TouchStick stick = Object.FindAnyObjectByType<TouchStick>(FindObjectsInactive.Include);
            Assert.IsNotNull(stick, "キャンパスに移動のスティックが無い");
            Assert.IsFalse(stick.isActiveAndEnabled, "マウスで遊んでいるのにスティックが動いている");
            int gamepads = Gamepad.all.Count;

            InputMode.Force(InputKind.Touch);
            yield return PlayModeScenes.Advance(2, 0f);
            Assert.IsTrue(stick.isActiveAndEnabled, "指に切り替えてもスティックが出ない");
            Assert.AreEqual(gamepads + 1, Gamepad.all.Count, "指で遊ぶときの仮想のゲームパッドが 1 台でない");
            Assert.IsFalse(KCDInput.GameplayBlocked, "入場の直後なのに操作が止まっている");
            CanvasGroup play = stick.GetComponentInParent<CanvasGroup>();
            Assert.AreEqual(1f, play.alpha, "歩ける間なのにスティックとボタンが隠れている");
            Assert.IsTrue(play.blocksRaycasts);

            Vector2 at = ScreenPointOf((RectTransform)stick.transform);
            PointerEventData finger = Finger(1, at);
            stick.OnPointerDown(finger);
            finger.position = at + new Vector2(0f, Screen.height * 0.2f);
            stick.OnDrag(finger);
            yield return null;
            Assert.Greater(KCDInput.Move.y, 0.5f, "キャンパスのスティックを上へ倒しても前へ歩かない");
            stick.OnPointerUp(finger);

            OnScreenButton pauseButton = PadButton("<Gamepad>/start");
            Assert.IsNotNull(pauseButton, "キャンパスにポーズのボタンが無い");
            CanvasGroup pauseGroup = pauseButton.GetComponentInParent<CanvasGroup>();
            PauseMenu pause = HUD.Instance.PauseMenu;
            yield return Press(pauseButton);
            Assert.IsTrue(pause.IsPaused, "ポーズのボタンを押してもポーズにならない");
            Assert.AreEqual(0f, play.alpha, "ポーズ中もスティックとボタンが出ている");
            yield return null;
            Assert.AreEqual(0f, pauseGroup.alpha, "ポーズ中もポーズのボタンが出ていて、ポーズの枠の見出しに重なる");
            Assert.IsFalse(pauseGroup.blocksRaycasts);
            pause.Tick(new PauseMenu.FrameInput(Time.frameCount, tap: 0));
            Assert.IsFalse(pause.IsPaused, "「ゲームに戻る」の行を叩いてもポーズが閉じない");
            yield return null;
            Assert.AreEqual(1f, pauseGroup.alpha, "ポーズを閉じてもポーズのボタンが戻らない");

            InputMode.Force(InputKind.Pointer);
            yield return PlayModeScenes.Advance(2, 0f);
            Assert.IsFalse(stick.isActiveAndEnabled, "マウスに戻してもスティックが動いている");
            Assert.AreEqual(gamepads, Gamepad.all.Count, "マウスに戻しても仮想のゲームパッドが残っている");
            Assert.AreEqual(Vector2.zero, KCDInput.Move, "マウスに戻したのに歩き続ける");
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 写真モードの「案内」のボタン (#187)。写真モードの間だけ出て、押すたびに操作の案内の枠が出入りする。
        /// ボタンは仮想のゲームパッドの L3 を押し、PhotoSystem がそれを案内の切り替えとして読む。
        /// ボタンの作りは TouchLayoutTests が見ているので、ここは指で押して動くところまで確かめる。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_PhotoHintButtonAppearsOnlyInPhotoModeAndTogglesTheHint()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Pointer);

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);

            InputMode.Force(InputKind.Touch);
            yield return PlayModeScenes.Advance(2, 0f);

            PhotoSystem photo = PhotoSystem.Instance;
            Assert.IsNotNull(photo, "キャンパスに写真モードが無い");
            PhotoHintLayout frame = Object.FindAnyObjectByType<PhotoHintLayout>(FindObjectsInactive.Include);
            Assert.IsNotNull(frame, "キャンパスに操作の案内の枠が無い");
            OnScreenButton photoButton = PadButton("<Gamepad>/buttonNorth");
            OnScreenButton hintButton = PadButton("<Gamepad>/leftStickPress");
            Assert.IsNotNull(photoButton, "キャンパスに写真のボタンが無い");
            Assert.IsNotNull(hintButton, "キャンパスに案内のボタンが無い");
            TMP_Text photoLabel = photoButton.GetComponentInChildren<TMP_Text>(true);
            TMP_Text hintLabel = hintButton.GetComponentInChildren<TMP_Text>(true);
            TMP_Text frameText = frame.GetComponentInChildren<TMP_Text>(true);
            string hintText = PhotoModeHints.Text(L.IsEnglish, false, true);

            // 歩いている間は、案内のボタンも枠も出ない。
            Assert.IsFalse(KCDInput.PhotoMode, "入場の直後なのに写真モードになっている");
            Assert.IsFalse(IsPressable(hintButton), "写真モードでないのに案内のボタンが出ている");
            Assert.IsFalse(frame.gameObject.activeInHierarchy, "写真モードでないのに操作の案内が出ている");
            Assert.AreEqual(L.Pick("写真", "Photo"), photoLabel.text, "歩いている間の写真のボタンの文字が違う");

            // 写真のボタンで入ると、案内のボタンが出て、写真のボタンは「戻る」になり、枠は指の文で出る。
            yield return Press(photoButton);
            Assert.IsTrue(KCDInput.PhotoMode, "写真のボタンを押しても写真モードにならない");
            Assert.IsTrue(IsPressable(hintButton), "写真モードに入っても案内のボタンが出ない");
            Assert.AreEqual(L.Pick("案内", "Help"), hintLabel.text, "案内のボタンの文字が違う");
            Assert.AreEqual(L.Pick("戻る", "Back"), photoLabel.text, "写真モードの間の写真のボタンが「戻る」でない");
            Assert.IsTrue(frame.gameObject.activeInHierarchy, "写真モードに入っても操作の案内が出ない");
            Assert.AreEqual(hintText, frameText.text, "操作の案内が指のときの文でない");

            // 案内のボタンを押すたびに、枠が消える・出るを繰り返す。写真モードのまま、撮りも戻りもしない。
            int taken = photo.Count;
            for (int press = 1; press <= 4; press++)
            {
                bool shown = press % 2 == 0;
                yield return Press(hintButton);
                Assert.AreEqual(shown, frame.gameObject.activeInHierarchy,
                    press + " 回目: 案内のボタンを押しても操作の案内が" + (shown ? "出ない" : "消えない"));
                Assert.IsTrue(KCDInput.PhotoMode, press + " 回目: 案内のボタンを押したら写真モードを抜けた");
                Assert.AreEqual(taken, photo.Count, press + " 回目: 案内のボタンを押したら撮影した");
                Assert.IsTrue(IsPressable(hintButton), press + " 回目: 案内を消したあとにボタンまで消えて、もう押せない");
            }

            Assert.AreEqual(hintText, frameText.text, "出し入れしたあとの操作の案内の文が変わった");

            // 戻るで抜けると、案内のボタンも枠も消え、止めた時間が動き出す。
            Assert.IsTrue(IsPressable(photoButton), "写真モードで「戻る」のボタンが押せない");
            yield return Press(photoButton);
            Assert.IsFalse(KCDInput.PhotoMode, "「戻る」のボタンを押しても写真モードを抜けない");
            Assert.IsFalse(IsPressable(hintButton), "写真モードを抜けても案内のボタンが残っている");
            Assert.IsFalse(frame.gameObject.activeInHierarchy, "写真モードを抜けても操作の案内が残っている");
            Assert.AreEqual(L.Pick("写真", "Photo"), photoLabel.text, "写真モードを抜けても写真のボタンが「戻る」のまま");
            Assert.AreEqual(1f, Time.timeScale, "写真モードを抜けても時間が止まったまま");
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 部品だけを置くテストの舞台。前のテストが残したキャンパス（タッチの操作 UI と仮想のゲームパッド）を
        /// タイトルで置き換え、テストの順番によらず同じ状態から始める。
        /// </summary>
        private IEnumerator LoadBareStage()
        {
            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
        }

        private static TouchStick AddStick(Transform parent)
        {
            var host = new GameObject("Stick", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            ((RectTransform)host.transform).sizeDelta = new Vector2(400f, 400f);
            TouchStick stick = host.AddComponent<TouchStick>();
            stick.Bind(null, null, Range);
            return stick;
        }

        /// <summary>controlPath は動き出す前に決める必要があるので、止めた状態で足してから動かす。</summary>
        private static OnScreenButton AddButton(Transform parent, string controlPath)
        {
            var host = new GameObject("Button", typeof(RectTransform));
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            OnScreenButton button = host.AddComponent<OnScreenButton>();
            button.controlPath = controlPath;
            host.SetActive(true);
            return button;
        }

        private static OnScreenButton PadButton(string controlPath)
        {
            foreach (OnScreenButton button in Object.FindObjectsByType<OnScreenButton>())
            {
                if (button.controlPath == controlPath)
                {
                    return button;
                }
            }

            return null;
        }

        /// <summary>押して 1 フレーム進め、離してもう 2 フレーム進める（閉じ開きの演出の分）。</summary>
        private static IEnumerator Press(OnScreenButton button)
        {
            PointerEventData finger = Finger(3, ScreenPointOf((RectTransform)button.transform));
            button.OnPointerDown(finger);
            yield return null;
            button.OnPointerUp(finger);
            yield return PlayModeScenes.Advance(2, 0f);
        }

        /// <summary>向き forward の仰角（度。上を向くと正）。</summary>
        private static float Pitch(Vector3 forward)
        {
            return Mathf.Asin(Mathf.Clamp(forward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>向き forward の方位（度。+Z が 0、+X が 90）。</summary>
        private static float Yaw(Vector3 forward)
        {
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static PointerEventData Finger(int id, Vector2 position)
        {
            return new PointerEventData(EventSystem.current) { pointerId = id, position = position };
        }

        /// <summary>rect の中心の画面の座標。オーバーレイのキャンバスはカメラ無しで変換する。</summary>
        private static Vector2 ScreenPointOf(RectTransform rect)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        /// <summary>
        /// 指が届く状態か。part の上にある CanvasGroup のどれもが、隠す（alpha 0）・当たりを止める（blocksRaycasts）・
        /// 押せなくする（interactable）をしていない。TouchHud はグループごとにボタンを出し入れする。
        /// </summary>
        private static bool IsPressable(Component part)
        {
            foreach (CanvasGroup group in part.GetComponentsInParent<CanvasGroup>())
            {
                if (group.alpha <= 0f || !group.blocksRaycasts || !group.interactable)
                {
                    return false;
                }

                if (group.ignoreParentGroups)
                {
                    break;
                }
            }

            return true;
        }
    }
}
