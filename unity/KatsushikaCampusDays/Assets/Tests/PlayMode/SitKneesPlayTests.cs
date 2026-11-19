using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 座ったときに膝が外へ開かない（がに股にならない）。選べる体ごとに座面の箱へ座らせ、
    /// 体の左右の向きで測った膝の間隔が足の間隔と脚の付け根の間隔を大きく超えないこと、
    /// 腿が前へ向いていることを見る。判定は骨の位置だけで行う。目で確かめる絵（前と斜め上）は、
    /// 描画のデバイスがあるときだけ撮って unity/logs/sit/ に置く。
    /// </summary>
    public sealed class SitKneesPlayTests : SceneTestBase
    {
        /// <summary>膝の間隔が足の間隔を超えてよい幅（体の高さに対する割合）。膝の骨の太さぶんの揺れ。</summary>
        private const float KneeOverFeet = 0.04f;

        /// <summary>腿が真っ直ぐ前から外へ開いてよい角度（度）。</summary>
        private const float MaxThighSplay = 5f;

        /// <summary>膝が脚の付け根より前へ出ているべき距離 (m)。頭身の低い体でも座れば超える。</summary>
        private const float MinKneeAhead = 0.03f;

        /// <summary>座面の箱の高さ (m)。SitPose.SeatSurface と同じ。</summary>
        private const float SeatHeight = SitPose.SeatSurface;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Sitting_KeepsKneesOverTheFeet_ForEveryBody()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            PlayerAppearance appearance = player.GetComponent<PlayerAppearance>();
            Assert.IsNotNull(appearance, "プレイヤーに PlayerAppearance が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            var anchor = new GameObject("SitKneesAnchor").transform;
            anchor.SetPositionAndRotation(player.transform.position, Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));
            GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "SitKneesSeat";
            Object.Destroy(seat.GetComponent<Collider>());
            seat.transform.SetPositionAndRotation(anchor.position + Vector3.up * (SeatHeight * 0.5f) - anchor.forward * 0.05f, anchor.rotation);
            seat.transform.localScale = new Vector3(0.9f, SeatHeight, 0.45f);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "sit"));
            if (CanRender)
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // 描けないカメラを描かせるとエラーのログが出るうえ、絵も一色になって見る意味が無い。
                Debug.Log("[sit] 描画のデバイスが無い（-nographics）ので絵は撮らない。膝は骨の位置で測る");
            }

            var bad = new List<string>();
            try
            {
                foreach (string id in GameManager.PlayableCharacterIds)
                {
                    appearance.Apply(id);
                    yield return PlayModeScenes.Advance(5, 0.3f);
                    Animator animator = player.GetComponentInChildren<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                    player.SitAt(anchor);
                    Assert.IsTrue(player.IsSitting, id + ": 座れない");
                    yield return PlayModeScenes.Advance(30, 1.0f);

                    Measure(id, animator, player.transform, bad);
                    if (CanRender)
                    {
                        yield return Shoot(Path.Combine(folder, "front-" + id + ".png"), anchor, 0f, 1.5f, 0.75f);
                        yield return Shoot(Path.Combine(folder, "above-" + id + ".png"), anchor, 1.0f, 1.3f, 1.9f);
                    }

                    player.StandUp();
                    yield return PlayModeScenes.Advance(10, 0.5f);
                }
            }
            finally
            {
                Object.Destroy(seat);
                Object.Destroy(anchor.gameObject);
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("座って膝をそろえる");
        }

        /// <summary>座った体の膝・足・脚の付け根の左右の間隔と、腿の開きを測る。</summary>
        private static void Measure(string id, Animator animator, Transform root, List<string> bad)
        {
            Vector3 right = root.right;
            Vector3 forward = root.forward;
            Transform lHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform lKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform rKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Transform lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Assert.IsNotNull(lKnee, id + ": 膝の骨が無い");

            float hipGap = Vector3.Dot(rHip.position - lHip.position, right);
            float kneeGap = Vector3.Dot(rKnee.position - lKnee.position, right);
            float footGap = Vector3.Dot(rFoot.position - lFoot.position, right);
            float height = Vector3.Dot(head.position - root.position, Vector3.up);
            float splayL = Splay(lKnee.position - lHip.position, -right, forward);
            float splayR = Splay(rKnee.position - rHip.position, right, forward);
            float forwardL = Vector3.Dot(lKnee.position - lHip.position, forward);
            float forwardR = Vector3.Dot(rKnee.position - rHip.position, forward);

            Debug.Log("[sit] " + id + " 付け根 " + hipGap.ToString("F3") + " m, 膝 " + kneeGap.ToString("F3")
                + " m, 足 " + footGap.ToString("F3") + " m, 腿の開き 左 " + splayL.ToString("F1") + "° 右 " + splayR.ToString("F1")
                + "°, 膝の前への出 左 " + forwardL.ToString("F3") + " 右 " + forwardR.ToString("F3") + " m, 頭の高さ " + height.ToString("F2") + " m");

            float slack = KneeOverFeet * height;
            if (kneeGap > footGap + slack)
            {
                bad.Add(id + ": 膝の間隔 " + kneeGap.ToString("F3") + " m が足の間隔 " + footGap.ToString("F3") + " m より開いている（がに股）");
            }

            if (kneeGap > hipGap + slack)
            {
                bad.Add(id + ": 膝の間隔 " + kneeGap.ToString("F3") + " m が脚の付け根の間隔 " + hipGap.ToString("F3") + " m より開いている（がに股）");
            }

            if (splayL > MaxThighSplay || splayR > MaxThighSplay)
            {
                bad.Add(id + ": 腿が外へ開いている（左 " + splayL.ToString("F1") + "°, 右 " + splayR.ToString("F1") + "°）");
            }

            if (forwardL < MinKneeAhead || forwardR < MinKneeAhead)
            {
                bad.Add(id + ": 膝が前へ出ていない（座っていない）左 " + forwardL.ToString("F3") + " 右 " + forwardR.ToString("F3") + " m");
            }
        }

        /// <summary>腿（付け根から膝）が前から outward の側へ開いている角度（度）。内へ向くと負。</summary>
        private static float Splay(Vector3 thigh, Vector3 outward, Vector3 forward)
        {
            return Mathf.Atan2(Vector3.Dot(thigh, outward), Mathf.Max(0.001f, Vector3.Dot(thigh, forward))) * Mathf.Rad2Deg;
        }

        private static IEnumerator Shoot(string path, Transform anchor, float right, float forward, float height)
        {
            const int width = 900;
            const int heightPx = 900;
            var go = new GameObject("SitKneesShot");
            var camera = go.AddComponent<Camera>();
            var texture = new RenderTexture(width, heightPx, 24, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(width, heightPx, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Vector3 focus = anchor.position + Vector3.up * 0.55f + anchor.forward * 0.2f;
                go.transform.position = anchor.position + anchor.right * right + anchor.forward * forward + Vector3.up * height;
                go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position, Vector3.up);
                camera.fieldOfView = 45f;
                camera.nearClipPlane = 0.05f;
                camera.targetTexture = texture;
                camera.depth = 100f;
                yield return null;
                camera.Render();
                RenderTexture.active = texture;
                readable.ReadPixels(new Rect(0, 0, width, heightPx), 0, 0);
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
    }
}
