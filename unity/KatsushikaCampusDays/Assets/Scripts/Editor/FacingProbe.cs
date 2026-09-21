using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャラクター FBX の「顔がどちらを向いているか」をログに出す。
    /// 顔マテリアルの付いたサブメッシュの重心が、体全体の重心より +Z 側ならモデルは +Z を向いている。
    /// タイトルの立ち姿と歩行の向きを合わせるための物差し。
    /// </summary>
    public static class FacingProbe
    {
        public static void Report()
        {
            foreach (string characterId in GameManager.PlayableCharacterIds)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(ActorFactory.FbxPathOf(characterId));
                if (source == null)
                {
                    continue;
                }

                foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Probe(characterId, renderer);
                }
            }
        }

        private static void Probe(string characterId, SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            if (mesh == null || materials == null)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            Vector3 bodyCenter = Centroid(vertices, AllIndices(vertices.Length), toWorld);

            for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
            {
                Material material = materials[sub];
                if (material == null || !material.name.ToLowerInvariant().Contains("face"))
                {
                    continue;
                }

                Vector3 faceCenter = Centroid(vertices, mesh.GetIndices(sub), toWorld);
                Vector3 delta = faceCenter - bodyCenter;
                EditorPaths.Report(string.Format(
                    "FacingProbe {0}/{1}: face-body = ({2:F3}, {3:F3}, {4:F3}) -> 顔は {5} 側",
                    characterId, renderer.name, delta.x, delta.y, delta.z, delta.z >= 0f ? "+Z" : "-Z"));
            }
        }

        private static int[] AllIndices(int count)
        {
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }

            return indices;
        }

        private static Vector3 Centroid(Vector3[] vertices, int[] indices, Matrix4x4 toWorld)
        {
            if (indices.Length == 0)
            {
                return Vector3.zero;
            }

            var seen = new HashSet<int>();
            Vector3 sum = Vector3.zero;
            foreach (int index in indices)
            {
                if (index < vertices.Length && seen.Add(index))
                {
                    sum += toWorld.MultiplyPoint3x4(vertices[index]);
                }
            }

            return sum / Mathf.Max(1, seen.Count);
        }
    }
}
