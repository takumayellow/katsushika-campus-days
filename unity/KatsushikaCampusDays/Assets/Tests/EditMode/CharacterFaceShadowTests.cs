using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 顔の陰を頭の向きで決める KCD/Toon の _FACE_SHADOW_ON (#14) を守る。
    ///
    /// 式（FaceShading.Lit。シェーダの FaceLit と同じ）の性質と、FBX の顔の頂点に頭の前方向と左右の位置が
    /// 焼かれていること、顔の .mat にキーワードが付いていることを確かめる。
    /// </summary>
    public sealed class CharacterFaceShadowTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

        private static readonly Vector3 Forward = Vector3.forward;

        private static float Lit(float side, Vector3 light)
        {
            return FaceShading.Lit(Forward, side, light.normalized, FaceShading.Softness, FaceShading.Bias);
        }

        [Test]
        public void 正面の光では顔の全体が明るい()
        {
            for (float side = -FaceShading.EdgeSine; side <= FaceShading.EdgeSine; side += 0.08f)
            {
                Assert.AreEqual(1f, Lit(side, new Vector3(0f, 0.5f, 1f)), 1e-4f, "side " + side);
            }
        }

        [Test]
        public void 真後ろの光では顔の全体が陰になる()
        {
            for (float side = -FaceShading.EdgeSine; side <= FaceShading.EdgeSine; side += 0.08f)
            {
                Assert.AreEqual(0f, Lit(side, new Vector3(0f, 0.5f, -1f)), 1e-4f, "side " + side);
            }
        }

        [Test]
        public void 真上の光では顔の全体が明るい()
        {
            for (float side = -1f; side <= 1f; side += 0.1f)
            {
                Assert.AreEqual(1f, Lit(side, Vector3.up), 1e-4f, "side " + side);
            }
        }

        /// <summary>真横の光では、境目が顔の中央から Bias だけ陰の側に寄る（鼻筋が光の側に入る）。</summary>
        [Test]
        public void 真横の光では顔の中央の少し陰の側が境目になる()
        {
            Vector3 fromRight = Vector3.right;
            Assert.AreEqual(1f, Lit(0.3f, fromRight), 1e-4f, "光の側の頬が明るくない");
            Assert.AreEqual(1f, Lit(0f, fromRight), 1e-4f, "顔の中央が陰になった");
            Assert.AreEqual(0f, Lit(-0.3f, fromRight), 1e-4f, "光と反対の頬が陰になっていない");
            Assert.AreEqual(0.5f, Lit(-FaceShading.Bias, fromRight), 0.01f, "境目が中央から Bias の位置に無い");
        }

        [Test]
        public void 斜めの光では光と反対の頬の縁にだけ陰が残る()
        {
            var light = new Vector3(1f, 0.4f, 1f);
            Assert.AreEqual(1f, Lit(0f, light), 1e-4f, "斜めの光で顔の中央が陰になった");
            Assert.AreEqual(1f, Lit(0.9f, light), 1e-4f);
            Assert.AreEqual(0f, Lit(-0.96f, light), 1e-4f, "光と反対の頬の縁に陰が残らない");
        }

        [Test]
        public void 左右の光で陰が鏡に映したように付く()
        {
            var right = new Vector3(0.8f, 0.3f, 0.2f);
            var left = new Vector3(-0.8f, 0.3f, 0.2f);
            for (float side = -1f; side <= 1f; side += 0.05f)
            {
                Assert.AreEqual(Lit(side, right), Lit(-side, left), 1e-4f, "side " + side);
            }
        }

        [Test]
        public void 頭を回すと陰も一緒に回る()
        {
            Quaternion turn = Quaternion.Euler(0f, 70f, 0f);
            Vector3 light = new Vector3(0.6f, 0.3f, 0.5f).normalized;
            for (float side = -1f; side <= 1f; side += 0.1f)
            {
                float a = FaceShading.Lit(Forward, side, light, FaceShading.Softness, FaceShading.Bias);
                float b = FaceShading.Lit(turn * Forward, side, turn * light, FaceShading.Softness, FaceShading.Bias);
                Assert.AreEqual(a, b, 1e-4f, "side " + side);
            }
        }

        [Test]
        public void 顔の左右の位置は縁で_EdgeSine_になる()
        {
            Assert.AreEqual(FaceShading.EdgeSine, FaceShading.Side(0.1f, 0f, 0.1f), 1e-6f);
            Assert.AreEqual(-FaceShading.EdgeSine, FaceShading.Side(-0.1f, 0f, 0.1f), 1e-6f);
            Assert.AreEqual(1f, FaceShading.Side(1f, 0f, 0.1f), 1e-6f);
            Assert.AreEqual(0f, FaceShading.Side(0.3f, 0f, 0f), 1e-6f);
        }

        [Test]
        public void FBX_の顔の頂点に頭の向きと左右の位置が焼かれている()
        {
            var bad = new List<string>();
            int faces = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx"))
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path + " を読めない");
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal) || renderer.sharedMesh == null)
                    {
                        continue;
                    }

                    string problem = CheckFace(model.transform, renderer, ref faces);
                    if (problem != null)
                    {
                        bad.Add(model.name + "/" + renderer.name + ": " + problem);
                    }
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            Assert.Greater(faces, 0, "顔の頂点に頭の向きを焼いたメッシュが 1 つも無い");
        }

        private static string CheckFace(Transform root, SkinnedMeshRenderer renderer, ref int faces)
        {
            Mesh mesh = renderer.sharedMesh;
            Vector4[] tangents = mesh.tangents;
            var tagged = new List<int>();
            for (int i = 0; i < tangents.Length; i++)
            {
                if (tangents[i].w >= FaceShading.FrameTag && tangents[i].w <= FaceShading.FrameTag + 1f)
                {
                    tagged.Add(i);
                }
            }

            if (tagged.Count == 0)
            {
                return null;
            }

            faces++;
            var sides = new List<Vector2>();
            mesh.GetUVs(FaceShading.SideUvChannel, sides);
            if (sides.Count != mesh.vertexCount)
            {
                return "UV の 2 枚目が無い";
            }

            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            float minSide = 1f;
            float maxSide = -1f;
            foreach (int i in tagged)
            {
                Vector3 forward = toRoot.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));
                if (Vector3.Dot(forward.normalized, Vector3.forward) < 0.999f)
                {
                    return string.Format("頂点 {0} の頭の前方向が根元の空間で {1}", i, forward);
                }

                float side = sides[i].x;
                if (side < -1f || side > 1f)
                {
                    return string.Format("頂点 {0} の左右の位置が {1}", i, side);
                }

                minSide = Mathf.Min(minSide, side);
                maxSide = Mathf.Max(maxSide, side);
            }

            // 左右の端は ±EdgeSine（はみ出しは ±1 で止める）。片側に寄っていたら左右の位置が焼けていない
            if (minSide > -0.9f || maxSide < 0.9f)
            {
                return string.Format("顔の左右の位置が {0:F2}〜{1:F2} しか無い", minSide, maxSide);
            }

            return CheckBlendShapes(mesh, tagged);
        }

        /// <summary>表情のブレンドシェイプが顔の頂点の tangent（頭の前方向）を動かさないこと。</summary>
        private static string CheckBlendShapes(Mesh mesh, List<int> tagged)
        {
            var dv = new Vector3[mesh.vertexCount];
            var dn = new Vector3[mesh.vertexCount];
            var dt = new Vector3[mesh.vertexCount];
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    foreach (int i in tagged)
                    {
                        if (dt[i].sqrMagnitude > 1e-8f)
                        {
                            return string.Format("ブレンドシェイプ {0} が顔の頂点 {1} の tangent を {2} 動かす",
                                mesh.GetBlendShapeName(s), i, dt[i]);
                        }
                    }
                }
            }

            return null;
        }

        [Test]
        public void 顔の材質だけに顔の陰のキーワードが付いている()
        {
            string folder = Path.Combine(Application.dataPath, "Materials", "Characters");
            int checkedCount = 0;
            foreach (string mat in Directory.GetFiles(folder, "*_face.mat"))
            {
                StringAssert.Contains(FaceShading.Keyword, File.ReadAllText(mat), Path.GetFileName(mat) + " に顔の陰のキーワードが無い");
                checkedCount++;
            }

            foreach (string mat in Directory.GetFiles(folder, "*_eye*.mat"))
            {
                StringAssert.DoesNotContain(FaceShading.Keyword, File.ReadAllText(mat), Path.GetFileName(mat));
            }

            Assert.Greater(checkedCount, 0, "顔の .mat が見つからない");
        }
    }
}
