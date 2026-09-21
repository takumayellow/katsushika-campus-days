using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// スモークテスト用の進行役。コマンドライン引数 -kcd-* があるときだけ生まれる。
    ///   -kcd-start campus      タイトルを飛ばしてキャンパスへ
    ///   -kcd-time 17.5         ゲーム内時刻を合わせる
    ///   -kcd-talk inari        NPC の前へワープして会話を始める
    ///   -kcd-interior kyoso    建物の中へ入る
    ///   -kcd-title-yaw 180     タイトルの立ち姿の向きを試す
    /// 通常起動では何も生まれないので、製品版の挙動には影響しない。
    /// </summary>
    public sealed class SmokeDirector : MonoBehaviour
    {
        private string _start;
        private string _talk;
        private string _interior;
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
                    var go = new GameObject("SmokeDirector");
                    DontDestroyOnLoad(go);
                    go.AddComponent<SmokeDirector>();
                    return;
                }
            }
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
            if (raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
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
            _hours = ArgFloat("-kcd-time", -1f);
            _titleYaw = ArgFloat("-kcd-title-yaw", float.NaN);
            _faceCam = ArgFloat("-kcd-face-cam", float.NaN);
            SmokeProbe.Open(Arg("-kcd-log"));
            SmokeProbe.Log("args: " + string.Join(" ", Environment.GetCommandLineArgs()));
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

            if (_start == "campus")
            {
                SmokeProbe.Log("EnterCampus");
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
