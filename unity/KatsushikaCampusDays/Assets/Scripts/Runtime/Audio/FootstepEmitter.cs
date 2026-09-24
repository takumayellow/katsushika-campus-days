using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 足音・ジャンプ・着地。歩いた距離で足音を刻み、足元のマテリアル名から床の種類を決める。
    /// AudioManager がプレイヤーに付ける。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        [SerializeField] private float _walkStride = 1.15f;
        [SerializeField] private float _runStride = 1.55f;
        [SerializeField] private float _probeHeight = 0.5f;
        [SerializeField] private float _probeDepth = 1.6f;

        private PlayerController _player;
        private float _travelled;
        private string _surface = "concrete";
        private float _nextProbeAt;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            _player.Jumped += OnJumped;
            _player.Landed += OnLanded;
        }

        private void OnDisable()
        {
            _player.Jumped -= OnJumped;
            _player.Landed -= OnLanded;
        }

        private void Update()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio == null || !_player.IsGrounded)
            {
                return;
            }

            float speed = _player.PlanarSpeed;
            if (speed < 0.4f)
            {
                _travelled = 0f;
                return;
            }

            if (Time.time >= _nextProbeAt)
            {
                _surface = ProbeSurface();
                _nextProbeAt = Time.time + 0.4f;
            }

            _travelled += speed * Time.deltaTime;
            float stride = _player.IsRunning ? _runStride : _walkStride;
            if (_travelled >= stride)
            {
                _travelled -= stride;
                audio.PlayFootstep(_surface, _player.IsRunning);
            }
        }

        private void OnJumped()
        {
            AudioManager.Instance?.PlaySe("jump", 0.7f);
        }

        private void OnLanded()
        {
            AudioManager.Instance?.PlaySe("land", 0.8f);
            _travelled = 0f;
        }

        /// <summary>足元の描画物のマテリアル名から床を分類する。</summary>
        private string ProbeSurface()
        {
            Vector3 origin = transform.position + Vector3.up * _probeHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _probeDepth, ~0, QueryTriggerInteraction.Ignore))
            {
                return _surface;
            }

            Renderer renderer = hit.collider.GetComponent<Renderer>();
            Material hitMaterial = renderer != null ? MaterialAt(hit, renderer) : null;
            string material = hitMaterial != null
                ? hitMaterial.name.ToLowerInvariant()
                : string.Empty;
            string objectName = hit.collider.name.ToLowerInvariant();
            return Classify(material, objectName);
        }

        /// <summary>
        /// レイが当たった三角形のマテリアル。屋内は床・壁・段をマテリアルごとのサブメッシュで 1 つのメッシュに
        /// まとめているので（共創棟 2F のカーペットは wall_kyoso の中）、先頭のマテリアルだけでは床を取り違える（#28）。
        /// サブメッシュが決まらないときは先頭のマテリアルで代える。
        /// </summary>
        private static Material MaterialAt(RaycastHit hit, Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            var meshCollider = hit.collider as MeshCollider;
            Mesh mesh = meshCollider != null ? meshCollider.sharedMesh : null;

            int subMesh = SubMeshOfTriangle(mesh, materials.Length, hit.triangleIndex);
            if (subMesh >= 0 && materials[subMesh] != null)
            {
                return materials[subMesh];
            }

            return renderer.sharedMaterial;
        }

        /// <summary>
        /// 当たり判定メッシュの中で、当たった三角形が何番目のサブメッシュに入るか。決まらなければ -1。
        /// 決まらないのは次のとき。
        /// ・メッシュが無い（BoxCollider などメッシュでない当たり判定）
        /// ・マテリアルが 1 つしかない（サブメッシュを引く意味が無い）
        /// ・サブメッシュの数とマテリアルの数が合わない
        ///   （葉を落とした当たり判定用メッシュ #30 はサブメッシュが 1 つにまとめられている）
        /// ・三角形でないサブメッシュが混ざる（triangleIndex の数え方が合わなくなる）
        /// </summary>
        public static int SubMeshOfTriangle(Mesh mesh, int materialCount, int triangleIndex)
        {
            if (mesh == null || triangleIndex < 0 || materialCount <= 1 || mesh.subMeshCount != materialCount)
            {
                return -1;
            }

            var indexCounts = new int[mesh.subMeshCount];
            for (int i = 0; i < indexCounts.Length; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles)
                {
                    return -1;
                }

                indexCounts[i] = (int)mesh.GetIndexCount(i);
            }

            return SubMeshOfTriangle(triangleIndex, indexCounts);
        }

        /// <summary>
        /// 当たり判定メッシュとマテリアル名の並びから床の種類。サブメッシュが決まらないときは
        /// 先頭のマテリアル名で代える（ProbeSurface が Renderer.sharedMaterial に落ちるのと同じ扱い）。
        /// </summary>
        public static string SurfaceOfTriangle(Mesh mesh, IReadOnlyList<string> materialNames,
                                               int triangleIndex, string objectName)
        {
            string material = string.Empty;
            if (materialNames != null && materialNames.Count > 0)
            {
                int subMesh = SubMeshOfTriangle(mesh, materialNames.Count, triangleIndex);
                string name = materialNames[subMesh >= 0 ? subMesh : 0];
                material = name != null ? name.ToLowerInvariant() : string.Empty;
            }

            return Classify(material, objectName != null ? objectName.ToLowerInvariant() : string.Empty);
        }

        /// <summary>
        /// 三角形の通し番号（RaycastHit.triangleIndex）が何番目のサブメッシュに入るか。
        /// 当たり判定の三角形はサブメッシュ順に並ぶので、各サブメッシュのインデックス数 / 3 を順に引いていく。範囲外なら -1。
        /// </summary>
        public static int SubMeshOfTriangle(int triangleIndex, IReadOnlyList<int> indexCounts)
        {
            if (triangleIndex < 0 || indexCounts == null)
            {
                return -1;
            }

            int remaining = triangleIndex;
            for (int i = 0; i < indexCounts.Count; i++)
            {
                int triangles = indexCounts[i] / 3;
                if (remaining < triangles)
                {
                    return i;
                }

                remaining -= triangles;
            }

            return -1;
        }

        /// <summary>マテリアル名と物の名前から床の種類（concrete / grass / tile / wood / carpet）。</summary>
        public static string Classify(string material, string objectName)
        {
            if (material.Contains("grass") || material.Contains("sand") || objectName.Contains("lawn"))
            {
                return "grass";
            }

            if (material.Contains("wood") || material.Contains("parquet") || material.Contains("gym_floor")
                || objectName.Contains("floor_gym"))
            {
                return "wood";
            }

            // floor_carpet_* は "floor" も含むので、タイルより先に見る。
            if (material.Contains("carpet"))
            {
                return "carpet";
            }

            if (material.Contains("tile") || material.Contains("floor")
                || material.Contains("lino") || objectName.StartsWith("floor_"))
            {
                return "tile";
            }

            return "concrete";
        }
    }
}
