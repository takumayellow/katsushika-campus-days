using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内の窓の外に、キャンパスの遠景が見えることを守る (#60)。
    ///
    /// 屋内はキャンパスから遠く（x = 1200 m 以降）に並べてあるので、何も置かないと窓の外は空と地面の色だけになる。
    /// SceneBuilder（InteriorBackdropStage）は屋内ごとに、パノラマを貼った閉じたドームを置く。ここでは
    /// 置いたシーンを開き、窓のある屋内（図書館 2 階の自習室・寮のラウンジ・寮の食堂）の窓から外へ飛ばした線が
    /// 必ずドームに当たること、近景 ext_* に当たり判定が無いこと、WebGL の予算に収まることを確かめる。
    /// パノラマの PNG も読み、仮のグラデーションのままでなくキャンパスを撮った画であることを確かめる
    /// （SceneBuilder を -nographics で回しただけでは撮れないので、InteriorBackdropStage.Bake を回すまで赤になる）。
    /// </summary>
    public sealed class InteriorBackdropSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>遠景を置く屋内。Assets/Models/Interiors/&lt;id&gt;.json（配置情報）がある 10 棟。</summary>
        private static readonly string[] BuildingIds =
        {
            "greenhouse", "gym", "kyoso", "lab1", "lab2", "lecture", "library", "research1", "research2", DormRoute.Id,
        };

        /// <summary>WebGL の予算: 屋内 1 棟につきパノラマ 2048 x 512 を 1 枚まで。</summary>
        private const int MaxTextureWidth = 2048;
        private const int MaxTextureHeight = 512;

        /// <summary>ドーム 1 つの三角形の上限（今は 1,152）。</summary>
        private const int MaxDomeTriangles = 2000;

        /// <summary>
        /// 近景 ext_* の半径（建物の外皮から、m。#84 の exterior.radius）。InteriorBackdropStage.NearSceneryRadius と同じ値。
        /// 窓から水平より上へ見た遠景は、これより外になければ近景と食い込む。
        /// </summary>
        private const float NearSceneryRadius = 30f;

        /// <summary>近景 ext_* の地面のいちばん低いところ（床から、m。#84 がこれより下の頂点を持ち上げてある）。</summary>
        private const float ExteriorGroundLowest = -0.045f;

        /// <summary>重なる地面どうしに要る段差（m）。これより近いとちらつく。</summary>
        private const float GroundClearance = 0.001f;

        /// <summary>SafetyFloor が建物の AABB より広げる幅（InteriorStage・DormStage の AddSafetyFloor）。</summary>
        private const float SafetyFloorPadding = 20f;

        /// <summary>寮の屋内のローカル原点を spawn_dorm から出すときのずれ（ScenePreview.DormSpawnLocal と同じ）。</summary>
        private static readonly Vector3 DormSpawnLocal = new Vector3(0f, 0f, 4.6f);

        /// <summary>三角形の縁に当たった線を取りこぼさないための、重心座標の余裕。</summary>
        private const float BarycentricSlack = 1e-3f;

        /// <summary>
        /// パノラマの 1 行の、左右（列方向）の明るさの標準偏差（0〜255）の下限。見る行のうち最も大きい値と比べる。
        /// 仮のグラデーション（InteriorBackdropStage.Placeholder）は行ごとに一色なので、どの行も 0。
        /// キャンパスを撮った画は、地面から水平の少し上までに道・芝・建物・木・空が左右に並ぶ。
        /// docs/previews の目の高さの画（campus_mall・campus_lecture・campus_library・dorm_front・dorm_oblique）で、
        /// 画面の下 6 割の行の最大は 39.5〜55.7 あったので、その 5 分の 1 程度に置く。
        /// </summary>
        internal const float MinPanoramaRowStdDev = 8f;

        /// <summary>ばらつきを見る帯の上端の仰角（度）。パノラマの下端（地面）からここまでの行を見る。</summary>
        internal const float PanoramaBandTopDegrees = 10f;

        private Scene _scene;
        private readonly Dictionary<string, Transform> _interiors = new Dictionary<string, Transform>();
        private Transform _outsideGround;
        private Transform _outerGround;

        /// <summary>1 棟ぶんの、ワールドに置いたドームと建物の大きさ。</summary>
        private sealed class Dome
        {
            public InteriorBackdrop Backdrop;
            public Vector3 Centre;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds WorldBounds;
            public Bounds Building;
        }

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    string id = QuestObjectiveLocator.InteriorIdFromName(node.name);
                    if (!string.IsNullOrEmpty(id) && !_interiors.ContainsKey(id))
                    {
                        _interiors.Add(id, node);
                    }

                    if (node.name == "OutsideGround" && _outsideGround == null)
                    {
                        _outsideGround = node;
                    }

                    if (node.name == "OuterGround" && _outerGround == null)
                    {
                        _outerGround = node;
                    }
                }
            }
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        // --- 置いてあるか・予算 ------------------------------------------------------------

        [TestCaseSource(nameof(BuildingIds))]
        public void 屋内ごとに遠景のドームが一つある(string id)
        {
            Transform interior = Interior(id);
            InteriorBackdrop[] backdrops = interior.GetComponentsInChildren<InteriorBackdrop>(true);
            Assert.AreEqual(1, backdrops.Length, id + " の遠景の数（SceneBuilder を回し直したか）");

            InteriorBackdrop backdrop = backdrops[0];
            Assert.AreEqual(id, backdrop.BuildingId, "遠景の建物 id");
            MeshRenderer renderer = backdrop.GetComponent<MeshRenderer>();
            Assert.IsTrue(renderer != null, "ドームに MeshRenderer が無い");
            Assert.AreSame(renderer, backdrop.Target, "時刻の色を掛ける先がドームの Renderer でない");
            Assert.IsTrue(DomeMesh(backdrop) != null, "ドームのメッシュが無い");
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, "ドームが影を落とす");
            Assert.IsFalse(renderer.receiveShadows, "ドームが影を受ける");
            Assert.IsEmpty(backdrop.GetComponentsInChildren<Collider>(true), "ドームに当たり判定がある");
        }

        [TestCaseSource(nameof(BuildingIds))]
        public void パノラマとドームがWebGLの予算に収まる(string id)
        {
            InteriorBackdrop backdrop = Backdrop(id);
            MeshRenderer renderer = backdrop.GetComponent<MeshRenderer>();
            Assert.IsTrue(renderer != null, id + " のドームに MeshRenderer が無い");
            Material material = renderer.sharedMaterial;
            Assert.IsTrue(material != null, id + " のドームにマテリアルが無い");
            Assert.IsNotEmpty(AssetDatabase.GetAssetPath(material), "マテリアルがアセットとして保存されていない");

            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            Assert.IsTrue(texture != null, id + " のパノラマ（_BaseMap）が無い");
            Assert.IsNotEmpty(AssetDatabase.GetAssetPath(texture), "パノラマがアセットとして保存されていない");
            Assert.LessOrEqual(texture.width, MaxTextureWidth, "パノラマの幅");
            Assert.LessOrEqual(texture.height, MaxTextureHeight, "パノラマの高さ");

            Assert.IsTrue(material.HasProperty("_Cull"), "マテリアルに _Cull が無い（URP の Unlit でない）");
            Assert.AreEqual((float)CullMode.Off, material.GetFloat("_Cull"), "ドームを内側から見ると裏面になるので、両面を描く");

            Mesh mesh = DomeMesh(backdrop);
            Assert.IsTrue(mesh != null, id + " のドームのメッシュが無い");
            Assert.LessOrEqual(mesh.triangles.Length / 3, MaxDomeTriangles, "ドームの三角形の数");
        }

        /// <summary>
        /// 貼ってあるパノラマが、仮のグラデーションでなくキャンパスを撮った画であること。
        /// 取り込んだテクスチャは crunch で圧縮され読めないので、PNG を直接読む（可逆なので仮の画の行は一色のまま）。
        /// 地面から仰角 10 度までの行で、左右の明るさのばらつきが最も大きい行を見る。
        /// </summary>
        [TestCaseSource(nameof(BuildingIds))]
        public void パノラマは仮のグラデーションでなくキャンパスを撮った画(string id)
        {
            InteriorBackdrop backdrop = Backdrop(id);
            MeshRenderer renderer = backdrop.GetComponent<MeshRenderer>();
            Material material = renderer != null ? renderer.sharedMaterial : null;
            Assert.IsTrue(material != null, id + " のドームにマテリアルが無い");
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            Assert.IsTrue(texture != null, id + " のパノラマ（_BaseMap）が無い");

            string assetPath = AssetDatabase.GetAssetPath(texture);
            string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            Assert.IsTrue(File.Exists(file), id + " のパノラマの画像ファイルが無い: " + assetPath);

            var image = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(image.LoadImage(File.ReadAllBytes(file)), assetPath + " を読めない");
                int top = PanoramaRow(Mathf.Tan(PanoramaBandTopDegrees * Mathf.Deg2Rad),
                    backdrop.TanBottom, backdrop.TanTop, image.height);
                float spread = MaxRowStdDev(image.GetPixels32(), image.width, 0, top, out int row);
                Assert.GreaterOrEqual(spread, MinPanoramaRowStdDev,
                    id + " のパノラマ " + assetPath + " は、地面から仰角 " + PanoramaBandTopDegrees + " 度まで（行 0〜" + top
                    + "）のどの行も左右でほぼ一色（列方向の明るさの標準偏差は最大で行 " + row + " の " + spread.ToString("F1")
                    + "）。仮のグラデーションのままなので、-nographics を付けずに KCD.Editor.InteriorBackdropStage.Bake で撮る");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        // --- 窓の外 ------------------------------------------------------------------------

        /// <summary>
        /// 窓の前に立って外を見たとき、左右 ±40 度・水平の少し下から 25 度上までのどの向きにもドームがあり、
        /// 水平から上に見えるのは近景の外（建物の外皮から 30 m より先）であること。
        /// 座標は ScenePreview の窓のショットと同じ（寮は dorm.py、図書館は Interior_library の原点からのローカル）。
        /// 向きは 0 度 = +Z（入口から奥）、+90 度 = +X。
        /// </summary>
        [TestCase("library", -30.5f, 6f, 59.5f, 0f, TestName = "窓の外に遠景がある_図書館2階の自習室の北の窓")]
        [TestCase(DormRoute.Id, -2.2f, 1.6f, 17.5f, -90f, TestName = "窓の外に遠景がある_寮のラウンジの西の窓")]
        [TestCase(DormRoute.Id, 2.2f, 1.6f, 17f, 90f, TestName = "窓の外に遠景がある_寮の食堂の東の窓")]
        public void 窓の外に遠景がある(string id, float x, float y, float z, float bearing)
        {
            Dome dome = BuildDome(id);
            Vector3 eye = LocalOrigin(id) + new Vector3(x, y, z);
            Assert.IsTrue(InsideXZ(dome.Building, eye),
                id + " の窓の前の位置 " + eye + " が建物 " + dome.Building + " の外にある（テストの座標が古い）");

            float[] offsets = { -40f, -20f, 0f, 20f, 40f };
            float[] elevations = { -5f, 0f, 10f, 25f };
            foreach (float offset in offsets)
            {
                foreach (float elevation in elevations)
                {
                    // 周方向の継ぎ目（5 度ごと）をちょうどなぞらないよう、少しずらす。
                    Vector3 dir = Direction(bearing + offset + 1.25f, elevation);
                    string label = id + " 向き " + (bearing + offset) + " 度・仰角 " + elevation + " 度";
                    Assert.IsTrue(Raycast(dome, eye, dir, out float distance), label + " にドームが無い（隙間から空が抜ける）");

                    if (elevation < 0f || elevation > 10f)
                    {
                        continue;
                    }

                    Vector3 hit = eye + dir * distance;
                    float fromBuilding = RectDistance(dome.Building, hit);
                    Assert.GreaterOrEqual(fromBuilding, NearSceneryRadius - 0.5f,
                        label + " の遠景が建物から " + fromBuilding.ToString("F1") + " m で、近景 ext_* の内側に食い込む");
                }
            }
        }

        [TestCaseSource(nameof(BuildingIds))]
        public void ドームは屋内をすっぽり囲む(string id)
        {
            Dome dome = BuildDome(id);
            float floor = Interior(id).position.y;
            var eyes = new List<Vector3>
            {
                new Vector3(dome.Centre.x, floor + 1.6f, dome.Centre.z),
                new Vector3(dome.Centre.x, dome.Building.max.y - 0.5f, dome.Centre.z),
            };

            // 建物の四隅（少し内側）の目の高さからも。窓は外周の壁にある。
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = (corner & 1) == 0 ? dome.Building.min.x + 0.5f : dome.Building.max.x - 0.5f;
                float cz = (corner & 2) == 0 ? dome.Building.min.z + 0.5f : dome.Building.max.z - 0.5f;
                eyes.Add(new Vector3(cx, floor + 1.6f, cz));
            }

            float[] elevations = { -85f, -30f, -5f, 0f, 5f, 30f, 60f, 85f };
            foreach (Vector3 eye in eyes)
            {
                for (int column = 0; column < 72; column++)
                {
                    float azimuth = column * 5f + 2.5f;
                    foreach (float elevation in elevations)
                    {
                        Assert.IsTrue(Raycast(dome, eye, Direction(azimuth, elevation), out _),
                            id + " の " + eye + " から方位 " + azimuth + " 度・仰角 " + elevation + " 度にドームが無い");
                    }
                }
            }
        }

        [TestCaseSource(nameof(BuildingIds))]
        public void ドームの地面は近景の地面より下で屋内の並びの地面より上(string id)
        {
            Dome dome = BuildDome(id);
            Transform interior = Interior(id);
            float floor = interior.position.y;
            Assert.Less(dome.WorldBounds.min.y, floor + ExteriorGroundLowest - GroundClearance,
                id + " のドームの地面が近景 ext_* の地面より上にあり、近景を隠す");
            Assert.Greater(dome.WorldBounds.max.y, dome.Building.max.y + 1f, id + " のドームが建物より低い");

            // #84 の近景が入っていれば、その実物の地面とも比べる。
            foreach (MeshFilter filter in interior.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && InteriorBackdrop.IsExteriorDressing(filter.gameObject.name))
                {
                    Assert.Greater(MeshWorldBounds(filter).min.y, dome.WorldBounds.min.y + GroundClearance,
                        id + " の近景 " + filter.gameObject.name + " がドームの地面より下にもぐる");
                }
            }

            // キャンパスの OuterGround（x ±2000 m）は屋内の並びの前半の下にも広がっている。ドームの地面が
            // その下にあると、窓の外の地面がパノラマでなく一色の芝になる。
            AssertAboveGround(dome, _outerGround, GroundClearance, id + " のドームの地面が OuterGround の下に隠れる");
            AssertAboveGround(dome, _outsideGround, 0.01f, id + " のドームの地面が OutsideGround と同じ高さで、ちらつく");
        }

        private static void AssertAboveGround(Dome dome, Transform ground, float clearance, string message)
        {
            if (ground == null)
            {
                return;
            }

            Bounds plane = MeshWorldBounds(ground.GetComponent<MeshFilter>());
            if (OverlapsXZ(plane, dome.WorldBounds))
            {
                Assert.Greater(dome.WorldBounds.min.y, plane.max.y + clearance, message);
            }
        }

        [Test]
        public void となりのドームと重ならない()
        {
            var domes = new List<Dome>();
            foreach (string id in BuildingIds)
            {
                domes.Add(BuildDome(id));
            }

            for (int i = 0; i < domes.Count; i++)
            {
                for (int j = i + 1; j < domes.Count; j++)
                {
                    Assert.IsFalse(OverlapsXZ(domes[i].WorldBounds, domes[j].WorldBounds),
                        domes[i].Backdrop.BuildingId + " と " + domes[j].Backdrop.BuildingId
                        + " のドームが重なる（エディタでは全部見えるので、地面と帯がちらつく）");
                }
            }
        }

        // --- 近景 ext_* ----------------------------------------------------------------------

        [Test]
        public void 近景に当たり判定が無い()
        {
            int exteriors = 0;
            foreach (KeyValuePair<string, Transform> pair in _interiors)
            {
                foreach (Transform node in pair.Value.GetComponentsInChildren<Transform>(true))
                {
                    if (!InteriorBackdrop.IsExteriorDressing(node.name))
                    {
                        continue;
                    }

                    exteriors++;
                    Assert.IsEmpty(node.GetComponentsInChildren<Collider>(true),
                        pair.Key + " の近景 " + node.name + " に当たり判定がある（窓の外へは出られないので、描くだけにする）");
                }
            }

            if (exteriors == 0)
            {
                Assert.Pass("屋内に近景 ext_* がまだ無い（#84 の FBX が入ると確かめられる）。");
            }
        }

        [TestCaseSource(nameof(BuildingIds))]
        public void 近景は屋内の大きさに数えない(string id)
        {
            // SafetyFloor の大きさは、Dress が数えた屋内の AABB に 20 m 足したもの（InteriorStage・DormStage の AddSafetyFloor）。
            // box.size はその AABB の大きさをそのまま入れてあるので、ワールドへは写さずに比べる。
            Transform interior = Interior(id);
            Transform floor = FindChild(interior, "SafetyFloor");
            Assert.IsTrue(floor != null, id + " に SafetyFloor が無い");
            BoxCollider box = floor.GetComponent<BoxCollider>();
            Assert.IsTrue(box != null, id + " の SafetyFloor に BoxCollider が無い");

            Bounds building = BuildingBounds(interior);
            Assert.LessOrEqual(box.size.x, building.size.x + SafetyFloorPadding + 0.5f,
                id + " の SafetyFloor の幅が建物より広い（近景 ext_* を屋内の大きさに数えている）");
            Assert.LessOrEqual(box.size.z, building.size.z + SafetyFloorPadding + 0.5f,
                id + " の SafetyFloor の奥行きが建物より広い（近景 ext_* を屋内の大きさに数えている）");
        }

        // --- 下ごしらえ ----------------------------------------------------------------------

        private Transform Interior(string id)
        {
            Assert.IsTrue(_interiors.TryGetValue(id, out Transform interior),
                ScenePath + " に " + QuestObjectiveLocator.InteriorPrefix + id + " が無い");
            return interior;
        }

        private InteriorBackdrop Backdrop(string id)
        {
            InteriorBackdrop backdrop = Interior(id).GetComponentInChildren<InteriorBackdrop>(true);
            Assert.IsTrue(backdrop != null, id + " に遠景が無い（SceneBuilder を回し直したか）");
            return backdrop;
        }

        /// <summary>ScenePreview の窓のショットと同じローカル原点。寮は spawn_dorm から逆算する。</summary>
        private Vector3 LocalOrigin(string id)
        {
            Transform interior = Interior(id);
            if (id != DormRoute.Id)
            {
                return interior.position;
            }

            Transform spawn = FindChild(interior, "spawn_" + DormRoute.Id);
            return spawn != null ? spawn.position - DormSpawnLocal : interior.position;
        }

        private Dome BuildDome(string id)
        {
            InteriorBackdrop backdrop = Backdrop(id);
            Mesh mesh = DomeMesh(backdrop);
            Assert.IsTrue(mesh != null, id + " のドームのメッシュが無い");

            Matrix4x4 toWorld = backdrop.transform.localToWorldMatrix;
            Vector3[] local = mesh.vertices;
            var world = new Vector3[local.Length];
            var bounds = new Bounds(toWorld.MultiplyPoint3x4(local[0]), Vector3.zero);
            for (int i = 0; i < local.Length; i++)
            {
                world[i] = toWorld.MultiplyPoint3x4(local[i]);
                bounds.Encapsulate(world[i]);
            }

            return new Dome
            {
                Backdrop = backdrop,
                Centre = backdrop.transform.position,
                Vertices = world,
                Triangles = mesh.triangles,
                WorldBounds = bounds,
                Building = BuildingBounds(Interior(id)),
            };
        }

        private static Mesh DomeMesh(InteriorBackdrop backdrop)
        {
            MeshFilter filter = backdrop.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>建物のワールドの AABB。近景 ext_* と遠景のドームは入れない（InteriorStage.CountsAsBuilding と同じ）。</summary>
        private static Bounds BuildingBounds(Transform interior)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in interior.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null
                    || InteriorBackdrop.IsExteriorDressing(filter.gameObject.name)
                    || filter.GetComponent<InteriorBackdrop>() != null)
                {
                    continue;
                }

                Bounds part = MeshWorldBounds(filter);
                if (!any)
                {
                    bounds = part;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(part);
                }
            }

            Assert.IsTrue(any, interior.name + " に建物のメッシュが無い");
            return bounds;
        }

        private static Bounds MeshWorldBounds(MeshFilter filter)
        {
            Assert.IsTrue(filter != null, "MeshFilter が無い");
            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            var bounds = new Bounds(toWorld.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var p = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);
                bounds.Encapsulate(toWorld.MultiplyPoint3x4(p));
            }

            return bounds;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform node in parent.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == name)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>方位（0 度 = +Z、+90 度 = +X）と仰角（度）の向き。</summary>
        private static Vector3 Direction(float azimuth, float elevation)
        {
            float a = azimuth * Mathf.Deg2Rad;
            float e = elevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        private static bool InsideXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;
        }

        /// <summary>二つの AABB が上から見て重なるか。</summary>
        private static bool OverlapsXZ(Bounds a, Bounds b)
        {
            return a.min.x < b.max.x && b.min.x < a.max.x && a.min.z < b.max.z && b.min.z < a.max.z;
        }

        /// <summary>水平の、矩形（AABB の xz）からの距離。</summary>
        private static float RectDistance(Bounds bounds, Vector3 point)
        {
            float dx = Mathf.Max(Mathf.Abs(point.x - bounds.center.x) - bounds.extents.x, 0f);
            float dz = Mathf.Max(Mathf.Abs(point.z - bounds.center.z) - bounds.extents.z, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// tan(仰角) が入るパノラマの行（0 = 下端）。行 j の中心は tanBottom + (j + 0.5) / height * (tanTop - tanBottom)
        /// （InteriorBackdropStage.Stitch と同じ割り当て）。範囲の外は端の行にする。
        /// </summary>
        internal static int PanoramaRow(float tan, float tanBottom, float tanTop, int height)
        {
            float span = tanTop - tanBottom;
            if (span <= 0f)
            {
                return height - 1;
            }

            int row = Mathf.FloorToInt((tan - tanBottom) / span * height);
            return Mathf.Clamp(row, 0, height - 1);
        }

        /// <summary>
        /// firstRow〜lastRow の各行で、列方向の明るさ（sRGB の 0〜255 に Rec. 709 の重み）の標準偏差を求め、
        /// 最も大きい値とその行を返す。画素は行 0 = 下端の並び（Texture2D.GetPixels32 と同じ）。
        /// </summary>
        internal static float MaxRowStdDev(Color32[] pixels, int width, int firstRow, int lastRow, out int maxRow)
        {
            maxRow = firstRow;
            double best = 0.0;
            for (int row = firstRow; row <= lastRow; row++)
            {
                double sum = 0.0;
                double squares = 0.0;
                int start = row * width;
                for (int column = 0; column < width; column++)
                {
                    Color32 c = pixels[start + column];
                    double luminance = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    sum += luminance;
                    squares += luminance * luminance;
                }

                double mean = sum / width;
                double variance = System.Math.Max(squares / width - mean * mean, 0.0);
                if (variance > best)
                {
                    best = variance;
                    maxRow = row;
                }
            }

            return (float)System.Math.Sqrt(best);
        }

        /// <summary>
        /// ドームの三角形（両面）に線が当たるか（Möller–Trumbore）。いちばん近い当たりまでの距離を返す。
        /// 大きな x（1200〜4000 m）で桁が落ちないよう、ドームの中心からの相対で計算する。
        /// </summary>
        private static bool Raycast(Dome dome, Vector3 origin, Vector3 dir, out float distance)
        {
            distance = float.MaxValue;
            bool hit = false;
            Vector3 o = origin - dome.Centre;
            int[] tris = dome.Triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = dome.Vertices[tris[i]] - dome.Centre;
                Vector3 e1 = dome.Vertices[tris[i + 1]] - dome.Centre - a;
                Vector3 e2 = dome.Vertices[tris[i + 2]] - dome.Centre - a;
                Vector3 p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-8f)
                {
                    continue;
                }

                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < -BarycentricSlack || u > 1f + BarycentricSlack)
                {
                    continue;
                }

                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < -BarycentricSlack || u + v > 1f + BarycentricSlack)
                {
                    continue;
                }

                float t = Vector3.Dot(e2, q) * inv;
                if (t > 1e-3f && t < distance)
                {
                    distance = t;
                    hit = true;
                }
            }

            return hit;
        }
    }
}
