using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 夜だけ灯る、加算合成の KCD/LampGlow で描く明かりの共通部分（街灯 <see cref="StreetLights"/> と
    /// 窓の明かり <see cref="WindowLights"/>）。
    ///
    /// 明るさは <see cref="SkyPalette"/> の LampGlow（18:00 から灯りはじめ、19:24 に全灯、6:00 に消灯）。
    /// シェーダの _Glow に入れ、昼はレンダラーごと止めて描画コールを出さない。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public abstract class LampGlowLights : MonoBehaviour
    {
        /// <summary>これより暗ければレンダラーを止める。</summary>
        public const float OffThreshold = 0.01f;

        public static readonly int GlowId = Shader.PropertyToID("_Glow");

        /// <summary>明るさがこれ以上変わったときだけ描き直す。</summary>
        private const float GlowStep = 0.002f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private DayNightCycle _cycle;
        private float _appliedGlow = -1f;

        /// <summary>その時刻の明るさ（0 = 消灯、1 = 全灯）。</summary>
        public static float GlowAt(float hours)
        {
            return SkyPalette.Evaluate(hours).LampGlow;
        }

        /// <summary>その明るさで描くかどうか。</summary>
        public static bool IsLit(float glow)
        {
            return glow > OffThreshold;
        }

        /// <summary>その時刻の明るさで描く（昼はレンダラーを止める）。</summary>
        public void ShowAt(float hours)
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<MeshRenderer>();
                _block = new MaterialPropertyBlock();
            }

            float glow = GlowAt(hours);
            if (Mathf.Abs(glow - _appliedGlow) < GlowStep)
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

        protected void Start()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            Apply();
        }

        protected void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            // 時計は DayNightCycle が持つ。無いシーン（プレビュー用の小さなシーンなど）では GameManager の時刻を見る。
            ShowAt(_cycle != null ? _cycle.Hours : GameManager.Instance.GameTimeHours);
        }
    }
}
