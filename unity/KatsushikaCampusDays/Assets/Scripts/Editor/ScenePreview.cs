using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KCD.Editor
{
    /// <summary>
    /// 本番のライティング（Sun + 霧 + 屋内の点光源）とトゥーンシェーダ・ポストプロセスのまま、
    /// Campus.unity からスクリーンショットを撮る。
    /// Blender のレンダーはライティングもシェーダも本編と違うので、紹介用の画はこちらで撮る (#41)。
    ///
    /// 使い方（メインセッションがバッチで回す。RenderTexture が要るので **-nographics は付けない**）:
    ///   Unity.exe -batchmode -quit -projectPath &lt;...&gt; -executeMethod KCD.Editor.ScenePreview.Capture
    /// 足せる引数:
    ///   -previewShots shot_dorm_hall,shot_dorm_interior  撮る画を絞る（部分一致・既定は全部）
    ///   -previewInteriorFill 1.2                         屋内が暗すぎたときだけ補助光を足す（既定 0 = 足さない）
    ///
    /// シーンは開くだけで **保存しない**。撮影のために作った GameObject には HideFlags.DontSave を付け、
    /// 撮り終わったら必ず捨てる（finally）。一時的に点け直したライトも元の状態へ戻す。
    /// </summary>
    public static class ScenePreview
    {
        /// <summary>出力先。Assets の親からの相対なので、リポジトリ直下の docs/previews になる。</summary>
        public const string OutputFolder = "../../docs/previews";

        /// <summary>書き出す PNG の大きさ。</summary>
        public const int Width = 1600;
        public const int Height = 900;

        /// <summary>既定の画角。ActorFactory.CreateCamera の本編カメラと同じ 55 度（縦）。</summary>
        public const float DefaultFov = 55f;

        /// <summary>ニア・ファーも本編カメラに合わせる。</summary>
        public const float NearClip = 0.12f;
        public const float FarClip = 900f;

        /// <summary>撮影用に作るものの名前。後片付けで探せるよう定数にしておく。</summary>
        private const string RigName = "__ScenePreviewCamera";
        private const string FillName = "__ScenePreviewInteriorFill";

        /// <summary>地面の高さが分からなかったときの目印。</summary>
        private const float UnknownGround = -1000f;

        /// <summary>座標の読み方。</summary>
        private enum Frame
        {
            /// <summary>ワールド座標そのまま。</summary>
            World,

            /// <summary>
            /// 寮の屋内のローカル座標（Unity の向きで x = 右, y = 上, z = 入口から奥）。
            /// blender/kcd_route/dorm.py の (x, y) がそのまま (x, z) になる。
            /// </summary>
            DormInterior,

            /// <summary>
            /// キャンパスの建物の屋内のローカル座標。原点は <see cref="Shot.Building"/> の Interior_&lt;id&gt; の位置で、
            /// 向きはワールドのまま（x = 右, y = 上, z = 入口から奥）。blender/kcd_interior の (x, y) がそのまま (x, z) になる。
            /// </summary>
            Interior,
        }

        /// <summary>1 枚の画。</summary>
        private struct Shot
        {
            /// <summary>ファイル名（拡張子なし）。-previewShots で指定する名前でもある。</summary>
            public string Name;

            /// <summary>Eye / Look をどの座標系で読むか。</summary>
            public Frame Space;

            /// <summary>Space が <see cref="Frame.Interior"/> のときの建物（InteriorLoader の id）。</summary>
            public string Building;

            /// <summary>カメラ位置。</summary>
            public Vector3 Eye;

            /// <summary>注視点。</summary>
            public Vector3 Look;

            /// <summary>画角（縦・度）。0 なら <see cref="DefaultFov"/>。</summary>
            public float Fov;

            /// <summary>
            /// この 1 枚だけ Sun をこの角度に向ける（Euler・度）。null なら保存された姿勢のまま。
            ///
            /// 平行光の前方向の方位角は Euler の Y とそのまま同じで、**光が進んでいく向き**を指す。
            /// つまり太陽が居るのは Y + 180 度の方位。保存された姿勢 Euler(52, 150, 0) は
            /// 南南東へ光を落とすので、南南東を向いている寮の玄関（方位 159.5 度）は
            /// いつも裏から照らされ、正面は環境光だけのまっ黒になってしまう。
            /// </summary>
            public Vector3? Sun;

            /// <summary>ログに出すひとこと。</summary>
            public string Note;
        }

        /// <summary>
        /// 屋外を撮るときの太陽。方位 125 度（南東）から差す昼前の角度。
        /// 玄関の面（法線の方位 159.5 度）とは 34.5 度、前面道路から見える東北東の面とは
        /// 53 度しか離れていないので、3 枚とも正面に光が当たる。
        /// シーンには保存しない（撮り終わったら元の姿勢へ戻す）。
        /// </summary>
        private static readonly Vector3 OutdoorSun = new Vector3(40f, 305f, 0f);

        /// <summary>
        /// 撮る画の表。**位置を直したいときはここだけ直せばよい。**
        ///
        /// 屋外の座標は data/osm/route.json の実測値から起こしてある:
        ///   寮の重心 (-464.52, 202.0) / 玄関 (-455.83, 183.24) 方位 159.5 度 / 高さ 17.8 m・5 階建て
        ///   西門 z 227.83..251.83（x = -340 付近の西壁）/ 前面道路は tertiary 幅 9.0 m
        /// 屋内の座標は blender/kcd_route/dorm.py の間取りから（Blender の (x, y) = Unity の (x, z)）:
        ///   部屋 x -8.00..8.00 / z 2.80..40.80、壁厚 0.30 なので内法は x -7.70..7.70 / z 3.10..40.50
        ///   天井 2.70 / 間仕切りの天端 3.20 / 玄関ホール z 3.10..12.60 / 中廊下の壁は x ±1.60
        ///   spawn_dorm (0, 0, 4.60) / exit_dorm (0, 0, 3.35) / 寮長 npc_dorm_head (2.60, 0, 9.60)
        ///   管理人カウンター x 4.02..4.38, z 9.20..11.60 / ラウンジは西・食堂は東（z 12.60..23.00）
        /// 屋内 3 枚の構図は dorm.py の c.cam(...) のプレビュー画角を、
        /// 目の高さ（1.6-1.7 m）と Unity の縦画角に読み替えたもの。
        /// </summary>
        private static readonly Shot[] Shots =
        {
            new Shot
            {
                Name = "shot_dorm_exterior",
                Space = Frame.World,
                Eye = new Vector3(-445.26f, 22f, 150.5f),
                Look = new Vector3(-464.52f, 8f, 202f),
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "寮を斜め上から。5 階建て 17.8 m が伝わる画",
            },
            new Shot
            {
                Name = "shot_dorm_entrance",
                Space = Frame.World,
                Eye = new Vector3(-450.93f, 3.5f, 170.13f),
                Look = new Vector3(-455.83f, 2f, 183.24f),
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "玄関を正面から（扉の方位 159.5 度の真向かい）",
            },
            new Shot
            {
                Name = "shot_dorm_route",
                Space = Frame.World,
                Eye = new Vector3(-348f, 6.5f, 238.5f),
                Look = new Vector3(-464.52f, 4f, 202f),
                Fov = 45f,
                Sun = OutdoorSun,
                Note = "西門を出た前面道路から寮を望む（道と沿道の家）",
            },
            new Shot
            {
                Name = "shot_dorm_interior",
                Space = Frame.DormInterior,
                Eye = new Vector3(0f, 1.7f, 4.1f),
                Look = new Vector3(0.6f, 1.25f, 12.4f),
                Fov = 0f,
                Note = "玄関ホールを入口から奥へ。突き当たりは z 12.60 の間仕切り",
            },
            new Shot
            {
                Name = "shot_dorm_hall",
                Space = Frame.DormInterior,
                Eye = new Vector3(-0.6f, 1.6f, 6f),
                Look = new Vector3(2.6f, 1.35f, 9.6f),
                Fov = 45f,
                Note = "寮長（ローカル 2.60, 9.60）と管理人カウンター",
            },
            new Shot
            {
                Name = "shot_dorm_lounge",
                Space = Frame.DormInterior,
                Eye = new Vector3(-1.9f, 1.65f, 13.6f),
                Look = new Vector3(-6.2f, 1.1f, 20f),
                Fov = 52f,
                Note = "ラウンジ（西・z 12.60..23.00）。dorm.py の cam \"lounge\" と同じ構図",
            },
            new Shot
            {
                Name = "shot_dorm_corridor",
                Space = Frame.DormInterior,
                Eye = new Vector3(0f, 1.7f, 13.4f),
                Look = new Vector3(0f, 1.5f, 34f),
                Fov = 46f,
                Note = "中廊下（x ±1.60 の壁のあいだ）を奥へ。突き当たりは居室エリアの仕切り",
            },

            // 窓の外の遠景（#60）。窓は外周の壁の腰 0.95 m から 2.55 m まで（dorm.py の envelope）。
            new Shot
            {
                Name = "shot_dorm_lounge_window",
                Space = Frame.DormInterior,
                Eye = new Vector3(-2.2f, 1.6f, 17.5f),
                Look = new Vector3(-9f, 1.7f, 18.5f),
                Fov = 0f,
                Note = "ラウンジから西の窓（x -8.00）の外。キャンパスの遠景（ドーム）が見えるか",
            },
            new Shot
            {
                Name = "shot_dorm_dining_window",
                Space = Frame.DormInterior,
                Eye = new Vector3(2.2f, 1.6f, 17f),
                Look = new Vector3(9f, 1.7f, 16f),
                Fov = 0f,
                Note = "食堂から東の窓（x 8.00）の外",
            },

            // 図書館 2 階の自習室（plan_library.py）。2 階の床は 4.40、机は y 62.435 に x -38.35..-22.75 で並び、
            // 外周のガラスは高さ 0.85..8.25 で途切れない。奥（+z）の外壁の窓を机ごしに見る。
            new Shot
            {
                Name = "shot_library_2f_window",
                Space = Frame.Interior,
                Building = "library",
                Eye = new Vector3(-30.5f, 6f, 59.5f),
                Look = new Vector3(-30.5f, 5.8f, 72f),
                Fov = 0f,
                Note = "図書館 2 階の自習室から奥の窓の外。キャンパスの遠景（ドーム）が見えるか",
            },

            // ここから下はキャンパス（#56）。site.py の (u, v) を CampusProps.Local で読み替えた値:
            //   堀 BASINS = 東の帯 u -59.5..-50（モールで途切れる）+ 南の池 u -100..-59.5 / v -78..-66
            //   モール MALL_V = -24、花壇はモールの北縁の内側 v -21.6..-18.0（BED_V）/ 牛乳 (u 94.9, v -29.7)、葉は公園の LeafSpots
            new Shot
            {
                Name = "shot_campus_moat",
                Space = Frame.World,
                Eye = new Vector3(-54.81f, 30f, -95.90f),     // (u -10, v -110)
                Look = new Vector3(-73.66f, 0f, -4.83f),      // (u -65, v -35)
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "図書館を囲む堀を南東の上から",
            },
            new Shot
            {
                Name = "shot_campus_moat_bank",
                Space = Frame.World,
                Eye = new Vector3(-59.55f, 1.7f, -24.47f),    // (u -44, v -47) 東岸の芝生
                Look = new Vector3(-80.29f, 1f, -7.30f),      // (u -70, v -40)
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "堀の東岸（ベンチの並び）から図書館を見る。プレイヤーの目の高さ",
            },
            new Shot
            {
                Name = "shot_campus_mall_beds",
                Space = Frame.World,
                Eye = new Vector3(16.07f, 2.4f, -37.03f),     // (u 30, v -27)
                Look = new Vector3(43.38f, 0.5f, -37.40f),    // (u 55, v -16)
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "モールの北縁の花壇（間は歩道と入口の前）",
            },
            new Shot
            {
                Name = "shot_campus_aerial",
                Space = Frame.World,
                Eye = new Vector3(40f, 120f, -300f),
                Look = new Vector3(0f, 0f, -20f),
                Fov = 0f,
                Sun = OutdoorSun,
                Note = "キャンパス全体を南の上空から（木の並び、#57）",
            },
            new Shot
            {
                Name = "shot_item_leaf",
                Space = Frame.World,
                Eye = new Vector3(-82.9f, 1.25f, -113.1f),
                Look = new Vector3(-84f, 0.45f, -112f),       // LeafSpots の 1 枚
                Fov = 40f,
                Sun = OutdoorSun,
                Note = "理科大グリーンの葉（公園）を近くから",
            },
            new Shot
            {
                Name = "shot_item_milk",
                Space = Frame.World,
                Eye = new Vector3(75.19f, 1.35f, -65.24f),    // (u 95.5, v -28.1) モール側から
                Look = new Vector3(73.97f, 0.42f, -66.49f),   // 共創棟の売店前
                Fov = 40f,
                Sun = OutdoorSun,
                Note = "売店前の牛乳を近くから",
            },
        };

        /// <summary>
        /// spawn_dorm のローカル座標。dorm.py の entry_kit(spawn_depth=1.50) と
        /// y_face 2.80 + 壁厚 0.30 から z = 4.60。ここからローカル原点を逆算する。
        /// </summary>
        private static readonly Vector3 DormSpawnLocal = new Vector3(0f, 0f, 4.6f);

        /// <summary>
        /// spawn_dorm が見つからなかったときのローカル原点。DormStage.SlotX(4000) に
        /// 部屋の半幅 8 m を足した値で、DormStage のログ「寮の屋内を置きました: x 4000..4016 z 3..41」と合う
        /// （部屋は x -8..8 / z 2.80..40.80 なので、x だけ +4008 ずれて z と y はずれない）。
        /// あくまで保険で、ふだんは spawn_dorm から出した値を使う。
        /// </summary>
        private static readonly Vector3 DormOriginFallback = new Vector3(4008f, 0f, 0f);

        /// <summary>どのやり方で描けたか。1 枚目で決まったら、残りも同じやり方で撮る。</summary>
        private enum DrawPath
        {
            /// <summary>まだ分からない。両方試す。</summary>
            Unknown,

            /// <summary>Camera.Render()。</summary>
            CameraRender,

            /// <summary>RenderPipeline.SubmitRenderRequest（Unity 6 の正式なやり方）。</summary>
            RenderRequest,
        }

        /// <summary>画の明るさ。真っ黒・単色の PNG をログだけで見分けるために測る。</summary>
        private struct Levels
        {
            /// <summary>平均輝度（0-1）。</summary>
            public float Mean;

            /// <summary>いちばん暗い画素の輝度（0-255）。</summary>
            public byte Min;

            /// <summary>いちばん明るい画素の輝度（0-255）。</summary>
            public byte Max;

            /// <summary>全部同じ色。描けていないときはこうなる。</summary>
            public bool Flat => Min == Max;
        }

        /// <summary>
        /// 「本当に描けたか」を見分けるための目印の色。撮る直前に毎回これで塗りつぶしておく。
        ///
        /// 塗らずに撮ると、描けなかったときに RenderTexture へ **前の画がそのまま残る**。
        /// 「単色かどうか」だけで判定していると、その残像を「描けた」と読み違えて、
        /// 同じ画を何枚も書き出してしまう。塗っておけば、塗ったままかどうかで確実に分かる。
        /// </summary>
        private static readonly Color MarkerColor = new Color(1f, 0f, 1f);

        /// <summary>目印で塗ったときの輝度。sRGB 変換で値が変わるので決め打ちにせず実測する。</summary>
        private static byte _markerLuma;

        /// <summary>目印が使えるか。塗っても単色にならなかったときだけ false。</summary>
        private static bool _markerKnown;

        /// <summary>
        /// 撮影のために一時的にいじったものを、元へ戻すための控え。
        /// シーンは保存しないが、同じ Unity セッションで続けて別の処理をされても困らないようにする。
        /// </summary>
        private sealed class Restore
        {
            private readonly List<GameObject> _activated = new List<GameObject>();
            private readonly List<Light> _enabled = new List<Light>();

            /// <summary>戻すべきものの数。0 なら何もいじっていない。</summary>
            public int Count => _activated.Count + _enabled.Count;

            public void Activate(GameObject go)
            {
                if (go == null || go.activeSelf)
                {
                    return;
                }

                go.SetActive(true);
                _activated.Add(go);
            }

            public void Enable(Light light)
            {
                if (light == null || light.enabled)
                {
                    return;
                }

                light.enabled = true;
                _enabled.Add(light);
            }

            public void Undo()
            {
                foreach (GameObject go in _activated)
                {
                    if (go != null)
                    {
                        go.SetActive(false);
                    }
                }

                foreach (Light light in _enabled)
                {
                    if (light != null)
                    {
                        light.enabled = false;
                    }
                }

                _activated.Clear();
                _enabled.Clear();
            }
        }

        /// <summary>バッチの入口。Campus.unity を開いて表のぶんだけ撮る。</summary>
        [MenuItem("KCD/プレビューを撮る")]
        public static void Capture()
        {
            // 再生中に OpenScene を呼ぶと例外になる。メニューから叩かれたときのために先に断る。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorPaths.Report("再生中はシーンを開けないので、プレビューは撮れません。再生を止めてからにしてください。");
                return;
            }

            string filter = EditorPaths.ReadArgument("-previewShots", string.Empty);
            float fill = ParseFloat(EditorPaths.ReadArgument("-previewInteriorFill", "0"), 0f);

            Scene scene = EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorPaths.Report("シーンを開けないのでプレビューを撮れません: " + EditorPaths.CampusScene);
                return;
            }

            string folder = EditorPaths.ProjectRelative(OutputFolder);
            Directory.CreateDirectory(folder);

            // 地面探りのために当たり判定の位置を確定させ、環境光（Trilight）の球面調和を焼き直す。
            Physics.SyncTransforms();
            DynamicGI.UpdateEnvironment();
            ReportEnvironment(folder);

            Transform interior = FindByName("Interior_" + DormRoute.Id);
            Vector3 dormOrigin = ResolveDormOrigin(interior);
            ReportDormHead(dormOrigin);

            // シェーダの非同期コンパイルが効いていると、間に合わなかったマテリアルが
            // 青緑の「コンパイル中」シェーダで写る。撮影のあいだだけ同期に落とす。
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            // Shot.Sun のために太陽を借りる。シーンは保存しないが、同じセッションで続けて
            // 別の処理をされても困らないよう、撮り終わったら元の姿勢へ必ず戻す（finally）。
            Transform sun = FindByName("Sun");
            Quaternion sunWas = sun != null ? sun.rotation : Quaternion.identity;

            var restore = new Restore();
            GameObject rig = null;
            GameObject fillLight = null;
            RenderTexture target = null;
            Texture2D buffer = null;
            Camera camera = null;

            try
            {
                // 屋内の明かり。DormStage.AddLights は有効な状態で置くので普段は何も起きないが、
                // 誰かが切っていた・親ごと非アクティブだったときのために点け直す。
                WakeInteriorLights(interior, restore);
                if (fill > 0f)
                {
                    fillLight = AddInteriorFill(dormOrigin, fill);
                }

                rig = new GameObject(RigName);
                rig.hideFlags = HideFlags.DontSave;
                camera = Setup(rig);

                // URP は Linear 色空間なので、RenderTexture は必ず sRGB で作る。
                // linear のまま ReadPixels すると白っぽい（ガンマの掛かっていない）PNG になる。
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.antiAliasing = 1;   // アンチエイリアスは URP 側の FXAA に任せる
                target.useMipMap = false;
                target.Create();
                camera.targetTexture = target;

                // 読み戻し先。linear: false なので、RenderTexture のバイト列がそのまま PNG になる。
                buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);

                // 「描けたか」を見分ける目印が、この RenderTexture でいくつの輝度になるかを実測しておく。
                MeasureMarker(target, buffer);

                // バッチで開いた直後はレンダーパイプラインの実体がまだ無く、
                // SubmitRenderRequest が黙って素通りする。1 回描いて起こしておく。
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                camera.Render();

                var path = DrawPath.Unknown;
                int taken = 0;
                int failed = 0;
                foreach (Shot shot in Shots)
                {
                    if (!Wanted(filter, shot.Name))
                    {
                        continue;
                    }

                    if (sun != null)
                    {
                        sun.rotation = shot.Sun.HasValue ? Quaternion.Euler(shot.Sun.Value) : sunWas;
                    }

                    if (!TryOrigin(shot, dormOrigin, out Vector3 offset))
                    {
                        failed++;
                        continue;
                    }

                    if (Shoot(camera, target, buffer, shot, offset, folder, ref path))
                    {
                        taken++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                EditorPaths.Report("プレビューを " + taken + " 枚撮りました: " + folder
                    + (failed > 0 ? "（" + failed + " 枚は描けなかったので書き出していません）" : string.Empty));
                EditorPaths.AppendVerify("preview " + taken + " shots (" + failed + " failed) -> " + folder);
            }
            finally
            {
                // 後片付け。ここを通らないと RenderTexture が居座り、シーンに撮影用カメラが残る。
                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                RenderTexture.active = null;

                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                if (buffer != null)
                {
                    UnityEngine.Object.DestroyImmediate(buffer);
                }

                if (fillLight != null)
                {
                    UnityEngine.Object.DestroyImmediate(fillLight);
                }

                if (rig != null)
                {
                    UnityEngine.Object.DestroyImmediate(rig);
                }

                if (restore.Count > 0)
                {
                    EditorPaths.Report("一時的にいじったライトを " + restore.Count + " 件戻します。");
                }

                if (sun != null)
                {
                    sun.rotation = sunWas;
                }

                restore.Undo();
                ShaderUtil.allowAsyncCompilation = wasAsync;

                // シーンは保存しない。開いて撮るだけ。
            }
        }

        /// <summary>撮影用カメラ。設定は本編カメラ（ActorFactory / PostProcessFactory）にそろえる。</summary>
        private static Camera Setup(GameObject rig)
        {
            Camera camera = rig.AddComponent<Camera>();
            camera.fieldOfView = DefaultFov;
            camera.nearClipPlane = NearClip;
            camera.farClipPlane = FarClip;
            camera.cullingMask = ~0;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;

            // targetTexture を持たせるので Game ビューには出ない。enabled は true のままにしておく。
            // SubmitRenderRequest は「ふつうの 1 フレーム」として描くので、切っていると取りこぼしうる。
            camera.enabled = true;

            if (RenderSettings.skybox != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                // スカイボックスが無いシーンだと空が真っ黒になるので、霧の色で塗る。
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                EditorPaths.Report("スカイボックスが無いので、空は霧の色で塗ります。");
            }

            // URP の設定。ポストプロセスと FXAA は PostProcessFactory.EnableOnCamera と同じ。
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderShadows = true;
                data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
                data.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;

                // グローバル Volume は Default レイヤーに置いてあるが、取りこぼさないよう全部見る。
                data.volumeLayerMask = ~0;
            }
            else
            {
                EditorPaths.Report("UniversalAdditionalCameraData を付けられません（URP のプロジェクトではない？）。");
            }

            return camera;
        }

        /// <summary>
        /// 1 枚撮って PNG にする。描けなかったときは **PNG を書かずに** false を返す。
        /// 書いてしまうと、前に撮れていた良い PNG を目印色の画で潰すことになる。
        /// </summary>
        private static bool Shoot(
            Camera camera, RenderTexture target, Texture2D buffer, Shot shot, Vector3 offset, string folder,
            ref DrawPath path)
        {
            Vector3 eye = shot.Eye + offset;
            Vector3 look = shot.Look + offset;
            Vector3 forward = look - eye;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward.normalized, Vector3.up));
            camera.fieldOfView = shot.Fov > 0f ? shot.Fov : DefaultFov;

            Levels levels = Draw(camera, target, buffer, ref path);
            bool undrawn = Undrawn(levels);

            string file = Path.Combine(folder, shot.Name + ".png");
            if (!undrawn)
            {
                File.WriteAllBytes(file, buffer.EncodeToPNG());
            }

            EditorPaths.Report("プレビュー " + shot.Name
                + ": 位置 " + Format(eye) + " → 注視 " + Format(look)
                + " 距離 " + forward.magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m"
                + " 画角 " + camera.fieldOfView.ToString("F0", CultureInfo.InvariantCulture) + " 度（縦）"
                + " 平均輝度 " + levels.Mean.ToString("F3", CultureInfo.InvariantCulture)
                + " 幅 " + levels.Min + ".." + levels.Max
                + " （" + shot.Note + "） → " + (undrawn ? "書き出しません" : file));

            float ground = ProbeGround(eye);
            if (ground > UnknownGround)
            {
                EditorPaths.Report("  カメラの足もとの地面: y " + ground.ToString("F2", CultureInfo.InvariantCulture)
                    + " m（カメラは " + (eye.y - ground).ToString("F2", CultureInfo.InvariantCulture) + " m 上）");
                if (eye.y < ground)
                {
                    EditorPaths.Report("  " + shot.Name + " のカメラが地面より下にあります。表の Eye.y を上げてください。");
                }
            }

            if (undrawn)
            {
                EditorPaths.Report("  " + shot.Name + " は 1 画素も描けていません（目印の色のまま）。"
                    + "-nographics を付けていないか確かめてください。PNG は上書きしていません。");
                return false;
            }

            if (levels.Flat)
            {
                EditorPaths.Report("  " + shot.Name + " が単色です。カメラが壁や地面の中に入っていないか"
                    + "確かめてください。");
            }
            else if (levels.Mean < 0.02f)
            {
                EditorPaths.Report("  " + shot.Name + " がほぼ真っ黒です。ライトが消えていないか、"
                    + "屋内なら -previewInteriorFill 1.2 を足して確かめてください。");
            }

            return true;
        }

        /// <summary>
        /// 実際に描いて、読み戻したバッファの明るさを返す。
        ///
        /// URP では <c>Camera.Render()</c> は正式には非対応で、環境によっては黙って何も描かない。
        /// そこで 1 枚目だけ Camera.Render() を試し、描けていなければ Unity 6 の
        /// 正式なやり方 <c>RenderPipeline.SubmitRenderRequest</c> で撮り直す。
        /// どちらで描けたかは覚えておいて、2 枚目からは同じやり方だけを使う。
        ///
        /// 判定は「単色かどうか」ではなく「<see cref="MarkerColor"/> で塗ったままかどうか」で見る。
        /// 描く前に必ず塗り直すので、ウォームアップや前の 1 枚が残っているのを
        /// 「描けた」と読み違えることがない。真っ黒な画（輝度 0）とも区別できる。
        /// </summary>
        private static Levels Draw(Camera camera, RenderTexture target, Texture2D buffer, ref DrawPath path)
        {
            if (path != DrawPath.RenderRequest)
            {
                ClearToMarker(target);

                // 1 枚目はポストプロセスの LUT やシェーダの用意が間に合わないことがあるので 2 回描く。
                camera.Render();
                camera.Render();

                Levels levels = Read(target, buffer);
                if (!Undrawn(levels))
                {
                    if (path == DrawPath.Unknown)
                    {
                        EditorPaths.Report("描画は Camera.Render() で通りました。");
                        path = DrawPath.CameraRender;
                    }

                    return levels;
                }

                if (path == DrawPath.CameraRender)
                {
                    // いちど通っているのにここだけ描けないのはおかしい。Shoot に警告させる。
                    return levels;
                }

                EditorPaths.Report("Camera.Render() では何も描けないので、"
                    + "RenderPipeline.SubmitRenderRequest に切り替えます。");
            }

            ClearToMarker(target);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest
            {
                destination = target,
                mipLevel = 0,
            };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);

            Levels requested = Read(target, buffer);
            if (!Undrawn(requested) && path == DrawPath.Unknown)
            {
                EditorPaths.Report("描画は RenderPipeline.SubmitRenderRequest で通りました。");
                path = DrawPath.RenderRequest;
            }

            return requested;
        }

        /// <summary>RenderTexture を目印の色で塗りつぶす。描く前に必ず通す。</summary>
        private static void ClearToMarker(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, MarkerColor);
            RenderTexture.active = previous;
        }

        /// <summary>
        /// 目印で塗ったときの輝度を実測する。sRGB 変換が入るので決め打ちにはできない。
        /// 塗っても単色にならなかったときは目印をあきらめ、昔どおり「単色かどうか」で見る。
        /// </summary>
        private static void MeasureMarker(RenderTexture target, Texture2D buffer)
        {
            ClearToMarker(target);
            Levels levels = Read(target, buffer);
            _markerLuma = levels.Min;
            _markerKnown = levels.Flat;
            EditorPaths.Report(_markerKnown
                ? "描けたかどうかの目印: 輝度 " + _markerLuma + "（この色のままなら 1 枚も描けていない）"
                : "目印で塗っても単色になりません。描けたかどうかは「単色かどうか」だけで見ます。");
        }

        /// <summary>目印のまま＝何も描けていない。</summary>
        private static bool Undrawn(Levels levels)
        {
            return levels.Flat && (!_markerKnown || levels.Min == _markerLuma);
        }

        /// <summary>RenderTexture の中身を Texture2D へ読み戻して、明るさを測る。</summary>
        private static Levels Read(RenderTexture target, Texture2D buffer)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            buffer.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            buffer.Apply(false);
            RenderTexture.active = previous;
            return Measure(buffer);
        }

        /// <summary>
        /// 寮の屋内のローカル原点（ワールド座標）。
        /// DormStage は屋内を x 方向にだけずらすので、変換は平行移動だけで済む。
        /// spawn_dorm はローカル (0, 0, 4.60) にあるので、そこから逆算する。
        /// </summary>
        private static Vector3 ResolveDormOrigin(Transform interior)
        {
            Transform spawn = interior != null ? FindChild(interior, DormStage.SpawnEmpty) : null;
            if (spawn != null)
            {
                Vector3 origin = spawn.position - DormSpawnLocal;
                EditorPaths.Report(DormStage.SpawnEmpty + " から寮の屋内の原点を出しました: " + Format(origin)
                    + "（" + DormStage.SpawnEmpty + " は " + Format(spawn.position) + "）");
                return origin;
            }

            EditorPaths.Report(DormStage.SpawnEmpty + " が見つからないので、決め打ちの原点を使います: "
                + Format(DormOriginFallback) + "（route.fbx が無いビルドかもしれません）");
            return DormOriginFallback;
        }

        /// <summary>
        /// 寮長の実際の位置をログに出す。shot_dorm_hall の注視点を直すときの手がかりになる。
        /// 表のローカル座標 (2.60, 0, 9.60) とずれていたら、ここに出る値へ合わせればよい。
        /// </summary>
        private static void ReportDormHead(Vector3 origin)
        {
            Transform head = FindByName("NPC_" + DormRoute.DialogueId);
            if (head == null)
            {
                EditorPaths.Report("寮長（NPC_" + DormRoute.DialogueId + "）が見つかりません。"
                    + "shot_dorm_hall には誰も写らないかもしれません。");
                return;
            }

            EditorPaths.Report("寮長の位置: ワールド " + Format(head.position)
                + " ローカル " + Format(head.position - origin));
        }

        /// <summary>
        /// どんなライティングで撮っているかをログに出す。
        /// DayNightCycle は実行時にしか動かないので、エディタの画は Sun に保存された姿勢
        /// （SkyFactory が置くゲーム開始 8:30 の向き。空のマテリアルも同じ時刻の色）のままになる。
        /// 画が想像と違ったときは、まずここの値を疑う。
        /// </summary>
        private static void ReportEnvironment(string folder)
        {
            int level = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            UnityEngine.Rendering.RenderPipelineAsset pipeline =
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;

            EditorPaths.Report("プレビュー: シーン " + EditorPaths.CampusScene
                + " / " + Width + "x" + Height + " → " + folder);
            EditorPaths.Report("  品質 " + (level >= 0 && level < names.Length ? names[level] : level.ToString())
                + " / パイプライン " + (pipeline != null ? pipeline.name : "(なし = ビルトイン)")
                + " / 色空間 " + QualitySettings.activeColorSpace);
            EditorPaths.Report("  環境光 " + RenderSettings.ambientMode
                + " / 霧 " + (RenderSettings.fog
                    ? RenderSettings.fogMode + " " + RenderSettings.fogStartDistance.ToString("F0", CultureInfo.InvariantCulture)
                        + ".." + RenderSettings.fogEndDistance.ToString("F0", CultureInfo.InvariantCulture) + " m"
                    : "なし")
                + " / スカイボックス " + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "なし"));

            Transform sun = FindByName("Sun");
            Light sunLight = sun != null ? sun.GetComponent<Light>() : null;
            if (sunLight == null)
            {
                EditorPaths.Report("  Sun が見つかりません。屋外がまっ平らな明るさになります。");
                return;
            }

            EditorPaths.Report("  Sun 角度 " + Format(sun.rotation.eulerAngles)
                + " 強さ " + sunLight.intensity.ToString("F2", CultureInfo.InvariantCulture)
                + " 影 " + sunLight.shadows
                + "（DayNightCycle は実行時だけ動くので、これは保存された朝の姿勢）");
        }

        /// <summary>
        /// 寮の屋内の明かりを点け直す。DormStage.AddLights が Interior_dorm/Lights に
        /// 点光源（暖色・影なし・range 14 / 強さ 1.35、中央にもう 1 灯）を置いている。
        /// 普段は最初から有効なので、ここは保険。
        /// </summary>
        private static void WakeInteriorLights(Transform interior, Restore restore)
        {
            if (interior == null)
            {
                EditorPaths.Report("Interior_" + DormRoute.Id + " が無いので、屋内の明かりは確かめられません。");
                return;
            }

            restore.Activate(interior.gameObject);

            // 点け直した件数だけを数える（Interior_dorm 本体の有効化は数に入れない）。
            int before = restore.Count;
            int total = 0;
            Light[] lights = interior.GetComponentsInChildren<Light>(true);
            foreach (Light light in lights)
            {
                restore.Activate(light.gameObject);
                restore.Enable(light);
                total++;
            }

            EditorPaths.Report("寮の屋内の明かり: " + total + " 灯（うち " + (restore.Count - before) + " 件を一時的に点け直し）"
                + "。URP の追加ライトは 1 オブジェクト 4 灯までなので、遠くの灯は効きません。");
        }

        /// <summary>
        /// -previewInteriorFill &lt;強さ&gt; を付けたときだけ足す、屋内の補助光。
        /// **本番のライティングではない**ので、既定（0）では足さない。
        /// 屋内が暗すぎたときの逃げ道として残す。
        /// </summary>
        private static GameObject AddInteriorFill(Vector3 origin, float intensity)
        {
            var go = new GameObject(FillName);
            go.hideFlags = HideFlags.DontSave;
            go.transform.position = origin + new Vector3(0f, 2.4f, 9f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 26f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.None;

            EditorPaths.Report("屋内に一時的な補助光を足しました（強さ "
                + intensity.ToString("F2", CultureInfo.InvariantCulture) + "）。本番のライティングではありません。");
            return go;
        }

        /// <summary>
        /// カメラの足もとの地面の高さ。屋外の画で「地面に潜っていないか」を見るためだけに使う。
        /// Ground レイヤーが無い／当たらないときは <see cref="UnknownGround"/>。
        /// </summary>
        private static float ProbeGround(Vector3 eye)
        {
            int layer = LayerMask.NameToLayer("Ground");
            if (layer < 0)
            {
                return UnknownGround;
            }

            var from = new Vector3(eye.x, eye.y + 200f, eye.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 600f,
                1 << layer, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : UnknownGround;
        }

        /// <summary>画の明るさを測る。全画素は見ずに 2 万点ほど間引いて拾う。</summary>
        private static Levels Measure(Texture2D texture)
        {
            var levels = new Levels { Mean = 0f, Min = 0, Max = 0 };
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
            {
                return levels;
            }

            int step = Mathf.Max(1, pixels.Length / 20000);
            long sum = 0;
            int count = 0;
            byte min = 255;
            byte max = 0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color32 c = pixels[i];
                var luma = (byte)((c.r * 2 + c.g * 5 + c.b) / 8);
                sum += luma;
                count++;
                if (luma < min)
                {
                    min = luma;
                }

                if (luma > max)
                {
                    max = luma;
                }
            }

            levels.Mean = count > 0 ? sum / (float)count / 255f : 0f;
            levels.Min = min;
            levels.Max = max;
            return levels;
        }

        /// <summary>
        /// -previewShots で選ばれているか。引数が無ければ全部撮る。
        /// 名前そのもの（shot_dorm_hall）でも、その一部（hall / dorm）でも当たる。
        /// </summary>
        private static bool Wanted(string filter, string name)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            foreach (string part in filter.Split(','))
            {
                string want = part.Trim();
                if (want.Length == 0)
                {
                    continue;
                }

                if (want.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    want = want.Substring(0, want.Length - 4);
                }

                if (name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Eye / Look の座標系の原点（ワールド）。建物の屋内が見つからなければ false（その 1 枚は撮らない。
        /// ワールドの原点で撮るとキャンパスのまったく別の場所が写るため）。
        /// </summary>
        private static bool TryOrigin(Shot shot, Vector3 dormOrigin, out Vector3 origin)
        {
            origin = Vector3.zero;
            switch (shot.Space)
            {
                case Frame.DormInterior:
                    origin = dormOrigin;
                    return true;
                case Frame.Interior:
                    Transform interior = FindByName("Interior_" + shot.Building);
                    if (interior == null)
                    {
                        EditorPaths.Report(shot.Name + ": Interior_" + shot.Building + " が無いので撮りません。");
                        return false;
                    }

                    origin = interior.position;
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>非アクティブなものも含めて、名前で Transform を 1 つ探す。</summary>
        private static Transform FindByName(string name)
        {
            Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            foreach (Transform transform in all)
            {
                if (transform.name == name)
                {
                    return transform;
                }
            }

            return null;
        }

        /// <summary>子孫から名前で Transform を 1 つ探す（DormStage.Find と同じ探し方）。</summary>
        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("F2", CultureInfo.InvariantCulture)
                + ", " + value.y.ToString("F2", CultureInfo.InvariantCulture)
                + ", " + value.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        private static float ParseFloat(string text, float fallback)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }
    }
}
