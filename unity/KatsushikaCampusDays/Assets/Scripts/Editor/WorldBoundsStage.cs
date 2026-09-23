using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 歩ける範囲の縁に立てる見えない壁（#40）。範囲は WorldBounds.DefaultArea（WorldBounds.HalfExtent）から作るので、
    /// 実行時の見張り（WorldBounds）と必ず同じ矩形になる。壁の内側の面がちょうど矩形の縁に来る。
    /// レンダラを持たない厚い BoxCollider の垂直な面なので、CharacterController は揺れずにそのまま止まる。
    /// レイヤーは Default。カメラの当たり判定（Ground / Building）には入らないので、壁際でカメラが寄らない。
    /// </summary>
    public static class WorldBoundsStage
    {
        /// <summary>壁の厚さ（m）。薄いと速く当たったときにすり抜けることがあるので厚めにする。</summary>
        public const float WallThickness = 2f;

        /// <summary>壁の上端（m）。ジャンプは 1.1 m なので十分越えられない。</summary>
        public const float WallTop = 6f;

        /// <summary>壁の下端（m）。地面（y = -0.35）より下まで伸ばして、足元に隙間を作らない。</summary>
        public const float WallBottom = -2f;

        /// <summary>範囲の 4 辺に壁を立てる。CampusStage.Build の直後に呼ぶ。</summary>
        public static void Build(Transform root)
        {
            Build(root, WorldBounds.DefaultArea);
        }

        /// <summary>任意の矩形（xz）の 4 辺に壁を立てる。</summary>
        public static GameObject Build(Transform root, Rect area)
        {
            var group = new GameObject("WorldBoundsWalls");
            group.transform.SetParent(root, false);

            float height = WallTop - WallBottom;
            float centerY = (WallTop + WallBottom) * 0.5f;
            float half = WallThickness * 0.5f;

            // 南北の壁は角まで覆うよう東西へ厚さぶん伸ばす。東西の壁は矩形の辺の長さだけ。
            float spanX = area.width + WallThickness * 2f;
            float spanZ = area.height;

            AddWall(group.transform, "Wall_North",
                new Vector3(area.center.x, centerY, area.yMax + half), new Vector3(spanX, height, WallThickness));
            AddWall(group.transform, "Wall_South",
                new Vector3(area.center.x, centerY, area.yMin - half), new Vector3(spanX, height, WallThickness));
            AddWall(group.transform, "Wall_East",
                new Vector3(area.xMax + half, centerY, area.center.y), new Vector3(WallThickness, height, spanZ));
            AddWall(group.transform, "Wall_West",
                new Vector3(area.xMin - half, centerY, area.center.y), new Vector3(WallThickness, height, spanZ));

            // NavMesh のベイクには入れない（レンダラが無いので本来拾われないが、念のため）。子の壁にも効く。
            CampusStage.Ignore(group);

            EditorPaths.Report("範囲の壁を立てました: x " + area.xMin.ToString("F0") + ".." + area.xMax.ToString("F0")
                + " z " + area.yMin.ToString("F0") + ".." + area.yMax.ToString("F0"));
            return group;
        }

        private static void AddWall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.position = center;
            wall.transform.rotation = Quaternion.identity;

            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = size;
        }
    }
}
