using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 横持ち・指のときの知らせ（トースト）が、カメラをどこまで寄せても自分の人物の頭にかからないこと (#187)。
    /// 頭の上端はカメラが寄るほど画面の上へ上がる。ピンチで寄せ、実カメラで写した頭の上端と
    /// 実際に置かれたトーストの下端を、画面の高さに対する割合で比べる。
    /// </summary>
    public sealed class ToastPlacementPlayTests : SceneTestBase
    {
        /// <summary>カメラの軌道の半径の倍率（RadialAxis）の初期値と下限。ActorFactory の置き方と同じ。</summary>
        private const float FarRadial = 1f;
        private const float NearRadial = 0.34f;

        /// <summary>元の置き方（下端が画面の高さのこの割合）。カメラが遠いときはここから動かない。</summary>
        private const float RestingBottom = 0.56f;

        /// <summary>頭にかからないとみなす、トーストの下端と頭の上端の最小の間（画面の高さに対する割合）。</summary>
        private const float Clearance = 0.01f;

        private static readonly Vector3[] Corners = new Vector3[4];

        [TearDown]
        public void ReleaseForcedMode()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            TouchInput.Clear();
        }

        /// <summary>
        /// 遠い・中くらい・最も寄ったの 3 つの距離で、トーストの下端が頭の上端より <see cref="Clearance"/> 以上上にある。
        /// 遠いときは元の置き場所（0.56）に留まり、遠くへ戻せば戻る。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_ToastStaysAboveTheHeadAtEveryCameraDistance()
        {
            CollectErrorsInsteadOfLogAssert();
            InputMode.Force(InputKind.Touch);
            ScreenShape.Force(false);

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsFalse(KCDInput.GameplayBlocked, "入場の直後なのに操作が止まっている");

            var layout = Object.FindAnyObjectByType<ToastLayout>();
            var player = Object.FindAnyObjectByType<PlayerAppearance>();
            Camera camera = Camera.main;
            Assert.IsNotNull(layout, "キャンパスにトーストの置き場が無い");
            Assert.IsNotNull(player, "キャンパスに自分の人物が居ない");
            Assert.IsNotNull(camera, "メインカメラが無い");
            Transform aim = player.transform.Find(PlayerAppearance.AimName);
            Assert.IsNotNull(aim, "カメラの注視点が無い");
            var toast = (RectTransform)layout.transform;
            HUD.Instance.ShowToast("トーストの置き場所を確かめる", false);

            var readings = new List<Reading>();
            var settle = new CameraSettle(camera.transform, aim);
            float radial = FarRadial;
            yield return settle.Wait();
            Assert.IsFalse(settle.TimedOut, "入場の直後のカメラが落ち着かない");
            readings.Add(Measure(radial, settle.Distance, camera, player, toast));

            foreach (float next in new[] { 0.67f, NearRadial, FarRadial })
            {
                // 2 本指の間隔を radial / next 倍にすると、CinemachineInputBridge が半径の倍率を next / radial 倍にする。
                TouchInput.AddPinch(1f, radial / next);
                yield return settle.Wait();
                Assert.IsFalse(settle.TimedOut, "半径の倍率を " + Format(next) + " に向けて動かしたあと、カメラが落ち着かない");
                radial = next;
                readings.Add(Measure(radial, settle.Distance, camera, player, toast));
            }

            string log = Describe(camera, player, readings);
            Reading far = readings[0];
            Reading nearest = readings[2];
            float limit = LiftLimit((RectTransform)toast.parent);
            Assert.Greater(nearest.HeadTop + Clearance, RestingBottom,
                "前提: 最も寄ったとき、元の置き場所のままでは頭との間が " + Format(Clearance) + " に足りない。足りていると、上げる働きを確かめられない" + log);
            Assert.LessOrEqual(nearest.HeadTop + Clearance, limit,
                "前提: 最も寄ったときの頭の上に、トーストを上げられる限度 " + Format(limit) + " までで間を空けられる。空けられないと、頭にかからないことを確かめられない" + log);
            Assert.GreaterOrEqual(far.ToastRight, far.PlayerX, "前提: 人物がトーストの横の範囲の中に居る。外に居ると、頭にかかるかを確かめられない" + log);
            Assert.LessOrEqual(far.ToastLeft, far.PlayerX, "前提: 人物がトーストの横の範囲の中に居る。外に居ると、頭にかかるかを確かめられない" + log);

            var failures = new List<string>();
            foreach (Reading r in readings)
            {
                if (r.ToastBottom < r.HeadTop + Clearance)
                {
                    failures.Add("カメラの距離 " + Format(r.Distance) + " m: トーストの下端 " + Format(r.ToastBottom) +
                                 " が頭の上端 " + Format(r.HeadTop) + " から " + Format(Clearance) + " 上に届かず、頭にかかる");
                }
            }

            AssertAtRest(far.ToastBottom, "遠いときのトーストが元の置き場所から動いた" + log);
            AssertAtRest(readings[3].ToastBottom, "遠くへ戻してもトーストが上がったまま" + log);
            Assert.IsEmpty(failures, string.Join("\n", failures) + log);
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// トーストの下端が元の置き場所にある。頭の上端が刻みの境目に乗ると 1 刻みだけ上がるので、その分は許す。
        /// 刻みの値そのものは EditMode の ToastLayoutTests が見る。
        /// </summary>
        private static void AssertAtRest(float bottom, string message)
        {
            Assert.GreaterOrEqual(bottom, RestingBottom - 0.002f, message);
            Assert.LessOrEqual(bottom, RestingBottom + ToastLayout.Step + 0.002f, message);
        }

        /// <summary>
        /// トーストを上げられる限度（画面の高さに対する割合）。上端がクエストの追跡の下端に接する高さで、
        /// 画面が低くてそれが元の置き場所を割るときは元の置き場所。canvas は ToastLayout が置く先の枠。
        /// </summary>
        private static float LiftLimit(RectTransform canvas)
        {
            float occupied = ToastLayout.TrackerBottom + ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch).Size.y;
            return Mathf.Max(RestingBottom, 1f - occupied / canvas.rect.height);
        }

        private static Reading Measure(float radial, float distance, Camera camera, PlayerAppearance player, RectTransform toast)
        {
            float height = player.BodyHeight > 0.3f ? player.BodyHeight : PlayerAppearance.ReferenceHeight;
            Vector3 foot = player.transform.position;
            Vector3 top = camera.WorldToViewportPoint(foot + Vector3.up * height);

            toast.GetWorldCorners(Corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, Corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, Corners[2]);
            return new Reading
            {
                Radial = radial,
                Distance = distance,
                HeadTop = top.y,
                PlayerX = top.x,
                ToastBottom = bottomLeft.y / Screen.height,
                ToastLeft = bottomLeft.x / Screen.width,
                ToastRight = topRight.x / Screen.width
            };
        }

        private static string Describe(Camera camera, PlayerAppearance player, List<Reading> readings)
        {
            var text = new StringBuilder();
            text.Append("\n実測: 画面 ").Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" 縦の画角 ").Append(Format(camera.fieldOfView))
                .Append("° 背丈 ").Append(Format(player.BodyHeight)).Append(" m");
            foreach (Reading r in readings)
            {
                text.Append("\n  狙った半径の倍率 ").Append(Format(r.Radial))
                    .Append(" 距離 ").Append(Format(r.Distance))
                    .Append(" m 頭の上端 ").Append(Format(r.HeadTop))
                    .Append(" トーストの下端 ").Append(Format(r.ToastBottom))
                    .Append(" 人物の横 ").Append(Format(r.PlayerX))
                    .Append(" トーストの横 ").Append(Format(r.ToastLeft)).Append('-').Append(Format(r.ToastRight));
            }

            return text.ToString();
        }

        private static string Format(float value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private struct Reading
        {
            public float Radial;
            public float Distance;
            public float HeadTop;
            public float PlayerX;
            public float ToastBottom;
            public float ToastLeft;
            public float ToastRight;
        }
    }
}
