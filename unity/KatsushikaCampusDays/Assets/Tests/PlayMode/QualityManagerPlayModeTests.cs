using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif
using Object = UnityEngine.Object;

namespace KCD.Tests
{
    /// <summary>
    /// 画質の段を再生中に切り替えたとき (#70)。QualityManager が URP のアセットの写しを差し替え, カメラのポストプロセスと
    /// 木を描く距離を段に合わせ, Assets の URP のアセット（Assets/Settings/*_RPAsset）は変えないことを確かめる。
    ///
    /// シーンは読まず, カメラと木はテストで置く。先に走ったテストのシーンが残っていれば, そのカメラと木も段に合わせて
    /// 変わるが, TearDown で段を戻すと元に戻る。段と PlayerPrefs の「KCD.QualityTier」はテストの前の値に戻す。
    /// </summary>
    public sealed class QualityManagerPlayModeTests
    {
        /// <summary>QualityManager が起動時に作る常駐の GameObject の名前。</summary>
        private const string HostName = "KCD.QualityManager";

        /// <summary>木のまとまりを置く位置。キャンパスのどのカメラからも Low の距離（300 m）より遠い。</summary>
        private static readonly Vector3 FarAway = new Vector3(100000f, 0f, 0f);

        private readonly List<Object> _created = new List<Object>();
        private QualityTier _tierBefore;
        private bool _hadPref;
        private string _prefBefore;

#if UNITY_EDITOR
        private readonly List<DiskAsset> _diskAssets = new List<DiskAsset>();
#endif

