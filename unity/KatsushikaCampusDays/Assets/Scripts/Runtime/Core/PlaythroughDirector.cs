using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// 紹介動画のための通しプレイの自動操縦。-kcd-playthrough &lt;formal|dorm&gt; があるときだけ生まれる。
    ///   formal  新しく始めて本編のクエストを順に片付け、本編のエンディング（<see cref="FormalEnding"/>）まで
    ///   dorm    新しく始めてまっすぐ寮へ帰り、寮長と話して裏エンド（<see cref="DormEnding"/>）まで
    ///   full    新しく始めて本編・サブ・宝探し・隠しアイテム・写真スポットを全部終えるまで（日をまたぐ）。
    ///           本編のエンディングを見てもキャンパスへ戻って続け、寮には行かない
    ///   -kcd-record &lt;base&gt;   録画する（<see cref="PlaythroughRecorder"/>）。-kcd-ffmpeg &lt;path&gt; で ffmpeg を指定
    /// 操作は人と同じく仮想のキーボード（E / Enter / ↓）と、プレイヤーの自動歩行（NavMesh の経路をたどる）で行う。
    /// 結末を見てタイトルへ戻ったら録画を閉じて終了する。セーブは persistentDataPath/playthrough/ に分ける。
    /// </summary>
    public sealed partial class PlaythroughDirector : MonoBehaviour
    {
        public const string SaveFolder = "playthrough";
        private const float WalkSpeed = 3.2f;
        private const float InteractRange = 2.6f;
        private const float InteractInterval = 1.5f;
        private const float StuckWindow = 4f;
        private const float TeleportAfter = 15f;
        private const float TeleportAfterNear = 8f;
        private const float NearDistance = 20f;

        /// <summary>目的地がこれより遠ければ走る（m）。寮までは 700 m ある。</summary>
        private const float RunDistance = 60f;

        /// <summary>これより遠い NavMesh の外の目的地は、NavMesh の端まで経路で行ってから格子で探す。</summary>
        private const float GridSpan = 150f;

        private enum Route { Formal, Dorm, Full }

        private Route _route;
        private PlaythroughRecorder _recorder;
        private Keyboard _keyboard;
        private float _clock;
        private float _maxSeconds;
        private bool _sawEnding;
        private float _endingGoneAt = -1f;
        private float _nextKeyAt;
        private object _lastLine;
        private float _lineShownAt;
        private float _choiceShownAt = -1f;
        private string _lastGoalKey;
        private Vector3 _pathGoal;
        private Vector3[] _corners = new Vector3[0];
        private int _corner;
        private Vector3 _gridGoal;
        private Vector3[] _gridCorners = new Vector3[0];
        private int _gridSearches;
        private float _pathAt = -10f;
        private float _bestDistance = float.MaxValue;

        /// <summary>今のフレームで _clock を進めた秒数。</summary>
        private float _step;

        /// <summary>前の Tick の目的地。</summary>
        private Vector3 _lastGoal;
        private float _progressAt;
        private float _windowStart;
        private float _windowDistance = float.MaxValue;
        private float _sidestepUntil = -1f;
        private float _nearSince = -1f;
        private bool _facedGoal;
        private int _teleports;
        private int _keyTeleports;

        /// <summary>この目的で NavMesh の経路を捨てて格子で探し直したか。NavMesh は通れても実際は柵で止まる所がある。</summary>
        private bool _gridForced;

        /// <summary>この目的で、進めないので格子で探し直した回数。</summary>
        private int _gridRetries;

        /// <summary>進めないとき格子で探し直す回数。使い切ったらワープする。探すたびに出発点が変わり、2 回目で抜けることがある。</summary>
        private const int GridRetryLimit = 2;

        /// <summary>この目的で、引き直した経路の長さに合わせて残りの道のりを引き上げた回数。</summary>
        private int _routeRaises;

        /// <summary>
        /// 残りの道のりを引き上げてよい回数。隅で避けては引き直すたびに引き上げると、その後わずかに縮むだけで
        /// 進んだことになり、いつまでもワープも探し直しもしない（第1実験棟の隅で 8 分止まった）。
        /// </summary>
        public const int MaxRouteRaises = 3;

        /// <summary>ログに出す道の角の数。</summary>
        private const int RouteLogCorners = 6;

        private bool _waitingForHour;
        private string _scene = string.Empty;
        private float _sceneAt;
        private float _gateAt = -10f;

        /// <summary>結果画面が開いたのを見た時刻。開いていなければ -1。</summary>
        private float _resultShownAt = -1f;
        private bool _localeFixed;
        private bool _volumeFixed;
        private bool _formalDone;
        private string _extraTarget;
        private readonly HashSet<string> _skipped = new HashSet<string>();

        /// <summary>full で、寄り道（隠しアイテム・写真スポット・宝探し）1 つにワープがこの回数を超えたら諦めて次へ行く。</summary>
        private const int ExtraTeleportLimit = 3;

        internal static void Install(string route)
        {
            string folder = Path.Combine(Application.persistentDataPath, SaveFolder,
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            SaveSystem.DirectoryOverride = folder;
            PhotoSystem.DirectoryOverride = folder;

            var go = new GameObject("PlaythroughDirector");
            DontDestroyOnLoad(go);
            PlaythroughDirector director = go.AddComponent<PlaythroughDirector>();
            director._route = route == "dorm" ? Route.Dorm : route == "full" ? Route.Full : Route.Formal;
            director._maxSeconds = SmokeDirector.ArgFloat("-kcd-playthrough-max", 5f * 3600f);

            string record = SmokeDirector.Arg("-kcd-record");
            if (!string.IsNullOrEmpty(record))
            {
                // 録画を始めるまでは本体から音を出さない（始めたら AudioRenderer が音を受け取る）。
                AudioListener.volume = 0f;
                director._recorder = go.AddComponent<PlaythroughRecorder>();
                director._recorder.Configure(record, SmokeDirector.Arg("-kcd-ffmpeg"));
            }

            Debug.Log("[KCD] playthrough: route=" + director._route + " save=" + folder);
        }

        private void Awake()
        {
            // 窓がフォーカスを失っても仮想キーボードを止めない（既定では裏に回るとキーボードが無効になる）。
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("PlaythroughKeyboard");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_keyboard != null && _keyboard.added)
            {
                InputSystem.RemoveDevice(_keyboard);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _scene = scene.name;
            _sceneAt = _clock;
            Debug.Log("[KCD] playthrough: scene " + scene.name + " t=" + Clock());
        }

        private string Clock() => _clock.ToString("F1", CultureInfo.InvariantCulture);

        private void Update()
        {
            _step = _recorder != null && _recorder.IsRecording
                ? 1f / PlaythroughRecorder.FrameRate
                : Time.unscaledDeltaTime;
            _clock += _step;
            ApplyDefaults();

            if (string.IsNullOrEmpty(_scene))
            {
                _scene = SceneManager.GetActiveScene().name;
                _sceneAt = _clock;
            }

            if (_recorder != null && !_recorder.Attempted && _clock - _sceneAt > 0.5f)
            {
                _recorder.Begin();
            }

            if (_clock > _maxSeconds)
            {
                Finish("時間切れ");
                return;
            }

            if (TrackEnding())
            {
                return;
            }

            if (_scene == GameManager.TitleSceneName)
            {
                if (_clock - _sceneAt > 4f && Due(1.8f))
                {
                    Press(Key.Enter);
                }

                return;
            }

            if (_scene == GameManager.CampusSceneName)
            {
                TickCampus();
            }
        }

        /// <summary>結末を見届けたら true（その後は何もしない）。</summary>
        private bool TrackEnding()
        {
            bool showing = FormalEnding.IsAnyShowing || DormEnding.IsAnyShowing;
            if (showing)
            {
                if (!_sawEnding)
                {
                    Debug.Log("[KCD] playthrough: 結末 t=" + Clock());
                }

                _sawEnding = true;
                Player()?.ClearAutopilot();
                // 1 枚ずつ読めるだけ待ってから Enter（本編は 3 枚、裏エンドは 1 枚）。
                if (Due(DormEnding.IsAnyShowing ? 9f : 7f))
                {
                    Press(Key.Enter);
                }

                return true;
            }

            if (!_sawEnding)
            {
                return false;
            }

            if (_endingGoneAt < 0f)
            {
                _endingGoneAt = _clock;
            }

            if (_clock - _endingGoneAt > 4f)
            {
                if (_route == Route.Full)
                {
                    // 本編のエンディングを閉じるとキャンパスへ戻る。残りのサブと寄り道を続ける。
                    Debug.Log("[KCD] playthrough: 本編のエンディングを見た。続ける t=" + Clock());
                    _formalDone = true;
                    _sawEnding = false;
                    _endingGoneAt = -1f;
                    return false;
                }

                Finish("結末まで到達");
            }

            return true;
        }

        /// <summary>
        /// 動画は日本語・既定の音量で撮る。どちらも保存しない（普段の設定を書き換えない）。
        /// 音量は AudioManager ができてから戻す。
        /// </summary>
        private void ApplyDefaults()
        {
            if (!_localeFixed)
            {
                _localeFixed = true;
                L.ResetForNextVisitor();
            }

            if (!_volumeFixed && AudioManager.Instance != null)
            {
                _volumeFixed = true;
                AudioManager.Instance.ResetVolumesForNextVisitor();
            }
        }

        private void Finish(string why)
        {
            Debug.Log("[KCD] playthrough: 終了（" + why + "）t=" + Clock() + " teleports=" + _teleports + " grid=" + _gridSearches);
            _recorder?.End();
            enabled = false;
            Application.Quit();
        }

        private void TickCampus()
        {
            if (_clock - _sceneAt < 3f)
            {
                return;
            }

            if (ResultScreen.IsAnyOpen)
            {
                Player()?.ClearAutopilot();
                _progressAt = _clock;
                if (_resultShownAt < 0f)
                {
                    _resultShownAt = _clock;
                }

                // 開いた直後は結果画面が入力を受け付けない（InputGuardSeconds）。読める間を置いてから 1 回で進める。
                if (_clock - _resultShownAt >= 3f && Due(6f))
                {
                    Debug.Log("[KCD] playthrough: 一日の終わり → もう一日 t=" + Clock());
                    Press(Key.Enter);
                }

                return;
            }

            _resultShownAt = -1f;
            if (PhotoSystem.Instance != null && PhotoSystem.Instance.IsCapturing)
            {
                // 撮影はプレイヤーを三脚の前へ自動で走らせる。終わるまで手を出さない。
                _progressAt = _clock;
                return;
            }

            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue != null && dialogue.IsPlaying)
            {
                Player()?.ClearAutopilot();
                // 会話の間は進めない時間に数えない。ただし今に戻しはしない。違う相手と話し続けるとき
                // （306 の PC のそばの TA など）も、会話の外で止まっている時間がたまってワープに回る。
                _progressAt += _step;
                TickDialogue(dialogue);
                return;
            }

            _lastLine = null;
            _choiceShownAt = -1f;
            if (TickElevatorMenu())
            {
                return;
            }

            if (KCDInput.GameplayBlocked)
            {
                Player()?.ClearAutopilot();
                _progressAt = _clock;
                if (Due(2.5f))
                {
                    Press(Key.Enter);
                }

                return;
            }

            PlayerController player = Player();
            if (player == null)
            {
                return;
            }

            if (!TryGoal(player, out Vector3 goal, out string key, out bool interact, out bool hurry))
            {
                player.ClearAutopilot();
                return;
            }

            if (interact && TryTalkerPosition(key, goal, out Vector3 talker))
            {
                goal = talker;
            }

            TryFloorGoal(player.transform.position, ref goal, ref key, ref interact);

            GateEntrances(goal, key);
            Walk(player, goal, key, interact, hurry);
        }

        private static string NpcIdOf(string key)
        {
            if (key == "dorm_head")
            {
                return "dorm_head";
            }

            int at = key.IndexOf('@');
            return key.StartsWith("Talk:", StringComparison.Ordinal) && at > 5 ? key.Substring(5, at - 5) : null;
        }

        /// <summary>
        /// 話しかける相手の今の位置。案内の目的地は相手の持ち場の印で、歩き回る相手や印から離れて立つ相手とは
        /// ずれる。印のそばで E を押しても別の物が選ばれ続けるので、相手そのものへ向かう。印から 40 m 以内の
        /// 同じ id の NPC のうち最も近いもの。
        /// </summary>
        private static bool TryTalkerPosition(string key, Vector3 goal, out Vector3 position)
        {
            position = goal;
            string npcId = NpcIdOf(key);
            if (npcId == null)
            {
                return false;
            }

            float best = 40f;
            bool found = false;
            foreach (NPCTalker talker in FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (talker.NpcId != npcId || !talker.CanInteract)
                {
                    continue;
                }

                float d = Flat(talker.transform.position - goal).magnitude;
                if (d < best)
                {
                    best = d;
                    position = talker.transform.position;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 目的地のそばの入口だけ歩いて入れるようにする。道の途中で別の建物の入口を踏んで
        /// 屋内へ運ばれないように、それ以外の入口は止めておく（止めた入口は E でも入れない）。
        /// </summary>
        private void GateEntrances(Vector3 goal, string key)
        {
            if (_clock - _gateAt < 1f)
            {
                return;
            }

            _gateAt = _clock;
            foreach (EntranceTrigger entrance in FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                entrance.enabled = Flat(entrance.transform.position - goal).magnitude < 8f;
            }

            // 屋内の出口も同じ。屋内には NavMesh が無く、まっすぐ歩くと途中の出口を踏んで外へ戻され、
            // 入っては出るを繰り返す。出口が目的地のときだけ踏めるようにする（MonoBehaviour を止めても
            // OnTriggerEnter は届くので、コライダーを切る）。食堂の注文端末のように出口のすぐそばにある
            // 目的地もあるので、距離ではなく目的が出口かどうかで決める。
            bool leaving = key != null && key.StartsWith(ExitKey, StringComparison.Ordinal);
            foreach (InteriorExit exit in FindObjectsByType<InteriorExit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (exit.TryGetComponent(out Collider box))
                {
                    box.enabled = leaving && Flat(exit.transform.position - goal).magnitude < 8f;
                }
            }
        }

        private void TickDialogue(DialogueSystem dialogue)
        {
            if (dialogue.IsChoosing)
            {
                if (_choiceShownAt < 0f)
                {
                    _choiceShownAt = _clock;
                }

                if (_clock - _choiceShownAt < 2f || !Due(0.5f))
                {
                    return;
                }

                int target = PickChoice(dialogue);
                if (dialogue.ChoiceIndex < target)
                {
                    Press(Key.DownArrow);
                }
                else if (dialogue.ChoiceIndex > target)
                {
                    Press(Key.UpArrow);
                }
                else
                {
                    Press(Key.Enter);
                    _choiceShownAt = -1f;
                }

                return;
            }

            _choiceShownAt = -1f;
            DialogueLine line = dialogue.CurrentLine;
            if (!ReferenceEquals(line, _lastLine))
            {
                _lastLine = line;
                _lineShownAt = _clock;
            }

            int length = line != null ? line.DisplayText.Length : 0;
            float read = Mathf.Min(1.4f + 0.07f * length, 7f);
            if (_clock - _lineShownAt > read && Due(1f))
            {
                Press(Key.Enter);
            }
        }

        /// <summary>フラグを立てる選択肢（正解）を先に、無ければやり直しにならないものを選ぶ。</summary>
        private static int PickChoice(DialogueSystem dialogue)
        {
            var choices = dialogue.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                if (!string.IsNullOrEmpty(choices[i].SetsFlag))
                {
                    return i;
                }
            }

            for (int i = 0; i < choices.Count; i++)
            {
                if (!choices[i].Retry)
                {
                    return i;
                }
            }

            return 0;
        }

        private bool TryGoal(PlayerController player, out Vector3 goal, out string key, out bool interact, out bool hurry)
        {
            goal = Vector3.zero;
            key = null;
            interact = false;
            hurry = false;
            _waitingForHour = false;
            Vector3 from = player.transform.position;
            if (_route == Route.Dorm)
            {
                return DormGoal(from, out goal, out key, out interact);
            }

            QuestSystem quests = GameManager.Instance != null ? GameManager.Instance.Quests : null;
            string building = InteriorLoader.Instance != null ? InteriorLoader.Instance.CurrentId : string.Empty;
            if (_route == Route.Full)
            {
                return FullGoal(quests, from, building, out goal, out key, out interact, out hurry);
            }

            return StepGoal(FormalStep(quests), from, building, out goal, out key, out interact, out hurry);
        }

        /// <summary>ステップの目的地。案内（ミニマップ）と同じ場所を指す。屋内で別の建物が目的地なら、まず出口。</summary>
        private bool StepGoal(QuestStep step, Vector3 from, string building, out Vector3 goal, out string key, out bool interact, out bool hurry)
        {
            goal = Vector3.zero;
            key = null;
            interact = false;
            hurry = false;
            _waitingForHour = false;
            if (step == null)
            {
                return false;
            }

            Minimap minimap = FindAnyObjectByType<Minimap>();
            bool found = minimap != null
                ? minimap.TryGoal(step, from, building, out goal)
                : QuestObjectiveLocator.TryLocate(step, from, out goal);
            if (!found)
            {
                return false;
            }

            // 屋内は互いに遠く離して建ててある。中にいて目的地がずっと遠ければ別の建物なので、まず外へ出る。
            // 外の目的地は案内がこの建物の出口に置き換えて返すので、それも出口として扱う（出口の判定を開ける）。
            if (!string.IsNullOrEmpty(building)
                && QuestObjectiveLocator.TryLocateExit(building, from, out Vector3 exit)
                && (Flat(goal - from).magnitude > 150f || Flat(goal - exit).magnitude < 0.5f))
            {
                goal = exit;
                key = ExitKey + building;
                return true;
            }

            QuestStepKind kind = step.AwaitingGiver ? QuestStepKind.Talk : step.Kind;
            key = kind + ":" + (step.AwaitingGiver ? step.Giver : step.Target) + "@" + building;
            // 入口も E で入る。扉の前で止まると、歩いて判定の箱の奥まで踏み込まないことがある。
            interact = kind == QuestStepKind.Talk || kind == QuestStepKind.Collect || kind == QuestStepKind.Flag
                       || kind == QuestStepKind.Enter || AtEntrance(goal);
            hurry = step.IsTimed;
            _waitingForHour = TooEarly(step);
            return true;
        }

        /// <summary>
        /// 目的地が建物の入口そのものか。屋内の地点を外から目指すと、案内はその建物の入口を指す。
        /// 入口の前で止まると判定の箱の奥まで踏み込まないことがあるので、そのときも E で入る。
        /// </summary>
        private static bool AtEntrance(Vector3 goal)
        {
            foreach (EntranceTrigger entrance in FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (Flat(entrance.transform.position - goal).magnitude < 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>時刻の条件（minHour）より前か。着いても達成にならない。</summary>
        private static bool TooEarly(QuestStep step)
        {
            return step != null && step.MinHour > 0f && GameManager.Instance != null
                   && GameManager.Instance.GameTimeHours < step.MinHour;
        }

        /// <summary>受けている本編のクエストのうち order が最も小さいものの今のステップ。無ければ次の依頼主。</summary>
        private static QuestStep FormalStep(QuestSystem quests)
        {
            if (quests == null)
            {
                return null;
            }

            // 時刻の条件がまだ来ていないステップ（18 時以降の水盤など）は後回しにし、ほかの本編か次の依頼を先にする。
            // どちらも無ければその場で時刻を待つ。
            QuestData best = null;
            QuestData early = null;
            foreach (QuestData quest in quests.All)
            {
                if (quest.Side || quest.Secret || !quests.IsActive(quest.Id))
                {
                    continue;
                }

                if (TooEarly(quest.CurrentStep))
                {
                    if (early == null || quest.Order < early.Order)
                    {
                        early = quest;
                    }
                }
                else if (best == null || quest.Order < best.Order)
                {
                    best = quest;
                }
            }

            if (best != null)
            {
                return best.CurrentStep;
            }

            QuestStep offer = quests.NextOffer?.OfferStep;
            return offer ?? early?.CurrentStep;
        }

        /// <summary>
        /// 完全クリアの次の目的地。受けているクエスト（本編を先に、order 順）→ 次の依頼（本編 → サブ）→ 宝探しの依頼主
        /// → 残りの隠しアイテム → 残りの写真スポット → 時刻待ちのクエスト（夜の散歩など）の順に、場所の分かる最初のもの。
        /// 何も残っておらず本編のエンディングも見ていれば終える。
        /// </summary>
        private bool FullGoal(QuestSystem quests, Vector3 from, string building, out Vector3 goal, out string key, out bool interact, out bool hurry)
        {
            goal = Vector3.zero;
            key = null;
            interact = false;
            hurry = false;
            _waitingForHour = false;
            if (quests == null)
            {
                return false;
            }

            _extraTarget = null;
            if (TryActive(quests, false, from, building, out goal, out key, out interact, out hurry)
                || StepGoal(quests.NextOffer?.OfferStep, from, building, out goal, out key, out interact, out hurry)
                || StepGoal(quests.NextSideOffer?.OfferStep, from, building, out goal, out key, out interact, out hurry))
            {
                return true;
            }

            QuestStep hunt = ExploreGuide.HuntStep(DayStats.HiddenCollectedCount(CollectibleCatalog.Instance), quests);
            if (hunt != null && !_skipped.Contains("hunt"))
            {
                if (StepGoal(hunt, from, building, out goal, out key, out interact, out hurry))
                {
                    _extraTarget = "hunt";
                    return true;
                }

                Debug.Log("[KCD] playthrough: 場所が分からないので飛ばす hunt t=" + Clock());
                _skipped.Add("hunt");
            }

            CollectibleCatalog catalog = CollectibleCatalog.Instance;
            CatalogItem item = catalog != null
                ? ExploreGuide.NextHidden(catalog.Items, id => DayStats.HasCollected(id) || _skipped.Contains(id))
                : null;
            if (item != null && StepGoal(new QuestStep { Kind = QuestStepKind.Collect, Target = item.Id }, from, building,
                    out goal, out key, out interact, out hurry))
            {
                _extraTarget = item.Id;
                return true;
            }

            CatalogPhotoSpot spot = catalog != null
                ? ExploreGuide.NextPhotoSpot(catalog.PhotoSpots, id => DayStats.HasPhotographed(id) || _skipped.Contains(id))
                : null;
            if (spot != null && PhotoGoal(spot, from, building, out goal, out key, out interact))
            {
                _extraTarget = spot.Id;
                return true;
            }

            if (TryActive(quests, true, from, building, out goal, out key, out interact, out hurry))
            {
                return true;
            }

            if (item != null || spot != null)
            {
                // 場所が分からないものが残っている。止まらずに飛ばす。
                string id = item != null ? item.Id : spot.Id;
                Debug.Log("[KCD] playthrough: 場所が分からないので飛ばす " + id + " t=" + Clock());
                _skipped.Add(id);
                return false;
            }

            // 受けられる依頼が残っている間は終えない（後の日に出てくるサブを飛ばさない）。
            if (_formalDone && quests.NextOffer == null && quests.NextSideOffer == null
                && quests.All.All(q => !quests.IsActive(q.Id)))
            {
                Finish("完全クリア" + (_skipped.Count > 0 ? " 飛ばした " + string.Join(",", _skipped) : string.Empty));
            }

            return false;
        }

        /// <summary>受けているクエストのうち order が最も小さいものの今のステップ。本編を先に、サブ・隠しはその後。</summary>
        private bool TryActive(QuestSystem quests, bool early, Vector3 from, string building,
            out Vector3 goal, out string key, out bool interact, out bool hurry)
        {
            goal = Vector3.zero;
            key = null;
            interact = false;
            hurry = false;
            QuestData[] active = quests.All
                .Where(q => quests.IsActive(q.Id) && q.CurrentStep != null && TooEarly(q.CurrentStep) == early)
                .OrderBy(q => q.Side || q.Secret ? 1 : 0)
                .ThenBy(q => q.Order)
                .ToArray();
            foreach (QuestData quest in active)
            {
                if (StepGoal(quest.CurrentStep, from, building, out goal, out key, out interact, out hurry))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 写真スポットの目的地。三脚（<see cref="PhotoSpot"/>）が今の場所にあればその前。屋内のスポットで外にいれば
        /// その建物の入口、別の建物の中にいればそこの出口。
        /// </summary>
        private bool PhotoGoal(CatalogPhotoSpot spot, Vector3 from, string building, out Vector3 goal, out string key, out bool interact)
        {
            goal = Vector3.zero;
            key = null;
            interact = false;
            string place = spot.Place.IsIndoor ? spot.Place.Building : string.Empty;
            if (place == (building ?? string.Empty))
            {
                foreach (PhotoSpot tripod in FindObjectsByType<PhotoSpot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (tripod.SpotId == spot.Id)
                    {
                        goal = tripod.transform.position;
                        key = PhotoKey + spot.Id + "@" + building;
                        interact = true;
                        return true;
                    }
                }

                return false;
            }

            if (!string.IsNullOrEmpty(building))
            {
                key = ExitKey + building;
                return QuestObjectiveLocator.TryLocateExit(building, from, out goal);
            }

            return StepGoal(new QuestStep { Kind = QuestStepKind.Enter, Target = place }, from, building,
                out goal, out key, out interact, out _);
        }

        private const string PhotoKey = "Photo:";

        private const string ExitKey = "exit:";

        private static bool DormGoal(Vector3 from, out Vector3 goal, out string key, out bool interact)
        {
            goal = Vector3.zero;
            interact = true;
            InteriorLoader interior = InteriorLoader.Instance;
            if (interior != null && interior.CurrentId == DormRoute.Id)
            {
                key = "dorm_head";
                foreach (NPCTalker npc in FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (npc.NpcId == "dorm_head")
                    {
                        goal = npc.transform.position;
                        return true;
                    }
                }

                return false;
            }

            if (interior != null && interior.IsInside)
            {
                key = ExitKey + interior.CurrentId;
                interact = false;
                return QuestObjectiveLocator.TryLocateExit(interior.CurrentId, from, out goal);
            }

            key = "dorm_entrance";
            DormEntrance entrance = FindAnyObjectByType<DormEntrance>();
            if (entrance == null)
            {
                return false;
            }

            goal = entrance.transform.position;
            return true;
        }

        private void Walk(PlayerController player, Vector3 goal, string key, bool interact, bool hurry)
        {
            Vector3 here = player.transform.position;
            float distance = Flat(goal - here).magnitude;
            if (key != _lastGoalKey)
            {
                Debug.Log("[KCD] playthrough: 目的 " + key + " 距離 " + distance.ToString("F0", CultureInfo.InvariantCulture)
                          + "m t=" + Clock() + " 時刻 " + (GameManager.Instance != null ? GameManager.Instance.GameTimeHours : 0f).ToString("F2", CultureInfo.InvariantCulture));
                _lastGoalKey = key;
                _keyTeleports = 0;
                _gridForced = false;
                _gridRetries = 0;
                _routeRaises = 0;
                _gridMissed = false;
                _gridRecoveries = 0;
                _bestDistance = distance;
                _progressAt = _clock;
                _windowStart = _clock;
                _windowDistance = distance;
                _pathAt = -10f;
                _nearSince = -1f;
                _facedGoal = false;
            }

            // 進んだかは道のりの残りで測る。机の列を回り込むときは目的地との直線の距離が縮まらない。
            float left = RouteLeft(here, goal);

            // 同じキーのまま目的地が移ることがある（葉を 1 枚拾うと次の葉へ）。近づいた距離を測り直す。
            if (Flat(goal - _lastGoal).magnitude > 3f)
            {
                _bestDistance = left;
                _progressAt = _clock;
            }

            _lastGoal = goal;
            if (left < _bestDistance - 0.5f)
            {
                _bestDistance = left;
                _progressAt = _clock;
            }

            // 近くで E を押し続けても何も起きないとき（扉の前で止まるなど）もワープに回す。
            // 目的地の近くで止まっている絵は長く見せたくないので、近いほど早く諦める。
            if (_waitingForHour && distance < 3f)
            {
                // 時刻の条件を待っている。着いているのでワープしない。
                _progressAt = _clock;
            }

            float giveUp = distance < NearDistance ? TeleportAfterNear : TeleportAfter;
            if (_gridRetries < GridRetryLimit && _clock - _progressAt > giveUp * 0.5f)
            {
                // 今の経路で進めない。ワープの前に、当たり判定の格子で道を探し直す。
                Debug.Log("[KCD] playthrough: 進めないので格子で探し直す " + key + " t=" + Clock());
                _gridForced = true;
                _gridRetries++;

                // 探し直した道のりから測り直す。測り直しの回数を使い切ったままだと、
                // 回り道を正しく歩いていても前の短い道のりと比べられ、進んでいない扱いになる。
                _routeRaises = 0;
                _progressAt = _clock;
                _pathAt = -10f;
                _gridCorners = Array.Empty<Vector3>();
            }

            if (_clock - _progressAt > giveUp)
            {
                TeleportNear(player, goal);
                return;
            }

            if (interact && distance < InteractRange + 1.5f)
            {
                if (TryInteract(player, goal, key))
                {
                    return;
                }
            }
            else
            {
                _nearSince = -1f;
                _facedGoal = false;
            }

            if (distance < 0.6f)
            {
                player.ClearAutopilot();
                return;
            }

            if (_clock < _sidestepUntil)
            {
                return;
            }

            if (_clock - _windowStart > StuckWindow)
            {
                bool stuck = _windowDistance - left < 0.5f;
                _windowStart = _clock;
                _windowDistance = left;
                if (stuck)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, Flat(goal - here).normalized) * (UnityEngine.Random.value < 0.5f ? -2.5f : 2.5f);
                    player.SetAutopilot(here + side, WalkSpeed);
                    _sidestepUntil = _clock + 1.2f;
                    _pathAt = -10f;
                    return;
                }
            }

            if (_clock - _pathAt > 2f || (goal - _pathGoal).sqrMagnitude > 1f)
            {
                PlanPath(player, goal);

                // 回り道の経路に替わったら、その道のりから測り直す（進んでいない時間はそのまま数える）。
                float route = RouteLeft(here, goal);
                _bestDistance = RaisedBest(_bestDistance, route, ref _routeRaises);
                _windowDistance = Mathf.Max(_windowDistance, route);
            }

            // 着いたかは高さも見る。1F の出発点の真上にある 2F の角を、着いたことにして飛ばさない。
            // 格子の道は壁の端を回る角を持つ。角に寄り切る前に次の角へ向くと壁の端に当たって向こう側を滑るので、
            // 次の角が見えるまで（または角の上に乗るまで）は今の角へ向かう。
            while (_corner < _corners.Length - 1 && Flat(_corners[_corner] - here).magnitude < 0.8f
                   && Mathf.Abs(_corners[_corner].y - here.y) < GridPath.LevelGap
                   && (_corners != _gridCorners || Flat(_corners[_corner] - here).magnitude < CornerOn
                       || Sees(here, _corners[_corner + 1])))
            {
                _corner++;
            }

            Vector3 next = _corner < _corners.Length ? _corners[_corner] : goal;
            player.SetAutopilot(next, hurry || distance > RunDistance ? Mathf.Max(WalkSpeed, player.RunSpeed) : WalkSpeed);
        }

        /// <summary>here から target が、机より高い所で壁に遮られずに見えるか。</summary>
        private static bool Sees(Vector3 here, Vector3 target)
        {
            return !Physics.Linecast(here + Vector3.up * SightHeight, target + Vector3.up * SightHeight,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 経路を引き直したあとの、進んだかを測る基準の残りの道のり。回り道に替わって route が長くなれば
        /// <see cref="MaxRouteRaises"/> 回までは引き上げる（raises を数える）。使い切ったら best のまま。
        /// </summary>
        public static float RaisedBest(float best, float route, ref int raises)
        {
            if (route <= best || raises >= MaxRouteRaises)
            {
                return best;
            }

            raises++;
            return route;
        }

        /// <summary>今の経路をたどったときの目的地までの残りの道のり（水平）。経路が別の目的地のものなら直線の距離。</summary>
        private float RouteLeft(Vector3 here, Vector3 goal)
        {
            if (_corners.Length == 0 || _corner >= _corners.Length || (goal - _pathGoal).sqrMagnitude > 1f)
            {
                return Flat(goal - here).magnitude;
            }

            float left = Flat(_corners[_corner] - here).magnitude;
            for (int i = _corner + 1; i < _corners.Length; i++)
            {
                left += Flat(_corners[i] - _corners[i - 1]).magnitude;
            }

            return left + Flat(goal - _corners[_corners.Length - 1]).magnitude;
        }

        /// <summary>近くで E を押す。押す相手が前に無ければ目的地の方を向く。押したら true。</summary>
        private bool TryInteract(PlayerController player, Vector3 goal, string key)
        {
            InteractionPrompt prompt = player.GetComponent<InteractionPrompt>();
            Interactable current = prompt != null ? prompt.Current : null;
            if (IsRightTarget(current, goal, key))
            {
                player.ClearAutopilot();
                if (Due(InteractInterval))
                {
                    Press(Key.E);
                }

                return true;
            }

            float distance = Flat(goal - player.transform.position).magnitude;
            if (distance > InteractRange)
            {
                return false;
            }

            // 相手の反応する距離（NPC は 2.4 m、拾う物は 2 m）より遠いと何も選ばれない。もう少し寄る。
            if (current == null && distance > 1.2f)
            {
                return false;
            }

            if (_nearSince < 0f)
            {
                _nearSince = _clock;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[KCD] playthrough: そばに来たが選ばれているのは {0} ({1}) 残り {2:F1}m 高さ {3}/{4}",
                    current != null ? current.name : "なし", current != null ? current.GetType().Name : "-", distance,
                    Height(player.transform.position), Height(goal)));
            }

            // 向き直しても別の物（写真スポット・椅子など）が選ばれたままなら、いつもの道で目的の物へもう一歩寄る。
            if (_facedGoal && current != null && distance > 1.0f)
            {
                return false;
            }

            if (_clock - _nearSince > 1.5f)
            {
                _facedGoal = true;
                Vector3 to = Flat(goal - player.transform.position);
                if (to.sqrMagnitude > 0.01f)
                {
                    player.ClearAutopilot();
                    player.Teleport(player.transform.position, Quaternion.LookRotation(to).eulerAngles.y);
                    CameraRig.SnapBehind(player.transform);
                }

                _nearSince = _clock;
            }

            player.ClearAutopilot();
            return true;
        }

        private void PlanPath(PlayerController player, Vector3 goal)
        {
            Vector3 from = player.transform.position;
            _pathAt = _clock;
            _pathGoal = goal;
            _corner = 0;
            bool fromOnMesh = NavMesh.SamplePosition(from, out NavMeshHit a, 1.5f, NavMesh.AllAreas);
            var path = new NavMeshPath();
            if (!_gridForced && fromOnMesh && NavMesh.SamplePosition(goal, out NavMeshHit b, 1.5f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 0)
            {
                _corners = path.corners;
                _corner = _corners.Length > 1 ? 1 : 0;
                return;
            }

            // NavMesh の無い所（建物の中・西の回廊）は当たり判定の格子で道を探す。作った道は目的地が同じで
            // 道から外れていない間は使い回す（格子を調べ直すのは重い）。
            // 一番近い区間が壁の向こうのこともあるので、向かう角が見えないときは今いる所から引き直す。
            if ((goal - _gridGoal).sqrMagnitude < 1f && _gridCorners.Length > 0 && GridPath.NearPath(from, _gridCorners, 3f))
            {
                int next = GridPath.NextCorner(from, _gridCorners);
                if (Sees(from, _gridCorners[next]))
                {
                    _corners = _gridCorners;
                    _corner = next;
                    return;
                }
            }

            Vector3[] prefix = Array.Empty<Vector3>();
            Vector3 start = from;
            if (!_gridForced && Flat(goal - from).magnitude > GridSpan && fromOnMesh
                && TryNavMeshEdge(from, goal, out Vector3 reach, out _)
                && NavMesh.CalculatePath(a.position, reach, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0)
            {
                // 遠い目的地（寮）は NavMesh の端まで経路で行き、その先だけ格子で探す。
                prefix = path.corners;
                start = prefix[prefix.Length - 1];
            }

            if (TryGrid(player, start, goal, out Vector3[] tail))
            {
                _gridCorners = new Vector3[prefix.Length + tail.Length];
                Array.Copy(prefix, _gridCorners, prefix.Length);
                Array.Copy(tail, 0, _gridCorners, prefix.Length, tail.Length);
                _gridGoal = goal;
                _corners = _gridCorners;

                // 格子の道は今いる所から引いたので、最初の角から歩く（近い区間を選ぶと壁の向こうの区間を拾うことがある）。
                _corner = prefix.Length > 0 ? GridPath.NextCorner(from, _corners) : 0;
                return;
            }

            _gridCorners = Array.Empty<Vector3>();
            if (fromOnMesh && TryNavMeshEdge(from, goal, out Vector3 edge, out bool exact)
                && NavMesh.CalculatePath(a.position, edge, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0)
            {
                int count = path.corners.Length;
                _corners = new Vector3[exact ? count : count + 1];
                Array.Copy(path.corners, _corners, count);
                _corners[_corners.Length - 1] = goal;
                _corner = _corners.Length > 1 ? 1 : 0;
                return;
            }

            _corners = new[] { goal };
        }

        /// <summary>体の幅では道が無いとき、格子を調べ直す細い体の半径（m）。</summary>
        private const float ThinRadius = 0.2f;

        /// <summary>別の階の目的地で道が無いとき、範囲を広げて探し直す幅（m）。建物の端から端の階段まで入る。</summary>
        private const float WideMargin = 45f;

        /// <summary>格子の道の出発点を寄せる距離（m）。</summary>
        private const float StartSnap = 1f;

        /// <summary>格子の道の目的地を寄せる距離（m）。目的地の印は物の上や壁際にある。</summary>
        private const float GoalSnap = 4f;

        /// <summary>出発点から寄せる升が見えるかを調べる高さ（m、足もとから）。机より上、壁より下。</summary>
        private const float SightHeight = 1.2f;

        /// <summary>道の終わりが目的地からこれより遠いとき、間に壁が無いかを確かめる（m）。</summary>
        private const float ShortEnd = 1.5f;

        /// <summary>格子の道の角に乗ったとみなす距離（m）。次の角が見えなくても次へ進む。</summary>
        private const float CornerOn = 0.3f;

        /// <summary>1 つの目的で、道が見つかり直したときに時間切れを数え直す回数の上限。</summary>
        private const int GridRecoveryLimit = 2;

        /// <summary>直前の格子の探索で道が無かったか。</summary>
        private bool _gridMissed;

        /// <summary>今の目的で時間切れを数え直した回数。</summary>
        private int _gridRecoveries;

        /// <summary>
        /// 経路の行き先。目的地が NavMesh の上ならそこ（exact）。外なら目的地から自分の方へ 10 m ずつ戻り、
        /// 最初に NavMesh に乗る所。
        /// </summary>
        private static bool TryNavMeshEdge(Vector3 from, Vector3 goal, out Vector3 reach, out bool exact)
        {
            exact = NavMesh.SamplePosition(goal, out NavMeshHit hit, 4f, NavMesh.AllAreas);
            if (exact)
            {
                reach = hit.position;
                return true;
            }

            Vector3 back = from - goal;
            float length = back.magnitude;
            for (float d = 10f; d < length; d += 10f)
            {
                if (NavMesh.SamplePosition(goal + back * (d / length), out hit, 6f, NavMesh.AllAreas))
                {
                    reach = hit.position;
                    return true;
                }
            }

            reach = goal;
            return false;
        }

        private void TeleportNear(PlayerController player, Vector3 goal)
        {
            Vector3 back = Flat(player.transform.position - goal);
            back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            float gap = 1.8f;
            if (TryDoorOutward(goal, out Vector3 outward))
            {
                // 扉が目的地なら建物の裏ではなく扉の正面に置く。
                back = outward;
                gap = 2.5f;
            }

            Vector3 spot = FreeSpot(goal, back, gap, _keyTeleports);
            _keyTeleports++;
            _teleports++;
            if (_extraTarget != null && _keyTeleports > ExtraTeleportLimit && _skipped.Add(_extraTarget))
            {
                Debug.Log("[KCD] playthrough: ワープしても届かないので飛ばす " + _extraTarget + " t=" + Clock());
            }
            Vector3 at = player.transform.position;
            Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                "[KCD] playthrough: 進めないのでワープ {0} t={1} 位置 ({2:F1},{3:F1}) 残り {4:F1}m 高さ {5}/{6}",
                _lastGoalKey, Clock(), at.x, at.z, Flat(goal - at).magnitude, Height(at), Height(goal)));
            DumpLastGrid("warp");
            LogTouching(player);
            player.Teleport(spot + Vector3.up * 0.1f, Quaternion.LookRotation(-back).eulerAngles.y);
            CameraRig.SnapBehind(player.transform);
            _progressAt = _clock;
            _bestDistance = Flat(goal - spot).magnitude;
            _pathAt = -10f;
        }

        private static readonly Collider[] Touching = new Collider[16];

        /// <summary>ワープの直前に体のまわり（体の幅 + 0.2 m）にあるコライダーを書く。何に引っかかったかを後で調べる。</summary>
        private static void LogTouching(PlayerController player)
        {
            CharacterController body = player.GetComponent<CharacterController>();
            float radius = (body != null ? body.radius : 0.3f) + 0.2f;
            float height = body != null ? body.height : 1.7f;
            Vector3 foot = player.transform.position;
            int count = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * radius, foot + Vector3.up * Mathf.Max(radius, height - radius),
                radius, Touching, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var names = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (Touching[i] == null || Touching[i].transform.IsChildOf(player.transform))
                {
                    continue;
                }

                Bounds b = Touching[i].bounds;
                names.Append(names.Length > 0 ? ", " : string.Empty).Append(Touching[i].name)
                    .Append(string.Format(CultureInfo.InvariantCulture, " [{0:F1}..{1:F1}]", b.min.y, b.max.y));
            }

            Debug.Log("[KCD] playthrough: ワープ前に体に触れていた物 " + (names.Length > 0 ? names.ToString() : "なし"));
        }

        /// <summary>
        /// ワープ先。目的地から back の向きに gap 離れた所を基本に、同じ目的地で何度もワープしているときは
        /// 反対側・横へ向きを回す（ゲートや柵の手前に戻され続けないように）。体が壁にめり込まず、
        /// 目的地まで遮るものが無い所を選ぶ。見つからなければ基本の所。
        /// </summary>
        private static Vector3 FreeSpot(Vector3 goal, Vector3 back, float gap, int attempt)
        {
            float[] turns = { 0f, 180f, 90f, -90f, 45f, -45f, 135f, -135f };
            Vector3 fallback = goal + back * gap;
            for (int i = 0; i < turns.Length; i++)
            {
                float turn = turns[(attempt + i) % turns.Length];
                Vector3 spot = goal + Quaternion.Euler(0f, turn, 0f) * back * gap;
                if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                {
                    spot = hit.position;
                }
                else if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    spot = floor.point;
                }
                else
                {
                    continue;
                }

                bool blocked = Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
                Vector3 eye = spot + Vector3.up * 1.2f;
                Vector3 target = goal + Vector3.up * 1.2f;
                Vector3 toward = target - eye;
                bool hidden = toward.magnitude > 0.8f
                              && Physics.Raycast(eye, toward.normalized, out RaycastHit wall, toward.magnitude - 0.8f, ~0, QueryTriggerInteraction.Ignore)
                              && wall.collider.GetComponentInParent<Interactable>() == null;
                if (!blocked && !hidden)
                {
                    return spot;
                }

                if (i == 0)
                {
                    fallback = spot;
                }
            }

            return fallback;
        }

        /// <summary>目的地のすぐそばに入口があれば、その外向き。</summary>
        private static bool TryDoorOutward(Vector3 goal, out Vector3 outward)
        {
            outward = Vector3.zero;
            foreach (DormEntrance door in FindObjectsByType<DormEntrance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (Flat(door.transform.position - goal).magnitude < 3f)
                {
                    outward = Flat(door.Outward).normalized;
                    return outward.sqrMagnitude > 0.5f;
                }
            }

            foreach (EntranceTrigger door in FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (Flat(door.transform.position - goal).magnitude < 3f)
                {
                    outward = Flat(door.Outward).normalized;
                    return outward.sqrMagnitude > 0.5f;
                }
            }

            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static PlayerController Player() => FindAnyObjectByType<PlayerController>();

        private bool Due(float interval)
        {
            if (_clock < _nextKeyAt)
            {
                return false;
            }

            _nextKeyAt = _clock + interval;
            return true;
        }

        private void Press(Key key)
        {
            StartCoroutine(Tap(key));
        }

        private IEnumerator Tap(Key key)
        {
            _keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        }
    }
}
