using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内の環境光の建物ごとの倍率 (#423)。講義棟だけ天井が受ける地面の色を明るくし、
    /// 他の建物はこれまでの DayNightCycle の色のままにする。
    /// </summary>
    public class IndoorAmbientProfileTests
    {
        private static readonly Color Base = new Color(0.56f, 0.57f, 0.60f, 1f);

        [Test]
        public void OtherBuildings_KeepPreviousColors()
        {
            foreach (string id in new[] { "library", "", null })
            {
                IndoorAmbientProfile.Resolve(id, Base, out Color sky, out Color equator, out Color ground);
                AssertColor(Base, sky);
                AssertColor(Base * 0.92f, equator);
                AssertColor(Base * 0.7f, ground);
            }
        }

        [Test]
        public void Lecture_BrightensSkyAndCeilingGround()
        {
            IndoorAmbientProfile.Resolve("lecture", Base, out Color sky, out Color equator, out Color ground);
            Color scaled = new Color(Base.r * 1.25f, Base.g * 1.25f, Base.b * 1.25f, 1f);
            AssertColor(scaled, sky);
            AssertColor(scaled * 0.95f, equator);
            AssertColor(scaled * 0.92f, ground);
            Assert.Greater(ground.r, Base.r * 0.7f);
        }

        [Test]
        public void Kyoso_BrightensAndWarms()
        {
            IndoorAmbientProfile.Resolve("kyoso", Base, out Color sky, out Color equator, out Color ground);
            Color scaled = new Color(Base.r * 1.6f * 1.05f, Base.g * 1.6f, Base.b * 1.6f * 0.9f, 1f);
            AssertColor(scaled, sky);
            AssertColor(scaled * 0.95f, equator);
            AssertColor(scaled * 0.8f, ground);
            Assert.Greater(sky.r / sky.b, Base.r / Base.b);
        }

        [Test]
        public void Resolve_KeepsAlpha()
        {
            Color baseAmbient = new Color(0.5f, 0.5f, 0.5f, 1f);
            IndoorAmbientProfile.Resolve("lecture", baseAmbient, out Color sky, out _, out _);
            Assert.AreEqual(1f, sky.a, 1e-5f);
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f);
            Assert.AreEqual(expected.g, actual.g, 1e-4f);
            Assert.AreEqual(expected.b, actual.b, 1e-4f);
        }
    }
}