        [SetUp]
        public void SetUp()
        {
            _tierBefore = QualityManager.Current;
            _hadPref = PlayerPrefs.HasKey(QualityTiers.PrefKey);
            _prefBefore = PlayerPrefs.GetString(QualityTiers.PrefKey, string.Empty);

#if UNITY_EDITOR
            _diskAssets.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset != null)
                {
                    _diskAssets.Add(new DiskAsset(asset, path));
                }
            }
#endif
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null)
                {
                    Object.Destroy(created);
                }
            }

            _created.Clear();

            // 常駐を消すテストのあとは作り直す。エディタで再生を止めたときに写しを捨てるのは常駐の OnDestroy なので。
            if (Object.FindAnyObjectByType<QualityManager>() == null)
            {
                var host = new GameObject(HostName);
                host.AddComponent<QualityManager>();
                Object.DontDestroyOnLoad(host);
            }

            QualityManager.SetTier(_tierBefore);
            if (_hadPref)
            {
                PlayerPrefs.SetString(QualityTiers.PrefKey, _prefBefore);
            }
            else
            {
                PlayerPrefs.DeleteKey(QualityTiers.PrefKey);
            }

            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator Bootstrap_LeavesOneResidentManager()
        {
            yield return null;

            QualityManager[] managers = Object.FindObjectsByType<QualityManager>(FindObjectsInactive.Include);
            Assert.AreEqual(1, managers.Length, "常駐の QualityManager が 1 つではない");
            Assert.AreEqual("DontDestroyOnLoad", managers[0].gameObject.scene.name,
                            "QualityManager がシーンを読み直すと消える所にいる");
        }

        [UnityTest]
        public IEnumerator SetTier_DrawsWithTheTierValues_WithoutTouchingTheAssets()
        {
#if UNITY_EDITOR
            Assert.Greater(_diskAssets.Count, 0, "Assets に URP のアセットが見つからない");
#endif

            // Low から High は写しの値を変える道, Medium は Web 版のアセットと同じ値, 最後の Low は Medium からの道。
            foreach (QualityTier tier in new[] { QualityTier.Low, QualityTier.High, QualityTier.Medium, QualityTier.Low })
            {
                QualityManager.SetTier(tier);
                yield return null;

                Assert.AreEqual(QualityTiers.Key(tier), PlayerPrefs.GetString(QualityTiers.PrefKey, string.Empty),
                                tier + ": 選んだ段が保存されない");

                PipelineQuality wanted = QualityTiers.Settings(tier).Pipeline;
                RenderPipelineAsset active = GraphicsSettings.currentRenderPipeline;
                Assert.IsNotNull(active, tier + ": URP で描いていない");
                Assert.IsTrue(QualityPipeline.TryRead(active, out PipelineQuality current),
                              tier + ": " + active.name + " が URP のアセットではない");
                Assert.IsTrue(current.Approximately(wanted), tier + ": 描いている値 " + current + " / 段の値 " + wanted);

#if UNITY_EDITOR
                AssertDrawsFromACopyIfTheAssetDiffers(active, wanted, tier);
                AssertAssetsUnchanged(tier.ToString());
#endif
            }
        }

        [UnityTest]
        public IEnumerator SetTier_TurnsPostProcessingOffOnlyOnLow_AndRestoresOnlyTheCamerasItTurnedOff()
        {
            UniversalAdditionalCameraData withPost = CreateCamera("quality test camera (post)", true);
            UniversalAdditionalCameraData withoutPost = CreateCamera("quality test camera (no post)", false);

            QualityManager.SetTier(QualityTier.Low);
            yield return null;
            Assert.IsFalse(withPost.renderPostProcessing, "Low でポストプロセスが切れない");
            Assert.IsFalse(withoutPost.renderPostProcessing, "Low でポストプロセスが入った");

            QualityManager.SetTier(QualityTier.High);
            yield return null;
            Assert.IsTrue(withPost.renderPostProcessing, "Low から High にしてもポストプロセスが戻らない");
            Assert.IsFalse(withoutPost.renderPostProcessing, "もともと切ってあったカメラのポストプロセスを High で入れた");

            QualityManager.SetTier(QualityTier.Low);
            yield return null;
            Assert.IsFalse(withPost.renderPostProcessing, "2 回目の Low でポストプロセスが切れない");

            QualityManager.SetTier(QualityTier.Medium);
            yield return null;
            Assert.IsTrue(withPost.renderPostProcessing, "Low から Medium にしてもポストプロセスが戻らない");
            Assert.IsFalse(withoutPost.renderPostProcessing, "もともと切ってあったカメラのポストプロセスを Medium で入れた");
        }

        [UnityTest]
        public IEnumerator Low_HidesTreeChunksBeyondTheLimit_AndMediumShowsThemAgain()
        {
            EnsureMainCamera();
            TreeChunkCombiner combiner = CreateFarTrees();
            Assert.AreEqual(1, combiner.Chunks.Count, "テストの木が 1 つのまとまりにならない");
            MeshRenderer chunk = combiner.Chunks[0].GetComponent<MeshRenderer>();

            QualityManager.SetTier(QualityTier.Low);
            Assert.AreEqual(QualityTiers.Settings(QualityTier.Low).TreeDrawDistance, TreeChunkDistance.Limit, 0.001f,
                            "Low の木を描く距離が入らない");
            Assert.IsTrue(combiner.TryGetComponent(out TreeChunkDistance _), "Low で木のまとまりに TreeChunkDistance が付かない");

            // 付けた次のフレームで Start と Update が走る。1 フレームの余裕を見て 2 フレーム待つ。
            yield return null;
            yield return null;
            Assert.IsTrue(chunk.forceRenderingOff, "カメラから Low の距離より遠い木のまとまりを描いている");

            QualityManager.SetTier(QualityTier.Medium);
            Assert.AreEqual(0f, TreeChunkDistance.Limit, 0.001f, "Medium で木を描く距離の制限が残る");

            yield return null;
            yield return null;
            Assert.IsFalse(chunk.forceRenderingOff, "Medium に戻しても遠くの木のまとまりが消えたまま");
        }

