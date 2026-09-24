using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// モールの照明柱の明かり (#8)。夕方から灯り、夜明けに消える。
    ///
    /// 実ライトは使わない。WebGL（Mobile_RPAsset）は Forward で 1 オブジェクトあたり追加ライト 4 灯までなので、
    /// 22 本の点光源を置くと、モール全体を覆う 1 枚の地面メッシュ（site_ground）には近い 4 灯しか当たらない。
    /// 代わりに、灯具の下の光だまり・灯具の発光面・まわりの暈を 1 つのメッシュにまとめ、加算合成の
    /// KCD/LampGlow で描く（StreetLampStage が作る）。描画コールは夜だけ 1 回、昼はレンダラーごと止める。
    /// 明るさは <see cref="SkyPalette"/> の LampGlow（18:00 から灯りはじめ、19:24 に全灯、6:00 に消灯）。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class StreetLights : MonoBehaviour
    {
        /// <summary>これより暗ければレンダラーを止める。</summary>
        public const float OffThreshold = 0.01f;

        public static readonly int GlowId = Shader.PropertyToID("_Glow");

        [SerializeField] private Vector3[] _lamps = new Vector3[0];

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private DayNightCycle _cycle;
        private float _appliedGlow = -1f;

        /// <summary>灯具（発光面の中心）のワールド座標。StreetLampStage が入れる。</summary>
        public IReadOnlyList<Vector3> Lamps => _lamps;

        /// <summary>シーン生成から灯具の位置を入れる。</summary>
        public void SetLamps(IReadOnlyList<Vector3> lamps)
        {
            var copy = new Vector3[lamps != null ? lamps.Count : 0];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = lamps[i];
            }

            _lamps = copy;
        }

        /// <summary>その時刻の街灯の明るさ（0 = 消灯、1 = 全灯）。</summary>
        public static float GlowAt(float hours)
        {
            return SkyPalette.Evaluate(hours).LampGlow;
        }

        /// <summary>その明るさで描くかどうか。</summary>
        public static bool IsLit(float glow)
        {
            return glow > OffThreshold;
        }

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            // 時計は DayNightCycle が持つ。無いシーン（プレビュー用の小さなシーンなど）では GameManager の時刻を見る。
            float hours = _cycle != null ? _cycle.Hours : GameManager.Instance.GameTimeHours;
            float glow = GlowAt(hours);
            if (Mathf.Abs(glow - _appliedGlow) < 0.002f)
            {
                return;
            }

            _appliedGlow = glow;
            bool lit = IsLit(glow);
            _renderer.enabled = lit;
            if (!lit)
            {
                return;
            }

            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(GlowId, glow);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
