using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画質の段が Low のとき, カメラから遠い木のまとまり（<see cref="TreeChunkCombiner"/>）を描かない (#70)。
    ///
    /// 木はまとめても約 104 回の描画コールがあり（#70 の実測 563 − 459）, 遠くのまとまりは霧で薄く見えるだけなので,
    /// 距離で切ると描画コールが減る。LOD の仕組み（LODGroup）は木に付いていないので, まとまりの MeshRenderer を
    /// 距離で止める。距離は <see cref="QualityManager"/> が段の値から入れ, 0 なら全部描く。
    /// シーンには置かない。QualityManager が木のまとまりを持つ GameObject に付ける。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreeChunkDistance : MonoBehaviour
    {
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<Bounds> _bounds = new List<Bounds>();
        private TreeChunkCombiner _combiner;
        private int _chunkCount = -1;
        private bool _anyHidden;

        /// <summary>木のまとまりを描く距離 [m]。0 以下は制限なし。</summary>
        public static float Limit { get; set; }

        /// <summary>ドメインを読み直さない再生でも, 前回の再生の距離を持ち越さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Limit = 0f;
        }

        /// <summary>箱が見る位置から制限の距離より遠いか。制限が 0 以下なら常に false。</summary>
        public static bool IsBeyond(Bounds bounds, Vector3 viewer, float limit)
        {
            return limit > 0f && bounds.SqrDistance(viewer) > limit * limit;
        }

        private void Awake()
        {
            _combiner = GetComponent<TreeChunkCombiner>();
        }

        private void Update()
        {
            Camera viewer = Camera.main;
            if (Limit <= 0f || viewer == null || _combiner == null)
            {
                ShowAll();
                return;
            }

            RefreshChunks();
            Vector3 position = viewer.transform.position;
            bool anyHidden = false;
            for (int i = 0; i < _renderers.Count; i++)
            {
                MeshRenderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                bool hide = IsBeyond(_bounds[i], position, Limit);
                if (renderer.forceRenderingOff != hide)
                {
                    renderer.forceRenderingOff = hide;
                }

                anyHidden |= hide;
            }

            _anyHidden = anyHidden;
        }

        private void OnDisable()
        {
            ShowAll();
        }

        /// <summary>まとまりの MeshRenderer と箱を拾い直す。まとまりの数が変わったときだけ。木は動かないので箱は固定。</summary>
        private void RefreshChunks()
        {
            IReadOnlyList<GameObject> chunks = _combiner.Chunks;
            if (chunks.Count == _chunkCount)
            {
                return;
            }

            ShowAll();
            _renderers.Clear();
            _bounds.Clear();
            foreach (GameObject chunk in chunks)
            {
                if (chunk != null && chunk.TryGetComponent(out MeshRenderer renderer))
                {
                    _renderers.Add(renderer);
                    _bounds.Add(renderer.bounds);
                }
            }

            _chunkCount = chunks.Count;
        }

        private void ShowAll()
        {
            if (!_anyHidden)
            {
                return;
            }

            foreach (MeshRenderer renderer in _renderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = false;
                }
            }

            _anyHidden = false;
        }
    }
}
