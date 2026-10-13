using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャンパスの空（KCD/Sky）のマテリアルを作ってシーンの skybox にし、朝（ゲーム開始の時刻）の色で
    /// 霧・環境光・太陽をそろえる (#8)。実行中の色は DayNightCycle が毎フレーム書き直す。
    /// 霧の色は空の地平線の色と同じ値にする。上空から見ても地面と空の境目に灰色の帯が出ない (#40)。
    /// </summary>
    public static class SkyFactory
    {
        public const string SkyMaterialPath = "Assets/Materials/Sky/KCD_Sky.mat";

        /// <summary>空のマテリアルを用意して skybox にし、開始時刻の色を空・霧・環境光・太陽へ書く。</summary>
        public static Material Apply(Light sun)
        {
            Material material = EnsureMaterial(SkyMaterialPath, SkyMaterial.ShaderName);
            if (material == null)
            {
                return null;
            }

            float hours = DayRestart.DayStartHour;
            SkyState sky = SkyPalette.Evaluate(hours);
            Vector3 sunDirection = DayNightCycle.DefaultSunDirection(hours);

            SkyMaterial.Apply(material, sky, sunDirection, DayNightCycle.MoonDirection);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            SkyMaterial.ApplyEnvironment(sky);

            // エディタで開いたときやプレビューの画も、ゲームが始まった瞬間と同じ朝の光にする。
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(-sunDirection, Vector3.up);
                sun.color = sky.SunColor;
                sun.intensity = DayNightCycle.SunLightIntensity(sky, sunDirection);
            }

            EditorPaths.Report("空: " + SkyMaterialPath + " を skybox にし、" + hours.ToString("F1")
                + " 時の色で霧と環境光をそろえました（太陽の高さ "
                + DayNightCycle.HeightDegrees(sunDirection).ToString("F1") + "°）。");
            return material;
        }

        /// <summary>
        /// path のマテリアルを返す。無ければ shaderName で作って保存する。シェーダが違っていれば差し替える。
        /// シェーダが見つからなければ null（ログに出す）。
        /// </summary>
        public static Material EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                EditorPaths.Report("シェーダ " + shaderName + " が見つかりません。" + path + " を作れませんでした。");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EditorPaths.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
        }
    }
}
