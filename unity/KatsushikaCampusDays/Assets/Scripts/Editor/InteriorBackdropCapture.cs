using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD.Editor
{
    // InteriorBackdropStage の撮影の側 (#60)。その建物がキャンパスで建っている位置と向きから
    // 30 度ずつ 12 枚撮り、2048 x 512 のパノラマ 1 枚に貼り合わせる。RenderTexture が要るので -nographics では動かない。
    public static partial class InteriorBackdropStage
    {
        /// <summary>-nographics では描けない。</summary>
        private static bool CanRender()
        {
            return SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }

        /// <summary>撮ってパノラマにする。途中で失敗したら null（呼び手が前の画か仮の画に落とす）。</summary>
        private static Color32[] TryCapture(Rig rig)
        {
            try
            {
                return Capture(rig);
            }
            catch (System.Exception error)
            {
                EditorPaths.Report("遠景 " + rig.Id + " を撮れませんでした: " + error.Message);
                return null;
            }
        }

        private static Color32[] Capture(Rig rig)
        {
            ShotFrame frame = FrameFor(rig);
            List<Renderer> hidden = HideForCapture(rig);
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            GameObject go = null;
            Camera camera = null;
            RenderTexture target = null;
            Texture2D buffer = null;
            try
            {
                Physics.SyncTransforms();
                DynamicGI.UpdateEnvironment();

                go = new GameObject(RigName) { hideFlags = HideFlags.DontSave };
                camera = SetupCamera(go);

                target = new RenderTexture(frame.Width, frame.Height, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                target.antiAliasing = 1;
                target.useMipMap = false;
                target.Create();
                buffer = new Texture2D(frame.Width, frame.Height, TextureFormat.RGB24, false, false);

                // 投影は targetTexture と aspect を決めてから入れる（後から aspect を変えると作り直される）。
                camera.targetTexture = target;
                camera.aspect = (float)frame.Width / frame.Height;
                float n = camera.nearClipPlane;
                camera.projectionMatrix = Matrix4x4.Frustum(
                    -frame.Half * n, frame.Half * n, frame.Bottom * n, frame.Top * n, n, camera.farClipPlane);

                Color32 marker = MeasureMarker(target, buffer);

                // バッチで開いた直後はパイプラインの実体がまだ無いことがあるので、1 回描いて起こす。
                camera.transform.SetPositionAndRotation(rig.Eye, Quaternion.Euler(0f, rig.Yaw, 0f));
                camera.Render();

                bool useRequest = false;
                var shots = new Color32[ShotCount][];
                float step = 360f / ShotCount;
                for (int s = 0; s < ShotCount; s++)
                {
                    camera.transform.SetPositionAndRotation(rig.Eye, Quaternion.Euler(0f, rig.Yaw + s * step, 0f));
                    if (!Draw(camera, target, buffer, marker, ref useRequest))
                    {
                        EditorPaths.Report("遠景 " + rig.Id + " の " + (s + 1) + " 枚目が描けません（目印の色のまま）。");
                        return null;
                    }

                    shots[s] = buffer.GetPixels32();
                }

                return Stitch(shots, frame, rig.TanBottom, rig.TanTop);
            }
            finally
            {
                if (camera != null)
                {
                    camera.targetTexture = null;
                    camera.ResetProjectionMatrix();
                }

                RenderTexture.active = null;
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                if (buffer != null)
                {
                    Object.DestroyImmediate(buffer);
                }

                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }

                foreach (Renderer renderer in hidden)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }

                ShaderUtil.allowAsyncCompilation = wasAsync;
            }
        }

        /// <summary>
        /// 1 枚の画角。左右は ±15 度 + 余白、上下は tan(仰角) の範囲を 1/cos(15 度) だけ広げる
        /// （横に振れた向きほど、同じ仰角でも画面の上下の端へ寄るため）。
        /// </summary>
        private static ShotFrame FrameFor(Rig rig)
        {
            float halfAngle = 0.5f * (2f * Mathf.PI / ShotCount);
            float pad = PadPixels / PixelsPerTan;
            float cos = Mathf.Cos(halfAngle);
            float half = Mathf.Tan(halfAngle) + pad;
            float bottom = Mathf.Min(rig.TanBottom, rig.TanBottom / cos) - pad;
            float top = Mathf.Max(rig.TanTop, rig.TanTop / cos) + pad;
            return new ShotFrame
            {
                Half = half,
                Bottom = bottom,
                Top = top,
                Width = Mathf.CeilToInt(2f * half * PixelsPerTan),
                Height = Mathf.CeilToInt((top - bottom) * PixelsPerTan),
            };
        }

        /// <summary>
        /// 撮るときに消すもの: その建物の外装（bld_&lt;id&gt; と bld_&lt;id&gt;_*。目がその中にある）、
        /// 人（SkinnedMeshRenderer）、建物の名札、屋内の並び（x &gt; 900 m）、近景が受け持つ範囲にすっぽり入る
        /// キャンパスの地物（Campus・Trees の下）。
        /// 消したものは返して、撮り終わったら戻す。
        /// </summary>
        private static List<Renderer> HideForCapture(Rig rig)
        {
            var hidden = new List<Renderer>();
            string own = "bld_" + rig.Id;
            int near = 0;
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                bool always = ShouldHide(renderer, own);
                bool inNear = !always && rig.NearRadius > 0f && IsSiteContent(renderer.transform)
                    && InsideNear(renderer.bounds, rig);
                if (always || inNear)
                {
                    renderer.enabled = false;
                    hidden.Add(renderer);
                    near += inNear ? 1 : 0;
                }
            }

            if (near > 0)
            {
                EditorPaths.Report("遠景 " + rig.Id + ": 近景が受け持つ範囲のもの " + near + " 個を写さずに撮ります。");
            }

            return hidden;
        }

        private static bool ShouldHide(Renderer renderer, string own)
        {
            string name = renderer.gameObject.name;
            if (name == own || name.StartsWith(own + "_", System.StringComparison.Ordinal))
            {
                return true;
            }

            if (renderer is SkinnedMeshRenderer || renderer.bounds.center.x > InteriorSlotX)
            {
                return true;
            }

            return renderer.GetComponentInParent<BuildingLabel>(true) != null;
        }

        /// <summary>campus.fbx か trees.fbx から置いたものか（親をたどって SiteRoots の名前に当たるか）。</summary>
        private static bool IsSiteContent(Transform node)
        {
            for (Transform t = node; t != null; t = t.parent)
            {
                if (System.Array.IndexOf(SiteRoots, t.name) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// キャンパスのワールドの AABB が、近景の範囲（建物の矩形から NearRadius 以内）にすっぽり入るか。
        /// 範囲は凸なので、AABB の水平の 4 隅がすべて入れば全体が入る。目は矩形の中心にあり、
        /// 屋内のローカルへは -yaw 回せば戻る（CampusEye の逆）。
        /// </summary>
        private static bool InsideNear(Bounds world, Rig rig)
        {
            if (rig.NearRadius <= 0f)
            {
                return false;
            }

            Quaternion toLocal = Quaternion.Euler(0f, -rig.Yaw, 0f);
            float limit = rig.NearRadius * rig.NearRadius;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner & 1) == 0 ? world.min.x : world.max.x;
                float z = (corner & 2) == 0 ? world.min.z : world.max.z;
                Vector3 local = toLocal * new Vector3(x - rig.Eye.x, 0f, z - rig.Eye.z);
                float dx = Mathf.Max(Mathf.Abs(local.x) - rig.NearHalf.x, 0f);
                float dz = Mathf.Max(Mathf.Abs(local.z) - rig.NearHalf.y, 0f);
                if (dx * dx + dz * dz > limit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>撮影用カメラ。ポストプロセスは掛けない（本編では屋内のカメラがドームごと 1 回掛ける）。</summary>
        private static Camera SetupCamera(GameObject go)
        {
            Camera camera = go.AddComponent<Camera>();
            camera.nearClipPlane = CaptureNear;
            camera.farClipPlane = CaptureFar;
            camera.cullingMask = CullingMask();
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.enabled = true;

            if (RenderSettings.skybox != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;
                data.volumeLayerMask = 0;
            }

            return camera;
        }

        private static int CullingMask()
        {
            int mask = ~0;
            foreach (string layer in HiddenLayers)
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0)
                {
                    mask &= ~(1 << index);
                }
            }

            return mask;
        }

        /// <summary>
        /// 1 枚描いて buffer へ読み戻す。Camera.Render() で描けなければ RenderPipeline.SubmitRenderRequest に切り替え、
        /// 以後はそちらだけを使う（ScenePreview.Draw と同じ理由）。目印の色のままなら false。
        /// </summary>
        private static bool Draw(Camera camera, RenderTexture target, Texture2D buffer, Color32 marker,
            ref bool useRequest)
        {
            if (!useRequest)
            {
                ClearToMarker(target);
                camera.Render();
                camera.Render();
                if (Drawn(target, buffer, marker))
                {
                    return true;
                }

                useRequest = true;
                EditorPaths.Report("Camera.Render() では描けないので、RenderPipeline.SubmitRenderRequest で撮ります。");
            }

            ClearToMarker(target);
            var request = new RenderPipeline.StandardRequest { destination = target, mipLevel = 0 };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            return Drawn(target, buffer, marker);
        }

        private static void ClearToMarker(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, MarkerColor);
            RenderTexture.active = previous;
        }

        private static void ReadBack(RenderTexture target, Texture2D buffer)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            buffer.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            buffer.Apply(false);
            RenderTexture.active = previous;
        }

        /// <summary>目印で塗って読み戻した色。sRGB 変換が入るので決め打ちにせず実測する。</summary>
        private static Color32 MeasureMarker(RenderTexture target, Texture2D buffer)
        {
            ClearToMarker(target);
            ReadBack(target, buffer);
            return buffer.GetPixel(0, 0);
        }

        /// <summary>読み戻して、目印と違う画素が 1 つでもあれば描けたとみなす。</summary>
        private static bool Drawn(RenderTexture target, Texture2D buffer, Color32 marker)
        {
            ReadBack(target, buffer);
            Color32[] pixels = buffer.GetPixels32();
            for (int i = 0; i < pixels.Length; i += 7)
            {
                Color32 p = pixels[i];
                if (p.r != marker.r || p.g != marker.g || p.b != marker.b)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 12 枚をパノラマ（行 0 = 下端）へ貼り合わせる。方位 φ（u）の画素は、φ にいちばん近い向きの 1 枚から、
        /// 画面の X = tan α（α = その 1 枚の正面からのずれ）、Y = tan(仰角) / cos α をバイリニアで引く。
        /// </summary>
        private static Color32[] Stitch(Color32[][] shots, ShotFrame frame, float tanBottom, float tanTop)
        {
            int n = Supersample;
            int columns = TextureWidth * n;
            int rows = TextureHeight * n;
            float step = 2f * Mathf.PI / ShotCount;

            var shotOf = new int[columns];
            var px = new float[columns];
            var secant = new float[columns];
            for (int c = 0; c < columns; c++)
            {
                float phi = (c + 0.5f) / columns * 2f * Mathf.PI;
                int s = Mathf.RoundToInt(phi / step);
                float alpha = phi - s * step;
                shotOf[c] = s % ShotCount;
                px[c] = (Mathf.Tan(alpha) + frame.Half) / (2f * frame.Half) * frame.Width - 0.5f;
                secant[c] = 1f / Mathf.Cos(alpha);
            }

            var tans = new float[rows];
            for (int r = 0; r < rows; r++)
            {
                tans[r] = tanBottom + (r + 0.5f) / rows * (tanTop - tanBottom);
            }

            float toPixel = frame.Height / (frame.Top - frame.Bottom);
            float weight = 1f / (n * n);
            var result = new Color32[TextureWidth * TextureHeight];
            for (int j = 0; j < TextureHeight; j++)
            {
                for (int i = 0; i < TextureWidth; i++)
                {
                    var sum = Vector3.zero;
                    for (int sy = 0; sy < n; sy++)
                    {
                        float t = tans[j * n + sy];
                        for (int sx = 0; sx < n; sx++)
                        {
                            int c = i * n + sx;
                            float py = (t * secant[c] - frame.Bottom) * toPixel - 0.5f;
                            sum += Bilinear(shots[shotOf[c]], frame.Width, frame.Height, px[c], py);
                        }
                    }

                    sum *= weight;
                    result[j * TextureWidth + i] = new Color32(ToByte(sum.x), ToByte(sum.y), ToByte(sum.z), 255);
                }
            }

            return result;
        }

        private static Vector3 Bilinear(Color32[] pixels, int width, int height, float x, float y)
        {
            x = Mathf.Clamp(x, 0f, width - 1f);
            y = Mathf.Clamp(y, 0f, height - 1f);
            int x0 = Mathf.Min((int)x, width - 2);
            int y0 = Mathf.Min((int)y, height - 2);
            float fx = x - x0;
            float fy = y - y0;
            Color32 a = pixels[y0 * width + x0];
            Color32 b = pixels[y0 * width + x0 + 1];
            Color32 c = pixels[(y0 + 1) * width + x0];
            Color32 d = pixels[(y0 + 1) * width + x0 + 1];
            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx * fy;
            return new Vector3(
                a.r * w00 + b.r * w10 + c.r * w01 + d.r * w11,
                a.g * w00 + b.g * w10 + c.g * w01 + d.g * w11,
                a.b * w00 + b.b * w10 + c.b * w01 + d.b * w11);
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
        }
    }
}
