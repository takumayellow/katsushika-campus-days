using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KCD.Editor
{
    /// <summary>
    /// キャラクターをゲームと同じ見た目（Campus.unity のライティング・トゥーンシェーダ・
    /// ポストプロセス・差し替え後の .mat）で撮る (#47, #55)。
    /// Blender のレンダーは色もシェーダも輪郭線も本編と違うので、顔の出来はこちらで確かめる。
    ///
    /// 使い方（RenderTexture が要るので **-nographics は付けない**）:
    ///   Unity.exe -batchmode -quit -projectPath &lt;...&gt; -executeMethod KCD.Editor.CharacterPreview.Capture
    /// 足せる引数:
    ///   -charaIds mirai,botchan   撮るキャラ（既定は Assets/Models/Characters にいる全員）
    ///   -charaOut &lt;dir&gt;          出力先（既定は docs/previews/characters）
    ///   -charaPose run            動きの途中を撮る（idle / walk / run / jump。既定は idle）(#49)
    ///   -charaPoseTimes 0.1,0.3   動き出してから何秒後を撮るか（既定は 0.1,0.2,0.3,0.4）
    ///
    /// 動きを撮るときは顔を撮らず、全身を正面・35 度・真横から撮る。スカートや袴から脚が
    /// 突き抜けていないかを見るため。ファイル名は &lt;id&gt;_&lt;pose&gt;&lt;ミリ秒&gt;_body_f90.png のようになる。
    ///
    /// 1 キャラにつき 顔（正面・35 度・真横）と全身（正面・35 度）を撮る。
    ///
    /// 描画の有効・無効は取り込んだまま触らない。Blender で焼いた輪郭シェル（&lt;id&gt;_outline）は
    /// CharacterImporter が無効にしている。有効にすると KCD/Toon の Outline パス（Cull Front）が
    /// シェルの手前側を輪郭色で描き、服や髪の外向きの面が真っ黒に塗りつぶされる。
    ///
    /// キャラは空中（地面から <see cref="Altitude"/> m 上）に置くので、建物や木が写り込まない。
    /// シーンは開くだけで保存しない。置いたものは finally で必ず捨てる。
    /// </summary>
    public static class CharacterPreview
    {
        public const string OutputFolder = "../../docs/previews/characters";
        public const int Width = 1024;
        public const int Height = 1024;

        /// <summary>撮影位置の高さ。建物や木より十分上。</summary>
        private const float Altitude = 400f;

        private static readonly Color MarkerColor = new Color(1f, 0f, 1f);

        private struct Shot
        {
            public string Suffix;
            public float Yaw;
            public bool Face;
        }

        private static readonly Shot[] Shots =
        {
            new Shot { Suffix = "face_f0", Yaw = 0f, Face = true },
            new Shot { Suffix = "face_f35", Yaw = 35f, Face = true },
            new Shot { Suffix = "face_f90", Yaw = 90f, Face = true },
            new Shot { Suffix = "body_f0", Yaw = 0f, Face = false },
            new Shot { Suffix = "body_f35", Yaw = 35f, Face = false },
        };

        private static readonly Shot[] MotionShots =
        {
            new Shot { Suffix = "body_f0", Yaw = 0f, Face = false },
            new Shot { Suffix = "body_f35", Yaw = 35f, Face = false },
            new Shot { Suffix = "body_f90", Yaw = 90f, Face = false },
        };

        private struct Frame
        {
            public string Pose;
            public float Time;

            /// <summary>ファイル名に挟む印。Idle は今までどおり何も挟まない。</summary>
            public string Label => Pose == "idle"
                ? string.Empty
                : "_" + Pose + Mathf.RoundToInt(Time * 1000f).ToString("D4", CultureInfo.InvariantCulture);
        }

        [MenuItem("KCD/キャラクターを撮る")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorPaths.Report("再生中はシーンを開けないので、キャラクターは撮れません。");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorPaths.Report("シーンを開けません: " + EditorPaths.CampusScene);
                return;
            }

            string folder = EditorPaths.ReadArgument("-charaOut", string.Empty);
            folder = string.IsNullOrEmpty(folder) ? EditorPaths.ProjectRelative(OutputFolder) : Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            DynamicGI.UpdateEnvironment();
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            Light sun = FindSun();
            Quaternion sunWas = sun != null ? sun.transform.rotation : Quaternion.identity;
            GameObject rig = null;
            GameObject body = null;
            RenderTexture target = null;
            Texture2D buffer = null;

            try
            {
                rig = new GameObject("__CharacterPreviewCamera") { hideFlags = HideFlags.DontSave };
                Camera camera = Setup(rig);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);

                int taken = 0;
                foreach (string id in Ids())
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(ActorFactory.FbxPathOf(id));
                    if (source == null)
                    {
                        EditorPaths.Report("FBX が無いので飛ばします: " + id);
                        continue;
                    }

                    foreach (Frame frame in Frames())
                    {
                        // Humanoid のクリップを当てると体の位置がクリップのルート（原点）へ戻されるので、
                        // 空中に置いた親の下に入れる。
                        body = new GameObject("__CharacterPreviewStand") { hideFlags = HideFlags.DontSave };
                        body.transform.SetPositionAndRotation(new Vector3(0f, Altitude, 0f), Quaternion.identity);
                        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        model.hideFlags = HideFlags.DontSave;
                        model.transform.SetParent(body.transform, false);
                        float bindHeight = BoundsOf(body).size.y;
                        PoseAsInGame(model, id, frame);

                        Bounds bounds = BoundsOf(body);
                        Vector3 forward = body.transform.forward;
                        Vector3 head = HeadCenter(model, bounds, out float headSpan);

                        // 光はカメラ（正面）の左上から。保存された太陽の向きだと顔が逆光になることがある。
                        if (sun != null)
                        {
                            Vector3 toward = -forward + Quaternion.AngleAxis(90f, Vector3.up) * forward * 0.6f;
                            sun.transform.rotation = Quaternion.LookRotation(
                                (toward.normalized * 0.8f + Vector3.down * 0.6f).normalized, Vector3.up);
                        }

                        int shellsDrawn = 0;
                        foreach (Renderer shell in Shells(body))
                        {
                            if (shell.enabled)
                            {
                                shellsDrawn++;
                            }
                        }

                        foreach (Shot shot in frame.Pose == "idle" ? Shots : MotionShots)
                        {
                            Vector3 dir = Quaternion.AngleAxis(shot.Yaw, Vector3.up) * forward;
                            Vector3 look;
                            float distance;
                            if (shot.Face)
                            {
                                // 頭（首の付け根〜頭頂）の 2.2 倍が縦に入る距離。等身の違うキャラでも顔の大きさが揃う。
                                look = head;
                                camera.fieldOfView = 20f;
                                distance = headSpan * 1.1f / Mathf.Tan(10f * Mathf.Deg2Rad);
                            }
                            else
                            {
                                look = bounds.center;
                                camera.fieldOfView = 30f;
                                distance = bounds.size.y * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.12f;
                            }

                            Vector3 eye = look + dir * distance;
                            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                            if (Draw(camera, target, buffer))
                            {
                                File.WriteAllBytes(Path.Combine(folder, id + frame.Label + "_" + shot.Suffix + ".png"), buffer.EncodeToPNG());
                                taken++;
                            }
                            else
                            {
                                EditorPaths.Report("描けませんでした: " + id + frame.Label + "_" + shot.Suffix + "（-nographics を付けていないか）");
                            }
                        }

                        EditorPaths.Report(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}{7}: 高さ {1:F3} m（ポーズ前 {2:F3} m）, 足の下端 {3:F3} m, 頭の中心 {4:F3} m, 頭の縦 {5:F3} m, 描かれている輪郭シェル {6} 個",
                            id, bounds.size.y, bindHeight, bounds.min.y - Altitude, head.y - bounds.min.y, headSpan, shellsDrawn, frame.Label));
                        if (frame.Pose == "idle" && Mathf.Abs(bounds.size.y - bindHeight) > bindHeight * 0.05f)
                        {
                            EditorPaths.Report(id + ": ポーズを付けると高さが変わります。meta の Humanoid の骨格が古い"
                                + "（KCD/シーンを組み直す で CharacterImporter.SyncSkeletons を通す）");
                        }

                        if (shellsDrawn > 0 && frame.Pose == "idle")
                        {
                            EditorPaths.Report(id + ": 輪郭シェルが描かれています。服や髪が黒く潰れます（CharacterImporter を確認）");
                        }

                        Object.DestroyImmediate(body);
                        body = null;
                    }
                }

                EditorPaths.Report("キャラクターを " + taken + " 枚撮りました: " + folder);
            }
            finally
            {
                RenderTexture.active = null;
                if (body != null)
                {
                    Object.DestroyImmediate(body);
                }

                if (rig != null)
                {
                    Object.DestroyImmediate(rig);
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                if (buffer != null)
                {
                    Object.DestroyImmediate(buffer);
                }

                if (sun != null)
                {
                    sun.transform.rotation = sunWas;
                }

                ShaderUtil.allowAsyncCompilation = wasAsync;
            }
        }

        /// <summary>
        /// 遠くの人物の輪郭線の太さを確かめる (#44)。ゲームのカメラと同じ画角 55°・1920x1080 で、
        /// 1 人のキャラを <see cref="LineupDistances"/> の距離に並べて 1 枚に撮る。各人物の周りを
        /// 4 倍に拡大した切り抜きも書き出す（輪郭が何 px あるかを数えるため）。
        ///   Unity.exe -batchmode -quit -projectPath &lt;...&gt; -executeMethod KCD.Editor.CharacterPreview.CaptureDistances
        ///   -charaIds mirai  -charaOut &lt;dir&gt;  は Capture と同じ。出力は &lt;id&gt;_distances.png と &lt;id&gt;_d&lt;m&gt;.png。
        /// </summary>
        public static void CaptureDistances()
        {
            Scene scene = EditorSceneManager.OpenScene(EditorPaths.CampusScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                EditorPaths.Report("シーンを開けません: " + EditorPaths.CampusScene);
                return;
            }

            string folder = EditorPaths.ReadArgument("-charaOut", string.Empty);
            folder = string.IsNullOrEmpty(folder) ? EditorPaths.ProjectRelative(OutputFolder) : Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            DynamicGI.UpdateEnvironment();
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            const int w = 1920;
            const int h = 1080;
            var placed = new List<GameObject>();
            GameObject rig = null;
            RenderTexture target = null;
            Texture2D buffer = null;
            try
            {
                rig = new GameObject("__CharacterPreviewCamera") { hideFlags = HideFlags.DontSave };
                Camera camera = Setup(rig);
                camera.fieldOfView = 55f;
                target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                buffer = new Texture2D(w, h, TextureFormat.RGB24, false, false);

                // 肩越しカメラの高さ（目の高さ少し上）から、奥へ向かって見る。
                Vector3 eye = new Vector3(0f, Altitude + 1.6f, 0f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(4f, 0f, 0f));

                foreach (string id in Ids())
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(ActorFactory.FbxPathOf(id));
                    if (source == null)
                    {
                        EditorPaths.Report("FBX が無いので飛ばします: " + id);
                        continue;
                    }

                    var rects = new List<KeyValuePair<float, Rect>>();
                    for (int k = 0; k < LineupDistances.Length; k++)
                    {
                        float d = LineupDistances[k];
                        // 手前の人物に隠れないよう、画面の横位置を 1 人ずつずらす（左端 -0.8 〜 右端 +0.7）。
                        float across = -0.8f + 1.5f * k / Mathf.Max(1, LineupDistances.Length - 1);
                        float x = across * d * Mathf.Tan(27.5f * Mathf.Deg2Rad) * (w / (float)h);
                        var stand = new GameObject("__CharacterPreviewStand") { hideFlags = HideFlags.DontSave };
                        stand.transform.SetPositionAndRotation(new Vector3(x, Altitude, d), Quaternion.Euler(0f, 180f, 0f));
                        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        model.hideFlags = HideFlags.DontSave;
                        model.transform.SetParent(stand.transform, false);
                        PoseAsInGame(model, id, new Frame { Pose = "idle", Time = 0f });
                        placed.Add(stand);

                        Bounds b = BoundsOf(stand);
                        Vector3 lo = camera.WorldToScreenPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                        Vector3 hi = camera.WorldToScreenPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                        float tall = Mathf.Max(8f, hi.y - lo.y);
                        float side = tall * 1.3f;
                        rects.Add(new KeyValuePair<float, Rect>(d, new Rect(lo.x - side * 0.5f, lo.y - tall * 0.15f, side, side)));
                        EditorPaths.Report(string.Format(CultureInfo.InvariantCulture,
                            "{0} {1:F0} m: 画面上の身長 {2:F1} px", id, d, hi.y - lo.y));
                    }

                    if (!Draw(camera, target, buffer))
                    {
                        EditorPaths.Report("描けませんでした: " + id + "（-nographics を付けていないか）");
                        continue;
                    }

                    File.WriteAllBytes(Path.Combine(folder, id + "_distances.png"), buffer.EncodeToPNG());
                    foreach (KeyValuePair<float, Rect> r in rects)
                    {
                        Texture2D crop = Enlarge(buffer, r.Value, 4);
                        File.WriteAllBytes(Path.Combine(folder, id + "_d" + r.Key.ToString("00", CultureInfo.InvariantCulture) + ".png"), crop.EncodeToPNG());
                        Object.DestroyImmediate(crop);
                    }

                    foreach (GameObject go in placed)
                    {
                        Object.DestroyImmediate(go);
                    }

                    placed.Clear();
                }
            }
            finally
            {
                RenderTexture.active = null;
                foreach (GameObject go in placed)
                {
                    Object.DestroyImmediate(go);
                }

                if (rig != null)
                {
                    Object.DestroyImmediate(rig);
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                if (buffer != null)
                {
                    Object.DestroyImmediate(buffer);
                }

                ShaderUtil.allowAsyncCompilation = wasAsync;
            }
        }

        /// <summary>遠近の確認で並べる距離 (m)。輪郭は 5 m から細くなり、30〜60 m で消える。</summary>
        private static readonly float[] LineupDistances = { 3f, 5f, 10f, 20f, 30f, 45f };

        /// <summary>rect（画面の画素座標、左下原点）を切り抜いて scale 倍に最近傍で拡大する。</summary>
        private static Texture2D Enlarge(Texture2D source, Rect rect, int scale)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.x), 0, source.width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(rect.y), 0, source.height - 1);
            int cw = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, source.width - x0);
            int ch = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, source.height - y0);
            Color[] src = source.GetPixels(x0, y0, cw, ch);
            var dst = new Color[cw * scale * ch * scale];
            for (int y = 0; y < ch * scale; y++)
            {
                for (int x = 0; x < cw * scale; x++)
                {
                    dst[y * cw * scale + x] = src[(y / scale) * cw + x / scale];
                }
            }

            var result = new Texture2D(cw * scale, ch * scale, TextureFormat.RGB24, false, false);
            result.SetPixels(dst);
            result.Apply(false);
            return result;
        }

        private static IEnumerable<string> Ids()
        {
            string filter = EditorPaths.ReadArgument("-charaIds", string.Empty);
            if (!string.IsNullOrEmpty(filter))
            {
                foreach (string id in filter.Split(','))
                {
                    if (id.Trim().Length > 0)
                    {
                        yield return id.Trim();
                    }
                }

                yield break;
            }

            foreach (string folder in AssetDatabase.GetSubFolders(EditorPaths.CharactersFolder))
            {
                yield return Path.GetFileName(folder);
            }
        }

        private static IEnumerable<Frame> Frames()
        {
            string pose = EditorPaths.ReadArgument("-charaPose", "idle").Trim().ToLowerInvariant();
            if (pose != "walk" && pose != "run" && pose != "jump")
            {
                yield return new Frame { Pose = "idle", Time = 0f };
                yield break;
            }

            string times = EditorPaths.ReadArgument("-charaPoseTimes", "0.1,0.2,0.3,0.4");
            int parsed = 0;
            foreach (string t in times.Split(','))
            {
                if (float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                {
                    yield return new Frame { Pose = pose, Time = Mathf.Max(0f, seconds) };
                    parsed++;
                }
            }

            if (parsed == 0)
            {
                EditorPaths.Report("-charaPoseTimes を数として読めませんでした: " + times);
            }
        }

        /// <summary>
        /// T ポーズのままだと腕が画面を横切るので、ゲームと同じ Animator Controller で最初の姿勢（Idle）にする。
        /// AnimationMode.SampleAnimationClip で Humanoid のクリップを当てるとゲームと違う姿勢になることがあるので使わない。
        /// </summary>
        private static void PoseAsInGame(GameObject body, string id, Frame frame)
        {
            AnimatorController controller = AnimatorFactory.EnsureForCharacter(id, ActorFactory.FbxPathOf(id));
            Animator animator = body.GetComponent<Animator>();
            if (controller == null || animator == null)
            {
                return;
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            animator.Update(0.02f);
            if (frame.Pose == "idle")
            {
                return;
            }

            // ゲームと同じパラメータ名で動かす。ただし GaitRate は既定の 1 のままなので、クリップは
            // 本来の速さで進む（ゲームでは PlayerAnimatorDriver が移動速度に合わせて変える）。
            // 歩き・走りは Locomotion ブレンドツリーの中の切り替えなので遷移は無い。Speed を入れてから
            // 十分に回して周期の頭をそろえ、そこから Time 秒進める。
            if (frame.Pose == "jump")
            {
                animator.SetBool("Grounded", false);
                animator.SetTrigger("Jump");
                // Time が 0 でもトリガーを受け付けさせる（下の while は Time > 0 のときしか回らない）。
                animator.Update(0f);
            }
            else
            {
                animator.SetFloat("Speed", frame.Pose == "run" ? AnimatorFactory.RunSpeed : AnimatorFactory.WalkSpeed);
                for (int i = 0; i < 30; i++)
                {
                    animator.Update(1f / 30f);
                }

                float length = animator.GetCurrentAnimatorStateInfo(0).length;
                float into = animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
                if (length > 0f)
                {
                    animator.Update((1f - into) * length);
                }
            }

            const float step = 1f / 60f;
            float left = frame.Time;
            while (left > 0f)
            {
                float dt = Mathf.Min(step, left);
                animator.Update(dt);
                left -= dt;
            }
        }

        private static Bounds BoundsOf(GameObject body)
        {
            // SkinnedMeshRenderer.bounds は取り込んだときの姿勢のままで、2 頭身の坊っちゃんでは頭が枠からはみ出す。
            // 今の姿勢で焼いたメッシュの頂点から測る。
            Bounds? bounds = null;
            var baked = new Mesh();
            try
            {
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled)
                    {
                        continue;
                    }

                    if (r is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(baked, false);
                        Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                        foreach (Vector3 v in baked.vertices)
                        {
                            bounds = Grow(bounds, new Bounds(toWorld.MultiplyPoint3x4(v), Vector3.zero));
                        }
                    }
                    else
                    {
                        bounds = Grow(bounds, r.bounds);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }

            return bounds ?? new Bounds(body.transform.position, Vector3.one);
        }

        private static Bounds Grow(Bounds? bounds, Bounds add)
        {
            if (bounds is Bounds b)
            {
                b.Encapsulate(add);
                return b;
            }

            return add;
        }

        /// <summary>
        /// 顔の中心。頭のボーン（首の付け根寄り）と頭頂の間の 45% の高さ。
        /// span は頭のボーンから頭頂までの高さ（2 頭身の坊っちゃんでは体の高さの 1 割では足りない）。
        /// </summary>
        private static Vector3 HeadCenter(GameObject body, Bounds bounds, out float span)
        {
            Animator animator = body.GetComponent<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null)
            {
                span = bounds.size.y * 0.18f;
                return new Vector3(bounds.center.x, bounds.max.y - span * 0.5f, bounds.center.z);
            }

            Vector3 p = head.position;
            span = Mathf.Max(bounds.max.y - p.y, bounds.size.y * 0.08f);
            p.y = Mathf.Lerp(p.y, bounds.max.y, 0.45f);
            return p;
        }

        /// <summary>Blender で焼いた輪郭シェル（反転ハル）。マテリアル名 &lt;id&gt;_outline で見分ける。</summary>
        private static List<Renderer> Shells(GameObject body)
        {
            var shells = new List<Renderer>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.name.EndsWith("_outline"))
                    {
                        shells.Add(r);
                        break;
                    }
                }
            }

            return shells;
        }

        private static Light FindSun()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }

            return null;
        }

        private static Camera Setup(GameObject rig)
        {
            Camera camera = rig.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 900f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderShadows = true;
                data.volumeLayerMask = ~0;
            }

            return camera;
        }

        /// <summary>描いて読み戻す。目印の色のままなら false（ScenePreview.Draw と同じ考え方）。</summary>
        private static bool Draw(Camera camera, RenderTexture target, Texture2D buffer)
        {
            Clear(target);
            camera.Render();
            camera.Render();
            if (Read(target, buffer))
            {
                return true;
            }

            Clear(target);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target, mipLevel = 0 };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            return Read(target, buffer);
        }

        private static void Clear(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, MarkerColor);
            RenderTexture.active = previous;
        }

        /// <summary>読み戻して、目印の色以外の画素があれば true。</summary>
        private static bool Read(RenderTexture target, Texture2D buffer)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            buffer.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            buffer.Apply(false);
            RenderTexture.active = previous;

            Color32[] pixels = buffer.GetPixels32();
            Color32 first = pixels[0];
            for (int i = 0; i < pixels.Length; i += 97)
            {
                Color32 p = pixels[i];
                if (p.r != first.r || p.g != first.g || p.b != first.b)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
