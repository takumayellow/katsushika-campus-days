using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD.Editor
{
    /// <summary>
    /// URP のポストプロセス。Bloom / 色調 / ビネット / トーンマッピングを 1 つのプロファイルにまとめ、
    /// グローバル Volume で両シーンに効かせる。カメラ側の renderPostProcessing も一緒に入れる。
    /// </summary>
    public static class PostProcessFactory
    {
        public const string ProfilePath = "Assets/Settings/KCD_PostProcess.asset";

        /// <summary>グローバル Volume を置く。withDriver なら時刻連動も付ける（Campus 用）。</summary>
        public static Volume Place(Transform root, bool withDriver)
        {
            VolumeProfile profile = EnsureProfile();

            var go = new GameObject("PostProcessVolume");
            go.transform.SetParent(root, false);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            if (withDriver)
            {
                go.AddComponent<PostProcessDriver>();
            }

            return volume;
        }

        /// <summary>カメラでポストプロセスと FXAA を有効にする。</summary>
        public static void EnableOnCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null)
            {
                return;
            }

            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        }

        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                EditorPaths.EnsureFolder("Assets/Settings");
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);

            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.contrast.Override(6f);
            color.saturation.Override(10f);
            color.postExposure.Override(0.05f);

            WhiteBalance balance = GetOrAdd<WhiteBalance>(profile);
            balance.temperature.Override(0f);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component))
            {
                return component;
            }

            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
