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

        /// <summary>見た目だけの体を作る。FBX があれば Humanoid、無ければカプセル。</summary>
        public static GameObject CreateBody(string characterId, Transform parent)
        {
            string fbxPath = FbxPathOf(characterId);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (source == null)
            {
                return CreatePlaceholder(characterId, parent);
            }

            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "Body";
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
        private static GameObject CreatePlaceholder(string characterId, Transform parent)
        {
            var body = new GameObject("Body");
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

        /// <summary>操作キャラクター一式。移動・接地・拾う判定までここで完結させる。</summary>
        public static GameObject CreatePlayer(Transform root, string characterId, Vector3 position, float yaw)
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

            CreateBody(characterId, player.transform);

            player.AddComponent<PlayerController>();
            player.AddComponent<PlayerAnimatorDriver>();
            player.AddComponent<InteractionPrompt>();
            return player;
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
            deoccluder.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
            {
                Enabled = true,
                CameraRadius = 0.32f,
                Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward,
                MaximumEffort = 4,
                Damping = 0.4f,
                DampingWhenOccluded = 0.2f
            };

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
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = "Item_" + itemId + "_" + Mathf.RoundToInt(position.x) + "_" + Mathf.RoundToInt(position.z);
            SetLayer(item, "Interactable");
            item.transform.SetParent(root, false);
            item.transform.position = position;
            item.transform.localScale = Vector3.one * 0.32f;

            var collider = item.GetComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.75f;

            item.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.EnsureCampus(materialName);

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
