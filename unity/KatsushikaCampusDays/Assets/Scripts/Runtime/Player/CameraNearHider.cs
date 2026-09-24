using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD
{
    /// <summary>
    /// カメラが壁に押されてプレイヤーのすぐ後ろまで寄ったとき、プレイヤーの体を隠す (#12)。
    /// 頭の裏側で画面が埋まるのを避ける。影を落としている Renderer は影だけ残し（ShadowsOnly）、
    /// 影を落としていないもの（輪郭シェルなど）は forceRenderingOff で消す。戻すときは元の設定に戻す。
    /// 近さの判定には行き来を抑える幅（<see cref="HideBelow"/> と <see cref="ShowAbove"/>）を持たせる。
    /// </summary>
    public sealed class CameraNearHider
    {
        /// <summary>見る点からカメラまでがこれより近くなったら隠す（m）。</summary>
        public const float HideBelow = 0.45f;

        /// <summary>隠したあと、これより離れたら出す（m）。</summary>
        public const float ShowAbove = 0.6f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<ShadowCastingMode> _modes = new List<ShadowCastingMode>();
        private readonly List<bool> _forcedOff = new List<bool>();

        /// <summary>いま隠しているか。</summary>
        public bool IsHidden { get; private set; }

        /// <summary>カメラまでの距離から、隠すべきかを決める（行き来を抑える幅つき）。</summary>
        public static bool ShouldHide(bool hidden, float distance)
        {
            return hidden ? distance < ShowAbove : distance < HideBelow;
        }

        /// <summary>距離に合わせて root の下の体を隠すか出す。root が null なら出すだけ。</summary>
        public void Apply(Transform root, float distance)
        {
            bool hide = root != null && ShouldHide(IsHidden, distance);
            if (hide && !IsHidden)
            {
                Hide(root);
            }
            else if (!hide && IsHidden)
            {
                Show();
            }
        }

        /// <summary>root の下の Renderer を隠す。隠す直前の設定を覚えておく。</summary>
        public void Hide(Transform root)
        {
            if (IsHidden)
            {
                Show();
            }

            if (root == null)
            {
                return;
            }

            root.GetComponentsInChildren(true, _renderers);
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer renderer = _renderers[i];
                _modes.Add(renderer.shadowCastingMode);
                _forcedOff.Add(renderer.forceRenderingOff);
                if (renderer.shadowCastingMode == ShadowCastingMode.Off)
                {
                    renderer.forceRenderingOff = true;
                }
                else
                {
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }

            IsHidden = true;
        }

        /// <summary>隠した Renderer を元に戻す。消えた Renderer は飛ばす。</summary>
        public void Show()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = _modes[i];
                renderer.forceRenderingOff = _forcedOff[i];
            }

            _renderers.Clear();
            _modes.Clear();
            _forcedOff.Clear();
            IsHidden = false;
        }
    }
}
