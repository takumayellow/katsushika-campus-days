using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// 画質の段（<see cref="QualityTier"/>）を実行中に切り替える常駐部品 (#70)。
    ///
    /// URP のアセット（Assets/Settings/*_RPAsset）は書き換えない。起動した品質レベルのアセットの写しを作って
    /// 段の値を入れ, QualitySettings.renderPipeline を写しに差し替える。段の値が起動したアセットと同じなら
    /// 差し替えない（Web 版の既定 Medium は Mobile_RPAsset と同じ値なので, 既定のままなら何も変わらない）。
    /// エディタでは再生を止めるときに元のアセットへ戻し, 写しを捨てる（ProjectSettings を汚さないため）。
    ///
    /// カメラのポストプロセスと木を描く距離も段に合わせる。シーンを読むたび（sceneLoaded）と, 最初のフレーム
    /// （シーンを読み直さない再生では sceneLoaded が来ない）と, 段を変えたときに入れ直す。
    /// シーンに置かなくても起動時に自分で現れるので, SceneBuilder 側の変更は要らない。
    /// </summary>
    public sealed class QualityManager : MonoBehaviour
    {
        private static readonly List<UniversalAdditionalCameraData> s_PostProcessingOff =
            new List<UniversalAdditionalCameraData>();

        private static bool s_Loaded;
        private static QualityTier s_Current;
        private static RenderPipelineAsset s_Original;
        private static RenderPipelineAsset s_Clone;

        /// <summary>いまの段。まだ読んでいなければ保存から読む。</summary>
        public static QualityTier Current
        {
            get
            {
                EnsureLoaded();
                return s_Current;
            }
        }

        public static QualityTierSettings CurrentSettings => QualityTiers.Settings(Current);

        /// <summary>段を変えて保存する。再生中ならすぐに描き方へ入れる。</summary>
        public static void SetTier(QualityTier tier)
        {
            EnsureLoaded();
            s_Current = tier;
            QualityTiers.Save(tier);
            if (Application.isPlaying)
            {
                ApplyAll();
            }
        }

        /// <summary>ドメインを読み直さない再生でも, 前回の再生の写しと段を持ち越さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RestorePipeline();
            s_PostProcessingOff.Clear();
            s_Loaded = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("KCD.QualityManager");
            host.AddComponent<QualityManager>();
            DontDestroyOnLoad(host);
            ApplyAll();
            Debug.Log(Describe());
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            ApplyAll();
        }

#if UNITY_EDITOR
        private void OnDestroy()
        {
            // 再生を止めると常駐の GameObject も消える。ここで元のアセットへ戻し, 写しが QualitySettings に残らないようにする。
            RestorePipeline();
        }
#endif

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyAll();
        }

        private static void EnsureLoaded()
        {
            if (s_Loaded)
            {
                return;
            }

            s_Current = QualityTiers.Load(QualityTiers.TargetPlatform);
            s_Loaded = true;
        }

        private static void ApplyAll()
        {
            QualityTierSettings settings = CurrentSettings;
            ApplyPipeline(settings.Pipeline);
            ApplyPostProcessing(settings.PostProcessing);
            ApplyTreeDistance(settings.TreeDrawDistance);
        }

        /// <summary>
        /// 写しを差し替えてあればその値を変える。まだなら, いまのアセットが段の値と違うときだけ写しを作って差し替える。
        /// 品質レベルにアセットが無いとき（既定のパイプラインで描いている）は既定のアセットを写し, 戻すときは null に戻す。
        /// </summary>
        private static void ApplyPipeline(PipelineQuality quality)
        {
            RenderPipelineAsset active = QualitySettings.renderPipeline;
            if (s_Clone != null && active == s_Clone)
            {
                if (!QualityPipeline.Matches(s_Clone, quality))
                {
                    QualityPipeline.Apply(s_Clone, quality);
                }

                return;
            }

            RenderPipelineAsset source = active != null ? active : GraphicsSettings.defaultRenderPipeline;
            if (source == null || QualityPipeline.Matches(source, quality))
            {
                return;
            }

            RenderPipelineAsset clone = QualityPipeline.CreateClone(source);
            if (clone == null)
            {
                return;
            }

            QualityPipeline.Apply(clone, quality);
            // 品質レベルを誰かが変えて前の写しが外れていたら, 前の写しは捨てる（戻す先は今のアセット）。
            DestroyClone();
            s_Original = active;
            s_Clone = clone;
            QualitySettings.renderPipeline = clone;
        }

        /// <summary>写しを差し替えていれば元へ戻し, 写しを捨てる。何度呼んでもよい。</summary>
        private static void RestorePipeline()
        {
            if (s_Clone != null && QualitySettings.renderPipeline == s_Clone)
            {
                QualitySettings.renderPipeline = s_Original;
            }

            DestroyClone();
            s_Original = null;
        }

        private static void DestroyClone()
        {
            if (s_Clone != null)
            {
                // HideFlags.DontSave の物はシーンを抜けても自動では消えないので, 自分で消す。
                DestroyImmediate(s_Clone);
            }

            s_Clone = null;
        }

        /// <summary>
        /// ポストプロセスを切る段では, いま描いているカメラを切って覚えておく。描く段に戻したら覚えたカメラだけ戻す
        /// （もともと切ってあるカメラは触らない）。
        /// </summary>
        private static void ApplyPostProcessing(bool enabled)
        {
            s_PostProcessingOff.RemoveAll(data => data == null);
            if (enabled)
            {
                foreach (UniversalAdditionalCameraData data in s_PostProcessingOff)
                {
                    data.renderPostProcessing = true;
                }

                s_PostProcessingOff.Clear();
                return;
            }

            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderPostProcessing)
                {
                    data.renderPostProcessing = false;
                    s_PostProcessingOff.Add(data);
                }
            }
        }

        /// <summary>距離を入れ, 制限があるなら木のまとまりに <see cref="TreeChunkDistance"/> を付ける。</summary>
        private static void ApplyTreeDistance(float limit)
        {
            TreeChunkDistance.Limit = limit;
            if (limit <= 0f)
            {
                return;
            }

            foreach (TreeChunkCombiner combiner in FindObjectsByType<TreeChunkCombiner>(FindObjectsInactive.Include))
            {
                if (!combiner.TryGetComponent(out TreeChunkDistance _))
                {
                    combiner.gameObject.AddComponent<TreeChunkDistance>();
                }
            }
        }

        /// <summary>起動時のログ。GameManager の「[KCD] quality=」とは別の頭にし, 計測側の読み取りを変えない。</summary>
        private static string Describe()
        {
            QualityTierSettings settings = CurrentSettings;
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineName = pipeline != null ? pipeline.name : "builtin";
            return "[KCD] graphics tier=" + QualityTiers.Key(Current)
                   + " " + settings.Pipeline
                   + " post=" + (settings.PostProcessing ? "on" : "off")
                   + " trees=" + (settings.TreeDrawDistance > 0f ? settings.TreeDrawDistance + "m" : "all")
                   + " pipeline=" + (s_Clone != null ? "copy of " + pipelineName : pipelineName)
                   + " vSyncCount=" + QualitySettings.vSyncCount
                   + " targetFrameRate=" + Application.targetFrameRate;
        }
    }
}
