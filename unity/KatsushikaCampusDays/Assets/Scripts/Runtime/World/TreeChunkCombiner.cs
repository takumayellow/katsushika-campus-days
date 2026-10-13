using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD
{
    /// <summary>
    /// キャンパスの木を、格子のマスごとに 1 つのメッシュへまとめて描く (#57, #70)。
    ///
    /// 木は 553 本あり、1 本がマテリアル（幹と葉, #51）× 2 パス（トゥーンと輪郭）で描かれる。葉を 4 マテリアルに
    /// 分けていたころ、1 本ずつ描くと WebGL で描画コールが約 4,700 回になり、CPU 側で 35〜45 fps まで落ちた。SRP Batcher は描画コールを減らさず、
    /// 静的バッチも効いていなかった。マスごとにまとめると、描画コールは「見えているマスの数 × マテリアルの数 × パスの数」になる。
    ///
    /// シーンには 1 本ずつの MeshRenderer を置いたままにし（エディタのプレビューとテストはそれを見る）、
    /// 再生を始めたときにまとめて、元の MeshRenderer は止める。まとめたメッシュをアセットにしないのは、
    /// 頂点が 20 万を超えて git と Web 版のダウンロードが太るため。原型のメッシュは Read/Write を有効にしておく。
    /// </summary>
    public sealed class TreeChunkCombiner : MonoBehaviour
    {
        /// <summary>まとめたメッシュの GameObject の名前の頭。</summary>
        public const string ChunkPrefix = "tree_chunk_";

        [Tooltip("まとめる格子の 1 辺 [m]。大きいほど描画コールが減り、画面外の木を省く粒度は粗くなる。")]
        [SerializeField] private float cellSize = 128f;

        private readonly List<GameObject> _chunks = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private bool _combined;

        public float CellSize
        {
            get => cellSize;
            set => cellSize = value;
        }

        /// <summary>まとめたメッシュの GameObject（MeshFilter と MeshRenderer を持つ）。まとめる前は空。</summary>
        public IReadOnlyList<GameObject> Chunks => _chunks;

        private void Awake()
        {
            Combine();
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in _meshes)
            {
                DestroyOwned(mesh);
            }

            _meshes.Clear();
        }

        /// <summary>
        /// 子の MeshRenderer をマスごとにまとめ、元の MeshRenderer を止める。2 回目以降は何もしない。
        /// マテリアルの並び・サブメッシュの数・頂点の属性がそろわないもの、読めないメッシュはまとめずに残す。
        /// </summary>
        /// <returns>まとめたメッシュの数。</returns>
        public int Combine()
        {
            if (_combined)
            {
                return _chunks.Count;
            }

            _combined = true;
            var groups = new Dictionary<string, List<MeshRenderer>>();
            var order = new List<string>();
            var unmerged = new List<string>();
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled || !renderer.TryGetComponent(out MeshFilter filter))
                {
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.subMeshCount != renderer.sharedMaterials.Length)
                {
                    unmerged.Add(renderer.name);
                    continue;
                }

                string key = GroupKey(renderer, mesh);
                if (!groups.TryGetValue(key, out List<MeshRenderer> members))
                {
                    members = new List<MeshRenderer>();
                    groups.Add(key, members);
                    order.Add(key);
                }

                members.Add(renderer);
            }

            if (unmerged.Count > 0)
            {
                // ビルドした版で Read/Write が切れると、木がすべて 1 本ずつ描かれて重くなる。黙って見逃さない。
                Debug.LogWarning(string.Format(
                    "TreeChunkCombiner: {0} 個はメッシュが読めないか、サブメッシュとマテリアルの数が合わず、まとめずに 1 つずつ描く（{1}）",
                    unmerged.Count, string.Join(", ", unmerged.GetRange(0, Mathf.Min(unmerged.Count, 5)))), this);
            }

            var chunksPerCell = new Dictionary<Vector2Int, int>();
            foreach (string key in order)
            {
                List<MeshRenderer> members = groups[key];
                Vector2Int cell = Cell(members[0]);
                chunksPerCell.TryGetValue(cell, out int index);
                chunksPerCell[cell] = index + 1;
                CombineGroup(members, ChunkName(cell, index));
            }

            return _chunks.Count;
        }

        private Vector2Int Cell(MeshRenderer renderer)
        {
            Vector3 p = renderer.bounds.center;
            float size = Mathf.Max(cellSize, 1f);
            return new Vector2Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.z / size));
        }

        /// <summary>マスに 2 つ目以降のまとまり（影やマテリアルの違うもの）があれば、名前の後ろに番号を付けて分ける。</summary>
        private static string ChunkName(Vector2Int cell, int index)
        {
            string name = string.Format("{0}{1}_{2}", ChunkPrefix, cell.x, cell.y);
            return index > 0 ? name + "_" + index : name;
        }

        /// <summary>マス・マテリアルの並び・影の設定・頂点の属性が同じものを 1 つにまとめる。</summary>
        private string GroupKey(MeshRenderer renderer, Mesh mesh)
        {
            Vector2Int cell = Cell(renderer);
            var key = new System.Text.StringBuilder();
            key.Append(cell.x).Append(',').Append(cell.y);
            key.Append('|').Append((int)renderer.shadowCastingMode).Append(renderer.receiveShadows ? 'r' : '-');
            key.Append(renderer.gameObject.layer);
            foreach (Material material in renderer.sharedMaterials)
            {
                key.Append('|').Append(material != null ? material.GetEntityId().ToString() : "-");
            }

            foreach (VertexAttributeDescriptor attribute in mesh.GetVertexAttributes())
            {
                key.Append('|').Append((int)attribute.attribute).Append('x').Append(attribute.dimension);
            }

            return key.ToString();
        }

        private void CombineGroup(List<MeshRenderer> members, string name)
        {
            MeshRenderer first = members[0];
            var chunk = new GameObject(name);
            chunk.layer = first.gameObject.layer;
            chunk.transform.SetParent(transform, false);

            Mesh mesh;
            try
            {
                mesh = MergeMeshes(members, chunk.transform.worldToLocalMatrix);
            }
            catch (System.Exception e)
            {
                // まとめ損ねたマスは元の木を 1 本ずつ描いたままにする。ほかのマスはまとめ続ける。
                DestroyOwned(chunk);
                Debug.LogWarning(string.Format(
                    "TreeChunkCombiner: {0} をまとめられず、{1} 本を 1 本ずつ描く: {2}", name, members.Count, e.Message), this);
                return;
            }

            mesh.name = chunk.name;
            _meshes.Add(mesh);

            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer target = chunk.AddComponent<MeshRenderer>();
            target.sharedMaterials = first.sharedMaterials;
            target.shadowCastingMode = first.shadowCastingMode;
            target.receiveShadows = first.receiveShadows;
            target.lightProbeUsage = first.lightProbeUsage;
            target.reflectionProbeUsage = first.reflectionProbeUsage;
            _chunks.Add(chunk);

            foreach (MeshRenderer renderer in members)
            {
                renderer.enabled = false;
            }
        }

        /// <summary>
        /// 頂点をまとめ先の座標に移して 1 つのメッシュにする。サブメッシュ（マテリアル）ごとの三角形は分けたまま並べる。
        /// Mesh.CombineMeshes はサブメッシュを指定しても元の頂点を全部写すので使わない（頂点がサブメッシュの数倍になる）。
        /// </summary>
        private static Mesh MergeMeshes(List<MeshRenderer> members, Matrix4x4 toChunk)
        {
            Mesh sample = members[0].GetComponent<MeshFilter>().sharedMesh;
            int subMeshCount = sample.subMeshCount;
            bool hasNormals = sample.HasVertexAttribute(VertexAttribute.Normal);
            bool hasTangents = sample.HasVertexAttribute(VertexAttribute.Tangent);
            bool hasColors = sample.HasVertexAttribute(VertexAttribute.Color);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color32>();
            var uvs = new List<Vector4>[8];
            var triangles = new List<int>[subMeshCount];
            for (int s = 0; s < subMeshCount; s++)
            {
                triangles[s] = new List<int>();
            }

            for (int channel = 0; channel < uvs.Length; channel++)
            {
                if (sample.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
                {
                    uvs[channel] = new List<Vector4>();
                }
            }

            var sourceVertices = new List<Vector3>();
            var sourceNormals = new List<Vector3>();
            var sourceTangents = new List<Vector4>();
            var sourceColors = new List<Color32>();
            var sourceUvs = new List<Vector4>();
            var sourceTriangles = new List<int>();
            foreach (MeshRenderer renderer in members)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 m = toChunk * renderer.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = m.inverse.transpose;
                bool mirrored = m.determinant < 0f;
                int offset = vertices.Count;

                mesh.GetVertices(sourceVertices);
                foreach (Vector3 v in sourceVertices)
                {
                    vertices.Add(m.MultiplyPoint3x4(v));
                }

                if (hasNormals)
                {
                    mesh.GetNormals(sourceNormals);
                    foreach (Vector3 n in sourceNormals)
                    {
                        normals.Add(normalMatrix.MultiplyVector(n).normalized);
                    }
                }

                if (hasTangents)
                {
                    mesh.GetTangents(sourceTangents);
                    foreach (Vector4 t in sourceTangents)
                    {
                        Vector3 direction = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                        tangents.Add(new Vector4(direction.x, direction.y, direction.z, mirrored ? -t.w : t.w));
                    }
                }

                if (hasColors)
                {
                    mesh.GetColors(sourceColors);
                    colors.AddRange(sourceColors);
                }

                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] != null)
                    {
                        mesh.GetUVs(channel, sourceUvs);
                        uvs[channel].AddRange(sourceUvs);
                    }
                }

                for (int s = 0; s < subMeshCount; s++)
                {
                    mesh.GetTriangles(sourceTriangles, s);
                    for (int i = 0; i + 2 < sourceTriangles.Count; i += 3)
                    {
                        // 鏡に映した向きで置かれた木は、三角形の巡る向きを戻す（裏を向いて消えないように）。
                        int b = sourceTriangles[i + 1];
                        int c = sourceTriangles[i + 2];
                        triangles[s].Add(offset + sourceTriangles[i]);
                        triangles[s].Add(offset + (mirrored ? c : b));
                        triangles[s].Add(offset + (mirrored ? b : c));
                    }
                }
            }

            var merged = new Mesh
            {
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            merged.SetVertices(vertices);
            if (hasNormals)
            {
                merged.SetNormals(normals);
            }

            if (hasTangents)
            {
                merged.SetTangents(tangents);
            }

            if (hasColors)
            {
                merged.SetColors(colors);
            }

            for (int channel = 0; channel < uvs.Length; channel++)
            {
                if (uvs[channel] != null)
                {
                    SetUvs(merged, sample, channel, uvs[channel]);
                }
            }

            merged.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                merged.SetTriangles(triangles[s], s, false);
            }

            merged.RecalculateBounds();
            // 実行時は GPU に上げたあと CPU 側の写しを捨てる。エディタのテストは中身を読むので残す。
            merged.UploadMeshData(Application.isPlaying);
            return merged;
        }

        /// <summary>
        /// UV は元のメッシュと同じ次元で入れる（2 次元の UV を 4 次元で持つと頂点が太る）。
        /// Mesh.SetUVs は 1 次元を受け取らないので、1 次元の UV は 2 次元で入れる。
        /// </summary>
        private static void SetUvs(Mesh merged, Mesh sample, int channel, List<Vector4> values)
        {
            int dimension = sample.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel);
            if (dimension <= 2)
            {
                var uv2 = new List<Vector2>(values.Count);
                foreach (Vector4 v in values)
                {
                    uv2.Add(new Vector2(v.x, v.y));
                }

                merged.SetUVs(channel, uv2);
            }
            else if (dimension == 3)
            {
                var uv3 = new List<Vector3>(values.Count);
                foreach (Vector4 v in values)
                {
                    uv3.Add(new Vector3(v.x, v.y, v.z));
                }

                merged.SetUVs(channel, uv3);
            }
            else
            {
                merged.SetUVs(channel, values);
            }
        }

        private static void DestroyOwned(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
