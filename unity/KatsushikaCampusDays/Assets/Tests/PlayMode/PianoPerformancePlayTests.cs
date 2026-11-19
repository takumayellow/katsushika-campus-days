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
    /// 曲が終わったら座ったまま手を戻す。シーンでは本物の椅子・曲・体で、手が鍵盤に届いていることを確かめる。目で確かめる画は、
    /// 描画のデバイスがあるときだけ撮って unity/logs/piano/ に置く。
    /// 音は MuteDuringTests で消してある（鳴らす処理は動き、聞こえる音量だけが 0）。
    /// </summary>
    public sealed class PianoPerformancePlayTests : SceneTestBase
    {
        private const string PianoClipId = PianoPerformance.DefaultClipId;
        private const string BgmId = "test_bgm";

        /// <summary>手首が鍵盤の目標から離れてよい距離 (m)。演奏中は手が左右に ±0.10、上下に 0.05 動く。</summary>
        private const float HandReach = 0.2f;

        /// <summary>鍵盤の目標が腕の届く範囲を超えてよい距離 (m)。身を乗り出すので、ほぼ 0。</summary>
        private const float MaxShortfall = 0.02f;

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
        public IEnumerator Teleport_WhileSitting_StandsUpStopsTheSongAndBgmRecovers()
        {
            yield return DropAudioManager();
            var rig = new Rig(3f);
            try
            {
                rig.Audio.PlayBgm(BgmId);
                rig.Seat.Interact(rig.Player.gameObject);
                yield return PlayModeScenes.Advance(5, 0.6f);
                Assert.IsTrue(rig.Audio.IsPianoPlaying);

                bool stood = false;
                rig.Player.SittingChanged += sitting => stood |= !sitting;
                rig.Player.Teleport(rig.Anchor.position + Vector3.right * 5f, 0f);
                Assert.IsFalse(rig.Player.IsSitting, "ワープしても座ったまま");
                Assert.IsTrue(stood, "ワープで立っても SittingChanged(false) が出ない");
                Assert.IsFalse(rig.Performance.IsPlaying, "ワープしても演奏が続く");
                Assert.IsFalse(rig.Audio.IsPianoPlaying, "ワープしても曲が止まらない");
                Assert.IsFalse(KCDInput.MovementLocked, "ワープしても移動が封じられたまま");

                yield return PlayModeScenes.Advance(5, 2.4f);
                Assert.GreaterOrEqual(rig.Audio.BgmDuck, 0.95f, "ワープのあと BGM が戻らない");
            }
            finally
            {
                rig.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator StandingUp_StopsInFrontOfAWallBehindTheStool()
        {
            yield return DropAudioManager();
            var rig = new Rig(3f);
            var wall = new GameObject("Wall");
            try
            {
                wall.transform.SetPositionAndRotation(rig.Anchor.position - rig.Anchor.forward * 1.0f + Vector3.up * 0.5f, rig.Anchor.rotation);
                wall.AddComponent<BoxCollider>().size = new Vector3(4f, 2f, 0.1f);
                rig.Seat.Interact(rig.Player.gameObject);
                yield return PlayModeScenes.Advance(3, 0.2f);

                rig.Player.StandUp();
                float behind = Vector3.Dot(rig.Player.transform.position - rig.Anchor.position, rig.Anchor.forward);
                Assert.Greater(behind, -0.95f, "立つと壁の中へ入る (" + behind + " m)");
                Assert.Less(behind, -0.2f, "壁の手前へも出られない");
            }
            finally
            {
                Object.Destroy(wall);
                rig.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PausedAudio_DoesNotRunDownThePhrase()
        {
            yield return DropAudioManager();
            var rig = new Rig(3f);
            try
            {
                rig.Audio.PlayBgm(BgmId);
                rig.Seat.Interact(rig.Player.gameObject);
                AudioListener.pause = true;
                yield return PlayModeScenes.Advance(5, 4f);
                Assert.IsTrue(rig.Audio.IsPianoPlaying, "ポーズ中に曲が終わった扱いになる");
                Assert.IsTrue(rig.Performance.IsPlaying);

                AudioListener.pause = false;
                yield return PlayModeScenes.Advance(5, 3.6f);
                Assert.IsFalse(rig.Audio.IsPianoPlaying, "ポーズを解いても曲が終わらない");
            }
            finally
            {
                AudioListener.pause = false;
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
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "piano"));
            if (CanRender)
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // 描けないカメラを描かせるとエラーのログが出るうえ、絵も一色になって見る意味が無い。
                Debug.Log("[piano] 描画のデバイスが無い（-nographics）ので絵は撮らない。手は骨の位置で測る");
            }

            PlayerAppearance appearance = player.GetComponent<PlayerAppearance>();
            Assert.IsNotNull(appearance, "プレイヤーに PlayerAppearance が無い");

            foreach (string id in GameManager.PlayableCharacterIds)
            {
                appearance.Apply(id);
                yield return PlayModeScenes.Advance(5, 0.3f);
                player.Teleport(anchor.position - anchor.forward * 1.1f, anchor.eulerAngles.y);
                yield return PlayModeScenes.Advance(5, 0.3f);

                // バッチモードでは画面に映らず、体のアニメーションが止まる。映っている前提で動かす。
                player.GetComponentInChildren<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                seat.Interact(player.gameObject);
                Assert.IsTrue(player.IsSitting, id + ": ピアノの椅子に座れない");
                Assert.IsTrue(performance.IsPlaying, id + ": 座っても演奏が始まらない（曲がシーンに入っていない？）");
                Assert.IsTrue(AudioManager.Instance.IsPianoPlaying);

                yield return PlayModeScenes.Advance(30, 2.0f);
                Animator animator = player.GetComponentInChildren<Animator>();
                SitPose pose = player.GetComponentInChildren<SitPose>();
                Assert.IsNotNull(pose, id + ": 体に SitPose が無い");
                Assert.GreaterOrEqual(pose.KeyboardWeight, 0.99f, id + ": 手が鍵盤に上がらない");
                Assert.LessOrEqual(AudioManager.Instance.BgmDuck, AudioManager.PianoDuckLevel + 0.02f, "BGM が絞られない");

                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                float toGoal = (hand.position - pose.RightHandGoal).magnitude;
                Debug.Log("[piano] " + id + " 手首から目標まで " + toGoal.ToString("F3") + " m, 目標が届かないぶん "
                    + pose.RightHandShortfall.ToString("F3") + " m, 腕 " + pose.ArmReach.ToString("F3") + " m, 手首から鍵盤まで "
                    + (hand.position - performance.Keys.position).magnitude.ToString("F3") + " m");

                Debug.Log("[piano] " + id + " 座面から見て 肩 " + anchor.InverseTransformPoint(shoulder.position).ToString("F2")
                    + " 手首 " + anchor.InverseTransformPoint(hand.position).ToString("F2")
                    + " 鍵盤 " + anchor.InverseTransformPoint(performance.Keys.position).ToString("F2"));
                if (CanRender)
                {
                    yield return Shoot(Path.Combine(folder, "side-" + id + ".png"), anchor, 1.5f, -0.1f, 1.25f);
                    if (id == "madonna")
                    {
                        yield return PlayModeScenes.Advance(10, 0.4f);
                        yield return Shoot(Path.Combine(folder, "back.png"), anchor, 1.1f, -1.9f, 1.9f);
                        yield return PlayModeScenes.Advance(10, 0.4f);
                        yield return Shoot(Path.Combine(folder, "left.png"), anchor, -1.5f, -0.1f, 1.25f);
                    }
                }

                if (id != "madonna")
                {
                    Assert.IsNotNull(pose.Bag, id + ": 鞄を持つ体に Bag ボーンが無い");
                }

                if (pose.Bag != null)
                {
                    Assert.Less(BagExtent(pose), 0.01f, id + ": 弾いている間も鞄が鍵盤のあたりに残っている");
                }

                Assert.LessOrEqual(pose.RightHandShortfall, MaxShortfall, id + ": 鍵盤が腕の届かない所にある (" + pose.RightHandShortfall + " m)");
                Assert.LessOrEqual(toGoal, HandReach, id + ": 右手が鍵盤の上の目標に届かない (" + toGoal + " m)");

                player.StandUp();
                Assert.IsFalse(performance.IsPlaying, id + ": 立っても演奏が続く");
                Assert.IsFalse(AudioManager.Instance.IsPianoPlaying, "立っても曲が止まらない");
                yield return PlayModeScenes.Advance(10, 2.4f);
                Assert.GreaterOrEqual(AudioManager.Instance.BgmDuck, 0.95f, "立ったあと BGM が戻らない");
                if (pose.Bag != null)
                {
                    Assert.Greater(BagExtent(pose), 0.05f, id + ": 立ったあと鞄が戻らない");
                }
            }

            AssertNoErrors("ピアノを弾く");
        }

        /// <summary>
        /// 体のメッシュのうち Bag ボーンだけに付いた頂点（鞄・風呂敷）が、いま張る大きさ (m)。
        /// 骨のスケールを 0 にすると 1 点に潰れて 0 になる。体は 1 枚のスキンメッシュなので、Renderer の bounds では鞄だけを測れない。
        /// </summary>
        private static float BagExtent(SitPose pose)
        {
            SkinnedMeshRenderer renderer = pose.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(renderer, "体に SkinnedMeshRenderer が無い");
            int bagIndex = System.Array.IndexOf(renderer.bones, pose.Bag);
            Assert.GreaterOrEqual(bagIndex, 0, "Bag ボーンが SkinnedMeshRenderer の骨に入っていない");

            var baked = new Mesh();
            renderer.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            BoneWeight[] weights = renderer.sharedMesh.boneWeights;
            var bounds = new Bounds();
            bool any = false;
            for (int i = 0; i < weights.Length && i < vertices.Length; i++)
            {
                if (weights[i].boneIndex0 != bagIndex || weights[i].weight0 < 0.99f)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = new Bounds(vertices[i], Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(vertices[i]);
                }
            }

            Object.Destroy(baked);
            Assert.IsTrue(any, "Bag ボーンだけに付いた頂点が無い");
            return bounds.size.magnitude;
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
