using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 空のシェーダ KCD/Sky へ <see cref="SkyState"/> を書く。実行時は DayNightCycle が毎フレーム、
    /// エディタではシーン生成（SkyFactory）が朝の色で 1 回呼ぶ。
    /// </summary>
    public static class SkyMaterial
    {
        public const string ShaderName = "KCD/Sky";

        public static readonly int ZenithColorId = Shader.PropertyToID("_ZenithColor");
        public static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        public static readonly int SunGlowColorId = Shader.PropertyToID("_SunGlowColor");
        public static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        public static readonly int SkylineColorId = Shader.PropertyToID("_SkylineColor");
        public static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        public static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");
        public static readonly int StarsId = Shader.PropertyToID("_Stars");

        /// <summary>KCD/Sky のマテリアルなら true。</summary>
        public static bool IsSky(Material material)
        {
            return material != null && material.shader != null && material.shader.name == ShaderName;
        }

        /// <summary>
        /// 色と太陽・月の向きを書く。向きは「その天体の方へ向かう」単位ベクトル（ライトの forward の逆）。
        /// 月の w には見え方（星と同じ値）を入れる。
        /// </summary>
        public static void Apply(Material material, SkyState sky, Vector3 sunDirection, Vector3 moonDirection)
        {
            if (material == null)
            {
                return;
            }

            material.SetColor(ZenithColorId, sky.Zenith);
            material.SetColor(HorizonColorId, sky.Horizon);
            material.SetColor(SunGlowColorId, sky.SunGlow);
            material.SetColor(CloudColorId, sky.Cloud);
            material.SetColor(SkylineColorId, sky.Skyline);

            Vector3 sun = sunDirection.sqrMagnitude > 1e-6f ? sunDirection.normalized : Vector3.up;
            Vector3 moon = moonDirection.sqrMagnitude > 1e-6f ? moonDirection.normalized : Vector3.up;
            material.SetVector(SunDirectionId, new Vector4(sun.x, sun.y, sun.z, 0f));
            material.SetVector(MoonDirectionId, new Vector4(moon.x, moon.y, moon.z, sky.Stars));
            material.SetFloat(StarsId, sky.Stars);
        }

        /// <summary>
        /// 霧・環境光・反射の強さを空に合わせる。霧の色は地平線の色と同じにする (#40)。
        /// 霧の距離とモードはシーンの値のまま触らない。
        /// </summary>
        public static void ApplyEnvironment(SkyState sky)
        {
            RenderSettings.fogColor = sky.Fog;
            RenderSettings.ambientSkyColor = sky.AmbientSky;
            RenderSettings.ambientEquatorColor = sky.AmbientEquator;
            RenderSettings.ambientGroundColor = sky.AmbientGround;
            RenderSettings.reflectionIntensity = sky.ReflectionIntensity;
        }
    }
}
