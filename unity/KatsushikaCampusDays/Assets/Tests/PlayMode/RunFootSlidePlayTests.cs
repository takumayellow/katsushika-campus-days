using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 走り・歩きで接地した足が地面に対して滑っていないこと (#232)。
    /// 走りの歩幅を詰めて脚を上げないようにしても、クリップの前進速度が移動速度（走り 5.4 m/s、歩き 2.6 m/s）と
    /// 合っていないと、接地した足が地面を滑る。実際にプレイヤーを自動で走らせ、毎フレーム足の接地点を
    /// 世界座標で追う。接地している間、その点は動かないはずなので、動いた分が滑り。
    /// 接地点は、かかとが着く間は足首の真下、母指球が着く間はつま先ボーンの位置（ロッカー）。
    /// </summary>
    public sealed class RunFootSlidePlayTests : SceneTestBase
    {
        /// <summary>接地とみなす足の高さ（床から、m）。</summary>
        private const float PlantedHeight = 0.015f;

        /// <summary>走り出してから測り始めるまでの待ちと測る長さ（どちらもゲーム時間の秒）。</summary>
        private const float SettleSeconds = 0.5f;
        private const float MeasureSeconds = 2.0f;

        /// <summary>測れた向きで走り直す回数（標本を増やす）。</summary>
        private const int Passes = 5;

        private const float StepDelta = 1f / 60f;

        /// <summary>
        /// 平均の滑り（m/s）の上限。走りは接地の瞬間と離れる瞬間（足が地面から 1.5 cm 以内で動いている間）も
        /// 数えるので、歩きより大きく出る。実測（#232、5 回走って平均）は走りが 0.9〜1.0 m/s、歩きが 0.3 m/s で、
        /// 上限はその 1.2 倍ほどに置いて、歩幅やクリップの速さの食い違いで悪化したときに落とす。
        /// </summary>
        private const float RunMeanSlipLimit = 1.2f;
        private const float WalkMeanSlipLimit = 0.36f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Run_PlantedFeetDoNotSlide()
        {
            yield return Measure("run", 5.4f, RunMeanSlipLimit);
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Walk_PlantedFeetDoNotSlide()
        {
            yield return Measure("walk", PlayerController.DefaultWalkSpeed, WalkMeanSlipLimit);
        }

        private IEnumerator Measure(string label, float speed, float limit)
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            Animator animator = player.GetComponentInChildren<Animator>();
            Assert.IsTrue(animator != null && animator.avatar != null && animator.isHuman, "Humanoid のアニメータが無い");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 画面外・batchmode でも姿勢を更新させる
            Transform[] ankles = { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            Transform[] toes = { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            Assert.IsTrue(ankles.All(t => t != null) && toes.All(t => t != null), "足首・つま先のボーンが無い");

            Vector3 start = player.transform.position;
            float yaw = player.transform.eulerAngles.y;
            float ankleHeight = ankles[0].position.y - start.y;   // 立ち姿での足首の高さ（かかとの接地点はその真下）

            float toeHeight = toes[0].position.y - start.y;      // 立ち姿でのつま先ボーン（母指球の関節）の高さ

            Time.captureDeltaTime = StepDelta;
            var slips = new List<float>();
            int planted = 0, frames = 0;
            float planarSum = 0f;
            bool measured = false;
            try
            {
                // 障害物の無い向きを探す: 8 方向のうち、測る間ずっと目標の速さで走れた向きで測る。
                int passes = 0;
                for (int k = 0; k < 8 && passes < Passes; )
                {
                    player.Teleport(start, yaw);
                    yield return null;
                    Vector3 dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                    player.SetAutopilot(start + dir * 60f, speed);
                    yield return Wait(SettleSeconds);

                    var trial = new List<float>();
                    int trialPlanted = 0, trialFrames = 0;
                    float trialPlanar = 0f;
                    Vector3[,] prev = null;
                    bool[,] prevPlanted = null;
                    float t = 0f;
                    float minH = 9f;
                    bool blocked = false;
                    while (t < MeasureSeconds)
                    {
                        yield return null;   // batchmode の Editor では WaitForEndOfFrame が来ない。足は 1 フレーム前の姿勢で、毎フレーム同じずれ方なので速さは変わらない
                        float dt = Time.deltaTime;
                        t += dt;
                        if (player.PlanarSpeed < speed - 0.3f)
                        {
                            Debug.Log("[KCD] foot_slide " + label + ": 向き " + k * 45 + " 度は " + t.ToString("0.00") + " 秒で止まった（速さ "
                                + player.PlanarSpeed.ToString("0.00") + "）");
                            blocked = true;
                            break;
                        }

                        trialFrames++;
                        trialPlanar += player.PlanarSpeed;
                        float floorY = player.transform.position.y;
                        var pos = new Vector3[2, 2];
                        var on = new bool[2, 2];
                        for (int f = 0; f < 2; f++)
                        {
                            pos[f, 0] = ankles[f].position + Vector3.down * ankleHeight;   // かかと
                            pos[f, 1] = toes[f].position + Vector3.down * toeHeight;       // 母指球
                            for (int p = 0; p < 2; p++)
                            {
                                minH = Mathf.Min(minH, pos[f, p].y - floorY);
                                on[f, p] = pos[f, p].y - floorY < PlantedHeight;
                                if (on[f, p])
                                {
                                    trialPlanted++;
                                }
                            }
                        }

                        if (prev != null && dt > 0f)
                        {
                            for (int f = 0; f < 2; f++)
                            {
                                for (int p = 0; p < 2; p++)
                                {
                                    if (on[f, p] && prevPlanted[f, p])
                                    {
                                        Vector3 d = pos[f, p] - prev[f, p];
                                        d.y = 0f;
                                        trial.Add(d.magnitude / dt);
                                    }
                                }
                            }
                        }

                        prev = pos;
                        prevPlanted = on;
                    }

                    player.ClearAutopilot();
                    Debug.Log("[KCD] foot_slide " + label + ": 向き " + k * 45 + " 度 blocked=" + blocked + " 組 " + trial.Count + " 最低の足 " + minH.ToString("0.000") + " m");
                    if (!blocked && trial.Count > 10)
                    {
                        slips.AddRange(trial);
                        planted += trialPlanted;
                        frames += trialFrames;
                        planarSum += trialPlanar;
                        measured = true;
                        passes++;
                    }
                    else if (passes == 0)
                    {
                        k++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                player.ClearAutopilot();
            }

            Assert.IsTrue(measured, "どの向きにも " + MeasureSeconds + " 秒走れる所が無い");
            slips.Sort();
            float mean = slips.Average();
            float p90 = slips[(int)(slips.Count * 0.9f)];
            Debug.Log("[KCD] foot_slide " + label + ": 速さ " + (planarSum / frames).ToString("0.00") + " m/s、接地した点 "
                + slips.Count + " 組、平均 " + mean.ToString("0.000") + " m/s、p90 " + p90.ToString("0.000")
                + " m/s、最大 " + slips[slips.Count - 1].ToString("0.000") + " m/s、接地 "
                + (planted / (4f * frames)).ToString("P0"));
            Assert.LessOrEqual(mean, limit, label + " の接地した足が滑っている: 平均 " + mean.ToString("0.000") + " m/s");
            AssertNoErrors(label);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
        }
    }
}
