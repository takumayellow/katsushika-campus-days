using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ポーズメニューのキー操作の流れ。EditMode では Update が走らず Time.frameCount も進まないので、
    /// フレーム番号を明示した <see cref="PauseMenu.FrameInput"/> を <see cref="PauseMenu.Tick"/> に渡して進める。
    /// ・封鎖の最中（暗転・会話）に Esc で開かない (#105)
    /// ・「設定」を決めた Enter を設定パネルに持ち越さない (#103)
    /// ・「タイトルへ戻る」は確認してから戻る (#104)
    /// ラベルは渡さない（Redraw は null の板を飛ばす）ので、TextMeshPro の部品もローカライズも要らない。
    /// </summary>
    public sealed class PauseMenuFlowTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _timeScale;

        /// <summary>ReturnToTitleAction が呼ばれた回数。EditMode で GameManager.ReturnToTitle（LoadScene）は呼べない。</summary>
        private int _titleReturns;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            _titleReturns = 0;
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

        private static PauseMenu.FrameInput Down(int frame) => new PauseMenu.FrameInput(frame, vertical: 1);

        /// <summary>行の並び（EntryKeys）での「設定」の位置。</summary>
        private const int SettingsRow = 3;

        /// <summary>行の並び（EntryKeys）での「タイトルへ戻る」の位置。</summary>
        private const int TitleRow = 4;

        /// <summary>ポーズを開き、row の行まで下へ送る。次に使えるフレーム番号を返す。</summary>
        private static int OpenAndMoveTo(PauseMenu pause, int row, int frame)
        {
            pause.Tick(Menu(frame++));
            for (int i = 0; i < row; i++)
            {
                pause.Tick(Down(frame++));
            }

            return frame;
        }

        /// <summary>ポーズを開き、「設定」の行まで下へ送る。次に使えるフレーム番号を返す。</summary>
        private static int OpenAndMoveToSettings(PauseMenu pause, int frame) => OpenAndMoveTo(pause, SettingsRow, frame);

        /// <summary>タイトルへ戻る処理を数えるだけにしたポーズ。</summary>
        private PauseMenu MakePauseCountingTitleReturns()
        {
            PauseMenu pause = MakePause();
            pause.ReturnToTitleAction = () => _titleReturns++;
            return pause;
        }

        /// <summary>ポーズを開き、「タイトルへ戻る」を Enter で決める。次に使えるフレーム番号を返す。</summary>
        private static int ChooseReturnToTitle(PauseMenu pause, int frame)
        {
            frame = OpenAndMoveTo(pause, TitleRow, frame);
            pause.Tick(Submit(frame++));
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

        // ---- 「タイトルへ戻る」の確認 (#104) ----

        [Test]
        public void ReturnToTitle_FirstEnter_AsksInsteadOfLeaving()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();

            ChooseReturnToTitle(pause, 400);

            // 前はこの Enter で SetOpen(false) → GameManager.ReturnToTitle() をその場で呼び、
            // セーブしていない進行を確かめもせずに捨てていた。
            Assert.AreEqual(0, _titleReturns, "確かめる前にタイトルへ戻った（セーブしていない進行が消える）");
            Assert.IsTrue(pause.IsConfirmingReturnToTitle, "「タイトルへ戻る」を決めても確認が出ない");
            Assert.IsTrue(pause.IsOpen, "確認を出す前にポーズが閉じた");
            Assert.IsTrue(pause.IsPaused);
            Assert.IsTrue(KCDInput.IsBlockedBy(pause), "確認の間にポーズの封鎖が外れた");
            Assert.AreEqual(0f, Time.timeScale, "確認の間に時間が動き出した");
        }

        [Test]
        public void ReturnToTitle_EnterAgainWithoutMoving_StaysInTheGame()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            int frame = ChooseReturnToTitle(pause, 500);

            // 既定は「いいえ」。Enter を続けて 2 回押しただけでは進行を消さない。
            pause.Tick(Submit(frame));

            Assert.AreEqual(0, _titleReturns, "Enter の連打でタイトルへ戻った（既定が「はい」になっている）");
            Assert.IsFalse(pause.IsConfirmingReturnToTitle, "「いいえ」を決めても確認が消えない");
            Assert.IsTrue(pause.IsOpen, "「いいえ」でポーズまで閉じた");
            Assert.AreEqual(0f, Time.timeScale);
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void ReturnToTitle_Yes_LeavesOnce(int step)
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            int frame = ChooseReturnToTitle(pause, 600);

            pause.Tick(new PauseMenu.FrameInput(frame++, vertical: step));
            Assert.AreEqual(0, _titleReturns, "カーソルを動かしただけでタイトルへ戻った");

            pause.Tick(Submit(frame++));

            Assert.AreEqual(1, _titleReturns, "「はい」を決めてもタイトルへ戻らない");
            Assert.IsFalse(pause.IsOpen);
            Assert.IsFalse(pause.IsPaused);
            Assert.IsFalse(pause.IsConfirmingReturnToTitle);
            Assert.IsFalse(KCDInput.IsBlockedBy(pause));
            Assert.AreEqual(1f, Time.timeScale);

            // 閉じたあとの Enter でもう一度戻ったりはしない。
            pause.Tick(Submit(frame));
            Assert.AreEqual(1, _titleReturns, "タイトルへ戻る処理が二度呼ばれた");
        }

        [Test]
        public void ReturnToTitle_Esc_GoesBackToTheListWithoutClosingThePause()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            int frame = ChooseReturnToTitle(pause, 700);

            pause.Tick(Menu(frame++));

            Assert.AreEqual(0, _titleReturns);
            Assert.IsFalse(pause.IsConfirmingReturnToTitle, "Esc で確認が消えない");
            Assert.IsTrue(pause.IsOpen, "確認を Esc で閉じたらポーズまで閉じた");

            // 一覧に戻ったあとの Esc は、いつもどおりポーズを閉じる。
            pause.Tick(Menu(frame));
            Assert.IsFalse(pause.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void ReturnToTitle_Cancel_GoesBackToTheList()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            int frame = ChooseReturnToTitle(pause, 800);

            // パッドの東ボタン（KCDInput.CancelPressed）。一覧では何もしないが、確認では「いいえ」と同じ。
            pause.Tick(new PauseMenu.FrameInput(frame, cancel: true));

            Assert.AreEqual(0, _titleReturns);
            Assert.IsFalse(pause.IsConfirmingReturnToTitle, "戻るボタンで確認が消えない");
            Assert.IsTrue(pause.IsOpen);
        }

        [Test]
        public void ReturnToTitle_ConfirmIsForgotten_WhenThePauseIsClosedAndReopened()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            int frame = ChooseReturnToTitle(pause, 900);

            pause.SetOpen(false);
            Assert.IsFalse(pause.IsConfirmingReturnToTitle, "閉じたポーズに確認が残った");

            // 開き直すと一覧の先頭（ゲームに戻る）から。Enter でそのまま再開し、タイトルへは戻らない。
            pause.Tick(Menu(frame++));
            Assert.IsFalse(pause.IsConfirmingReturnToTitle);
            pause.Tick(Submit(frame));

            Assert.AreEqual(0, _titleReturns, "開き直したポーズで前の確認が生きていた");
            Assert.IsFalse(pause.IsOpen, "開き直したポーズの先頭が「ゲームに戻る」でない");
        }

        [Test]
        public void ReturnToTitle_ConfirmIsForgotten_WhenThePauseIsAbandoned()
        {
            PauseMenu pause = MakePauseCountingTitleReturns();
            ChooseReturnToTitle(pause, 1000);

            // 開いたまま片付けられた（OnDisable → Abandon）。時間と封鎖と一緒に確認も捨てる。
            pause.Abandon();

            Assert.IsFalse(pause.IsConfirmingReturnToTitle, "片付けたポーズに確認が残った");
            Assert.IsFalse(pause.IsPaused);
            Assert.IsFalse(KCDInput.IsBlockedBy(pause));
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, _titleReturns);
        }
    }
}
