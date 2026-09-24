using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Tests
{
    /// <summary>
    /// カメラが頭の裏まで寄ったらプレイヤーの体を隠し、離れたら元の設定に戻す (#12)。
    /// </summary>
    public sealed class CameraNearHiderTests
    {
        private GameObject _root;
        private MeshRenderer _body;
        private MeshRenderer _outline;
        private MeshRenderer _inactive;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Player");
            _body = Child("Body", ShadowCastingMode.On);
            _outline = Child("Outline", ShadowCastingMode.Off);
            _inactive = Child("Body_botchan", ShadowCastingMode.TwoSided);
            _inactive.gameObject.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void ShouldHide_HasAGapSoItDoesNotFlicker()
        {
            Assert.IsTrue(CameraNearHider.ShouldHide(false, 0.44f));
            Assert.IsFalse(CameraNearHider.ShouldHide(false, 0.5f));
            Assert.IsTrue(CameraNearHider.ShouldHide(true, 0.5f), "隠したあとは 0.6 m まで隠したまま");
            Assert.IsFalse(CameraNearHider.ShouldHide(true, 0.61f));
            Assert.Less(CameraNearHider.HideBelow, CameraNearHider.ShowAbove);
        }

        [Test]
        public void Apply_HidesTheBodyButKeepsItsShadow()
        {
            var hider = new CameraNearHider();
            hider.Apply(_root.transform, 0.3f);

            Assert.IsTrue(hider.IsHidden);
            Assert.AreEqual(ShadowCastingMode.ShadowsOnly, _body.shadowCastingMode, "影は残す");
            Assert.IsFalse(_body.forceRenderingOff);
            Assert.AreEqual(ShadowCastingMode.Off, _outline.shadowCastingMode);
            Assert.IsTrue(_outline.forceRenderingOff, "影を落とさない輪郭は消す");
            Assert.AreEqual(ShadowCastingMode.ShadowsOnly, _inactive.shadowCastingMode, "選んでいない体も同じ扱い");
        }

        [Test]
        public void Apply_RestoresTheOriginalSettingsWhenTheCameraBacksOff()
        {
            var hider = new CameraNearHider();
            hider.Apply(_root.transform, 0.3f);
            hider.Apply(_root.transform, 0.5f);
            Assert.IsTrue(hider.IsHidden);

            hider.Apply(_root.transform, 0.7f);
            Assert.IsFalse(hider.IsHidden);
            Assert.AreEqual(ShadowCastingMode.On, _body.shadowCastingMode);
            Assert.AreEqual(ShadowCastingMode.Off, _outline.shadowCastingMode);
            Assert.IsFalse(_outline.forceRenderingOff);
            Assert.AreEqual(ShadowCastingMode.TwoSided, _inactive.shadowCastingMode);
        }

        [Test]
        public void Apply_WithoutARootShowsAgain()
        {
            var hider = new CameraNearHider();
            hider.Apply(_root.transform, 0.1f);
            hider.Apply(null, 0.1f);

            Assert.IsFalse(hider.IsHidden);
            Assert.AreEqual(ShadowCastingMode.On, _body.shadowCastingMode);
        }

        [Test]
        public void Show_SkipsRenderersThatWereDestroyed()
        {
            var hider = new CameraNearHider();
            hider.Hide(_root.transform);
            Object.DestroyImmediate(_outline.gameObject);

            Assert.DoesNotThrow(() => hider.Show());
            Assert.IsFalse(hider.IsHidden);
            Assert.AreEqual(ShadowCastingMode.On, _body.shadowCastingMode);
        }

        private MeshRenderer Child(string name, ShadowCastingMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = mode;
            return renderer;
        }
    }
}
