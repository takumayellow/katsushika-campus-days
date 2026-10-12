using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// キャンパスの建物は両面で影を落とす (#229)。
    ///
    /// 建物は build_campus.py が角柱（add_prism）を並べて作り、角で面ごとに法線が分かれている。
    /// 影を落とすときは頂点を法線の逆へ押す（URP の normal bias）ので、片面だけで落とすと角で壁と壁が離れ、
    /// 影の中に光の筋が通る。正門前では背景ビル（bld_background）の角から路面に白い斜めの線が出ていた。
    /// 裏の面も影に描けば、その隙間がふさがる。
    /// </summary>
    public sealed class CampusBuildingShadowTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        [Test]
        public void キャンパスの建物はすべて両面で影を落とす()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var buildings = new List<MeshRenderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (renderer.gameObject.name.StartsWith("bld", System.StringComparison.OrdinalIgnoreCase))
                        {
                            buildings.Add(renderer);
                        }
                    }
                }

                Assert.IsTrue(buildings.Exists(r => r.gameObject.name == "bld_background"),
                    ScenePath + " に bld_background が無い");
                foreach (MeshRenderer renderer in buildings)
                {
                    Assert.AreEqual(ShadowCastingMode.TwoSided, renderer.shadowCastingMode,
                        renderer.gameObject.name + " が片面でしか影を落とさない（角の継ぎ目から光が漏れる）");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
