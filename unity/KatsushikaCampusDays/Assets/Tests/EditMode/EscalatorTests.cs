using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// エスカレーターの運ぶ範囲と段の流れ。区間は講義棟の 1F→2F の南の列と同じ寸法
    /// （段の線の水平の長さ 8.0 m、高さ 4.6 m、23 段）で、+x へ上る。
    /// </summary>
    public sealed class EscalatorTests
    {
        private static readonly Vector3 Bottom = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Top = new Vector3(8f, 4.6f, 0f);
        private const float Half = Escalator.DefaultHalfWidth;
        private const float Speed = Escalator.DefaultSpeed;

        private static Vector3 OnLine(float x, float side = 0f)
        {
            return new Vector3(x, Top.y * x / Top.x, side);
        }

        [Test]
        public void OnTheSteps_CarriesUphillAlongTheFlight()
        {
            Vector3 v = Escalator.CarryVelocity(Bottom, Top, Half, Speed, OnLine(4f));
            Assert.Greater(v.x, 0f, "上る向き（+x）へ運ばない");
            Assert.AreEqual(0f, v.y, 1e-6f, "運ぶのは水平の成分だけ（高さは斜めの板が受け持つ）");
            Assert.AreEqual(0f, v.z, 1e-6f, "横へ流す");
            float along = Speed * Top.x / Top.magnitude;
            Assert.AreEqual(along, v.x, 1e-4f, "水平の速さが段の線に沿った 0.5 m/s の水平の成分でない");
        }

        [Test]
        public void JustPastTheTop_StillPushesOntoTheLanding()
        {
            Vector3 feet = new Vector3(Top.x + Escalator.RunOff * 0.5f, Top.y, 0f);
            Assert.Greater(Escalator.CarryVelocity(Bottom, Top, Half, Speed, feet).x, 0f,
                "降り口で止まり、段に押し戻される");
        }

        [Test]
        public void OffTheFlight_DoesNotCarry()
        {
            Assert.AreEqual(Vector3.zero, Escalator.CarryVelocity(Bottom, Top, Half, Speed, new Vector3(-0.3f, 0f, 0f)),
                "乗り口の手前の床で運ぶ");
            Assert.AreEqual(Vector3.zero,
                Escalator.CarryVelocity(Bottom, Top, Half, Speed, new Vector3(Top.x + Escalator.RunOff + 0.2f, Top.y, 0f)),
                "降りたあとの平場の奥まで運ぶ");
            Assert.AreEqual(Vector3.zero, Escalator.CarryVelocity(Bottom, Top, Half, Speed, OnLine(4f, Half + 0.3f)),
                "隣の列（腹板の外）で運ぶ");
        }

        [Test]
        public void UnderOrAboveTheFlight_DoesNotCarry()
        {
            Vector3 under = new Vector3(4f, 0f, 0f);
            Assert.AreEqual(Vector3.zero, Escalator.CarryVelocity(Bottom, Top, Half, Speed, under),
                "区間の真下の 1F の床で運ぶ");
            Vector3 above = OnLine(4f) + Vector3.up * 4.2f;
            Assert.AreEqual(Vector3.zero, Escalator.CarryVelocity(Bottom, Top, Half, Speed, above),
                "上の階の同じ x で運ぶ");
        }

        [Test]
        public void DownFlight_CarriesDownhillAtTheSameSpeed()
        {
            Vector3 up = Escalator.CarryVelocity(Bottom, Top, Half, Speed, OnLine(4f));
            Vector3 down = Escalator.CarryVelocity(Bottom, Top, Half, Speed, OnLine(4f), down: true);
            Assert.Less(down.x, 0f, "下りの区間が下る向き（−x）へ運ばない");
            Assert.AreEqual(-up.x, down.x, 1e-4f, "下りの速さが上りと違う");
        }

        [Test]
        public void DownFlight_PushesOntoTheLowerLandingAndNotPastTheTop()
        {
            Vector3 pastBottom = new Vector3(-Escalator.RunOff * 0.5f, 0f, 0f);
            Assert.Less(Escalator.CarryVelocity(Bottom, Top, Half, Speed, pastBottom, down: true).x, 0f,
                "下りの降り口（下の端）で止まり、段に押し戻される");
            Vector3 pastTop = new Vector3(Top.x + Escalator.RunOff * 0.5f, Top.y, 0f);
            Assert.AreEqual(Vector3.zero, Escalator.CarryVelocity(Bottom, Top, Half, Speed, pastTop, down: true),
                "下りの乗り口の手前（上の階の平場）で段へ引き込む");
        }

        [Test]
        public void StepPhase_RepeatsEveryStep()
        {
            float stepLength = Top.magnitude / 23f;
            float period = stepLength / Speed;
            Assert.AreEqual(0f, Escalator.StepPhase(0f, Speed, stepLength), 1e-5f);
            Assert.AreEqual(0.5f, Escalator.StepPhase(period * 0.5f, Speed, stepLength), 1e-4f);
            Assert.AreEqual(0.25f, Escalator.StepPhase(period * 3.25f, Speed, stepLength), 1e-3f);
            Assert.AreEqual(0f, Escalator.StepPhase(1f, Speed, 0f), 1e-6f, "段の長さ 0 で割る");
        }
    }
}
