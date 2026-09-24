using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 「はじめから」が必ず 8:30 / 1 日目 / まっさらな進行で始まること (#53)。
    ///
    /// GameManager は DontDestroyOnLoad でシーンをまたいで生き続けるので、裏エンドやリザルトから
    /// タイトルへ戻ったあとの新規開始が、前の周回の時刻・日付・クエスト・探索の記録を引きずっていた。
    /// 戻す口は <c>GameManager.BeginNewGame</c> ただ 1 つで、そこが呼ぶ部品
    /// （<c>DayStats.Reset</c> / <c>QuestSystem.ResetForNewGame</c>）と、時計の分かれ道
    /// （<c>DayNightCycle.StartingHours</c>）をここで押さえる。
    ///
    /// 「つづきから」(#38) は同じ分かれ道の逆側なので、同じ純関数で一緒に守る。
    /// </summary>
    public sealed class NewGameTests
    {
        /// <summary>autoStart のクエスト 1 件。黙る / 黙らないの違いだけを見るための最小の型。</summary>
        private const string AutoStartJson =
            "{\"id\":\"q_auto\",\"order\":1,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"npc\"}]}";

        [TearDown]
        public void TearDown()
        {
            // DayStats は static でテストをまたいで残る。次のテストに持ち越さない。
            DayStats.Reset();
        }

        private static QuestData[] Parse(string json)
        {
            return new[] { QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>) };
        }

        // ---- 時計の分かれ道（はじめから / つづきから）----

        [Test]
        public void StartingHours_GoesBackToMorningForANewGame()
        {
            // BeginNewGame が HasEnteredCampus を false に戻す。夜 19:54 でサボって終わっても、
            // 次の「はじめから」は _startHour（朝）から。
            Assert.AreEqual(DayRestart.DayStartHour,
                DayNightCycle.StartingHours(false, 19.9f, DayRestart.DayStartHour), 0.001f);
            Assert.AreEqual(8.5f, DayRestart.DayStartHour, 0.001f, "朝の時刻が 8:30 でなくなった");
        }

        [Test]
        public void StartingHours_KeepsTheClockWhenComingBackIntoCampus()
        {
            // 「つづきから」(SaveSystem.PrepareContinue) はセーブの時刻を入れて入場済みにしてから入る (#38)。
            // 建物から出入りして Campus を読み直すときも同じ道を通る。ここが壊れると時刻が朝に飛ぶ。
            var data = new SaveData { TimeHours = 17.25f };
            Assert.AreEqual(17.25f,
                DayNightCycle.StartingHours(true, data.TimeHours, DayRestart.DayStartHour), 0.001f);
        }

        [Test]
        public void StartingHours_IgnoresTheRememberedClockOnlyWhenNotEntered()
        {
            // 入場済みの真偽だけで決まる（時刻の値そのものでは切り替えない）。
            Assert.AreEqual(0f, DayNightCycle.StartingHours(true, 0f, DayRestart.DayStartHour), 0.001f,
                "0 時も引き継ぐ（覚えていない扱いにしない）");
            Assert.AreEqual(DayRestart.DayStartHour,
                DayNightCycle.StartingHours(false, DayRestart.DayStartHour, DayRestart.DayStartHour), 0.001f);
        }

        [Test]
        public void CampusScene_KeepsTheSameMorningAsDayRestart()
        {
            // DayNightCycle の _startHour はシーンに焼かれている。コードの既定値を
            // DayRestart.DayStartHour にしても、Campus.unity に残った古い値が使われる。
            // 朝の時刻が 2 か所に分かれていないかをシーンの実物で見る。
            string path = Path.Combine(Application.dataPath, "Scenes", "Campus.unity");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            Match match = Regex.Match(File.ReadAllText(path), @"^\s*_startHour:\s*([-\d.eE+]+)\s*$",
                RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "Campus.unity に DayNightCycle の _startHour が無い");

            float scened = float.Parse(match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(DayRestart.DayStartHour, scened, 0.001f,
                "Campus.unity の _startHour と DayRestart.DayStartHour がずれている");
        }

        // ---- 一日ごとの記録（探索率のもと）----

        [Test]
        public void DayStats_ResetClearsEverythingTheResultCounts()
        {
            DayStats.NoteEnter("library");
            DayStats.NoteCollect("item_card");
            DayStats.NotePhoto("ps_gate");

            Assert.AreEqual(1, DayStats.BuildingCount);
            Assert.AreEqual(1, DayStats.CollectedCount);
            Assert.AreEqual(1, DayStats.PhotoSpotCount);

            DayStats.Reset();

            // static なのでシーンを読み直しても消えない。消さないと 2 周目の探索率が前回ぶんから始まる。
            Assert.AreEqual(0, DayStats.BuildingCount);
            Assert.AreEqual(0, DayStats.CollectedCount);
            Assert.AreEqual(0, DayStats.PhotoSpotCount);
            Assert.IsFalse(DayStats.HasEntered("library"));
            Assert.IsFalse(DayStats.HasCollected("item_card"));
        }

        // ---- クエストの作り直し ----

        [Test]
        public void ResetForNewGame_ThrowsAwayTheProgressAndMakesNoSound()
        {
            var quests = new QuestSystem();
            quests.LoadFromResources();
            Assert.IsTrue(quests.IsActive("q_orientation"), "autoStart のクエストが受注されていない");

            // 前の周回の進行を作る。
            QuestStep dirty = quests.Find("q_orientation").Steps[0];
            dirty.Progress = dirty.Count;
            dirty.Completed = true;

            int started = 0;
            int changed = 0;
            int timers = 0;
            quests.QuestStarted += _ => started++;
            quests.Changed += () => changed++;
            quests.TimerStarted += _ => timers++;

            quests.ResetForNewGame();

            // タイトル画面での作り直し。AudioManager は同じ QuestSystem を購読したままなので、
            // イベントを出すと quest_start がタイトルで鳴る（AudioManager.Scene の OnQuestStarted）。
            Assert.AreEqual(0, started, "タイトルで受注のイベントが飛んでいる");
            Assert.AreEqual(0, changed, "タイトルで進行のイベントが飛んでいる");
            Assert.AreEqual(0, timers, "タイトルで計時開始のイベントが飛んでいる");

            // 中身は初回の起動と同じ。QuestData ごと読み直すので、汚したステップも別物に置き換わる。
            Assert.IsTrue(quests.IsActive("q_orientation"), "autoStart が受注し直されていない");
            QuestStep fresh = quests.Find("q_orientation").Steps[0];
            Assert.IsFalse(fresh.Completed, "達成済みのステップが残っている");
            Assert.AreEqual(0, fresh.Progress, "途中の進行が残っている");
        }

        [Test]
        public void SilentLoad_StillActivatesAutoStartQuests()
        {
            // 黙らせるのはイベントだけ。受注そのものを飛ばすと、はじめからでオリエンが始まらない。
            var quests = new QuestSystem();
            int started = 0;
            int changed = 0;
            quests.QuestStarted += _ => started++;
            quests.Changed += () => changed++;

            quests.Load(Parse(AutoStartJson), true);

            Assert.IsTrue(quests.IsActive("q_auto"));
            Assert.AreEqual(0, started);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void NormalLoad_StillTellsEveryone()
        {
            // 黙るのは silent のときだけ。ふだんの読み込み（GameManager.Awake）は今までどおり知らせる。
            var quests = new QuestSystem();
            int started = 0;
            int changed = 0;
            quests.QuestStarted += _ => started++;
            quests.Changed += () => changed++;

            quests.Load(Parse(AutoStartJson));

            Assert.AreEqual(1, started);
            Assert.AreEqual(1, changed);
        }

        // ---- まとめ役（GameManager.BeginNewGame）----

        [Test]
        public void BeginNewGame_PutsEverythingBackToTheFirstMorning()
        {
            // EditMode では Awake が走らない（ExecuteAlways ではない）ので、DontDestroyOnLoad も
            // 走らず Instance にも入らない。万一 Awake が走る Unity でも落ちないよう、
            // この 1 件だけログの取りこぼしを許す。
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("GameManagerForTest");

            try
            {
                GameManager manager = go.AddComponent<GameManager>();

                // 前の周回の残り。夜 19:54 に寮でサボって 3 日目、建物にも入った状態。
                manager.GameTimeHours = 19.9f;
                manager.DayNumber = 3;
                manager.HasEnteredCampus = true;
                DayStats.NoteEnter("library");
                DayStats.NotePhoto("ps_gate");

                manager.BeginNewGame();

                Assert.AreEqual(DayRestart.DayStartHour, manager.GameTimeHours, 0.001f, "朝に戻っていない");
                Assert.AreEqual(DayRestart.FirstDay, manager.DayNumber, "1 日目に戻っていない");
                Assert.IsFalse(manager.HasEnteredCampus,
                    "入場済みのままだと DayNightCycle.Start が前回の時刻を引き継ぐ");
                Assert.AreEqual(0, DayStats.BuildingCount, "探索の記録が残っている");
                Assert.AreEqual(0, DayStats.PhotoSpotCount, "写真の記録が残っている");
                Assert.IsNotNull(manager.Quests, "クエストが用意されていない");
                Assert.IsTrue(manager.Quests.IsActive("q_orientation"), "オリエンが始まっていない");

                // 時計は DayNightCycle の分かれ道と噛み合っていること（ここが #53 の直接の原因）。
                Assert.AreEqual(DayRestart.DayStartHour,
                    DayNightCycle.StartingHours(manager.HasEnteredCampus, manager.GameTimeHours,
                        DayRestart.DayStartHour), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = ignoring;
            }
        }

        [Test]
        public void BeginNewGame_LeavesTheChosenCharacterAlone()
        {
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("GameManagerForTest");

            // SelectedCharacterId の setter は PlayerPrefs に書く。テストで開発機の設定を書き換えない
            // （キャラを選んだことのない機械では、終わったあともキーを無いままにする）。
            var characterPref = new PlayerPrefsKeyScope(GameManager.CharacterPrefKey);

            try
            {
                GameManager manager = go.AddComponent<GameManager>();
                manager.SelectedCharacterId = "madonna";

                manager.BeginNewGame();

                // 「はじめから」はこのあとキャラ選択で決まる（CharacterSelect.Confirm が
                // BeginNewGame の直後に入れ直す）。既定値としても覚えておいてよいので消さない。
                Assert.AreEqual("madonna", manager.SelectedCharacterId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                characterPref.Dispose();
                LogAssert.ignoreFailingMessages = ignoring;
            }
        }

        [Test]
        public void SaveDefaults_StillMatchTheNewGame()
        {
            // セーブは「はじめから」で消さない。消さない代わりに、まっさらなセーブの既定値が
            // 新規開始の値と食い違っていないことを見ておく（SaveDataTests と同じ約束）。
            SaveData fresh = JsonUtility.FromJson<SaveData>("{}");
            Assert.AreEqual(DayRestart.DayStartHour, fresh.TimeHours, 1e-6f);
        }

        [Test]
        public void GameManager_FirstLaunch_StartsAtTheSameMorningAsANewGame()
        {
            // 起動直後（BeginNewGame を通る前）の時計も、はじめからの朝と同じ定数から取る (#53)。
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("GameManagerForTest");

            try
            {
                GameManager manager = go.AddComponent<GameManager>();

                Assert.AreEqual(DayRestart.DayStartHour, manager.GameTimeHours, 1e-6f);
                Assert.AreEqual(DayRestart.FirstDay, manager.DayNumber);
                Assert.IsFalse(manager.HasEnteredCampus);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = ignoring;
            }
        }
    }
}
