using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 軌道カメラ（CinemachineOrbitalFollow の Sphere）の縦角と半径倍率の範囲 (#12)。
    /// 半径そのものは背丈に合わせて PlayerAppearance が決めるので触らず、倍率の範囲だけを差し替える。
    /// 屋外の範囲はシーンに保存されたもの（ActorFactory が入れる -28〜62 度 / 0.34〜1.8）を起動時に読む。
    /// 室内は <see cref="Indoor"/> に絞る。
    /// </summary>
    public readonly struct CameraOrbitProfile
    {
        /// <summary>
        /// 室内（InteriorStage の部屋。天井 3.2〜3.9 m）の範囲。縦角 -20〜30 度、半径倍率 0.34〜2/3。
        /// 背丈 1.6 m（半径 4.2 m）なら距離は最大 2.8 m、見る点（床から 1.47 m）からの高さは
        /// 最大 2.8 × sin 30° = 1.4 m で、カメラは床から 2.87 m まで。いちばん低い天井 3.2 m の 0.3 m 下に収まる。
        /// </summary>
        public static readonly CameraOrbitProfile Indoor =
            new CameraOrbitProfile(new Vector2(-20f, 30f), new Vector2(0.34f, 2f / 3f));

        public CameraOrbitProfile(Vector2 pitchRange, Vector2 radialRange)
        {
            PitchRange = pitchRange;
            RadialRange = radialRange;
        }

        /// <summary>縦角の範囲（度。正が上）。</summary>
        public Vector2 PitchRange { get; }

        /// <summary>軌道の半径に掛ける倍率の範囲（ホイールのズーム）。</summary>
        public Vector2 RadialRange { get; }

        /// <summary>半径 radius のとき、カメラが見る点から離れる最大の距離（m）。</summary>
        public float MaxDistance(float radius)
        {
            return Mathf.Max(0f, radius * RadialRange.y);
        }

        /// <summary>半径 radius のとき、カメラが見る点より上に出る最大の高さ（m）。</summary>
        public float MaxHeightAboveTarget(float radius)
        {
            return CameraMath.MaxOrbitHeight(radius, RadialRange.y, PitchRange.y);
        }
    }
}
