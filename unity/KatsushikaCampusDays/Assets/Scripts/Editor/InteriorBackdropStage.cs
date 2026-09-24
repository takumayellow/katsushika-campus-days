using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KCD.Editor
{
    /// <summary>
    /// 屋内の窓の外に見えるキャンパスの遠景 (#60)。
    ///
    /// 屋内はキャンパスから遠く（x = 1200 m 以降）に置くので、窓の外には本物のキャンパスが無い。
    /// そこで、その建物がキャンパスで建っている位置と向きから、目の高さ 1.6 m で 360 度を 30 度ずつ 12 枚撮り、
    /// 2048 x 512 のパノラマ 1 枚に焼いて、屋内を囲む閉じたドームに貼る。
    ///   ドーム = 床の下の地面の円盤（建物の外周から帯の足もとまで）+ 外周から 36 m 外の円筒の帯 + ふた
    /// 窓のすぐ外の近景 ext_*（外周から約 30 m, #84）はドームの内側に収まり、ドームの地面はその地面より下に敷く。
    /// 地面も帯も「ドームの中心の目から見た向き」でパノラマを引くので、中心から見れば実物と同じ向きに見える。
    ///
    /// 低ポリの建物を並べ直す案（案 B）は採らない。キャンパスの 9 棟の屋内だけで三角形が 296,725 あり、
    /// 屋内 1 棟あたり 30 万の枠に 3,275 しか残らないため。ドームは 1 棟 1,152 三角形・描画 1 回で済む。
    ///
    /// SceneBuilder はふつう -nographics で回るので、その場合は撮れない。前に撮った PNG があればそれを使い、
    /// 無ければ空と地面のグラデーションを仮に置く。撮り直しは <see cref="Bake"/>（-nographics を付けずに回す）:
    ///   Unity.exe -batchmode -quit -projectPath &lt;...&gt; -executeMethod KCD.Editor.InteriorBackdropStage.Bake
    /// Bake はシーンを開いて撮り、同じパスの PNG を上書きするだけで、シーンは保存しない（GUID は変わらない）。
    /// </summary>
    public static partial class InteriorBackdropStage
    {
        /// <summary>パノラマ・マテリアル・ドームのメッシュの置き場。</summary>
        public const string Folder = EditorPaths.GeneratedFolder + "/Backdrops";

        /// <summary>パノラマの大きさ。WebGL の予算で、屋内 1 棟につき 2048 x 512 を 1 枚まで。</summary>
        public const int TextureWidth = 2048;
        public const int TextureHeight = 512;

        /// <summary>
        /// 帯を建物の外周からどれだけ外に立てるか（m）。近景 ext_*（外周から 30 m まで・木の樹冠は数 m はみ出す）より外で、
        /// 隣の屋内との隙間（InteriorStage の Gap = 80 m）の半分より内側。エディタでは全棟のドームが見えるので、
        /// 半分を超えると隣のドームと食い込む。
        /// </summary>
        public const float Margin = 36f;

        /// <summary>撮る目の高さ（地面から、m）。ドームの中心の目も同じ高さにそろえる。</summary>
        public const float EyeHeight = 1.6f;

        /// <summary>
        /// 近景 ext_* の半径（建物の外皮から、m。#84 の exterior.radius）。屋内に近景があるときは、この範囲に
        /// すっぽり入るもの（木・花壇・隣の棟・自分の棟の庇など）をパノラマに写さない。写すと、中心から外れた位置で
        /// 立体の近景と帯に描かれた同じものが二重に見える。
        /// </summary>
        public const float NearSceneryRadius = 30f;

        /// <summary>パノラマの上端の仰角（度）。それより上はふたで、上端の色（空）一色になる。</summary>
        public const float TopDegrees = 40f;

        /// <summary>
        /// ドームの地面の高さ（屋内の床面から、m）。近景 ext_* の地面（-0.03〜-0.055, #84）より下で、
        /// 屋内の並びの下の地面（InteriorStage.OutsideGroundY = -0.10）より上。
        /// </summary>
        public const float GroundY = -0.07f;

        /// <summary>パノラマの下端を決める距離の下限（m）。小さな建物で真下まで撮らないように。</summary>
        public const float MinGroundReach = 6f;

        /// <summary>ドームの周方向の分割数（5 度ずつ）。</summary>
        public const int Columns = 72;

        /// <summary>地面の輪の数（建物の外周から帯の足もとまで）。</summary>
        public const int GroundRings = 6;

        /// <summary>1 周を何枚で撮るか（30 度ずつ）。</summary>
        public const int ShotCount = 12;

        /// <summary>撮るときの解像度（tan 1 あたりの画素）。パノラマの水平方向の 2 倍の細かさ。</summary>
        private const float PixelsPerTan = 652f;

        /// <summary>1 枚の周りに足す余白（画素）。バイリニアで端を引くため。</summary>
        private const float PadPixels = 4f;

        /// <summary>パノラマ 1 画素を何 x 何で平均するか。</summary>
        private const int Supersample = 2;

        /// <summary>撮るカメラのニアとファー。ファーは本編カメラと同じ。</summary>
        private const float CaptureNear = 0.3f;
        private const float CaptureFar = 900f;

        /// <summary>これより x が大きい描画は屋内の並び（x = 1200 m 以降）なので、撮るときは消す。</summary>
        private const float InteriorSlotX = 900f;

        private const string RigName = "__InteriorBackdropCamera";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        private const int CrunchQuality = 70;

        /// <summary>撮るときに写さないレイヤー（プレイヤー・NPC・UI）。</summary>
        private static readonly string[] HiddenLayers = { "Player", "NPC", "UI" };

        /// <summary>
        /// 近景 ext_* の元（kcd_lib.site の屋外）と同じものを持つ親。CampusStage.Build が campus.fbx を "Campus"、
        /// trees.fbx を "Trees" として置く。近景の範囲で写さないのはこの下のものだけにし、Unity で足したベンチなどは残す。
        /// </summary>
        private static readonly string[] SiteRoots = { "Campus", "Trees" };

        /// <summary>描けたかどうかの目印の色。撮る前に毎回これで塗る（ScenePreview と同じやり方）。</summary>
        private static readonly Color MarkerColor = new Color(1f, 0f, 1f);

        /// <summary>1 棟ぶんの撮り方。</summary>
        private struct Rig
        {
            public string Id;
            public Vector3 Eye;
            public float Yaw;
            public float TanBottom;
            public float TanTop;
            public Vector2 NearHalf;
            public float NearRadius;
        }

        /// <summary>撮る 1 枚の画角（tan で表した左右上下の端）と画素数。</summary>
        private struct ShotFrame
        {
            public float Half;
            public float Bottom;
            public float Top;
            public int Width;
            public int Height;
        }

        /// <summary>ドームの形（ログ用）。</summary>
        private struct DomeShape
        {
            public Mesh Mesh;
            public float MinReach;
            public float MaxReach;
            public float MaxTop;
        }

        /// <summary>
        /// 屋内 1 棟に遠景を置く。bounds は屋内のワールドの AABB（スロットへずらしたあと）。
        /// 屋内のローカル座標（= ワールド − 屋内の原点）は Blender の (x, y) で、サイドカー
        /// Assets/Models/Interiors/&lt;id&gt;.json の entrance_world / yaw_deg でキャンパスへ写せる。
        /// </summary>
        public static InteriorBackdrop Build(Transform interior, string id, Bounds bounds)
        {
            if (!ReadSidecar(id, out Vector2 entrance, out float yaw))
            {
                return null;
            }

            float halfX = Mathf.Max(bounds.extents.x, 1f);
            float halfZ = Mathf.Max(bounds.extents.z, 1f);
            float reach = Mathf.Max(Mathf.Min(halfX, halfZ), MinGroundReach);
            var centre = new Vector3(bounds.center.x, interior.position.y, bounds.center.z);

            var rig = new Rig
            {
                Id = id,
                Eye = CampusEye(entrance, yaw, centre - interior.position),
                Yaw = yaw,
                TanBottom = -EyeHeight / reach,
                TanTop = Mathf.Tan(TopDegrees * Mathf.Deg2Rad),
                NearHalf = new Vector2(halfX, halfZ),
                NearRadius = HasExteriorDressing(interior) ? NearSceneryRadius : 0f,
            };

            EditorPaths.EnsureFolder(Folder);
            DomeShape dome = BuildDome(halfX, halfZ, rig.TanBottom, rig.TanTop);
            Mesh mesh = SaveMesh(dome.Mesh, Folder + "/" + id + "_dome.asset");
            string source;
            Texture2D texture = EnsureTexture(rig, out source);
            Material material = EnsureMaterial(id, texture);

            var go = new GameObject("Backdrop");
            go.transform.SetParent(interior, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            Vector3 parentScale = interior.lossyScale;
            go.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            CampusStage.Ignore(go);

            InteriorBackdrop backdrop = go.AddComponent<InteriorBackdrop>();
            backdrop.BuildingId = id;
            backdrop.Target = renderer;
            backdrop.CampusEye = rig.Eye;
            backdrop.YawDegrees = rig.Yaw;
            backdrop.TanBottom = rig.TanBottom;
            backdrop.TanTop = rig.TanTop;
            backdrop.NearHalfSize = rig.NearHalf;
            backdrop.NearRadius = rig.NearRadius;

            EditorPaths.Report("遠景 " + id + ": 目 " + Format(rig.Eye) + " 向き " + F(rig.Yaw, "F1") + " 度"
                + " 帯は外周の " + F(Margin, "F0") + " m 外（中心から " + F(dome.MinReach, "F0") + ".."
                + F(dome.MaxReach, "F0") + " m・高さ " + F(dome.MaxTop, "F0") + " m まで）"
                + " 下端 " + F(Mathf.Atan(rig.TanBottom) * Mathf.Rad2Deg, "F1") + " 度"
                + (rig.NearRadius > 0f ? " 近景あり（外周 " + F(rig.NearRadius, "F0") + " m 以内は写さない）" : " 近景なし")
                + " 三角形 " + (mesh.triangles.Length / 3) + " 画 = " + source);
            return backdrop;
        }

        /// <summary>
        /// 前に置いた遠景を、シーンに残っている撮り方のまま撮り直して PNG を上書きする。
        /// RenderTexture が要るので -nographics では動かない。シーンは開くだけで保存しない。
        /// </summary>
        [MenuItem("KCD/屋内の遠景を撮り直す")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorPaths.Report("再生中はシーンを開けないので、遠景は撮り直せません。再生を止めてからにしてください。");
                return;
            }

            if (!CanRender())
            {
                EditorPaths.Report("-nographics では遠景を撮れません。-nographics を付けずに回してください。");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorPaths.Report("シーンを開けないので遠景を撮り直せません: " + EditorPaths.CampusScene);
                return;
            }

            int taken = 0;
            int failed = 0;
            foreach (InteriorBackdrop backdrop in Object.FindObjectsByType<InteriorBackdrop>(FindObjectsInactive.Include))
            {
                var rig = new Rig
                {
                    Id = backdrop.BuildingId,
                    Eye = backdrop.CampusEye,
                    Yaw = backdrop.YawDegrees,
                    TanBottom = backdrop.TanBottom,
                    TanTop = backdrop.TanTop,
                    NearHalf = backdrop.NearHalfSize,
                    NearRadius = backdrop.NearRadius,
                };

                Color32[] pixels = TryCapture(rig);
                if (pixels == null)
                {
                    failed++;
                    continue;
                }

                string path = TexturePath(rig.Id);
                WritePng(path, pixels);
                ImportTexture(path);
                taken++;
                EditorPaths.Report("遠景 " + rig.Id + " を撮り直しました: " + path);
            }

            AssetDatabase.SaveAssets();
            EditorPaths.Report("遠景を " + taken + " 棟撮り直しました" + (failed > 0 ? "（" + failed + " 棟は撮れず、前の画のまま）" : "。"));
            EditorPaths.AppendVerify("backdrop bake " + taken + " taken, " + failed + " failed");
        }

        /// <summary>サイドカーから入口のワールド位置（x, z）と、屋内の +Z がキャンパスで向く方位（度）を読む。</summary>
        private static bool ReadSidecar(string id, out Vector2 entrance, out float yaw)
        {
            entrance = Vector2.zero;
            yaw = 0f;
            string path = InteriorStage.ModelsFolder + "/" + id + ".json";
            string absolute = EditorPaths.Absolute(path);
            if (!File.Exists(absolute))
            {
                EditorPaths.Report("屋内 " + id + " の配置情報が無いので、窓の外の遠景を置きません: " + path);
                return false;
            }

            var root = MiniJson.Deserialize(File.ReadAllText(absolute)) as Dictionary<string, object>;
            Dictionary<string, object> world = MiniJson.GetObject(root, "entrance_world");
            if (world == null)
            {
                EditorPaths.Report("屋内 " + id + " の配置情報に entrance_world が無いので、遠景を置きません。");
                return false;
            }

            entrance = new Vector2(MiniJson.GetFloat(world, "x"), MiniJson.GetFloat(world, "z"));
            yaw = MiniJson.GetFloat(root, "yaw_deg");
            return true;
        }

        /// <summary>
        /// 屋内のローカル（Blender の (x, y) = Unity の (x, z)）の点を、キャンパスのワールドへ写した目の位置。
        /// ローカル原点は入口の真下の床で、+Z（入口から奥）はキャンパスで方位 yaw を向く（spec.py の dump_sidecar）。
        /// </summary>
        private static Vector3 CampusEye(Vector2 entrance, float yaw, Vector3 local)
        {
            Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * new Vector3(local.x, 0f, local.z);
            var eye = new Vector3(entrance.x + flat.x, 0f, entrance.y + flat.z);
            float ground = CampusGround(new Vector3(entrance.x, 0f, entrance.y));
            eye.y = ground + EyeHeight;
            return eye;
        }

        /// <summary>屋内に窓の外の近景 ext_* があるか（#84 の FBX から入る）。</summary>
        private static bool HasExteriorDressing(Transform interior)
        {
            foreach (Renderer renderer in interior.GetComponentsInChildren<Renderer>(true))
            {
                if (InteriorBackdrop.IsExteriorDressing(renderer.gameObject.name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>入口の足もとの地面の高さ。Ground の当たり判定が無ければ 0。</summary>
        private static float CampusGround(Vector3 point)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
            Physics.SyncTransforms();
            var from = new Vector3(point.x, 300f, point.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 600f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : 0f;
        }

        // ---- ドーム ----

        /// <summary>
        /// 閉じたドーム（ローカル原点 = 屋内の中心の床面）。u = 方位 atan2(x, z) / 2π、
        /// v は中心の目（GroundY + EyeHeight）から見た tan(仰角) をパノラマの下端〜上端に割り当てたもの。
        /// 地面の円盤は建物の外周から帯の足もとまで輪で刻み、外周より内側は床の下なので 1 枚の扇でふさぐ。
        /// </summary>
        private static DomeShape BuildDome(float halfX, float halfZ, float tanBottom, float tanTop)
        {
            float eyeY = GroundY + EyeHeight;
            int ring = Columns + 1;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var shape = new DomeShape { MinReach = float.MaxValue, MaxReach = 0f, MaxTop = 0f };

            var inner = new float[ring];
            var outer = new float[ring];
            var dirs = new Vector2[ring];
            for (int i = 0; i < ring; i++)
            {
                float phi = (i % Columns) * (2f * Mathf.PI / Columns);
                dirs[i] = new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));
                inner[i] = RectReach(halfX, halfZ, dirs[i]);
                outer[i] = OuterReach(halfX, halfZ, dirs[i], Margin);
                shape.MinReach = Mathf.Min(shape.MinReach, outer[i]);
                shape.MaxReach = Mathf.Max(shape.MaxReach, outer[i]);
            }

            // 地面の輪。k = 0 が建物の外周、k = GroundRings が帯の足もと。1/r が等間隔になるように刻む
            // （v は -EyeHeight / r なので、輪ごとに v がそろって並ぶ）。
            for (int k = 0; k <= GroundRings; k++)
            {
                for (int i = 0; i < ring; i++)
                {
                    float invR = Mathf.Lerp(1f / inner[i], 1f / outer[i], (float)k / GroundRings);
                    float r = 1f / invR;
                    vertices.Add(new Vector3(dirs[i].x * r, GroundY, dirs[i].y * r));
                    uvs.Add(new Vector2((float)i / Columns, V(-EyeHeight / r, tanBottom, tanTop)));
                }
            }

            // 帯の上端。v = 1。
            int topStart = vertices.Count;
            for (int i = 0; i < ring; i++)
            {
                float top = eyeY + outer[i] * tanTop;
                shape.MaxTop = Mathf.Max(shape.MaxTop, top);
                vertices.Add(new Vector3(dirs[i].x * outer[i], top, dirs[i].y * outer[i]));
                uvs.Add(new Vector2((float)i / Columns, 1f));
            }

            for (int k = 0; k < GroundRings; k++)
            {
                for (int i = 0; i < Columns; i++)
                {
                    int a = k * ring + i;
                    int b = (k + 1) * ring + i;
                    Quad(triangles, a, b, b + 1, a + 1);
                }
            }

            int footStart = GroundRings * ring;
            for (int i = 0; i < Columns; i++)
            {
                Quad(triangles, footStart + i, topStart + i, topStart + i + 1, footStart + i + 1);
            }

            // 外周より内側（床の下）の扇と、上のふた。中心の頂点は u をそろえるため三角形ごとに持つ。
            for (int i = 0; i < Columns; i++)
            {
                float u = (i + 0.5f) / Columns;
                int centre = vertices.Count;
                vertices.Add(new Vector3(0f, GroundY, 0f));
                uvs.Add(new Vector2(u, 0f));
                triangles.Add(centre);
                triangles.Add(i);
                triangles.Add(i + 1);

                int apex = vertices.Count;
                vertices.Add(new Vector3(0f, shape.MaxTop, 0f));
                uvs.Add(new Vector2(u, 1f));
                triangles.Add(apex);
                triangles.Add(topStart + i + 1);
                triangles.Add(topStart + i);
            }

            var mesh = new Mesh { name = "backdrop_dome" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            shape.Mesh = mesh;
            return shape;
        }

        private static void Quad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        /// <summary>tan(仰角) をパノラマの v（0 = 下端, 1 = 上端）へ。</summary>
        private static float V(float tan, float tanBottom, float tanTop)
        {
            return Mathf.Clamp01((tan - tanBottom) / (tanTop - tanBottom));
        }

        /// <summary>中心から dir（x, z）の向きに、半幅 halfX, halfZ の矩形の縁までの距離。</summary>
        private static float RectReach(float halfX, float halfZ, Vector2 dir)
        {
            float ax = Mathf.Abs(dir.x);
            float az = Mathf.Abs(dir.y);
            float rx = ax > 1e-6f ? halfX / ax : float.MaxValue;
            float rz = az > 1e-6f ? halfZ / az : float.MaxValue;
            return Mathf.Min(rx, rz);
        }

        /// <summary>中心から dir の向きに、矩形からの距離がちょうど margin になるところまでの距離（二分法）。</summary>
        private static float OuterReach(float halfX, float halfZ, Vector2 dir, float margin)
        {
            float lo = RectReach(halfX, halfZ, dir);
            float hi = Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + margin;
            for (int n = 0; n < 40; n++)
            {
                float mid = 0.5f * (lo + hi);
                float dx = Mathf.Max(Mathf.Abs(dir.x * mid) - halfX, 0f);
                float dz = Mathf.Max(Mathf.Abs(dir.y * mid) - halfZ, 0f);
                if (dx * dx + dz * dz < margin * margin)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return 0.5f * (lo + hi);
        }

        /// <summary>
        /// メッシュをアセットに書く。すでにあれば中身だけ入れ替え、GUID（シーンからの参照）を保つ。
        /// CollectableMeshes.Save と同じ作りだが、UV も写す。
        /// </summary>
        private static Mesh SaveMesh(Mesh built, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            existing.Clear();
            existing.name = built.name;
            existing.SetVertices(built.vertices);
            existing.SetUVs(0, built.uv);
            existing.SetTriangles(built.triangles, 0);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            return existing;
        }

        // ---- テクスチャとマテリアル ----

        private static string TexturePath(string id)
        {
            return Folder + "/" + id + ".png";
        }

        /// <summary>撮れるなら撮る。撮れなければ前の PNG、それも無ければ仮のグラデーション。</summary>
        private static Texture2D EnsureTexture(Rig rig, out string source)
        {
            string path = TexturePath(rig.Id);
            Color32[] pixels = CanRender() ? TryCapture(rig) : null;
            if (pixels != null)
            {
                WritePng(path, pixels);
                source = "撮影";
            }
            else if (File.Exists(EditorPaths.Absolute(path)))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                source = "前の画（撮り直しは InteriorBackdropStage.Bake）";
            }
            else
            {
                WritePng(path, Placeholder(rig.TanBottom, rig.TanTop));
                source = "仮のグラデーション（InteriorBackdropStage.Bake で撮る）";
            }

            return ImportTexture(path);
        }

        private static void WritePng(string path, Color32[] pixels)
        {
            var texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGB24, false, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                File.WriteAllBytes(EditorPaths.Absolute(path), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// 取り込み設定。横は 1 周でつながるので Repeat、縦は Clamp。WebGL 向けに crunch で圧縮する
        /// （DXT1 + ミップで 1 枚 約 683 KB の VRAM）。
        /// </summary>
        private static Texture2D ImportTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                EditorPaths.Report("遠景のテクスチャを取り込めません: " + path);
                return null;
            }

            bool stale = importer.textureType != TextureImporterType.Default
                || !importer.sRGBTexture
                || !importer.mipmapEnabled
                || importer.wrapModeU != TextureWrapMode.Repeat
                || importer.wrapModeV != TextureWrapMode.Clamp
                || importer.filterMode != FilterMode.Bilinear
                || importer.maxTextureSize != TextureWidth
                || importer.alphaSource != TextureImporterAlphaSource.None
                || importer.textureCompression != TextureImporterCompression.Compressed
                || !importer.crunchedCompression
                || importer.compressionQuality != CrunchQuality;
            if (stale)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = TextureWidth;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.crunchedCompression = true;
                importer.compressionQuality = CrunchQuality;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>URP の Unlit。光も影も受けず、パノラマの色をそのまま出す。両面を描く。</summary>
        private static Material EnsureMaterial(string id, Texture2D texture)
        {
            string path = Folder + "/" + id + ".mat";
            Shader shader = Shader.Find(UnlitShader);
            if (shader == null)
            {
                EditorPaths.Report("シェーダが見つかりません: " + UnlitShader);
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "backdrop_" + id };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>撮れないときの仮の画。水平より下は地面の色、上は地平の白から空の青へ。</summary>
        private static Color32[] Placeholder(float tanBottom, float tanTop)
        {
            var ground = new Color32(104, 118, 92, 255);
            var horizon = new Color32(214, 222, 228, 255);
            var zenith = new Color32(118, 158, 212, 255);
            var pixels = new Color32[TextureWidth * TextureHeight];
            for (int j = 0; j < TextureHeight; j++)
            {
                float t = tanBottom + (j + 0.5f) / TextureHeight * (tanTop - tanBottom);
                Color32 color = t < 0f ? ground : Color32.Lerp(horizon, zenith, Mathf.Sqrt(t / tanTop));
                for (int i = 0; i < TextureWidth; i++)
                {
                    pixels[j * TextureWidth + i] = color;
                }
            }

            return pixels;
        }

        private static string Format(Vector3 value)
        {
            return "(" + F(value.x, "F1") + ", " + F(value.y, "F1") + ", " + F(value.z, "F1") + ")";
        }

        private static string F(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
