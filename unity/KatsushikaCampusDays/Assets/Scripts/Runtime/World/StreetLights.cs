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
    /// KCD/LampGlow で描く（StreetLampStage が作る）。灯す時刻と明るさは <see cref="LampGlowLights"/>。
    /// </summary>
    public sealed class StreetLights : LampGlowLights
    {
        [SerializeField] private Vector3[] _lamps = new Vector3[0];

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
    }
}