#if UNITY_EDITOR
        /// <summary>エディタで再生を止めると常駐が消える。そのとき元のアセットへ戻し, 写しを捨てる。</summary>
        [UnityTest]
        public IEnumerator DestroyingTheManager_PutsTheAssetBackAndDropsTheCopy()
        {
            // Low の値はどの品質レベルのアセットとも違う（描く画素 0.7）ので, 必ず写しに差し替わる。
            QualityManager.SetTier(QualityTier.Low);
            yield return null;

            RenderPipelineAsset copy = QualitySettings.renderPipeline;
            Assert.IsTrue(copy != null, "Low で QualitySettings.renderPipeline が空");
            Assert.IsFalse(AssetDatabase.Contains(copy), "Low で写しに差し替わっていない");
            string copyName = copy.name;

            QualityManager manager = Object.FindAnyObjectByType<QualityManager>();
            Assert.IsNotNull(manager, "常駐の QualityManager が無い");
            Object.Destroy(manager.gameObject);
            yield return null;

            Assert.IsTrue(copy == null, "QualityManager が消えても写しが残る");
            RenderPipelineAsset restored = QualitySettings.renderPipeline;
            if (restored != null)
            {
                Assert.IsTrue(AssetDatabase.Contains(restored), "QualityManager が消えても " + restored.name + " の写しで描いている");
                Assert.AreEqual(copyName, restored.name, "写しの元と違うアセットに戻った");
            }

            AssertAssetsUnchanged("QualityManager を消したあと");
        }

        /// <summary>段の値が Assets の同じ名前のアセットと違うなら, 描いているのは保存しない写しのはず。</summary>
        private void AssertDrawsFromACopyIfTheAssetDiffers(RenderPipelineAsset active, PipelineQuality wanted, QualityTier tier)
        {
            DiskAsset disk = _diskAssets.Find(candidate => candidate.Asset.name == active.name);
            Assert.IsNotNull(disk, tier + ": 描いているアセット " + active.name + " と同じ名前のアセットが Assets に無い（写しの名前は元と同じ）");
            if (QualityPipeline.Matches(disk.Asset, wanted))
            {
                // 同じ値ならアセットのままでも, 前の段の写しの値を変えたものでもよい。
                return;
            }

            Assert.IsFalse(AssetDatabase.Contains(active),
                           tier + ": 段の値が " + disk.AssetPath + " と違うのに, アセットそのもので描いている");
            Assert.AreEqual(HideFlags.DontSave, active.hideFlags & HideFlags.DontSave, tier + ": 写しが保存される設定になっている");
        }

        /// <summary>Assets の URP のアセットが, 値もファイルも SetUp のときのままか。</summary>
        private void AssertAssetsUnchanged(string when)
        {
            foreach (DiskAsset disk in _diskAssets)
            {
                Assert.IsTrue(disk.Json == EditorJsonUtility.ToJson(disk.Asset), when + ": " + disk.AssetPath + " の値が変わった");
                Assert.AreEqual(disk.Dirty, EditorUtility.IsDirty(disk.Asset), when + ": " + disk.AssetPath + " に保存待ちの印が付いた");
                Assert.IsTrue(disk.FileText == File.ReadAllText(disk.AssetPath), when + ": " + disk.AssetPath + " のファイルが書き換わった");
            }
        }

        /// <summary>SetUp のときの Assets の URP のアセットの値とファイルの中身。</summary>
        private sealed class DiskAsset
        {
            public DiskAsset(RenderPipelineAsset asset, string assetPath)
            {
                Asset = asset;
                AssetPath = assetPath;
                Json = EditorJsonUtility.ToJson(asset);
                Dirty = EditorUtility.IsDirty(asset);
                FileText = File.ReadAllText(assetPath);
            }

            public RenderPipelineAsset Asset { get; }
            public string AssetPath { get; }
            public string Json { get; }
            public bool Dirty { get; }
            public string FileText { get; }
        }
#endif

        /// <summary>描かないカメラ。QualityManager は止めてあるカメラも見る。</summary>
        private UniversalAdditionalCameraData CreateCamera(string name, bool postProcessing)
        {
            var go = new GameObject(name);
            _created.Add(go);
            Camera camera = go.AddComponent<Camera>();
            camera.enabled = false;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            return data;
        }

        /// <summary>TreeChunkDistance は Camera.main からの距離を見る。先のテストのシーンのカメラがあればそれを使う。</summary>
        private void EnsureMainCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var go = new GameObject("quality test main camera") { tag = "MainCamera" };
            _created.Add(go);
            go.AddComponent<Camera>();
        }

        /// <summary>遠くに木を 1 本置き, TreeChunkCombiner でまとめる（まとめるのは付けたときの Awake）。</summary>
        private TreeChunkCombiner CreateFarTrees()
        {
            var root = new GameObject("quality test trees");
            _created.Add(root);
            root.transform.position = FarAway;

            var mesh = new Mesh();
            _created.Add(mesh);
            mesh.SetVertices(new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up });
            mesh.SetTriangles(new List<int> { 0, 1, 2 }, 0);
            mesh.RecalculateNormals();

            var tree = new GameObject("tree");
            tree.transform.SetParent(root.transform, false);
            tree.AddComponent<MeshFilter>().sharedMesh = mesh;
            tree.AddComponent<MeshRenderer>().sharedMaterials = new Material[1];

            return root.AddComponent<TreeChunkCombiner>();
        }
    }
}
