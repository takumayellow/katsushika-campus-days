using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Editor
{
    /// <summary>
    /// 屋外の水面（site_water）のマテリアルを KCD/Water に合わせる (#58)。
    ///
    /// 以前の水は URP Lit の半透明で、平らな板に空が映るだけの氷のような見た目だった。
    /// KCD/Water は縁からの距離をワールド位置と水盤の矩形から出す（Web の URP 設定は深度テクスチャを切っているため）。
    /// その矩形とキャンパス軸は、手で書き写さずに <see cref="CampusStage.Basins"/> と <see cref="CampusProps.Local"/> から入れる。
    ///
    /// 見た目の数値の正はシェーダの Properties の既定値。ここでは毎回その既定値へ戻す
    /// （MaterialLibrary の Repaint と同じく、コードを直したのに古い .mat の値が残る事故を防ぐ）。
    /// 例外は _BaseColor（MaterialLibrary の CampusColors が正）と、ここで計算して入れる水盤の形とテクスチャ。
    /// </summary>
    public static class WaterMaterial
    {
        public const string ShaderName = "KCD/Water";

        /// <summary>さざ波のテクスチャ。RG = 高さの傾き、B = ゆらぎ、A = 高さ（いずれも 0〜1 に詰めた線形の値）。</summary>
        public const string RippleTexture = "Assets/Generated/Textures/water_ripple.png";

        /// <summary>さざ波のテクスチャの 1 辺（px）。128² の RGBA32 にミップを付けて約 85 KB。</summary>
        public const int RippleSize = 128;

        /// <summary>シェーダが受け取れる矩形の数（_Basin0〜_Basin3）。</summary>
        public const int BasinSlots = 4;

        private const string BaseColorProperty = "_BaseColor";
        private const string RippleProperty = "_RippleMap";
        private const string AxisProperty = "_CampusAxis";
        private static readonly string[] BasinProperties = { "_Basin0", "_Basin1", "_Basin2", "_Basin3" };

        /// <summary>辺が接しているとみなす距離（m）。</summary>
        private const float Touch = 1e-4f;

        /// <summary>マテリアルを KCD/Water にして、既定値・水盤の形・さざ波のテクスチャを入れる。変えたときだけ dirty にする。</summary>
        public static void Apply(Material material)
        {
            if (material == null)
            {
                return;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                EditorPaths.Report("水のシェーダが見つかりません: " + ShaderName);
                return;
            }

            bool changed = false;
            if (material.shader != shader)
            {
                material.shader = shader;
                changed = true;
            }

            changed |= ResetToShaderDefaults(material, shader);

            Vector4[] basins = ShaderBasins(CampusStage.Basins);
            for (int i = 0; i < BasinSlots; i++)
            {
                changed |= SetVector(material, BasinProperties[i], basins[i]);
            }

            Vector2 axis = CampusProps.Local(1f, 0f);
            changed |= SetVector(material, AxisProperty, new Vector4(axis.x, axis.y, 0f, 0f));

            Texture2D ripple = EnsureRippleTexture();
            if (ripple != null && material.GetTexture(RippleProperty) != ripple)
            {
                material.SetTexture(RippleProperty, ripple);
                changed = true;
            }

            // URP Lit の頃のキーワード（_SURFACE_TYPE_TRANSPARENT など）を残さない。KCD/Water はマテリアルのキーワードを使わない。
            if (material.shaderKeywords.Length > 0)
            {
                material.shaderKeywords = new string[0];
                changed = true;
            }

            if (material.renderQueue != (int)RenderQueue.Transparent)
            {
                material.renderQueue = (int)RenderQueue.Transparent;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// シェーダへ渡す矩形。となりの矩形がこちらの辺を丸ごと覆っているとき、その辺を相手の向こう側の辺まで延ばす。
        ///
        /// シェーダは「矩形ごとの内側の距離の最大」を縁からの距離とみなす。矩形をそのまま渡すと、2 枚が接する継ぎ目で
        /// 距離が 0 に落ち、水の真ん中に縁の白い線と暗い帯が出てしまう。延ばした矩形も水面の内側に収まるので、
        /// 距離を多く見積もることはない（少なく見積もるのは入り隅の近くだけ）。
        /// 空きの枠は (0, 0, 0, 0)。内側の距離が常に 0 以下になるので最大を取っても効かない。
        /// </summary>
        public static Vector4[] ShaderBasins(IList<Vector4> basins)
        {
            var result = new Vector4[BasinSlots];
            if (basins == null)
            {
                return result;
            }

            if (basins.Count > BasinSlots)
            {
                EditorPaths.Report("水盤の矩形が " + basins.Count + " 枚あり、KCD/Water の枠 " + BasinSlots
                    + " 枚を超えています。超えた分の水面は縁の距離が出ません。");
            }

            int count = Mathf.Min(basins.Count, BasinSlots);
            for (int i = 0; i < count; i++)
            {
                Vector4 r = basins[i];
                float u0 = r.x;
                float v0 = r.y;
                float u1 = r.z;
                float v1 = r.w;
                for (int j = 0; j < basins.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    Vector4 o = basins[j];
                    bool coversV = o.y <= r.y + Touch && o.w >= r.w - Touch;
                    bool coversU = o.x <= r.x + Touch && o.z >= r.z - Touch;
                    if (coversV && Mathf.Abs(o.x - r.z) < Touch)
                    {
                        u1 = Mathf.Max(u1, o.z);
                    }

                    if (coversV && Mathf.Abs(o.z - r.x) < Touch)
                    {
                        u0 = Mathf.Min(u0, o.x);
                    }

                    if (coversU && Mathf.Abs(o.y - r.w) < Touch)
                    {
                        v1 = Mathf.Max(v1, o.w);
                    }

                    if (coversU && Mathf.Abs(o.w - r.y) < Touch)
                    {
                        v0 = Mathf.Min(v0, o.y);
                    }
                }

                result[i] = new Vector4(u0, v0, u1, v1);
            }

            return result;
        }

        /// <summary>
        /// さざ波のテクスチャを作って取り込む。中身が同じなら書き直さない。
        /// 正弦波の和なので、端と端がつながって継ぎ目なく並べられる（向き・波数は整数で、周期がテクスチャ 1 枚に収まる）。
        /// </summary>
        public static Texture2D EnsureRippleTexture()
        {
            EditorPaths.EnsureFolder(Path.GetDirectoryName(RippleTexture).Replace('\\', '/'));

            byte[] png = EncodeRipplePng();
            string absolute = EditorPaths.ProjectRelative(RippleTexture);
            if (!File.Exists(absolute) || !SameBytes(File.ReadAllBytes(absolute), png))
            {
                File.WriteAllBytes(absolute, png);
                AssetDatabase.ImportAsset(RippleTexture, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = AssetImporter.GetAtPath(RippleTexture) as TextureImporter;
            if (importer == null)
            {
                EditorPaths.Report("さざ波のテクスチャを取り込めません: " + RippleTexture);
                return null;
            }

            if (ConfigureImporter(importer))
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(RippleTexture);
        }

        private static bool ResetToShaderDefaults(Material material, Shader shader)
        {
            bool changed = false;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                if (IsComputed(name))
                {
                    continue;
                }

                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        Color color = shader.GetPropertyDefaultVectorValue(i);
                        if (material.GetColor(name) != color)
                        {
                            material.SetColor(name, color);
                            changed = true;
                        }

                        break;
                    case ShaderPropertyType.Vector:
                        changed |= SetVector(material, name, shader.GetPropertyDefaultVectorValue(i));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        float value = shader.GetPropertyDefaultFloatValue(i);
                        if (material.GetFloat(name) != value)
                        {
                            material.SetFloat(name, value);
                            changed = true;
                        }

                        break;
                    case ShaderPropertyType.Int:
                        int number = shader.GetPropertyDefaultIntValue(i);
                        if (material.GetInteger(name) != number)
                        {
                            material.SetInteger(name, number);
                            changed = true;
                        }

                        break;
                }
            }

            return changed;
        }

        /// <summary>既定値へ戻さない（ほかで決める）プロパティ。</summary>
        private static bool IsComputed(string name)
        {
            return name == BaseColorProperty || name == RippleProperty || name == AxisProperty
                || Array.IndexOf(BasinProperties, name) >= 0;
        }

        private static bool SetVector(Material material, string name, Vector4 value)
        {
            if (material.GetVector(name) == value)
            {
                return false;
            }

            material.SetVector(name, value);
            return true;
        }

        private static bool ConfigureImporter(TextureImporter importer)
        {
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            // 傾きと高さの数値なので、色として sRGB → 線形の変換をかけない。圧縮もしない（傾きが段になる）。
            if (importer.sRGBTexture || importer.alphaIsTransparency
                || importer.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            // 遠くでは細かい波をミップで平らにし、きらめきがちらつかないようにする。
            if (!importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Repeat
                || importer.filterMode != FilterMode.Trilinear)
            {
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                dirty = true;
            }

            if (importer.maxTextureSize < RippleSize)
            {
                importer.maxTextureSize = RippleSize;
                dirty = true;
            }

            return dirty;
        }

        private static byte[] EncodeRipplePng()
        {
            var texture = new Texture2D(RippleSize, RippleSize, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(RipplePixels());
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return png;
        }

        private readonly struct Wave
        {
            public readonly int Kx;
            public readonly int Ky;
            public readonly double Amplitude;
            public readonly double Phase;

            public Wave(int kx, int ky, double amplitude, double phase)
            {
                Kx = kx;
                Ky = ky;
                Amplitude = amplitude;
                Phase = phase;
            }
        }

        /// <summary>
        /// 向きを黄金角で散らした正弦波。波数 (kx, ky) は整数なので、テクスチャ 1 枚でちょうど周期が閉じる。
        /// 振幅は 1/|k|（細かい波ほど低い）。乱数を使わないので、何度作っても同じ画素になる。
        /// </summary>
        private static List<Wave> Waves(int count, double kMin, double kSpan, double phaseSeed, double kSeed, double angle0)
        {
            var waves = new List<Wave>();
            for (int i = 0; i < count; i++)
            {
                double angle = i * 2.39996323 + angle0;
                double k = kMin + kSpan * Frac(i * kSeed);
                int kx = (int)Math.Round(k * Math.Cos(angle));
                int ky = (int)Math.Round(k * Math.Sin(angle));
                if (kx == 0 && ky == 0)
                {
                    continue;
                }

                double amplitude = 1.0 / Math.Sqrt(kx * kx + ky * ky);
                double phase = 2.0 * Math.PI * Frac(i * phaseSeed + 0.1);
                waves.Add(new Wave(kx, ky, amplitude, phase));
            }

            return waves;
        }

        private static Color32[] RipplePixels()
        {
            List<Wave> heightWaves = Waves(12, 3.0, 5.0, 0.7548776662, 0.618034, 0.3);
            List<Wave> noiseWaves = Waves(6, 2.0, 3.0, 0.5698402910, 0.4142136, 1.1);

            int n = RippleSize;
            var height = new double[n * n];
            var slopeX = new double[n * n];
            var slopeY = new double[n * n];
            var noise = new double[n * n];
            for (int py = 0; py < n; py++)
            {
                double y = (py + 0.5) / n;
                for (int px = 0; px < n; px++)
                {
                    double x = (px + 0.5) / n;
                    int index = py * n + px;
                    foreach (Wave wave in heightWaves)
                    {
                        double arg = 2.0 * Math.PI * (wave.Kx * x + wave.Ky * y) + wave.Phase;
                        double cos = Math.Cos(arg);
                        height[index] += wave.Amplitude * Math.Sin(arg);
                        slopeX[index] += wave.Amplitude * 2.0 * Math.PI * wave.Kx * cos;
                        slopeY[index] += wave.Amplitude * 2.0 * Math.PI * wave.Ky * cos;
                    }

                    foreach (Wave wave in noiseWaves)
                    {
                        noise[index] += wave.Amplitude * Math.Sin(2.0 * Math.PI * (wave.Kx * x + wave.Ky * y) + wave.Phase);
                    }
                }
            }

            double maxSlope = 1e-9;
            for (int i = 0; i < slopeX.Length; i++)
            {
                maxSlope = Math.Max(maxSlope, Math.Max(Math.Abs(slopeX[i]), Math.Abs(slopeY[i])));
            }

            MinAndSpan(height, out double heightMin, out double heightSpan);
            MinAndSpan(noise, out double noiseMin, out double noiseSpan);

            var pixels = new Color32[n * n];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(
                    ToByte(slopeX[i] / maxSlope * 0.5 + 0.5),
                    ToByte(slopeY[i] / maxSlope * 0.5 + 0.5),
                    ToByte((noise[i] - noiseMin) / noiseSpan),
                    ToByte((height[i] - heightMin) / heightSpan));
            }

            return pixels;
        }

        private static void MinAndSpan(double[] values, out double min, out double span)
        {
            min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in values)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            span = Math.Max(max - min, 1e-9);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Round(255.0 * Math.Min(1.0, Math.Max(0.0, value)));
        }

        private static double Frac(double value)
        {
            return value - Math.Floor(value);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
