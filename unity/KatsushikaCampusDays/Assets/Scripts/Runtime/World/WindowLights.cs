using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 夜に研究棟・実験棟の窓がぽつぽつ灯る明かり (#166)。研究室に残っている人の部屋だけが点いている。
    ///
    /// 街灯（<see cref="StreetLights"/>）と同じく実ライトは使わず、点ける窓のガラスの外側に貼った発光面を
    /// 1 つのメッシュにまとめ、加算合成の KCD/LampGlow で描く（WindowLightStage が作る）。
    /// 灯す時刻と明るさは街灯と同じ（<see cref="LampGlowLights"/>）。
    /// </summary>
    public sealed class WindowLights : LampGlowLights
    {
        [SerializeField] private Vector3[] _windows = new Vector3[0];
        [SerializeField] private int _paneCount;

        /// <summary>点ける窓（ガラスの中心）のワールド座標。WindowLightStage が入れる。</summary>
        public IReadOnlyList<Vector3> Windows => _windows;

        /// <summary>対象の建物の窓ガラスの枚数（点けない窓も含む）。</summary>
        public int PaneCount => _paneCount;

        /// <summary>シーン生成から点ける窓と、窓ガラスの総数を入れる。</summary>
        public void SetWindows(IReadOnlyList<Vector3> windows, int paneCount)
        {
            var copy = new Vector3[windows != null ? windows.Count : 0];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = windows[i];
            }

            _windows = copy;
            _paneCount = paneCount;
        }
    }
}
