using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD
{
    /// <summary>
    /// 写真スポットの三脚のカメラ (#170)。画面ではなく専用のカメラで RenderTexture に描き、Texture2D にして返す。
    /// 画面を撮らないので、HUD も肩越しカメラの位置も写らない（batchmode のテストでも撮れる）。
    /// 設定はメインのカメラ（ポストプロセス・描く層・空）を写し、画角と位置だけ構図に合わせる。
    /// </summary>
    public static class SelfieCamera
    {
        public const int Width = 1600;
        public const int Height = 900;

        /// <summary>描けたかを見分けるために、描く前に塗っておく色（この色のままなら描けていない）。</summary>
        private static readonly Color32 Marker = new Color32(255, 0, 255, 255);

        /// <summary>描画のデバイスが無いときに返す写真の色。</summary>
        private static readonly Color32 Unrendered = new Color32(128, 128, 128, 255);

        /// <summary>
        /// position から rotation の向きに fieldOfView（縦、度）で撮る。描けなければ null。
        /// hidden（三脚）の下の Renderer は撮る間だけ消す（カメラを三脚の後ろへ引いたとき脚が写らないように）。
        /// 描画のデバイスが無いとき（-nographics の batchmode）は、カメラを作らずに一色で塗った写真を返す。
        /// 返した Texture2D は呼んだ側が Destroy する。
        /// </summary>
        public static Texture2D Shoot(Vector3 position, Quaternion rotation, float fieldOfView, Transform hidden = null)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return UnrenderedShot();
            }

            GameObject rig = null;
            RenderTexture target = null;
            Texture2D shot = null;
            List<Renderer> turnedOff = Hide(hidden);
            try
            {
                rig = new GameObject("__SelfieCamera") { hideFlags = HideFlags.DontSave };
                Camera camera = Setup(rig, position, rotation, fieldOfView);
                AddFillLight(rig);

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);

                if (Draw(camera, target, shot))
                {
                    Texture2D result = shot;
                    shot = null;
                    return result;
                }

                Debug.LogWarning("[KCD] 記念写真のカメラが描けなかった");
                return null;
            }
            finally
            {
                foreach (Renderer renderer in turnedOff)
                {
                    if (renderer != null)
                    {
                        renderer.forceRenderingOff = false;
                    }
                }

                if (shot != null)
                {
                    Object.Destroy(shot);
                }

                if (target != null)
                {
                    target.Release();
                    Object.Destroy(target);
                }

                if (rig != null)
                {
                    Object.Destroy(rig);
                }
            }
        }

        /// <summary>
        /// 描画のデバイスが無いときの写真。そのときにカメラを描かせると、URP が描くための RenderTexture を作れずにエラーのログを
        /// 出すだけで何も写らない（読み戻しても画素は変わらない）。描かずに一色で塗った絵を返し、撮ったあとの保存と数え上げは
        /// いつもどおり進める。
        /// </summary>
        private static Texture2D UnrenderedShot()
        {
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var pixels = new Color32[Width * Height];
            System.Array.Fill(pixels, Unrendered);
            shot.SetPixels32(pixels);
            shot.Apply(false);
            return shot;
        }

        /// <summary>root の下で描かれている Renderer を消し、消したものを返す（もとから消えているものは触らない）。</summary>
        private static List<Renderer> Hide(Transform root)
        {
            var turnedOff = new List<Renderer>();
            if (root == null)
            {
                return turnedOff;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.forceRenderingOff)
                {
                    renderer.forceRenderingOff = true;
                    turnedOff.Add(renderer);
                }
            }

            return turnedOff;
        }

        private static Camera Setup(GameObject rig, Vector3 position, Quaternion rotation, float fieldOfView)
        {
            Camera camera = rig.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null)
            {
                camera.CopyFrom(main);
            }
            else
            {
                camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                camera.allowHDR = true;
            }

            // CopyFrom は位置と向きも写すので、構図はそのあとで入れる。毎フレームは描かず、呼んだときだけ描く。
            rig.transform.SetPositionAndRotation(position, rotation);
            camera.enabled = false;
            // 物理カメラの設定まで写すと、画角がセンサーの大きさから決め直されて構図がずれる。
            camera.usePhysicalProperties = false;
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.depth = -100f;
            camera.allowMSAA = false;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData source = main != null ? main.GetUniversalAdditionalCameraData() : null;
            if (data != null)
            {
                data.renderType = CameraRenderType.Base;
                data.renderPostProcessing = source == null || source.renderPostProcessing;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderShadows = true;
                data.volumeLayerMask = source != null ? source.volumeLayerMask : (LayerMask)~0;
            }

            return camera;
        }

        /// <summary>
        /// カメラの横から人物へ当てる弱い補助光。夜や屋内の暗い所でも顔が沈まないように、撮る間だけ置く。
        /// rig と一緒に消える。
        /// </summary>
        private static void AddFillLight(GameObject rig)
        {
            var holder = new GameObject("FillLight");
            holder.transform.SetParent(rig.transform, false);
            holder.transform.localPosition = new Vector3(0.6f, 0.4f, 0f);
            Light fill = holder.AddComponent<Light>();
            fill.type = LightType.Spot;
            fill.spotAngle = 55f;
            fill.range = 9f;
            fill.intensity = 1.6f;
            fill.color = new Color(1f, 0.95f, 0.88f);
            fill.shadows = LightShadows.None;
        }

        /// <summary>描いて読み戻す。描画要求で描けなければ Camera.Render で描き直す（CharacterPreview の逆順）。</summary>
        private static bool Draw(Camera camera, RenderTexture target, Texture2D buffer)
        {
            var request = new RenderPipeline.StandardRequest { destination = target, mipLevel = 0 };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                Clear(target);
                RenderPipeline.SubmitRenderRequest(camera, request);
                if (Read(target, buffer))
                {
                    return true;
                }
            }

            Clear(target);
            camera.Render();
            return Read(target, buffer);
        }

        private static void Clear(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Marker);
            RenderTexture.active = previous;
        }

        /// <summary>読み戻して、塗っておいた色以外の画素があれば true。</summary>
        private static bool Read(RenderTexture target, Texture2D buffer)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            buffer.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            buffer.Apply(false);
            RenderTexture.active = previous;

            Color32[] pixels = buffer.GetPixels32();
            for (int i = 0; i < pixels.Length; i += 97)
            {
                Color32 p = pixels[i];
                if (p.r != Marker.r || p.g != Marker.g || p.b != Marker.b)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
