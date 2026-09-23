using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
#if KCD_CINEMACHINE
using Unity.Cinemachine;
#endif

namespace KCD.Editor
{
    /// <summary>
    /// プレイヤー・カメラ・NPC を組み立てる。
    /// キャラクター FBX が未着のうちは色付きのカプセルで代用し、届いたら自動で差し替わる。
    /// </summary>
    public static class ActorFactory
    {
        private const float BodyHeight = 1.6f;
        private const float BodyRadius = 0.28f;

        /// <summary>キャラクター FBX の置き場所。無ければプレースホルダーになる。</summary>
        public static string FbxPathOf(string characterId)
        {
            return EditorPaths.CharactersFolder + "/" + characterId + "/" + characterId + ".fbx";
        }

        /// <summary>
        /// 見た目だけの体を作る。FBX があれば Humanoid、無ければカプセル。
        /// bodyName を渡すと子オブジェクト名を変えられる（プレイヤーは 3 体並ぶので id を付ける, #6）。
        /// </summary>
        public static GameObject CreateBody(string characterId, Transform parent, string bodyName = "Body")
        {
            string fbxPath = FbxPathOf(characterId);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (source == null)
            {
                return CreatePlaceholder(characterId, parent, bodyName);
            }

            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = bodyName;
            body.transform.SetParent(parent, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            Animator animator = body.GetComponent<Animator>();
            if (animator == null)
            {
                animator = body.AddComponent<Animator>();
            }

            AnimatorController controller = AnimatorFactory.EnsureForCharacter(characterId, fbxPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // 座り姿勢の IK。
            animator.gameObject.AddComponent<SitPose>();
            return body;
        }

        /// <summary>FBX が来るまでの代役。髪色でキャラクターを見分けられるようにしておく。</summary>
        private static GameObject CreatePlaceholder(string characterId, Transform parent, string bodyName = "Body")
        {
            var body = new GameObject(bodyName);
            body.transform.SetParent(parent, false);

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Placeholder";
            capsule.transform.SetParent(body.transform, false);
            capsule.transform.localPosition = new Vector3(0f, BodyHeight * 0.5f, 0f);
            capsule.transform.localScale = new Vector3(BodyRadius * 2f, BodyHeight * 0.5f, BodyRadius * 2f);

            Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.EnsureCharacter(characterId, "hair", null);

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Facing";
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, BodyHeight * 0.82f, BodyRadius * 0.9f);
            nose.transform.localScale = Vector3.one * 0.12f;

            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.EnsureCharacter(characterId, "skin", null);

            return body;
        }

        /// <summary>
        /// 操作キャラクター一式。移動・接地・拾う判定までここで完結させる。
        /// 体は GameManager.PlayableCharacterIds の全員分を焼き込み（Body_mirai / Body_botchan / Body_madonna）、
        /// どれを出すかは実行時に PlayerAppearance が決める (#6)。
        /// </summary>
        public static GameObject CreatePlayer(Transform root, Vector3 position, float yaw)
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            SetLayer(player, "Player");
            player.transform.SetParent(root, false);
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = BodyHeight;
            controller.radius = BodyRadius;
            controller.center = new Vector3(0f, BodyHeight * 0.5f + 0.02f, 0f);
            controller.stepOffset = 0.4f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.028f;
            controller.minMoveDistance = 0f;

            CreateSelectableBodies(player.transform);

            player.AddComponent<PlayerController>();
            player.AddComponent<PlayerAnimatorDriver>();
            player.AddComponent<InteractionPrompt>();
            return player;
        }

        /// <summary>
        /// 選べる 3 人ぶんの体を並べて焼き込み、PlayerAppearance に対応表を渡す (#6)。
        /// シーン上は既定のキャラだけを出しておく（3 体重なったまま保存すると Scene ビューで見分けが付かない）。
        /// 実行時は PlayerAppearance.Awake が選択に合わせて出し直す。
        /// </summary>
        private static void CreateSelectableBodies(Transform player)
        {
            string[] ids = GameManager.PlayableCharacterIds;
            var bodies = new GameObject[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                bodies[i] = CreateBody(ids[i], player, "Body_" + ids[i]);
                bodies[i].SetActive(i == 0);
            }

            PlayerAppearance appearance = player.gameObject.AddComponent<PlayerAppearance>();
            appearance.Bind(ids, bodies);
            EditorUtility.SetDirty(appearance);
        }

        /// <summary>肩越しカメラ。Cinemachine があればそちら、無ければ自前の追従カメラ。</summary>
        public static Camera CreateCamera(Transform root, GameObject player)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.SetParent(root, false);

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.12f;
            camera.farClipPlane = 900f;
            go.AddComponent<AudioListener>();
            PostProcessFactory.EnableOnCamera(camera);

