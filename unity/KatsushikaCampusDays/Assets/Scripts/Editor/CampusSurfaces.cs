using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャンパスの面（敷石・アスファルト・芝・土・コンクリート・煉瓦など）に貼る写真のテクスチャ (#59 / #78)。
    ///
    /// 画像は tools/fetch_textures.py が ambientCG (CC0) から焼いて <see cref="Folder"/> に
    /// &lt;マテリアル名&gt;.jpg で置き、1 枚が何 cm 四方かを同じフォルダの tiling.json に書く。
    /// FBX の UV は Blender がメートル単位で書いている（blender/kcd_lib/uv.py）ので、
    /// タイリングを 100 / tile_cm にすれば画像が実寸で並ぶ。
    /// 画像の平均色は CampusColors の宣言 hex に合わせて焼いてあるので、貼った面の _BaseColor は
    /// 白にして画像の色をそのまま出す。遠くから見た色は単色だったときと変わらない。
    /// </summary>
    public static class CampusSurfaces
    {
        public const string Folder = "Assets/Textures/surfaces";
        public const string TilingJson = Folder + "/tiling.json";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [System.Serializable]
        private sealed class TilingEntry
        {
            public string material;
            public float tile_cm;
        }

        [System.Serializable]
        private sealed class TilingFile
        {
            public TilingEntry[] surfaces;
        }

        /// <summary>tiling.json を読む。{マテリアル名: tile_cm}。ファイルが無ければ空（どの面にも貼らない）。</summary>
        public static Dictionary<string, float> LoadTiling()
        {
            var tiling = new Dictionary<string, float>();
            string path = EditorPaths.ProjectRelative(TilingJson);
            if (!File.Exists(path))
            {
                return tiling;
            }

            TilingFile file = JsonUtility.FromJson<TilingFile>(File.ReadAllText(path));
            foreach (TilingEntry entry in file?.surfaces ?? new TilingEntry[0])
            {
                if (string.IsNullOrEmpty(entry.material) || entry.tile_cm <= 0f)
                {
                    Debug.LogWarning("[KCD] " + TilingJson + " に名前か tile_cm の無い行がある。その行は貼らない");
                    continue;
                }

                tiling[entry.material] = entry.tile_cm;
            }

            return tiling;
        }

        /// <summary>
        /// 面のテクスチャを貼る。貼る面でなくなったなら、以前ここで貼った画像を外す。変えたら true。
        /// <paramref name="textured"/> は、呼んだあとに画像が貼られた状態かどうか。
        /// _BaseColor（白か宣言の色か）は呼び出し側が決める。
        /// </summary>
        public static bool Apply(Material material, string name, Dictionary<string, float> tiling, out bool textured)
        {
            textured = false;
            if (material == null || !material.HasProperty(BaseMapId))
            {
                return false;
            }

            Texture current = material.GetTexture(BaseMapId);
            if (tiling.TryGetValue(name, out float tileCm))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".jpg");
                if (texture != null)
                {
                    textured = true;
                    float tiles = 100f / tileCm;
                    var scale = new Vector2(tiles, tiles);
                    if (current == texture && material.GetTextureScale(BaseMapId) == scale
                        && material.GetTextureOffset(BaseMapId) == Vector2.zero)
                    {
                        return false;
                    }

                    SetMap(material, texture, scale);
                    return true;
                }

                Debug.LogWarning("[KCD] " + TilingJson + " の " + name + " に画像 " + name + ".jpg が無いので、単色で塗る");
            }

            // 手で別の画像を貼ったマテリアルは触らない。外すのはこのフォルダの画像だけ。
            if (current == null || !AssetDatabase.GetAssetPath(current).StartsWith(Folder + "/"))
            {
                return false;
            }

            SetMap(material, null, Vector2.one);
            return true;
        }

        private static void SetMap(Material material, Texture texture, Vector2 scale)
        {
            // URP Lit は _BaseMap を描き、_MainTex は旧 API 用の写し。両方そろえておく。
            foreach (int id in new[] { BaseMapId, MainTexId })
            {
                if (!material.HasProperty(id))
                {
                    continue;
                }

                material.SetTexture(id, texture);
                material.SetTextureScale(id, scale);
                material.SetTextureOffset(id, Vector2.zero);
            }
        }

        /// <summary>
        /// 実行時に作る Plane（10 m 四方で UV は 0..1）を、UV がメートルになる複製へ差し替える。
        /// FBX の面と同じタイリングで、画像が実寸で並ぶ。<paramref name="size"/> は置いたときの実寸（m）。
        /// 複製はシーンに一緒に保存される（Plane は 121 頂点なので小さい）。
        /// </summary>
        public static void UseMetricUv(MeshFilter filter, Vector2 size)
        {
            Mesh source = filter.sharedMesh;
            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_metric";

            Vector2[] uv = source.uv;
            var metric = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                metric[i] = Vector2.Scale(uv[i], size);
            }

            mesh.uv = metric;
            filter.sharedMesh = mesh;
        }

        /// <summary>
        /// 上から見た位置（m）を UV にした複製へ差し替える。円柱の台座のように UV が面ごとに 0..1 の
        /// プリミティブでも、上面は実寸で並ぶ。高さは UV に入れないので、側面には縁の画素が縦に引き伸ばされる。
        /// <paramref name="scale"/> は置いたときの localScale（メッシュの 1 単位が何 m か）。
        /// 親ごと拡大縮小されたときは、画像も一緒に拡大縮小される。
        /// </summary>
        public static void UseTopDownUv(MeshFilter filter, Vector3 scale)
        {
            Mesh source = filter.sharedMesh;
            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_topdown";

            Vector3[] vertices = source.vertices;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                uv[i] = new Vector2(vertices[i].x * scale.x, vertices[i].z * scale.z);
            }

            mesh.uv = uv;
            filter.sharedMesh = mesh;
        }
    }
}
