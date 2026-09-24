using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// スモークテスト用の進行役。コマンドライン引数 -kcd-* があるときだけ生まれる。
    ///   -kcd-start campus      タイトルを飛ばしてキャンパスへ
    ///   -kcd-start credits     タイトルでクレジットを開いたままにする
    ///   -kcd-lang en           表示する言語を合わせる（ja / en。選択は PlayerPrefs に残る）
    ///   -kcd-time 17.5         ゲーム内時刻を合わせる
    ///   -kcd-talk inari        NPC の前へワープして会話を始める
    ///   -kcd-interior kyoso    建物の中へ入る
    ///   -kcd-title-yaw 180     タイトルの立ち姿の向きを試す
    ///   -kcd-chara botchan     操作するキャラを選んでから本編へ入る（選択は PlayerPrefs に残る）
    ///   -kcd-wave inari        NPC の斜め後ろ 3.5 m へワープして、Q（手を振る）を 2.5 秒おきに 30 回押す
    ///   -kcd-goto Item_c_gate  名前がこれで始まる物の 1.6 m 手前へワープして向く
    ///                          （-kcd-goto-from 90 で立つ方角を、-kcd-goto-dist 3 で離れる距離を変える）
    ///   -kcd-interact 2        着いて 2 秒後、プレイヤーの前で E の対象になっている物を調べる
    /// セーブは persistentDataPath/smoke/ に分けて読み書きする（遊んでいるセーブには触らない）。
    /// 通常起動では何も生まれないので、製品版の挙動には影響しない。
    /// </summary>
    public sealed class SmokeDirector : MonoBehaviour
    {
        /// <summary>スモークのセーブの置き場（persistentDataPath の下）。</summary>
        public const string SmokeSaveFolder = "smoke";

        private string _start;
        private string _talk;
        private string _interior;
        private string _chara;
        private string _wave;
        private string _goto;
        private float _interactDelay = -1f;
        private float _hours = -1f;
        private float _titleYaw = float.NaN;
        private float _faceCam = float.NaN;
        private bool _campusHandled;
        private bool _titleHandled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-kcd-", StringComparison.Ordinal))
                {
                    UseSmokeSaveFolder();
                    var go = new GameObject("SmokeDirector");
                    DontDestroyOnLoad(go);
                    go.AddComponent<SmokeDirector>();
                    return;
                }
            }
        }

        /// <summary>
        /// スモークの自動セーブ・クイックセーブを persistentDataPath/smoke/ に書かせる。ワープや調べるで進んだ進行が、
        /// その端末で遊んでいるセーブを上書きしないように。フォルダを作れなくても差し替えは残す（書けずに終わるだけ）。
        /// </summary>
        private static void UseSmokeSaveFolder()
        {
            string folder = Path.Combine(Application.persistentDataPath, SmokeSaveFolder);
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[KCD] スモーク: セーブのフォルダを作れない: " + error.Message);
            }

            SaveSystem.DirectoryOverride = folder;
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static float ArgFloat(string name, float fallback)
        {
            string raw = Arg(name);
            if (raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                return value;
            }

            return fallback;
        }

        private void Awake()
        {
            _start = Arg("-kcd-start");
            _talk = Arg("-kcd-talk");
            _interior = Arg("-kcd-interior");
            _chara = Arg("-kcd-chara");
            _wave = Arg("-kcd-wave");
            _goto = Arg("-kcd-goto");
            _interactDelay = ArgFloat("-kcd-interact", -1f);
            _hours = ArgFloat("-kcd-time", -1f);
            _titleYaw = ArgFloat("-kcd-title-yaw", float.NaN);
            _faceCam = ArgFloat("-kcd-face-cam", float.NaN);
            SmokeProbe.Open(Arg("-kcd-log"));
            SmokeProbe.Log("args: " + string.Join(" ", Environment.GetCommandLineArgs()));
            string lang = Arg("-kcd-lang");
            if (!string.IsNullOrEmpty(lang))
            {
                L.SetLocale(lang);
                SmokeProbe.Log("locale: " + L.Locale);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // 購読より先に最初のシーンが載っていた場合の保険。
            Scene active = SceneManager.GetActiveScene();
            if (active.isLoaded && active.name == GameManager.TitleSceneName && !_titleHandled)
            {
                OnSceneLoaded(active, LoadSceneMode.Single);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SmokeProbe.Log("sceneLoaded: " + scene.name);
            if (scene.name == GameManager.TitleSceneName && !_titleHandled)
            {
                _titleHandled = true;
                StartCoroutine(HandleTitle());
            }
            else if (scene.name == GameManager.CampusSceneName && !_campusHandled)
            {
                _campusHandled = true;
                StartCoroutine(HandleCampus());
            }
        }

        private IEnumerator HandleTitle()
        {
            yield return null;

            if (!float.IsNaN(_titleYaw))
            {
                foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    if (t.name == "Facing")
                    {
                        t.localRotation = Quaternion.Euler(0f, _titleYaw, 0f);
                    }
                }
            }

            yield return new WaitForSecondsRealtime(0.5f);
            ApplyOutlineToggle();
            if (_start != "campus")
            {
                SmokeFaceCam.Apply(_faceCam);
            }

            SmokeProbe.Dump("title");

            if (_start == "credits")
            {
                CreditsView credits = FindAnyObjectByType<CreditsView>();
                if (credits == null)
                {
                    Debug.LogWarning("[KCD] スモーク: タイトルに CreditsView が無い");
                    yield break;
                }

                credits.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                SmokeProbe.Dump("credits");
                yield break;
            }

            if (_start == "campus")
            {
                // 知らない id を入れると PlayerPrefs に残り、次の通常起動まで引きずるので弾く。
                if (Array.IndexOf(GameManager.PlayableCharacterIds, _chara) >= 0)
                {
                    GameManager.Instance.SelectedCharacterId = _chara;
                }
                else if (!string.IsNullOrEmpty(_chara))
                {
                    Debug.LogWarning("[KCD] スモーク: 選べないキャラ: " + _chara);
                }

                SmokeProbe.Log("EnterCampus as " + GameManager.Instance.SelectedCharacterId);
                GameManager.Instance.EnterCampus();
            }
        }

        private IEnumerator HandleCampus()
        {
            yield return new WaitForSecondsRealtime(1.5f);

            if (_hours >= 0f)
            {
                DayNightCycle cycle = FindAnyObjectByType<DayNightCycle>();
                if (cycle != null)
                {
                    cycle.SetHours(_hours);
                }
            }

            if (!string.IsNullOrEmpty(_interior))
            {
                InteriorLoader loader = FindAnyObjectByType<InteriorLoader>();
                if (loader != null)
                {
                    loader.Enter(_interior);
                }

                yield return new WaitForSecondsRealtime(2.5f);
            }

            if (!string.IsNullOrEmpty(_talk))
            {
                TalkTo(_talk);
            }

            if (!string.IsNullOrEmpty(_wave))
            {
                StartCoroutine(WaveAt(_wave));
            }

            if (!string.IsNullOrEmpty(_goto))
            {
                GoTo(_goto, ArgFloat("-kcd-goto-from", 180f), Mathf.Clamp(ArgFloat("-kcd-goto-dist", 1.6f), 0.3f, 20f));
            }

            if (_interactDelay >= 0f)
            {
                StartCoroutine(InteractAfter(_interactDelay));
            }

            yield return new WaitForSecondsRealtime(1f);
            ApplyOutlineToggle();
            SmokeFaceCam.Apply(_faceCam);
            SmokeProbe.Dump("campus");
        }

        /// <summary>-kcd-hide-outline があれば、名前が _outline で終わる描画を止める（真っ黒な顔の切り分け用）。</summary>
        private static void ApplyOutlineToggle()
        {
            string hide = Arg("-kcd-hide");
            if (hide != null)
            {
                int hidden = 0;
                foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (SmokeProbe.Path(renderer.transform).Contains(hide))
                    {
                        renderer.enabled = false;
                        hidden++;
                    }
                }

                SmokeProbe.Log("hide " + hide + ": " + hidden);
            }

            if (Arg("-kcd-hide-outline") == null)
            {
                return;
            }

            int count = 0;
            foreach (SkinnedMeshRenderer renderer in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.name.EndsWith("_outline", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    count++;
                }
            }

            SmokeProbe.Log("hide-outline: " + count);
        }

        /// <summary>
        /// NPC の斜め後ろに立って手を振り、振り返してくれるかを撮る。
        /// 撮る瞬間が読めないので、振り返しの長さ（NPCWander.WaveBackSeconds）より少し間を空けて振り続ける。
        /// </summary>
        private static IEnumerator WaveAt(string npcId)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            NPCTalker target = null;
            foreach (NPCTalker talker in FindObjectsByType<NPCTalker>(FindObjectsSortMode.None))
            {
                if (talker.NpcId == npcId)
                {
                    target = talker;
                    break;
                }
            }

            if (player == null || target == null)
            {
                Debug.LogWarning("[KCD] スモーク: NPC が見つかりません: " + npcId);
                yield break;
            }

            Vector3 away = -target.transform.forward + target.transform.right;
            away.y = 0f;
            away = away.sqrMagnitude < 0.01f ? Vector3.back : away.normalized;
            Vector3 stand = target.transform.position + away * 3.5f;
            float yaw = Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg;
            player.Teleport(stand, yaw);
            CameraRig.SnapBehind(player.transform);
            PlayerAnimatorDriver driver = player.GetComponent<PlayerAnimatorDriver>();
            SmokeProbe.Log("wave at " + npcId);
            for (int i = 0; i < 30 && driver != null; i++)
            {
                driver.WaveHello();
                yield return new WaitForSecondsRealtime(NPCWander.WaveBackSeconds + 0.5f);
            }
        }

        /// <summary>
        /// 名前が prefix で始まる物から fromDegrees の方角（0 が +z、90 が +x）へ distance 離れた地面に立ち、その物を向く。
        /// 隠しアイテムや三脚を実際に拾える・撮れる位置にあるかを見るためのもの。
        /// </summary>
        private static void GoTo(string prefix, float fromDegrees, float distance)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            Transform target = FindTarget(prefix, out int matches);
            if (player == null || target == null)
            {
                Debug.LogWarning("[KCD] スモーク: ワープ先が見つかりません: " + prefix);
                return;
            }

            float radians = fromDegrees * Mathf.Deg2Rad;
            Vector3 away = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            Vector3 stand = target.position + away * distance;
            if (Physics.Raycast(stand + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 20f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                stand.y = hit.point.y + 0.05f;
            }
            else
            {
                Debug.LogWarning("[KCD] スモーク: ワープ先の足元に床が無いので、目標の高さに立たせます");
            }

            float yaw = Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg;
            player.Teleport(stand, yaw);
            CameraRig.SnapBehind(player.transform);
            SmokeProbe.Log("goto " + target.name + " at " + target.position + ", stand " + stand
                           + (matches > 1 ? ", matches=" + matches : string.Empty));
        }

        /// <summary>
        /// 名前が prefix の物。同じ名前があればそれ、無ければ prefix で始まる物のうち名前順で最初の物にする
        /// （FindObjectsByType の順は実行ごとに変わるので、最初に見つかった物だとワープ先が揺れる）。
        /// </summary>
        private static Transform FindTarget(string prefix, out int matches)
        {
            Transform best = null;
            matches = 0;
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                matches++;
                bool exact = t.name.Length == prefix.Length;
                bool bestExact = best != null && best.name.Length == prefix.Length;
                if (best == null || (exact && !bestExact)
                    || (exact == bestExact && string.CompareOrdinal(t.name, best.name) < 0))
                {
                    best = t;
                }
            }

            return best;
        }

        /// <summary>E を押したのと同じく、プレイヤーの InteractionPrompt が選んでいる物を調べ、結果をログに書く。</summary>
        private static IEnumerator InteractAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            PlayerController player = FindAnyObjectByType<PlayerController>();
            InteractionPrompt prompt = player != null ? player.GetComponent<InteractionPrompt>() : null;
            Interactable current = prompt != null ? prompt.Current : null;
            if (KCDInput.GameplayBlocked)
            {
                // 会話や画面が開いている間は、E を押しても調べない（InteractionPrompt と同じ）。
                SmokeProbe.Log("interact: blocked");
                yield break;
            }

            if (current == null)
            {
                Debug.LogWarning("[KCD] スモーク: E の対象が無い");
                SmokeProbe.Log("interact: none");
                yield break;
            }

            // 拾った物は Interact の中で Destroy されるので、id は先に取っておく。
            string name = current.name;
            string itemId = current is CollectableItem item ? item.ItemId : null;
            int photosBefore = DayStats.PhotoSpotIds.Count;
            current.Interact(player.gameObject);
            yield return null;

            // 三脚の撮影はフレームの描画とフラッシュを待ってから数えるので、枚数が増えるまで少し待つ。
            if (current is PhotoSpot)
            {
                float until = Time.realtimeSinceStartup + 3f;
                while (DayStats.PhotoSpotIds.Count == photosBefore && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }

            SmokeProbe.Log("interact " + name + (itemId != null ? ", collected=" + DayStats.HasCollected(itemId) : string.Empty)
                           + ", photos=" + DayStats.PhotoSpotIds.Count);
        }

        private static void TalkTo(string npcId)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            NPCTalker target = null;
            foreach (NPCTalker talker in FindObjectsByType<NPCTalker>(FindObjectsSortMode.None))
            {
                if (talker.NpcId == npcId)
                {
                    target = talker;
                    break;
                }
            }

            if (player == null || target == null)
            {
                Debug.LogWarning("[KCD] スモーク: NPC が見つかりません: " + npcId);
                return;
            }

            Vector3 toPlayer = -target.transform.forward;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f)
            {
                toPlayer = Vector3.back;
            }

            toPlayer.Normalize();
            Vector3 stand = target.transform.position + toPlayer * 1.8f;
            float yaw = Mathf.Atan2(-toPlayer.x, -toPlayer.z) * Mathf.Rad2Deg;
            player.Teleport(stand, yaw);
            CameraRig.SnapBehind(player.transform);

            target.Interact(player.gameObject);
        }
    }
}
