using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 図書館大ホールのピアノを弾く操作。座ると曲が鳴って BGM が絞られ、立つとどの時点でも止まって BGM が戻る。
    /// 曲が終わったら座ったまま手を戻す。シーンでは本物の椅子・曲・体で、手が鍵盤に届いていることと見た目の画も確かめる。
    /// 音は MuteDuringTests で消してある（鳴らす処理は動き、聞こえる音量だけが 0）。
    /// </summary>
    public sealed class PianoPerformancePlayTests : SceneTestBase
    {
        private const string PianoClipId = PianoPerformance.DefaultClipId;
        private const string BgmId = "test_bgm";

        /// <summary>手首が IK の目標から離れてよい距離 (m)。</summary>
        private const float HandReach = 0.4f;

        [UnityTest]
        public IEnumerator Sitting_PlaysThePhraseAndDucksBgm_StandingStopsItAndBgmRecovers()
        {
            yield return DropAudioManager();
            var rig = new Rig(3f);
            try
            {
                rig.Audio.PlayBgm(BgmId);
                yield return PlayModeScenes.Advance(5, 0.3f);
                Assert.AreEqual(1f, rig.Audio.BgmDuck, 0.01f, "座る前から BGM が絞られている");
                Assert.IsFalse(rig.Audio.IsPianoPlaying);

                rig.Seat.Interact(rig.Player.gameObject);
                Assert.IsTrue(rig.Player.IsSitting, "ピアノの椅子に座れない");
                Assert.IsTrue(rig.Performance.IsPlaying, "座っても演奏が始まらない");
                Assert.IsTrue(rig.Audio.IsPianoPlaying, "ピアノの曲が鳴らない");

                yield return PlayModeScenes.Advance(5, 0.6f);
                Assert.LessOrEqual(rig.Audio.BgmDuck, AudioManager.PianoDuckLevel + 0.02f, "演奏中に BGM が絞られない");
                Assert.IsTrue(rig.Performance.IsPlaying, "曲の途中で演奏が止まった");

                rig.Player.StandUp();
                Assert.IsFalse(rig.Player.IsSitting);
                Assert.IsFalse(rig.Performance.IsPlaying, "立っても演奏が続く");
                Assert.IsFalse(rig.Audio.IsPianoPlaying, "立っても曲が止まらない");
                float behind = Vector3.Dot(rig.Player.transform.position - rig.Anchor.position, rig.Anchor.forward);
                Assert.Less(behind, -0.5f, "立ったとき鍵盤の側へ出てしまう (" + behind + " m)");

                yield return PlayModeScenes.Advance(5, 2.4f);
                Assert.GreaterOrEqual(rig.Audio.BgmDuck, 0.95f, "立ったあと BGM が戻らない");
            }
            finally
            {
                rig.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator DisablingThePiano_WhilePlaying_StopsTheSongAndBgmRecovers()
        {
            yield return DropAudioManager();
            var rig = new Rig(3f);
            try
            {
                rig.Audio.PlayBgm(BgmId);
                rig.Seat.Interact(rig.Player.gameObject);
                yield return PlayModeScenes.Advance(5, 0.6f);
                Assert.IsTrue(rig.Audio.IsPianoPlaying);

                rig.Performance.enabled = false;
                Assert.IsFalse(rig.Audio.IsPianoPlaying, "無効にしても曲が止まらない");
                yield return PlayModeScenes.Advance(5, 2.4f);
                Assert.GreaterOrEqual(rig.Audio.BgmDuck, 0.95f, "曲が止まったあと BGM が戻らない");
            }
            finally
            {
                rig.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PhraseEnding_KeepsThePlayerSeatedAndBgmRecovers()
        {
            yield return DropAudioManager();
            var rig = new Rig(0.6f);
            try
            {
                rig.Audio.PlayBgm(BgmId);
                rig.Seat.Interact(rig.Player.gameObject);
                Assert.IsTrue(rig.Performance.IsPlaying);

                yield return PlayModeScenes.Advance(5, 1.0f);
                Assert.IsFalse(rig.Performance.IsPlaying, "曲が終わっても演奏中のまま");
                Assert.IsTrue(rig.Player.IsSitting, "曲が終わったら座ったまま");

                yield return PlayModeScenes.Advance(5, 2.4f);
                Assert.GreaterOrEqual(rig.Audio.BgmDuck, 0.95f, "曲が終わっても BGM が戻らない");
            }
            finally
            {
                rig.Dispose();
            }
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Library_PianoStool_SitsFacingTheKeysAndPlays()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            cycle.Paused = true;
            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Settle();
            yield return driver.Enter("library");

            PianoPerformance[] pianos = Object.FindObjectsByType<PianoPerformance>(FindObjectsInactive.Include);
            Assert.AreEqual(1, pianos.Length, "図書館のピアノの椅子が 1 つでない");
            PianoPerformance performance = pianos[0];
            SeatInteractable seat = performance.GetComponent<SeatInteractable>();
            Assert.IsNotNull(seat, "ピアノの椅子に SeatInteractable が無い");
            Assert.AreSame(performance, seat.Performance, "椅子と演奏が繋がっていない");
            Assert.IsNotNull(performance.Keys, "鍵盤の位置が無い");
            Assert.AreEqual(L.Get("ui.interact.play_piano", "ピアノを弾く"), seat.PromptLabel);

            Transform anchor = seat.Anchors[0];
            Vector3 approach = anchor.position - anchor.forward * 1.1f;
            player.Teleport(approach, anchor.eulerAngles.y);
            yield return PlayModeScenes.Advance(5, 0.3f);

            seat.Interact(player.gameObject);
            Assert.IsTrue(player.IsSitting, "ピアノの椅子に座れない");
            Assert.IsTrue(performance.IsPlaying, "座っても演奏が始まらない（曲がシーンに入っていない？）");
            Assert.IsTrue(AudioManager.Instance.IsPianoPlaying);

            yield return PlayModeScenes.Advance(30, 2.0f);
            Animator animator = player.GetComponentInChildren<Animator>();
            SitPose pose = player.GetComponentInChildren<SitPose>();
            Assert.IsNotNull(pose, "体に SitPose が無い");
            Assert.GreaterOrEqual(pose.KeyboardWeight, 0.99f, "手が鍵盤に上がらない");
            Assert.LessOrEqual(AudioManager.Instance.BgmDuck, AudioManager.PianoDuckLevel + 0.02f, "BGM が絞られない");

            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float reach = (hand.position - performance.Keys.position).magnitude;
            Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Debug.Log("[piano] head " + head.position.ToString("F2") + " elbow " + elbow.position.ToString("F2")
                + " scale " + animator.transform.lossyScale.ToString("F2") + " body " + animator.name);
            Debug.Log("[piano] 右手首から鍵盤の縁まで " + reach.ToString("F3") + " m, anchor " + anchor.position.ToString("F2")
                + " keys " + performance.Keys.position.ToString("F2") + " hand " + hand.position.ToString("F2")
                + " shoulder " + shoulder.position.ToString("F2") + " player " + player.transform.position.ToString("F2")
                + " yaw " + player.transform.eulerAngles.y.ToString("F0") + " anchorYaw " + anchor.eulerAngles.y.ToString("F0"));

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "piano"));
            Directory.CreateDirectory(folder);
            yield return Shoot(Path.Combine(folder, "side.png"), anchor, 1.5f, -0.1f, 1.25f);
            yield return PlayModeScenes.Advance(10, 0.4f);
            yield return Shoot(Path.Combine(folder, "back.png"), anchor, 1.1f, -1.9f, 1.9f);
            yield return PlayModeScenes.Advance(10, 0.4f);
            yield return Shoot(Path.Combine(folder, "left.png"), anchor, -1.5f, -0.1f, 1.25f);

            float toGoal = (hand.position - pose.RightHandGoal).magnitude;
            Assert.LessOrEqual(toGoal, HandReach, "右手が鍵盤の上の目標に届かない (" + toGoal + " m)");
            Vector3 toKeys = (performance.Keys.position - shoulder.position).normalized;
            float forward = Vector3.Dot(hand.position - shoulder.position, toKeys);
            Assert.GreaterOrEqual(forward, 0.1f, "手が膝の上のまま鍵盤へ伸びない (" + forward + " m)");

            player.StandUp();
            Assert.IsFalse(performance.IsPlaying, "立っても演奏が続く");
            Assert.IsFalse(AudioManager.Instance.IsPianoPlaying, "立っても曲が止まらない");
            yield return PlayModeScenes.Advance(10, 2.4f);
            Assert.GreaterOrEqual(AudioManager.Instance.BgmDuck, 0.95f, "立ったあと BGM が戻らない");
            AssertNoErrors("ピアノを弾く");
        }

        /// <summary>
        /// 座った人を斜めから 1 枚撮る。right = プレイヤーの右へ、forward = 鍵盤の側へのずれ (m)、height = カメラの床からの高さ。
        /// ゲームのカメラとは別に、1266 x 585 の RenderTexture へ描く。
        /// </summary>
        private static IEnumerator Shoot(string path, Transform anchor, float right, float forward, float height)
        {
            var go = new GameObject("PianoShot");
            var camera = go.AddComponent<Camera>();
            var texture = new RenderTexture(1266, 585, 24, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(1266, 585, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Vector3 focus = anchor.position + Vector3.up * 0.95f + anchor.forward * 0.35f;
                go.transform.position = anchor.position + anchor.right * right + anchor.forward * forward + Vector3.up * height;
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position, Vector3.up);
                camera.fieldOfView = 55f;
                camera.nearClipPlane = 0.05f;
                camera.targetTexture = texture;
                camera.depth = 100f;
                yield return null;
                camera.Render();
                RenderTexture.active = texture;
                readable.ReadPixels(new Rect(0, 0, 1266, 585), 0, 0);
                readable.Apply();
                File.WriteAllBytes(path, readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.Destroy(texture);
                Object.Destroy(readable);
                Object.Destroy(go);
            }
        }

        /// <summary>残っている AudioManager（DontDestroyOnLoad）を消して、このテストの物に差し替えられるようにする。</summary>
        private static IEnumerator DropAudioManager()
        {
            if (AudioManager.Instance != null)
            {
                Object.Destroy(AudioManager.Instance.gameObject);
                yield return null;
            }
        }

        /// <summary>CharacterController 付きのプレイヤー、曲を持つ AudioManager、ピアノの椅子ひと揃え。</summary>
        private sealed class Rig
        {
            private readonly GameObject[] _objects;

            public Rig(float pianoSeconds)
            {
                var audioHost = new GameObject("PianoTestAudio");
                audioHost.SetActive(false);
                Audio = audioHost.AddComponent<AudioManager>();
                typeof(AudioManager).GetField("_clips", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(Audio, new[] { Tone(PianoClipId, pianoSeconds), Tone(BgmId, 30f) });
                audioHost.SetActive(true);

                var playerHost = new GameObject("PianoTestPlayer", typeof(CharacterController));
                Player = playerHost.AddComponent<PlayerController>();

                var anchorHost = new GameObject("PianoTestAnchor");
                Anchor = anchorHost.transform;
                var keys = new GameObject("PianoTestKeys");
                keys.transform.position = Anchor.position + Anchor.forward * 0.58f + Vector3.up * 0.8f;

                var seatHost = new GameObject("PianoTestSeat");
                Seat = seatHost.AddComponent<SeatInteractable>();
                Seat.Anchors = new[] { Anchor };
                Performance = seatHost.AddComponent<PianoPerformance>();
                Performance.Keys = keys.transform;
                Seat.Performance = Performance;

                _objects = new[] { audioHost, playerHost, anchorHost, keys, seatHost };
            }

            public AudioManager Audio { get; }
            public PlayerController Player { get; }
            public Transform Anchor { get; }
            public SeatInteractable Seat { get; }
            public PianoPerformance Performance { get; }

            public void Dispose()
            {
                KCDInput.MovementLocked = false;
                foreach (GameObject go in _objects)
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
            }

            private static AudioClip Tone(string name, float seconds)
            {
                const int rate = 8000;
                int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * rate));
                var data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    data[i] = 0.1f * Mathf.Sin(2f * Mathf.PI * 220f * i / rate);
                }

                AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
                clip.SetData(data, 0);
                return clip;
            }
        }
    }
}
