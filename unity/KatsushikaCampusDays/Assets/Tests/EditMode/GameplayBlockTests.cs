using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 操作の封鎖（KCDInput.GameplayBlocked）をオーナーごとに持つこと (#40)。
    /// 建物の出入りや落下からの復帰の暗転が終わったときに、その間に始まった会話やポーズの封鎖まで外さない。
    /// 後半はポーズ・クエストログ・リザルトの実物で、閉じた画面がほかの画面の封鎖を外さないことを確かめる (#62)。
    /// </summary>
    public sealed class GameplayBlockTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
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

        /// <summary>
        /// ラベル無しのポーズメニュー。EditMode では Awake / Start / Update が走らないので、
        /// 開閉は SetOpen を直接呼ぶ（Esc の門は通らない）。
        /// </summary>
        private PauseMenu MakePause()
        {
            PauseMenu pause = Make("PauseForTest").AddComponent<PauseMenu>();
            pause.Bind(MakeRoot("PauseRoot"), null);
            return pause;
        }

        /// <summary>ラベル無しのクエストログ。Tab は Toggle、Esc で閉じるのは SetOpen(false) に当たる。</summary>
        private QuestLogView MakeLog()
        {
            QuestLogView log = Make("QuestLogForTest").AddComponent<QuestLogView>();
            log.Bind(MakeRoot("QuestLogRoot"), null);
            return log;
        }

        [Test]
        public void Unblock_OnlyReleasesTheOwnersOwnBlock()
        {
            var interior = new object();
            var dialogue = new object();

            KCDInput.Block(interior);
            KCDInput.Block(dialogue);
            Assert.IsTrue(KCDInput.GameplayBlocked);

            // 暗転が終わって出入り係が外しても、会話の封鎖は残る。
            KCDInput.Unblock(interior);
            Assert.IsTrue(KCDInput.GameplayBlocked, "会話中なのに操作が戻った");
            Assert.IsFalse(KCDInput.IsBlockedBy(interior));
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue));

            KCDInput.Unblock(dialogue);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void AssigningFalse_DoesNotReleaseOwnerBlocks()
        {
            var dialogue = new object();
            KCDInput.Block(dialogue);

            // ポーズやクエストログは閉じるときに GameplayBlocked = false を代入する。それで会話の封鎖は外れない。
            KCDInput.GameplayBlocked = true;
            KCDInput.GameplayBlocked = false;
            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを閉じたら会話中なのに動けるようになった");

            KCDInput.Unblock(dialogue);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void OwnerUnblock_DoesNotReleaseAScreenOpenedDuringTheFade()
        {
            // 落下からの復帰の暗転中に Esc のポーズを開いた。戻し終えても、ポーズを閉じるまでは封鎖したまま
            // （外すとポーズ中に F5/F9 や写真モードが効いてしまう）。
            var bounds = new object();
            KCDInput.Block(bounds);
            KCDInput.GameplayBlocked = true;

            KCDInput.Unblock(bounds);
            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを開いたまま封鎖が外れた");

            KCDInput.GameplayBlocked = false;
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void BlockingTwice_IsReleasedByOneUnblock()
        {
            var owner = new object();
            KCDInput.Block(owner);
            KCDInput.Block(owner);

            KCDInput.Unblock(owner);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void UnblockWithoutBlock_AndNullOwners_AreIgnored()
        {
            var other = new object();
            KCDInput.Block(other);

            KCDInput.Unblock(new object());
            KCDInput.Unblock(null);
            KCDInput.Block(null);
            Assert.IsTrue(KCDInput.GameplayBlocked);
            Assert.IsFalse(KCDInput.IsBlockedBy(null));

            KCDInput.Unblock(other);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void ClearAllBlocks_ReleasesOwnersAndTheSharedFlag()
        {
            KCDInput.Block(new object());
            KCDInput.GameplayBlocked = true;

            // シーンを切り替えるときだけ使う。
            KCDInput.ClearAllBlocks();
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        // ---- 画面ごとの封鎖 (#62) ----

        [Test]
        public void QuestLog_ClosedDuringThePause_LeavesThePauseBlocked()
        {
            PauseMenu pause = MakePause();
            QuestLogView log = MakeLog();

            pause.SetOpen(true);

            // ポーズ中にログを開いて閉じた。前は両方が共有の GameplayBlocked に代入していたので、
            // ログを閉じたところでポーズの封鎖まで外れ、timeScale 0 のまま F5 / F9 や写真モードが効いた。
            log.SetOpen(true);
            log.SetOpen(false);

            Assert.IsTrue(KCDInput.GameplayBlocked, "ログを閉じたらポーズ中なのに封鎖が外れた");
            Assert.IsTrue(KCDInput.IsBlockedBy(pause));
            Assert.AreEqual(0f, Time.timeScale, "ポーズ中に時間が動き出した");

            pause.SetOpen(false);
            Assert.IsFalse(KCDInput.GameplayBlocked);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void QuestLog_DoesNotOpenOverOtherScreens()
        {
            PauseMenu pause = MakePause();
            QuestLogView log = MakeLog();

            pause.SetOpen(true);
            log.Toggle();
            Assert.IsFalse(log.IsOpen, "ポーズの上に Tab でログが開いた");
            pause.SetOpen(false);

            var dialogue = new object();
            KCDInput.Block(dialogue);
            log.Toggle();
            Assert.IsFalse(log.IsOpen, "会話の最中に Tab でログが開いた");
            KCDInput.Unblock(dialogue);

            KCDInput.PhotoMode = true;
            log.Toggle();
            Assert.IsFalse(log.IsOpen, "写真モードの最中に Tab でログが開いた");
            KCDInput.PhotoMode = false;

            log.Toggle();
            Assert.IsTrue(log.IsOpen, "ほかに何も開いていないのに Tab でログが開かない");
            Assert.IsTrue(KCDInput.IsBlockedBy(log), "ログは自分の名前で封鎖する");
        }

        [Test]
        public void QuestLog_CanAlwaysBeClosed()
        {
            QuestLogView log = MakeLog();
            log.Toggle();
            Assert.IsTrue(log.IsOpen);

            // 開いている間にほかの封鎖が掛かっても（暗転など）、閉じるのは止めない。閉じても相手の封鎖は残す。
            var fade = new object();
            KCDInput.Block(fade);
            log.Toggle();

            Assert.IsFalse(log.IsOpen, "ほかの封鎖があるとログを閉じられない");
            Assert.IsFalse(KCDInput.IsBlockedBy(log));
            Assert.IsTrue(KCDInput.GameplayBlocked, "ログを閉じたら暗転の封鎖まで外れた");
        }

        [Test]
        public void QuestLog_CanOpen_IgnoresOnlyItsOwnBlock()
        {
            Assert.IsTrue(QuestLogView.CanOpen(false, false, false));
            Assert.IsFalse(QuestLogView.CanOpen(true, false, false), "ほかの封鎖があるのに開ける");
            Assert.IsTrue(QuestLogView.CanOpen(true, true, false), "自分の封鎖で自分を止めている");
            Assert.IsFalse(QuestLogView.CanOpen(false, false, true), "写真モード中に開ける");
        }

        [Test]
        public void Pause_Closing_LeavesADialogueBlocked()
        {
            PauseMenu pause = MakePause();
            var dialogue = new object();
            KCDInput.Block(dialogue);

            pause.SetOpen(true);
            pause.SetOpen(false);

            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを閉じたら会話中なのに動けるようになった");
            Assert.IsFalse(KCDInput.IsBlockedBy(pause));
        }

        [Test]
        public void Pause_Abandon_GivesBackOnlyWhatThePauseHeld()
        {
            PauseMenu pause = MakePause();
            var dialogue = new object();

            pause.SetOpen(true);
            KCDInput.Block(dialogue);
            Assert.IsTrue(pause.IsPaused);

            // 開いたまま無効にされた（OnDisable が呼ぶ）。止めた時間と自分の封鎖だけを戻す。
            pause.Abandon();
            Assert.IsFalse(pause.IsPaused);
            Assert.AreEqual(1f, Time.timeScale, "ポーズが止めた時間が戻らない");
            Assert.IsFalse(KCDInput.IsBlockedBy(pause), "ポーズの封鎖が残った");
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue), "会話の封鎖まで外れた");

            // 開いていないポーズは、ほかの画面（リザルト・裏エンド）が止めた時間に触らない。
            Time.timeScale = 0f;
            pause.Abandon();
            Assert.AreEqual(0f, Time.timeScale, "開いていないポーズが時間を動かした");
        }

        [Test]
        public void Result_Close_ReleasesOnlyItsOwnBlock()
        {
            ResultScreen screen = Make("ResultForTest").AddComponent<ResultScreen>();
            screen.Bind(MakeRoot("ResultRoot"), null, null, null, null);
            var fade = new object();

            try
            {
                screen.Show(new ResultData(), null, null);
                Assert.IsTrue(KCDInput.IsBlockedBy(screen), "リザルトは自分の名前で封鎖する");
                Assert.AreEqual(0f, Time.timeScale);

                KCDInput.Block(fade);
                screen.Close();

                Assert.IsFalse(KCDInput.IsBlockedBy(screen));
                Assert.IsTrue(KCDInput.GameplayBlocked, "リザルトを閉じたらほかの封鎖まで外れた");
                Assert.AreEqual(1f, Time.timeScale);
            }
            finally
            {
                if (screen.IsOpen)
                {
                    screen.Close();
                }
            }
        }
    }
}
