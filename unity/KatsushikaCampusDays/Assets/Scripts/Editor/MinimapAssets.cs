using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// ミニマップに使う画像の支度。Blender 出力の minimap.png を Sprite として取り込み、
    /// 円形マスク・矢印・点・輪の小さな画像はここで描いて Assets/Generated/UI に置く。
    /// </summary>
    public static class MinimapAssets
    {
        public const string MapTexture = "Assets/Textures/minimap.png";
        public const string MapJson = "Assets/Textures/minimap.json";
        private const string Folder = "Assets/Generated/UI";

        /// <summary>minimap.json の内容。</summary>
        public struct MapInfo
        {
            public Vector2 Center;
            public float HalfExtent;
        }

        [System.Serializable]
        private sealed class MapJsonShape
        {
            public float center_x;
            public float center_z;
            public float half_extent;
        }

        /// <summary>minimap.json を読む。無ければキャンパスの既定値。</summary>
        public static MapInfo ReadInfo()
        {
            var info = new MapInfo { Center = new Vector2(13.69f, -29.15f), HalfExtent = 175.37f };
            string path = EditorPaths.ProjectRelative(MapJson);
            if (!File.Exists(path))
            {
                EditorPaths.Report("minimap.json が無いので既定の範囲を使います。");
                return info;
            }

            var shape = JsonUtility.FromJson<MapJsonShape>(File.ReadAllText(path));
            if (shape != null && shape.half_extent > 1f)
            {
                info.Center = new Vector2(shape.center_x, shape.center_z);
                info.HalfExtent = shape.half_extent;
            }

            return info;
        }

        /// <summary>俯瞰図を Sprite として読む。無ければ null（ミニマップは緑一色になる）。</summary>
        public static Sprite EnsureMapSprite()
        {
            var importer = AssetImporter.GetAtPath(MapTexture) as TextureImporter;
            if (importer == null)
            {
                EditorPaths.Report("minimap.png が見つかりません: " + MapTexture);
                return null;
            }

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (importer.mipmapEnabled || importer.maxTextureSize < 2048 || !importer.alphaIsTransparency)
            {
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = true;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(MapTexture);
        }

        /// <summary>白い円。マスクと縁取りに使う。</summary>
        public static Sprite Circle()
        {
            return Ensure("minimap_circle", 256, (x, y) => Disk(x, y, 256, 127f));
        }

        /// <summary>輪。目的地マーカー。</summary>
        public static Sprite Ring()
        {
            return Ensure("minimap_ring", 64, (x, y) => Disk(x, y, 64, 31f) - Disk(x, y, 64, 23f));
        }

        /// <summary>点。NPC マーカー。</summary>
        public static Sprite Dot()
        {
            return Ensure("minimap_dot", 32, (x, y) => Disk(x, y, 32, 14f));
        }

        /// <summary>上向きの矢印。プレイヤー。</summary>
        public static Sprite Arrow()
        {
            return Ensure("minimap_arrow", 64, (x, y) =>
            {
                float px = x + 0.5f - 32f;
                float py = y + 0.5f - 32f;
                // 先端が上（+y）。底辺の中央を少し切り込んだ矢羽根の形。
                float half = Mathf.Lerp(20f, 0f, Mathf.InverseLerp(-26f, 30f, py));
                bool inside = py > -26f && py < 30f && Mathf.Abs(px) < half;
                bool notch = py < -26f + 12f * (1f - Mathf.Abs(px) / 20f);
                return inside && !notch ? 1f : 0f;
            });
        }

        private static float Disk(int x, int y, int size, float radius)
        {
            float dx = x + 0.5f - size * 0.5f;
            float dy = y + 0.5f - size * 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(radius + 0.5f - distance);
        }

        private static Sprite Ensure(string name, int size, System.Func<int, int, float> alpha)
        {
            EditorPaths.EnsureFolder(Folder);
            string assetPath = Folder + "/" + name + ".png";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alpha(x, y)));
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(EditorPaths.ProjectRelative(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
