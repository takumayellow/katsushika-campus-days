using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの足の裏（メッシュの最下点）がモデルの原点の高さにあることを守る (#47)。
    ///
    /// ゲームではモデルの原点が地面になる。以前は坊っちゃんの高下駄が素足の底から下へ伸びていて、
    /// 最下点が原点より 10.45 cm 下にあり、ゲームでは下駄が地面にめり込んでいた。
    /// Blender のプレビューは床をメッシュの最下点に敷くので、この差はプレビューでは見えない。
    /// 今は blender/build_characters.py の lift_to_ground が、書き出す前に最下点を z=0 へ揃える。
    /// </summary>
    public sealed class CharacterGroundingTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

        /// <summary>めり込み・浮きをどこまで許すか [m]。</summary>
        private const float Tolerance = 0.005f;

        [Test]
        public void 足の裏がモデルの原点の高さにある()
        {
            var bad = new List<string>();
            int checkedCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx"))
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path + " を読めない");

                float lowest = float.PositiveInfinity;
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Matrix4x4 toRoot = model.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    foreach (Vector3 v in renderer.sharedMesh.vertices)
                    {
                        lowest = Mathf.Min(lowest, toRoot.MultiplyPoint3x4(v).y);
                    }
                }

                Assert.IsFalse(float.IsInfinity(lowest), path + " に本体のメッシュが無い");
                checkedCount++;
                if (Mathf.Abs(lowest) > Tolerance)
                {
                    bad.Add(string.Format("{0}: 最下点 {1:F4} m", model.name, lowest));
                }
            }

            Assert.Greater(checkedCount, 0, CharactersFolder + " にキャラの FBX が無い");
            Assert.IsEmpty(bad, "足の裏が地面の高さに無い（負ならめり込み、正なら浮き）:\n" + string.Join("\n", bad));
        }
    }
}
