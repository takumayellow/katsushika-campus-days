using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Editor
{
    /// <summary>
    /// Campus と Title のシーンをコードだけで組み上げる。バッチから呼ぶ唯一の入口。
    /// </summary>
    public static class SceneBuilder
    {
        /// <summary>NPC の立ち位置。建物 id が空なら座標をそのまま使う。</summary>
        private struct NpcSpot
        {
            public string Id;
            public string Name;
            public string BuildingId;
            public float Distance;
            public Vector2 Fallback;
            public float WanderRadius;
        }

        private static readonly NpcSpot[] Npcs =
        {
            new NpcSpot
            {
                Id = "prof", Name = "教授", BuildingId = "lecture",
                Distance = 3.6f, Fallback = new Vector2(108.5f, -52.5f), WanderRadius = 2f
            },
            new NpcSpot
            {
                Id = "kaname", Name = "中川 かなめ", BuildingId = "research2",
                Distance = 3.2f, Fallback = new Vector2(-8.5f, -6.5f), WanderRadius = 4f
            },
            new NpcSpot
            {
                Id = "sora", Name = "金町 そら", BuildingId = "kyoso",
                Distance = 7.5f, Fallback = new Vector2(108f, -76f), WanderRadius = 3f
            },
            new NpcSpot
            {
                Id = "inari", Name = "花之木 いなり", BuildingId = string.Empty,
                Distance = 0f, Fallback = new Vector2(-59.55f, -24.47f), WanderRadius = 3f
            }
        };

        /// <summary>両方のシーンを作り直し、ビルド設定に登録する。</summary>
        [MenuItem("KCD/シーンを組み直す")]
        public static void BuildAll()
        {
            EditorPaths.EnsureFolder(EditorPaths.ScenesFolder);
            EditorPaths.EnsureFolder(EditorPaths.GeneratedFolder);

            FontLibrary.Ensure();
            EditorPaths.Report("データを同期しました: " + DataBundler.SyncAll() + " 件");
            EditorPaths.Report("マテリアルを差し替えました: " + CharacterImporter.ResolveMaterials() + " 件");
            EditorPaths.Report("顔テクスチャを貼り直しました: " + CharacterImporter.RefreshFaceTextures() + " 件");
            EditorPaths.Report("Humanoid の骨格を FBX に合わせました: " + CharacterImporter.SyncSkeletons() + " 体");
            EditorPaths.Report("キャラの色を palette.json に合わせました: " + MaterialLibrary.RepaintCharacters() + " 件");

            BuildCampus();
            BuildTitle();
            Register();
            FacingProbe.Report();
            PoseProbe.Report();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorPaths.Report("SceneBuilder.BuildAll 完了");
        }

        /// <summary>キャンパス本体。地形 → NavMesh → 小物 → 人 → UI の順に積む。</summary>
        private static void BuildCampus()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var world = new GameObject("World");
            Transform root = world.transform;

            CampusStage.Build(root);
            WorldBoundsStage.Build(root);

            // 西の回廊と寮 (#41)。route.fbx が無ければ RouteStage.Build が null を返すので、
            // 以下の追加はまるごと飛ばす（本編はそのまま動く）。
            // 置く場所は WorldBoundsStage.Build のあと（Wall_West を作ってから門を開ける）で、
            // NavMesh を焼く前（回廊は NavMeshModifier で焼かせない）。
            GameObject route = RouteStage.Build(root);
            if (route != null)
            {
                RouteStage.OpenWestGate(root);
                RouteStage.BuildAnnexWalls(root);
            }

            Physics.SyncTransforms();
            CampusStage.BakeNavMesh(root);

            CampusProps.Build(root);

            // 体は選べる 3 人ぶんを焼き込む。どれを出すかは実行時に PlayerAppearance が決めるので、
            // ここでキャラクターを決め打ちしない（以前は PlayableCharacterIds[0] = mirai 固定で、
            // タイトルで誰を選んでも本編は mirai のままだった, #6）。
            GameObject player = ActorFactory.CreatePlayer(
                root, CampusProps.PlayerSpawn + Vector3.up * 0.15f, CampusProps.PlayerYaw);
            ActorFactory.CreateCamera(root, player);

            PlaceNpcs(root);
            SeatFactory.PlaceCampus(root);
            PlaceSystems(root);
            if (route != null)
            {
                // 見張りの矩形を増築区画まで広げる。PlaceSystems が WorldBounds を足したあとでないと
                // 見つからない。広げないと門をくぐった瞬間に引き戻される。
                RouteStage.WidenWorldBounds();
            }

            InteriorStage.Build(root);
            if (route != null)
            {
                // 寮の屋内と玄関。本編の 9 棟とは別枠で、探索率にも「入った建物」にも数えない。
                DormStage.Build(root);
            }

            UIFactory.BuildCampusUI(root, player);
            if (route != null)
            {
                DormEndingFactory.Build(root);
            }

            Save(scene, EditorPaths.CampusScene);
        }

        /// <summary>タイトルとキャラクター選択。</summary>
        private static void BuildTitle()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var world = new GameObject("World");
            TitleStage.Build(world.transform);

            Save(scene, EditorPaths.TitleScene);
        }

        /// <summary>DESIGN §2 の 4 人。入口の少し外に立たせ、プレイヤーが来る向きを向く。</summary>
        private static void PlaceNpcs(Transform root)
        {
            var group = new GameObject("NPCs");
            group.transform.SetParent(root, false);

            foreach (NpcSpot spot in Npcs)
            {
                Vector3 position = string.IsNullOrEmpty(spot.BuildingId)
                    ? CampusProps.Ground(spot.Fallback)
                    : CampusProps.Outward(spot.BuildingId, spot.Distance, spot.Fallback);

                float yaw = YawTowardMall(position);
                ActorFactory.CreateNpc(
                    group.transform, spot.Id, spot.Name, position, yaw, spot.WanderRadius);
            }

            EditorPaths.Report("NPC を " + Npcs.Length + " 人置きました。");
        }

        /// <summary>キャンパスモールの中心を向く角度。だいたいプレイヤーが来る方角になる。</summary>
        private static float YawTowardMall(Vector3 position)
        {
            var mall = new Vector3(53.7f, position.y, -50.92f);
            Vector3 delta = mall - position;
            delta.y = 0f;

            if (delta.sqrMagnitude < 0.01f)
            {
                return 0f;
            }

            return Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        }

        /// <summary>シーンに 1 つずつ必要な進行役。GameManager と QuestSystem は自動で生まれる。</summary>
        private static void PlaceSystems(Transform root)
        {
            var systems = new GameObject("Systems");
            systems.transform.SetParent(root, false);

            systems.AddComponent<DialogueSystem>();
            systems.AddComponent<CampusDirector>();
            systems.AddComponent<InteriorLoader>();
            systems.AddComponent<DayEndEvaluator>();
            systems.AddComponent<WorldBounds>();
            AudioFactory.Place(root);
            PostProcessFactory.Place(root, true);
        }

        private static void Save(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                EditorPaths.Report("シーンの保存に失敗しました: " + path);
                return;
            }

            EditorPaths.Report("Saved scene: " + path);
            EditorPaths.AppendVerify("scene " + path);
        }

        /// <summary>Title を先頭にして両方をビルド対象へ入れる。</summary>
        private static void Register()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(EditorPaths.TitleScene, true),
                new EditorBuildSettingsScene(EditorPaths.CampusScene, true)
            };

            EditorBuildSettings.scenes = scenes.ToArray();
            EditorPaths.Report("ビルド対象シーン: " + scenes.Count + " 本");
        }
    }
}
