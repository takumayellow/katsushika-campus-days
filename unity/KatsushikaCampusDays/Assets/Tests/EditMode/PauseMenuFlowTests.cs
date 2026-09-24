using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ポーズメニューのキー操作の流れ。EditMode では Update が走らず Time.frameCount も進まないので、
    /// フレーム番号を明示した <see cref="PauseMenu.FrameInput"/> を <see cref="PauseMenu.Tick"/> に渡して進める。
    /// ・封鎖の最中（暗転・会話）に Esc で開かない (#105)
    /// ラベルは渡さない（Redraw は null の板を飛ばす）ので、TextMeshPro の部品もローカライズも要らない。
    /// </summary>
    public sealed class PauseMenuFlowTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            KCDInput.ClearAllBlocks();
            KCDInput.PhotoMode = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
            Time.timeScale = _timeScale;
            KCDInput.PhotoMode = false;
            KCDInput.ClearAllBlocks();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        /// <summary>画面の根。EditMode では Start が走らないので、Start と同じく閉じた状態で作る。</summary>
        private GameObject MakeRoot(string name)
        {
            GameObject root = Make(name);
            root.SetActive(false);
            return root;
        }

        private PauseMenu MakePause()
        {
            PauseMenu pause = Make("PauseForTest").AddComponent<PauseMenu>();
            pause.Bind(MakeRoot("PauseRoot"), null);
            return pause;
        }

        /// <summary>ラベル無しの設定パネル。Update は走らないので、開いたかどうかだけを見る。</summary>
        private SettingsView MakeSettings()
        {
            SettingsView settings = Make("SettingsForTest").AddComponent<SettingsView>();
            settings.Bind(MakeRoot("SettingsRoot"), null, null);
            return settings;
        }

        private static PauseMenu.FrameInput Menu(int frame) => new PauseMenu.FrameInput(frame, menu: true);

        private static PauseMenu.FrameInput Submit(int frame) => new PauseMenu.FrameInput(frame, submit: true);

        private static PauseMenu.FrameInput Idle(int frame) => new PauseMenu.FrameInput(frame);

        /// <summary>行の並び（EntryKeys）での「設定」の位置。</summary>
        private const int SettingsRow = 3;

        /// <summary>ポーズを開き、「設定」の行まで下へ送る。次に使えるフレーム番号を返す。</summary>
        private static int OpenAndMoveToSettings(PauseMenu pause, int frame)
        {
            pause.Tick(Menu(frame++));
            for (int i = 0; i < SettingsRow; i++)
            {
                pause.Tick(new PauseMenu.FrameInput(frame++, vertical: 1));
            }

            return frame;
        }

        // ---- 封鎖の最中の Esc (#105) ----

        [Test]
        public void Menu_WhileAFadeBlocks_DoesNotOpenThePause()
        {
            PauseMenu pause = MakePause();

            // 建物の出入り（InteriorLoader.Travel）と翌朝への戻り（DayEndEvaluator.RestartDay）の暗転は、
            // 自分の名前で Block するだけ。ResultScreen.IsAnyOpen も DormEnding.IsAnyShowing も立たない。
            var fade = new object();
            KCDInput.Block(fade);

            pause.Tick(Menu(10));

            Assert.IsFalse(pause.IsOpen, "暗転の最中に Esc でポーズが開いた");
            Assert.IsFalse(pause.IsPaused);
            Assert.IsFalse(KCDInput.IsBlockedBy(pause));
            Assert.AreEqual(1f, Time.timeScale, "開いていないポーズが時間を止めた");

            // 暗転が終われば、次の Esc で開く。
            KCDInput.Unblock(fade);
            pause.Tick(Menu(11));
            Assert.IsTrue(pause.IsOpen, "暗転が終わったのに Esc でポーズが開かない");
            Assert.IsTrue(KCDInput.IsBlockedBy(pause));
        }

        [Test]
        public void Menu_ThatFinishesADialogue_DoesNotAlsoOpenThePause()
        {
            PauseMenu pause = MakePause();

            // DialogueSystem.Update がポーズより後に走るフレーム。Esc で会話を閉じる前なので、会話の封鎖がまだ残っている。
            // ModalClosedThisFrame はまだ立っていない（consumed: false）。
            var dialogue = new object();
            KCDInput.Block(dialogue);

            pause.Tick(Menu(20));

            Assert.IsFalse(pause.IsOpen, "会話を閉じる Esc でポーズまで開いた");
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue));
        }

        [Test]
        public void Menu_AfterAModalClosedThisFrame_DoesNotOpenThePause()
        {
            PauseMenu pause = MakePause();

            // DialogueSystem.Update が先に走ったフレーム。Finish が封鎖を外し MarkModalClosed を呼んだあと。
            pause.Tick(new PauseMenu.FrameInput(30, menu: true, consumed: true));

            Assert.IsFalse(pause.IsOpen, "モーダルを閉じた Esc でポーズが開いた");
        }

        [Test]
        public void Menu_ClosesAnOpenPause_EvenWhenAnotherBlockAppeared()
        {
            PauseMenu pause = MakePause();
            pause.Tick(Menu(40));
            Assert.IsTrue(pause.IsOpen);

            // 開いている間にほかの封鎖が掛かっても、閉じるのは止めない。閉じても相手の封鎖は残す。
            var fade = new object();
            KCDInput.Block(fade);
            pause.Tick(Menu(41));

            Assert.IsFalse(pause.IsOpen, "ほかの封鎖があると Esc でポーズを閉じられない");
            Assert.IsFalse(KCDInput.IsBlockedBy(pause));
            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを閉じたら暗転の封鎖まで外れた");
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void Menu_WithNothingElseOpen_TogglesThePause()
        {
            PauseMenu pause = MakePause();

            pause.Tick(Menu(50));
            Assert.IsTrue(pause.IsOpen, "何も開いていないのに Esc でポーズが開かない");
            Assert.AreEqual(0f, Time.timeScale);

            pause.Tick(Menu(51));
            Assert.IsFalse(pause.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void AcceptsMenu_IgnoresOnlyThePausesOwnBlock()
        {
            Assert.IsTrue(PauseMenu.AcceptsMenu(false, false, false), "何も封鎖していないのに開けない");
            Assert.IsFalse(PauseMenu.AcceptsMenu(false, true, false), "ほかの封鎖があるのに開ける");
            Assert.IsTrue(PauseMenu.AcceptsMenu(true, true, false), "開いているポーズを閉じられない");
            Assert.IsTrue(PauseMenu.AcceptsMenu(false, true, true), "自分の封鎖で自分を止めている");
        }

        // ---- 「設定」を選んだ Enter の持ち越し (#103) ----

        [Test]
        public void Settings_ChosenWithEnter_OpensOnTheNextFrame()
        {
            PauseMenu pause = MakePause();
            SettingsView settings = MakeSettings();
            pause.Settings = settings;
            int frame = OpenAndMoveToSettings(pause, 100);

            // 決めた Enter のフレームで開くと、同じフレームの SettingsView.Update が同じ Enter を拾い、
            // 最初の行（BGM）で Adjust(1) → 音量 +10% を保存する。開くのは次のフレーム。
            pause.Tick(Submit(frame));
            Assert.IsFalse(settings.IsOpen, "「設定」を決めた Enter のフレームで設定が開いた（その Enter で BGM が +10% される）");
            Assert.IsTrue(pause.IsOpen, "設定が開く前にポーズの本体が消えた");

            // 同じフレームにもう一度回っても開かない。
            pause.Tick(Idle(frame));
            Assert.IsFalse(settings.IsOpen, "同じフレームのうちに設定が開いた");

            pause.Tick(Idle(frame + 1));
            Assert.IsTrue(settings.IsOpen, "次のフレームになっても設定が開かない");
            Assert.IsFalse(pause.IsOpen, "設定を出している間はポーズの本体を隠す");
            Assert.IsTrue(pause.IsPaused, "設定を出している間に時間が動き出した");
            Assert.IsTrue(KCDInput.IsBlockedBy(pause), "設定を出している間にポーズの封鎖が外れた");
            Assert.AreEqual(0f, Time.timeScale);
        }

        [Test]
        public void Settings_Closed_ShowsThePauseAgain()
        {
            PauseMenu pause = MakePause();
            SettingsView settings = MakeSettings();
            pause.Settings = settings;
            int frame = OpenAndMoveToSettings(pause, 200);
            pause.Tick(Submit(frame));
            pause.Tick(Idle(frame + 1));
            Assert.IsTrue(settings.IsOpen);

            settings.Close();

            Assert.IsTrue(pause.IsOpen, "設定を閉じてもポーズに戻らない");
            Assert.IsTrue(pause.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);

            // 設定から戻ったあとも、次の Enter でまた開ける（行は「設定」のまま）。
            pause.Tick(Submit(frame + 2));
            pause.Tick(Idle(frame + 3));
            Assert.IsTrue(settings.IsOpen, "設定から戻ったあとに設定を開き直せない");
        }

        [Test]
        public void Settings_NotOpened_WhenThePauseClosesBeforeTheNextFrame()
        {
            PauseMenu pause = MakePause();
            SettingsView settings = MakeSettings();
            pause.Settings = settings;
            int frame = OpenAndMoveToSettings(pause, 300);
            pause.Tick(Submit(frame));

            // 選んだあと、次のフレームを待たずにポーズが閉じられた（SetOpen(false) / シーンの片付け）。
            pause.SetOpen(false);
            pause.Tick(Idle(frame + 1));

            Assert.IsFalse(settings.IsOpen, "閉じたポーズから設定だけが開いた");
            Assert.IsFalse(pause.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
