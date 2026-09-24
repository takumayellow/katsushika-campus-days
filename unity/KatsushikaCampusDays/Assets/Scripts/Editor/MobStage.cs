using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// モブの学生を焼き込む (#22)。MobScheduler と、非アクティブの人を <see cref="MobScheduler.MaxActive"/> 人。
    ///
    /// 体は既存のキャラクター FBX（schedule.json の variants[].body）をそのまま使い、髪と服の色だけ変える。
    /// 色は MaterialPropertyBlock ではなく、組み合わせごとの .mat（Assets/Materials/Mobs）で変える。
    /// KCD_Toon の色はすべて UnityPerMaterial の CBUFFER にあり、MaterialPropertyBlock を渡した Renderer は
    /// SRP Batcher から外れて 1 枚ずつ描かれる。.mat を分けるぶんには、同じシェーダのバリアントどうしで
    /// SRP Batcher がまとめるので、マテリアルが増えても描画の準備は増えない。テクスチャは元の顔のものを
    /// 共有するので、テクスチャのメモリも増えない。
    ///
    /// 遠くで使う「_lod1」は同じ色で輪郭のパス（SRPDefaultUnlit）を切ったもの。
    /// 色を変えない部分（顔・目・肌・靴下など）は、近くでは名前のある NPC と同じ元の .mat を使う。
    ///
    /// 道のりの点（入口・VisitZone・座標）はここで NavMesh の上に解決し、MobScheduler に持たせる。
    /// CampusStage.BakeNavMesh と CampusProps.Build のあとに呼ぶこと。
    /// </summary>
    public static class MobStage
    {
        public const string SchedulePath = "Assets/Data/Mobs/schedule.json";
        public const string MobMaterialFolder = "Assets/Materials/Mobs";

        /// <summary>KCD_Toon の輪郭（反転ハル）のパスの LightMode。</summary>
        public const string OutlinePass = "SRPDefaultUnlit";

        private const float BodyHeight = 1.6f;
        private const float TriggerRadius = 0.34f;
        private const float AgentRadius = 0.32f;
        private const float MinScale = 0.85f;
        private const float MaxScale = 1.2f;

        private static readonly Color InariGlasses = new Color(0x2A / 255f, 0x2A / 255f, 0x30 / 255f);

        /// <summary>上着。これがある体では、下に着ているブラウスは元の白のまま残す。</summary>
        private static readonly string[] OuterSlots = { "cloth_hoodie", "cloth_apron", "labcoat" };

        public static GameObject Build(Transform root)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SchedulePath);
            MobSchedule schedule = asset != null ? MobSchedule.Parse(asset.text) : null;
            if (schedule == null || schedule.Variants.Count == 0)
            {
                EditorPaths.Report("モブの時間割が読めないので、モブを置きません: " + SchedulePath);
                return null;
            }

            var group = new GameObject("Mobs");
            group.transform.SetParent(root, false);
            MobScheduler scheduler = group.AddComponent<MobScheduler>();

            // 焼いた直後の NavMesh は、バッチモードでは NavMeshSurface がまだ世界に載せていないことがあり、
            // そのままだと SamplePosition がどの点も外す。載っていれば AddData は何もしない。
            foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
            {
                surface.AddData();
            }

            // CampusProps.Build が足した地面や小物の当たりを、下ろすときのレイに見せる。
            Physics.SyncTransforms();

            ResolveWaypoints(root, schedule, out string[] keys, out Vector3[] positions);

            var cache = new Dictionary<string, Material>();
            var pool = new MobWalker[MobScheduler.MaxActive];
            for (int i = 0; i < pool.Length; i++)
            {
                MobSchedule.Variant variant = schedule.Variants[i % schedule.Variants.Count];
                pool[i] = CreateMob(group.transform, i, variant, cache);
            }

            scheduler.Configure(pool, keys, positions);
            AssetDatabase.SaveAssets();

            EditorPaths.Report(string.Format(
                "モブを {0} 人（{1} 通りの見た目、マテリアル {2} 枚）置き、行き先を {3} か所 NavMesh の上に解決しました。",
                pool.Length, schedule.Variants.Count, cache.Count, keys.Length));
            return group;
        }

        private static void ResolveWaypoints(Transform root, MobSchedule schedule, out string[] keys, out Vector3[] positions)
        {
            var zones = new Dictionary<string, Vector3>();
            foreach (VisitZone zone in root.GetComponentsInChildren<VisitZone>(true))
            {
                if (!string.IsNullOrEmpty(zone.PlaceId))
                {
                    zones[zone.PlaceId] = zone.transform.position;
                }
            }

            var keyList = new List<string>();
            var positionList = new List<Vector3>();
            foreach (MobSchedule.Waypoint waypoint in schedule.AllWaypoints())
            {
                if (TryResolve(waypoint, zones, out Vector3 position))
                {
                    keyList.Add(waypoint.Key);
                    positionList.Add(position);
                }
                else
                {
                    EditorPaths.Report("モブの行き先を NavMesh の上に置けませんでした（この点を通る道は使いません）: " + waypoint.Key);
                }
            }

            keys = keyList.ToArray();
            positions = positionList.ToArray();
        }

        private static bool TryResolve(MobSchedule.Waypoint waypoint, Dictionary<string, Vector3> zones, out Vector3 position)
        {
            position = Vector3.zero;
            switch (waypoint.Kind)
            {
                case MobSchedule.KindEntrance:
                    // FBX の entrance_<id>（扉の前の床）。
                    Transform entrance = CampusStage.FindChild("entrance_" + waypoint.Id);
                    if (entrance == null)
                    {
                        return false;
                    }

                    Vector3 door = entrance.position;
                    return TrySample(door, out position) || TrySnapColumn(new Vector2(door.x, door.z), out position);
                case MobSchedule.KindPlace:
                    // VisitZone は箱の中心（地面から半分の高さ）にあるので、足もとの地面へ下ろす。
                    return zones.TryGetValue(waypoint.Id, out Vector3 zone)
                        && TrySnapColumn(new Vector2(zone.x, zone.z), out position);
                default:
                    return TrySnapColumn(new Vector2(waypoint.X, waypoint.Z), out position);
            }
        }

        /// <summary>
        /// xz の真下の、NavMesh に載る高さを探す。CampusProps.Ground は一番上の当たりを返すので、
        /// 木や庇の上に当たったときは、その下の当たりを上から順に試す。
        /// </summary>
        private static bool TrySnapColumn(Vector2 xz, out Vector3 position)
        {
            if (TrySample(CampusProps.Ground(xz), out position))
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(
                new Vector3(xz.x, 60f, xz.y), Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
            foreach (RaycastHit hit in hits)
            {
                if (TrySample(hit.point, out position))
                {
                    return true;
                }
            }

            return TrySample(new Vector3(xz.x, 0f, xz.y), out position);
        }

        private static bool TrySample(Vector3 guess, out Vector3 position)
        {
            if (NavMesh.SamplePosition(guess, out NavMeshHit hit, NPCWander.NavMeshSnapDistance, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }

            position = guess;
            return false;
        }

        private static MobWalker CreateMob(
            Transform parent, int index, MobSchedule.Variant variant, Dictionary<string, Material> cache)
        {
            var mob = new GameObject("Mob_" + index.ToString("00") + "_" + variant.Id);
            SetLayer(mob, "NPC");
            mob.transform.SetParent(parent, false);

            // trigger なのでプレイヤーを押さない。E で話しかける判定（InteractionPrompt は trigger も拾う）にだけ使う。
            CapsuleCollider trigger = mob.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.height = BodyHeight;
            trigger.radius = TriggerRadius;
            trigger.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            NavMeshAgent agent = mob.AddComponent<NavMeshAgent>();
            agent.radius = AgentRadius;
            agent.height = BodyHeight;
            agent.baseOffset = 0f;
            agent.speed = 1.4f;
            agent.angularSpeed = 240f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.4f;

            // 途中の点で止まりかけないように。着いたかどうかは MobWalker が残り距離で見る。
            agent.autoBraking = false;
            agent.avoidancePriority = MobWalker.AvoidancePriority;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            // 名前のある NPC と同じく、読み込みの時点では止めておく (#63)。MobWalker.Begin が NavMesh を確かめてから有効にする。
            agent.enabled = false;

            GameObject body = ActorFactory.CreateBody(variant.BodyId, mob.transform);

            // モブは座らないので、座り姿勢の IK は外す。
            SitPose sit = body.GetComponent<SitPose>();
            if (sit != null)
            {
                Object.DestroyImmediate(sit);
            }

            FitHeight(mob.transform, body, variant.Height);

            Animator animator = body.GetComponent<Animator>();
            if (animator != null)
            {
                // Mid の間引き更新と Hidden で Animator を止めても、歩きの状態が最初に巻き戻らないように。
                animator.keepAnimatorStateOnDisable = true;
                animator.writeDefaultValuesOnDisable = false;
            }

            MobWalker walker = mob.AddComponent<MobWalker>();
            walker.Configure(animator, variant.Id, BuildLods(body, variant, cache));

            MobTalker talker = mob.AddComponent<MobTalker>();
            talker.PromptLabel = "話す";
            talker.InteractionRange = 1.8f;

            mob.SetActive(false);
            return walker;
        }

        /// <summary>体の高さを variants[].height に合わせる。測れないとき・極端なときは元の大きさのまま。</summary>
        private static void FitHeight(Transform mob, GameObject body, float height)
        {
            bool found = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (found)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    found = true;
                }
            }

            float measured = found ? bounds.max.y - mob.position.y : 0f;
            if (measured < 1f || measured > 2.2f)
            {
                return;
            }

            float scale = Mathf.Clamp(height / measured, MinScale, MaxScale);
            body.transform.localScale = body.transform.localScale * scale;
        }

        private static MobWalker.RendererLod[] BuildLods(
            GameObject body, MobSchedule.Variant variant, Dictionary<string, Material> cache)
        {
            var renderers = new List<Renderer>();
            bool hasOuter = false;
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                // 輪郭の反転ハル（<id>_outline）は取り込み時に止めてある。止めてあるものは触らない。
                if (!renderer.enabled)
                {
                    continue;
                }

                renderers.Add(renderer);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && System.Array.IndexOf(OuterSlots, SlotOf(variant.BodyId, material.name)) >= 0)
                    {
                        hasOuter = true;
                    }
                }
            }

            var lods = new MobWalker.RendererLod[renderers.Count];
            for (int r = 0; r < renderers.Count; r++)
            {
                Material[] sources = renderers[r].sharedMaterials;
                var near = new Material[sources.Length];
                var far = new Material[sources.Length];
                for (int i = 0; i < sources.Length; i++)
                {
                    Material source = sources[i];
                    if (source == null)
                    {
                        continue;
                    }

                    string slot = SlotOf(variant.BodyId, source.name);
                    if (TryVariantColor(variant, slot, hasOuter, out Color color))
                    {
                        string stem = MobMaterialFolder + "/" + variant.Id + "_" + slot;
                        near[i] = Ensure(stem + ".mat", source, color, true, cache);
                        far[i] = Ensure(stem + "_lod1.mat", source, color, false, cache);
                    }
                    else
                    {
                        // 色を変えない部分は元の .mat のまま（名前のある NPC と同じバッチに入る）。
                        near[i] = source;
                        far[i] = Ensure(MobMaterialFolder + "/" + source.name + "_lod1.mat", source, null, false, cache);
                    }
                }

                renderers[r].sharedMaterials = near;
                lods[r] = MobWalker.RendererLod.Create(renderers[r], near, far);
            }

            return lods;
        }

        /// <summary>"sora_cloth_hoodie" → "cloth_hoodie"。</summary>
        private static string SlotOf(string bodyId, string materialName)
        {
            string prefix = bodyId + "_";
            return materialName.StartsWith(prefix) ? materialName.Substring(prefix.Length) : materialName;
        }

        /// <summary>
        /// 部位ごとの塗り分け。髪（とバンダナ・髪のリボン）は hair、眉は髪を少し暗く、上着は top、
        /// 上着の無い体のブラウスも top、スカート・ズボン・胸のリボンは bottom、靴は shoes。
        /// 肌と顔は塗らない（顔のテクスチャに肌の色が焼き込まれているので、肌だけ変えると首で色が切れる）。
        /// </summary>
        private static bool TryVariantColor(MobSchedule.Variant variant, string slot, bool hasOuter, out Color color)
        {
            switch (slot)
            {
                case "hair":
                case "cloth_bandana":
                case "ribbon_red":
                    color = variant.Hair;
                    return true;
                case "brow":
                    color = Color.Lerp(variant.Hair, Color.black, 0.25f);
                    return true;
                case "cloth_hoodie":
                case "cloth_apron":
                case "labcoat":
                    color = variant.Top;
                    return true;
                case "cloth_blouse" when !hasOuter:
                    color = variant.Top;
                    return true;
                case "cloth_skirt_navy":
                case "cloth_pants_gray":
                case "cloth_ribbon_green":
                    color = variant.Bottom;
                    return true;
                case "glasses" when variant.BodyId == "inari":
                    // 稲荷先輩の赤い縁のままだと本人に見えるので、黒縁にする。
                    color = InariGlasses;
                    return true;
            }

            if (slot.StartsWith("shoes_"))
            {
                color = variant.Shoes;
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>
        /// .mat を作るか、あれば中身を元に合わせ直す（GUID を変えないため、消して作り直さない）。
        /// color が null なら色は元のまま。outline が false なら輪郭のパスを切る。
        /// </summary>
        private static Material Ensure(
            string path, Material source, Color? color, bool outline, Dictionary<string, Material> cache)
        {
            if (cache.TryGetValue(path, out Material cached))
            {
                return cached;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                material = new Material(source);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            if (color.HasValue)
            {
                // MaterialLibrary.SetCharacterColor と同じ式。
                Color c = color.Value;
                material.SetColor("_BaseColor", c);
                material.SetColor("_ShadeColor", Color.Lerp(c, new Color(0.45f, 0.42f, 0.58f), 0.42f));
                material.SetColor("_ShadeColor2", Color.Lerp(c, new Color(0.30f, 0.28f, 0.44f), 0.55f));
            }

            material.SetShaderPassEnabled(OutlinePass, outline);

            if (created)
            {
                EditorPaths.EnsureFolder(MobMaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            cache[path] = material;
            return material;
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
        }
    }
}