            Transform aim = CreateAim(player.transform);

#if KCD_CINEMACHINE
            go.AddComponent<CinemachineBrain>();
            CreateVirtualCamera(root, aim);
#else
            ThirdPersonCamera follow = go.AddComponent<ThirdPersonCamera>();
            follow.Target = player.transform;
#endif
            return camera;
        }

        /// <summary>カメラが狙う点。頭のやや上に置く。</summary>
        private static Transform CreateAim(Transform player)
        {
            var aim = new GameObject("CameraAim");
            aim.transform.SetParent(player, false);
            aim.transform.localPosition = new Vector3(0f, BodyHeight * 0.92f, 0f);
            return aim.transform;
        }

#if KCD_CINEMACHINE
        /// <summary>軌道追従の仮想カメラ。視点入力は CinemachineInputBridge が渡す。</summary>
        private static void CreateVirtualCamera(Transform root, Transform aim)
        {
            var go = new GameObject("FollowCamera");
            go.transform.SetParent(root, false);

            CinemachineCamera vcam = go.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = aim;
            vcam.Lens.FieldOfView = 55f;
            vcam.Priority = 10;

            CinemachineOrbitalFollow orbital = go.AddComponent<CinemachineOrbitalFollow>();
            orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbital.Radius = 4.2f;
            orbital.TargetOffset = new Vector3(0.45f, 0f, 0f);
            orbital.VerticalAxis.Range = new Vector2(-28f, 62f);
            orbital.VerticalAxis.Value = 12f;
            orbital.RadialAxis.Range = new Vector2(0.34f, 1.8f);
            orbital.RadialAxis.Value = 1f;

            go.AddComponent<CinemachineRotationComposer>();

            CinemachineDeoccluder deoccluder = go.AddComponent<CinemachineDeoccluder>();
            deoccluder.CollideAgainst = LayerMaskFor("Ground", "Building");
            deoccluder.MinimumDistanceFromTarget = 0.8f;
            var avoid = new CinemachineDeoccluder.ObstacleAvoidance
            {
                Enabled = true,
                CameraRadius = 0.32f,
                Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward,
                MaximumEffort = 4,
                Damping = 0.4f
            };

            // 木や柱の脇でカメラが寄ったり戻ったりを細かく繰り返さないよう、平滑化を入れる (#30)。
            // SmoothingTime 0.4 / MinimumOcclusionTime 0.1 / DampingWhenOccluded 0.2（値は CinemachineInputBridge）。
            CinemachineInputBridge.ApplyDeoccluderSmoothing(ref avoid);
            deoccluder.AvoidObstacles = avoid;

            go.AddComponent<CinemachineInputBridge>();
        }
#endif

        /// <summary>会話できる NPC。NavMesh が無い場所ではその場で立ち止まる。</summary>
        public static GameObject CreateNpc(
            Transform root, string npcId, string displayName, Vector3 position, float yaw, float wanderRadius)
        {
            var npc = new GameObject("NPC_" + npcId);
            SetLayer(npc, "NPC");
            npc.transform.SetParent(root, false);
            npc.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            CapsuleCollider collider = npc.AddComponent<CapsuleCollider>();
            collider.height = BodyHeight;
            collider.radius = BodyRadius + 0.06f;
            collider.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            NavMeshAgent agent = npc.AddComponent<NavMeshAgent>();
            agent.radius = BodyRadius + 0.04f;
            agent.height = BodyHeight;
            agent.baseOffset = 0f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            CreateBody(npcId, npc.transform);

            NPCWander wander = npc.AddComponent<NPCWander>();
            wander.Home = position;
            wander.Radius = wanderRadius;

            NPCTalker talker = npc.AddComponent<NPCTalker>();
            talker.NpcId = npcId;
            talker.DisplayName = displayName;
            talker.PromptLabel = "話す";
            talker.InteractionRange = 2.4f;
            return npc;
        }

        /// <summary>拾える小物。クエストの collect ステップが見ている id を渡す。</summary>
        public static GameObject CreateCollectable(
            Transform root, string itemId, string displayName, Vector3 position, string materialName)
        {
            var item = new GameObject("Item_" + itemId + "_" + Mathf.RoundToInt(position.x) + "_" + Mathf.RoundToInt(position.z));
            SetLayer(item, "Interactable");
            item.transform.SetParent(root, false);
            item.transform.position = position;

            // 見た目は葉や紙パックの形（以前は直径 0.32 m の球, #56）。
            CollectableMeshes.Model model = CollectableMeshes.For(itemId, materialName);
            item.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            item.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;

            // 当たり判定は以前と同じ半径 0.24 m の球（0.32 m の球に半径 0.75 を掛けていた）。
            var collider = item.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.24f;

            CollectableItem collectable = item.AddComponent<CollectableItem>();
            collectable.ItemId = itemId;
            collectable.DisplayName = displayName;
            collectable.InteractionRange = 2f;
            return item;
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
        }

        private static int LayerMaskFor(params string[] names)
        {
            int mask = 0;
            foreach (string name in names)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }

            return mask;
        }
    }
}
