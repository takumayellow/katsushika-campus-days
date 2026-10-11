using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Editor
{
    /// <summary>
    /// 隠しアイテムの見た目 (#167)。以前は 20 個とも同じ八面体の宝石で、どんぐりを拾っても宝石が見えていた。
    ///
    /// 形は名前どおりの模型（<see cref="HiddenItemShapes"/>）。クエストの拾い物（葉・牛乳）との見分けは、形ではなく
    /// レア度の色の光で付ける: 足元の床に光る輪（<see cref="RingName"/>）と、模型のまわりを一緒に回るきらめき
    /// （<see cref="SparkleName"/>）。どちらも collectibles.json の rarity ごとの自己発光の色（mark_&lt;rarity&gt;）。
    /// メッシュは Assets/Generated/Meshes/HiddenItems に保存し、作り直しても GUID を変えない。
    /// </summary>
    public static class HiddenItemModels
    {
        public const string MeshFolder = CollectableMeshes.MeshFolder + "/HiddenItems";

        /// <summary>模型の一番長い所の下限と上限（m）。小さいと芝の上で見つからず、大きいと拾い物に見えない。</summary>
        public const float MinSize = 0.2f;

        public const float MaxSize = 0.4f;

        public const string RingName = "RarityRing";
        public const string SparkleName = "RaritySparkle";

        /// <summary>足元の輪の内と外の半径（m）。拾える距離（2 m）よりずっと小さく、模型の真下を囲む。</summary>
        public const float RingInner = 0.3f;

        public const float RingOuter = 0.36f;

        /// <summary>輪を床から浮かせる高さ（m）。床と重なってちらつかないように。</summary>
        public const float RingAboveFloor = 0.012f;

        /// <summary>きらめきが模型のまわりを回る半径（m）。</summary>
        public const float SparkleOrbit = 0.25f;

        /// <summary>輪ときらめきは 1 色なので、サブメッシュ 1 つにまとめる名前（マテリアルはレア度ごとに差す）。</summary>
        private const string MarkMaterialPlaceholder = "mark";

        private static readonly Dictionary<string, Func<PartMesh>> Shapes = new Dictionary<string, Func<PartMesh>>
        {
            { "c_gate_keyring", HiddenItemShapes.Keyring },
            { "c_mall_pin", HiddenItemShapes.PinBadge },
            { "c_bench_coin", HiddenItemShapes.Coin },
            { "c_pond_marble", HiddenItemShapes.Marble },
            { "c_park_acorn", HiddenItemShapes.Acorn },
            { "c_park_disc", HiddenItemShapes.FlyingDisc },
            { "c_shrine_bell", HiddenItemShapes.ShrineBell },
            { "c_bike_bell", HiddenItemShapes.BikeBell },
            { "c_greenhouse_seed", HiddenItemShapes.SeedPacket },
            { "c_gym_whistle", HiddenItemShapes.Whistle },
            { "c_library_bookmark", HiddenItemShapes.Bookmark },
            { "c_dome_key", HiddenItemShapes.DomeKey },
            { "c_kyoso_tumbler", HiddenItemShapes.Tumbler },
            { "c_kyoso_sticker", HiddenItemShapes.Sticker },
            { "c_lecture_chalk", HiddenItemShapes.Chalk },
            { "c_lecture_notebook", HiddenItemShapes.Notebook },
            { "c_tray_charm", HiddenItemShapes.TrayCharm },
            { "c_notice_magnet", HiddenItemShapes.Magnet },
            { "c_lab_goggles", HiddenItemShapes.Goggles },
            { "c_lab_beaker", HiddenItemShapes.Beaker },
        };

        /// <summary>模型のある隠しアイテムの id。</summary>
        public static IEnumerable<string> Ids => Shapes.Keys;

        public static bool Has(string itemId)
        {
            return itemId != null && Shapes.ContainsKey(itemId);
        }

        /// <summary>保存先（Assets/Generated/Meshes/HiddenItems/&lt;name&gt;.asset）。name は隠しアイテムの id か、輪・きらめきの名前。</summary>
        public static string MeshPath(string name)
        {
            return MeshFolder + "/" + name + ".asset";
        }

        /// <summary>itemId の模型（無ければ例外。先に <see cref="Has"/> で確かめる）。</summary>
        public static CollectableMeshes.Model For(string itemId)
        {
            if (!Has(itemId))
            {
                throw new ArgumentException("隠しアイテム " + itemId + " の模型が無い", nameof(itemId));
            }

            PartMesh parts = Shapes[itemId]();
            var materials = new Material[parts.Materials.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = MaterialLibrary.EnsureCampus(parts.Materials[i]);
            }

            return new CollectableMeshes.Model
            {
                Mesh = CollectableMeshes.SaveAt(parts.Build(itemId, true), MeshPath(itemId)),
                Materials = materials,
            };
        }

        /// <summary>レア度の光のマテリアル名（MaterialLibrary の mark_&lt;rarity&gt;）。</summary>
        public static string MarkMaterial(string rarity)
        {
            return "mark_" + rarity;
        }

        /// <summary>
        /// 拾い物 item の足元に光る輪、まわりにきらめきを付ける。item は床から lift だけ浮かせて置いてある前提。
        /// 輪は <see cref="CollectableItem.GroundMark"/> にして、模型が上下に揺れても床に留める。
        /// </summary>
        public static void AddRarityMarks(GameObject item, string rarity, float lift)
        {
            Material material = MaterialLibrary.EnsureCampus(MarkMaterial(rarity));

            GameObject ring = AddMark(item.transform, RingName, BuildRing(), material);
            ring.transform.localPosition = Vector3.down * (lift - RingAboveFloor);
            AddMark(item.transform, SparkleName, BuildSparkle(), material);

            item.GetComponent<CollectableItem>().GroundMark = ring.transform;
        }

        private static GameObject AddMark(Transform parent, string name, Mesh mesh, Material material)
        {
            var mark = new GameObject(name);
            mark.transform.SetParent(parent, false);
            mark.layer = parent.gameObject.layer;
            mark.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = mark.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mark;
        }

        /// <summary>床に寝た輪（上から見える面だけ）。</summary>
        private static Mesh BuildRing()
        {
            PartMesh ring = new PartMesh().Lathe(MarkMaterialPlaceholder, new[]
            {
                new Vector2(RingOuter, 0f), new Vector2(RingInner, 0f),
            }, 48, Matrix4x4.identity);
            return CollectableMeshes.SaveAt(ring.Build(RingName, false), MeshPath(RingName));
        }

        /// <summary>
        /// 模型のまわりの 3 つの 4 芒星（縦の 2 枚を十字に組み、どこから見ても星に見える）。
        /// 高さを変えて 120 度おきに置き、模型と一緒にゆっくり回る。
        /// </summary>
        private static Mesh BuildSparkle()
        {
            var sparkle = new PartMesh();
            float[] heights = { 0.12f, -0.06f, 0.04f };
            for (int i = 0; i < heights.Length; i++)
            {
                float a = i * Mathf.PI * 2f / heights.Length;
                var position = new Vector3(Mathf.Cos(a) * SparkleOrbit, heights[i], Mathf.Sin(a) * SparkleOrbit);
                sparkle
                    .Star(MarkMaterialPlaceholder, 4, 0.045f, 0.012f, 0f, PartMesh.At(position, Vector3.zero))
                    .Star(MarkMaterialPlaceholder, 4, 0.045f, 0.012f, 0f, PartMesh.At(position, new Vector3(0f, 90f, 0f)));
            }

            return CollectableMeshes.SaveAt(sparkle.Build(SparkleName, false), MeshPath(SparkleName));
        }
    }
}
