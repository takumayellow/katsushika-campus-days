using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの輪郭線の殻の向きが、FBX の tangent に入っていることを守る (#47)。
    ///
    /// KCD/Toon の Outline パスは、tangent の w が 2〜3 の頂点を tangent の向きへ押し出す。
    /// w は 2 + (1 − 輪郭線の太さの倍率) で、鼻の頂点だけ 2 より大きい（殻を細くする）。
    /// 向きは Blender が頂点カラー outline_normal に書き、CharacterImporter.BakeOutlineNormals が
    /// tangent に移す。陰の法線のまま押すと、スカートのヒダの谷ごとに殻が折れ返って黒い破線が出る。
    ///
    /// 殻の向きは角をならした向きなので、角を立てて陰を付けるまつ毛・眉・靴などでは陰の法線と
    /// 40〜60° ずれる（内積 0.5〜0.75）。そこで一致の度合いではなく、軸の取り違えを見分ける。
    /// 軸の並びか向きを 1 つでも取り違えると、mirai では内積の平均が 0.49 以下に下がる。
    /// 2026-09-30 の 7 体の実測は、平均 0.936〜0.984、内側を向く頂点 0.34% 以下。
    /// </summary>
    public sealed class CharacterOutlineNormalTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

        /// <summary>CharacterImporter.OutlineNormalTag（テストのアセンブリは KCD.Editor を参照しない）。</summary>
        private const float OutlineNormalTag = 2f;

        /// <summary>殻の向きと陰の法線の内積の、メッシュ全体での平均の下限。</summary>
        private const float MinMeanDot = 0.9f;

        /// <summary>殻の向きが陰の法線と同じ側（内積が正）を向く頂点の、最低限の割合。</summary>
        private const float MinOutwardShare = 0.99f;

        /// <summary>輪郭線を細くした頂点と見なす w（太さの倍率 0.5 未満。鼻の NOSE_OUTLINE_WIDTH は 0.3）。</summary>
        private const float ThinOutlineW = OutlineNormalTag + 0.5f;

        [Test]
        public void 輪郭線の殻の向きが_tangent_に入っている()
        {
            var bad = new List<string>();
            int checkedCount = 0;
            int thinCount = 0;

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
                    if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string problem = Check(renderer.sharedMesh);
                    checkedCount++;
                    thinCount += CountThin(renderer.sharedMesh);
                    if (problem != null)
                    {
                        bad.Add(model.name + "/" + renderer.name + ": " + problem);
                    }
                }
            }

            Assert.Greater(checkedCount, 0, "キャラの FBX が見つからない");
            Assert.IsEmpty(bad, string.Join("\n", bad));
            // 鼻の頂点（kcd_chara/body.py の NOSE_OUTLINE_WIDTH）。0 なら頂点カラーの alpha が FBX で落ちている
            Assert.Greater(thinCount, 0, "輪郭線を細くした頂点が 1 つも無い");
        }

        private static int CountThin(Mesh mesh)
        {
            int count = 0;
            foreach (Vector4 t in mesh.tangents)
            {
                count += t.w > ThinOutlineW ? 1 : 0;
            }

            return count;
        }

        private static string Check(Mesh mesh)
        {
            Vector4[] tangents = mesh.tangents;
            Vector3[] normals = mesh.normals;
            if (tangents.Length != mesh.vertexCount || normals.Length != mesh.vertexCount)
            {
                return "tangent か法線が無い";
            }

            if (mesh.colors.Length != 0)
            {
                return "頂点カラーが残っている";
            }

            double dotSum = 0;
            int outward = 0;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector4 t = tangents[i];
                var outline = new Vector3(t.x, t.y, t.z);
                if (t.w < OutlineNormalTag || t.w > OutlineNormalTag + 1f || Mathf.Abs(outline.magnitude - 1f) > 1e-3f)
                {
                    return string.Format("頂点 {0} の tangent が {1}", i, t);
                }

                float dot = Vector3.Dot(outline, normals[i]);
                dotSum += dot;
                outward += dot > 0f ? 1 : 0;
            }

            double meanDot = dotSum / tangents.Length;
            float outwardShare = (float)outward / tangents.Length;
            if (meanDot < MinMeanDot)
            {
                return string.Format("殻の向きと陰の法線の内積の平均が {0:F3}", meanDot);
            }

            return outwardShare >= MinOutwardShare
                ? null
                : string.Format("殻の向きが陰の法線と同じ側を向く頂点が {0:P1} しかない", outwardShare);
        }
    }
}
