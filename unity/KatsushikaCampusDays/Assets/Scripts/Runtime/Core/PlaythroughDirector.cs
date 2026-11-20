using System;
using System.Collections;
using System.Globalization;
using System.IO;
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
    ///   -kcd-record &lt;base&gt;   録画する（<see cref="PlaythroughRecorder"/>）。-kcd-ffmpeg &lt;path&gt; で ffmpeg を指定
    /// 操作は人と同じく仮想のキーボード（E / Enter / ↓）と、プレイヤーの自動歩行（NavMesh の経路をたどる）で行う。
    /// 結末を見てタイトルへ戻ったら録画を閉じて終了する。セーブは persistentDataPath/playthrough/ に分ける。
    /// </summary>
    public sealed class PlaythroughDirector : MonoBehaviour
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

        private enum Route { Formal, Dorm }

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
        private float _pathAt = -10f;
        private float _bestDistance = float.MaxValue;
        private float _progressAt;
        private float _windowStart;
        private float _windowDistance = float.MaxValue;
        private float _sidestepUntil = -1f;
        private float _nearSince = -1f;
        private int _teleports;
        private int _keyTeleports;
        private bool _waitingForHour;
        private string _scene = string.Empty;
        private float _sceneAt;
        private float _gateAt = -10f;
        private bool _localeFixed;
        private bool _volumeFixed;

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
            director._route = route == "dorm" ? Route.Dorm : Route.Formal;
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
            _clock += _recorder != null && _recorder.IsRecording
                ? 1f / PlaythroughRecorder.FrameRate
                : Time.unscaledDeltaTime;
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
            Debug.Log("[KCD] playthrough: 終了（" + why + "）t=" + Clock() + " teleports=" + _teleports);
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
                if (Due(6f))
                {
                    Debug.Log("[KCD] playthrough: 一日の終わり → もう一日 t=" + Clock());
                    Press(Key.Enter);
                }

                return;
            }

            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue != null && dialogue.IsPlaying)
            {
                Player()?.ClearAutopilot();
                _progressAt = _clock;
                TickDialogue(dialogue);
                return;
            }

            _lastLine = null;
            _choiceShownAt = -1f;
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

            GateEntrances(goal);
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
        private void GateEntrances(Vector3 goal)
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
            // OnTriggerEnter は届くので、コライダーを切る）。
            foreach (InteriorExit exit in FindObjectsByType<InteriorExit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (exit.TryGetComponent(out Collider box))
                {
                    box.enabled = Flat(exit.transform.position - goal).magnitude < 8f;
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
            QuestStep step = FormalStep(quests);
            if (step == null)
            {
                return false;
            }

            string building = InteriorLoader.Instance != null ? InteriorLoader.Instance.CurrentId : string.Empty;
            Minimap minimap = FindAnyObjectByType<Minimap>();
            bool found = minimap != null
                ? minimap.TryGoal(step, from, building, out goal)
                : QuestObjectiveLocator.TryLocate(step, from, out goal);
            if (!found)
            {
                return false;
            }

            // 屋内は互いに遠く離して建ててある。中にいて目的地がずっと遠ければ別の建物なので、まず外へ出る。
            if (!string.IsNullOrEmpty(building) && Flat(goal - from).magnitude > 150f
                && QuestObjectiveLocator.TryLocateExit(building, from, out Vector3 exit))
            {
                goal = exit;
                key = "exit:" + building;
                return true;
            }

            QuestStepKind kind = step.AwaitingGiver ? QuestStepKind.Talk : step.Kind;
            key = kind + ":" + (step.AwaitingGiver ? step.Giver : step.Target) + "@" + building;
            interact = kind == QuestStepKind.Talk || kind == QuestStepKind.Collect || kind == QuestStepKind.Flag;
            hurry = step.IsTimed;
            _waitingForHour = TooEarly(step);
            return true;
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
                key = "exit:" + interior.CurrentId;
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
                _bestDistance = distance;
                _progressAt = _clock;
                _windowStart = _clock;
                _windowDistance = distance;
                _pathAt = -10f;
            }

            if (distance < _bestDistance - 0.5f)
            {
                _bestDistance = distance;
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
            if (_clock - _progressAt > giveUp)
            {
                TeleportNear(player, goal);
                return;
            }

            if (interact && distance < InteractRange + 1.5f && TryInteract(player, goal, key))
            {
                return;
            }

            _nearSince = -1f;
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
                bool stuck = _windowDistance - distance < 0.5f;
                _windowStart = _clock;
                _windowDistance = distance;
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
                PlanPath(here, goal);
            }

            while (_corner < _corners.Length - 1 && Flat(_corners[_corner] - here).magnitude < 0.8f)
            {
                _corner++;
            }

            Vector3 next = _corner < _corners.Length ? _corners[_corner] : goal;
            player.SetAutopilot(next, hurry || distance > RunDistance ? Mathf.Max(WalkSpeed, player.RunSpeed) : WalkSpeed);
        }

        /// <summary>近くで E を押す。押す相手が前に無ければ目的地の方を向く。押したら true。</summary>
        private bool TryInteract(PlayerController player, Vector3 goal, string key)
        {
            InteractionPrompt prompt = player.GetComponent<InteractionPrompt>();
            Interactable current = prompt != null ? prompt.Current : null;
            string npcId = NpcIdOf(key);

            bool right = key == "dorm_entrance"
                ? current is DormEntrance
                : current != null && (npcId == null || (current is NPCTalker talker && talker.NpcId == npcId)
                                                     || current.GetComponentInParent<NPCTalker>()?.NpcId == npcId);
            if (right)
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
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[KCD] playthrough: そばに来たが選ばれているのは {0} ({1}) 残り {2:F1}m",
                    current != null ? current.name : "なし", current != null ? current.GetType().Name : "-", distance));
            }

            if (_clock - _nearSince > 1.5f)
            {
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

        private void PlanPath(Vector3 from, Vector3 goal)
        {
            _pathAt = _clock;
            _pathGoal = goal;
            _corner = 0;
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas)
                && TryNavMeshEdge(from, goal, out Vector3 reach, out bool exact)
                && NavMesh.CalculatePath(a.position, reach, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0)
            {
                // NavMesh の外の目的地（寮など）は、NavMesh の端まで経路で行き、その先はまっすぐ歩く。
                int count = path.corners.Length;
                _corners = new Vector3[exact ? count : count + 1];
                Array.Copy(path.corners, _corners, count);
                _corners[_corners.Length - 1] = goal;
                _corner = _corners.Length > 1 ? 1 : 0;
                return;
            }

            _corners = new[] { goal };
        }

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
            Vector3 at = player.transform.position;
            Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                "[KCD] playthrough: 進めないのでワープ {0} t={1} 位置 ({2:F1},{3:F1}) 残り {4:F1}m",
                _lastGoalKey, Clock(), at.x, at.z, Flat(goal - at).magnitude));
            player.Teleport(spot + Vector3.up * 0.1f, Quaternion.LookRotation(-back).eulerAngles.y);
            CameraRig.SnapBehind(player.transform);
            _progressAt = _clock;
            _bestDistance = Flat(goal - spot).magnitude;
            _pathAt = -10f;
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
