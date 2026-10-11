using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 箱・回転体・トーラス・星形を組み合わせて、マテリアルごとのサブメッシュを持つ小物のメッシュを作る (#167)。
    ///
    /// 部品は呼ぶたびに位置・回転・拡大（<paramref name="place"/>）を付けて置く。面の表は Unity の流儀
    /// （外から見て時計回り）。回転体は面の向きを輪郭の並びで決め、輪郭の点が 2 回続く所で頂点を分けて角を立てる。
    /// </summary>
    public sealed class PartMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<string> _materials = new List<string>();
        private readonly List<List<int>> _triangles = new List<List<int>>();

        /// <summary>サブメッシュの順に並んだマテリアル名。</summary>
        public IReadOnlyList<string> Materials => _materials;

        /// <summary>位置だけの置き方。</summary>
        public static Matrix4x4 At(float x, float y, float z)
        {
            return Matrix4x4.Translate(new Vector3(x, y, z));
        }

        /// <summary>位置と回転（Unity の Euler 角、度）の置き方。</summary>
        public static Matrix4x4 At(Vector3 position, Vector3 eulerDegrees)
        {
            return Matrix4x4.TRS(position, Quaternion.Euler(eulerDegrees), Vector3.one);
        }

        /// <summary>位置・回転・拡大の置き方。</summary>
        public static Matrix4x4 At(Vector3 position, Vector3 eulerDegrees, Vector3 scale)
        {
            return Matrix4x4.TRS(position, Quaternion.Euler(eulerDegrees), scale);
        }

        /// <summary>中心が原点の箱。</summary>
        public PartMesh Box(string material, Vector3 size, Matrix4x4 place)
        {
            List<int> slot = Slot(material);
            Vector3 e = size * 0.5f;
            Vector3 P(float x, float y, float z) => new Vector3(x * e.x, y * e.y, z * e.z);
            Quad(slot, place, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));     // +Z
            Quad(slot, place, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)); // -Z
            Quad(slot, place, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));     // +X
            Quad(slot, place, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)); // -X
            Quad(slot, place, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));     // +Y
            Quad(slot, place, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // -Y
            return this;
        }

        /// <summary>
        /// Y 軸まわりの回転体。profile は (半径, 高さ) を、外側の面を右に見ながらたどる順に並べる
        /// （閉じた立体なら下の軸上 → 外側を上へ → 上の軸上）。半径 0 の点は 1 頂点にまとめる。
        /// 同じ点が 2 回続く所で頂点を分け、そこを角にする。doubleSided なら裏面も張る（口の開いたコップなど）。
        /// 裏面は別の頂点に張る。表と同じ頂点を使うと、法線を計算したときに表と裏が打ち消し合って 0 になる。
        /// </summary>
        public PartMesh Lathe(string material, Vector2[] profile, int sides, Matrix4x4 place, bool doubleSided = false)
        {
            List<int> slot = Slot(material);
            int start = 0;
            for (int i = 1; i <= profile.Length; i++)
            {
                bool crease = i < profile.Length && profile[i] == profile[i - 1];
                if (i == profile.Length || crease)
                {
                    LatheRun(slot, profile, start, i, sides, place, false);
                    if (doubleSided)
                    {
                        LatheRun(slot, profile, start, i, sides, place, true);
                    }

                    start = i;
                }
            }

            return this;
        }

        /// <summary>底と天井を閉じた円柱（Y 軸、中心が原点）。縁は角を立てる。</summary>
        public PartMesh Cylinder(string material, float radius, float height, int sides, Matrix4x4 place)
        {
            float h = height * 0.5f;
            return Lathe(material, new[]
            {
                new Vector2(0f, -h), new Vector2(radius, -h), new Vector2(radius, -h),
                new Vector2(radius, h), new Vector2(radius, h), new Vector2(0f, h),
            }, sides, place);
        }

        /// <summary>楕円体（中心が原点、半径 radii）。</summary>
        public PartMesh Ellipsoid(string material, Vector3 radii, int rings, int sides, Matrix4x4 place)
        {
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * (i / (float)rings - 0.5f);
                profile[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            profile[0].x = 0f;
            profile[rings].x = 0f;
            return Lathe(material, profile, sides, place * Matrix4x4.Scale(radii));
        }

        /// <summary>
        /// XZ 面に寝たトーラス（中心が原点、Y 軸まわり）。sides を 8 にすると八角の輪になる。
        /// </summary>
        public PartMesh Torus(string material, float major, float minor, int sides, int tubeSides, Matrix4x4 place)
        {
            List<int> slot = Slot(material);
            int first = _vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j < tubeSides; j++)
                {
                    float b = j * Mathf.PI * 2f / tubeSides;
                    Vector3 local = radial * (major + Mathf.Cos(b) * minor) + Vector3.up * (Mathf.Sin(b) * minor);
                    _vertices.Add(place.MultiplyPoint3x4(local));
                }
            }

            for (int i = 0; i < sides; i++)
            {
                int i1 = (i + 1) % sides;
                for (int j = 0; j < tubeSides; j++)
                {
                    int j1 = (j + 1) % tubeSides;
                    int a = first + i * tubeSides + j;
                    int b = first + i1 * tubeSides + j;
                    int c = first + i1 * tubeSides + j1;
                    int d = first + i * tubeSides + j1;
                    // 管の外へ向く面（a→d が管の外を上へ回る向き、a→b が輪を回る向き）。
                    slot.AddRange(new[] { a, d, c, a, c, b });
                }
            }

            return this;
        }

        /// <summary>
        /// XY 面の星形（points 本の尖り、外の半径 outer・内の半径 inner）。両面に張る。
        /// </summary>
        public PartMesh Star(string material, int points, float outer, float inner, float depth, Matrix4x4 place)
        {
            List<int> slot = Slot(material);
            int count = points * 2;
            var rim = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = i % 2 == 0 ? outer : inner;
                rim[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }

            var front = new Vector3(0f, 0f, -depth * 0.5f);
            var back = new Vector3(0f, 0f, depth * 0.5f);
            for (int i = 0; i < count; i++)
            {
                Vector3 r0 = rim[i];
                Vector3 r1 = rim[(i + 1) % count];
                // 表は -Z から見る面、裏は +Z から見る面。厚みがあれば側面も張る。
                Tri(slot, place, front, r1 + front, r0 + front);
                Tri(slot, place, back, r0 + back, r1 + back);
                if (depth > 0f)
                {
                    Quad(slot, place, r0 + front, r1 + front, r1 + back, r0 + back);
                }
            }

            return this;
        }

        /// <summary>
        /// 1 つのメッシュにする。center なら外接箱の中心を原点に寄せる（浮かせて回す物の軸を真ん中にする）。
        /// </summary>
        public Mesh Build(string name, bool center)
        {
            var vertices = new List<Vector3>(_vertices);
            if (center && vertices.Count > 0)
            {
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 v in vertices)
                {
                    bounds.Encapsulate(v);
                }

                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] -= bounds.center;
                }
            }

            var mesh = new Mesh { name = name, subMeshCount = _triangles.Count };
            mesh.SetVertices(vertices);
            for (int i = 0; i < _triangles.Count; i++)
            {
                mesh.SetTriangles(_triangles[i], i);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private List<int> Slot(string material)
        {
            int index = _materials.IndexOf(material);
            if (index >= 0)
            {
                return _triangles[index];
            }

            _materials.Add(material);
            var triangles = new List<int>();
            _triangles.Add(triangles);
            return triangles;
        }

        /// <summary>輪郭の [from, to) を回して張る。半径 0 の点は軸上の 1 頂点にする。inward なら内を表にする。</summary>
        private void LatheRun(List<int> slot, Vector2[] profile, int from, int to, int sides, Matrix4x4 place,
            bool inward)
        {
            if (to - from < 2)
            {
                return;
            }

            var rings = new List<int>();
            for (int i = from; i < to; i++)
            {
                rings.Add(_vertices.Count);
                Vector2 p = profile[i];
                if (Mathf.Approximately(p.x, 0f))
                {
                    _vertices.Add(place.MultiplyPoint3x4(new Vector3(0f, p.y, 0f)));
                    continue;
                }

                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    _vertices.Add(place.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x)));
                }
            }

            for (int i = 0; i < rings.Count - 1; i++)
            {
                bool lowPole = Mathf.Approximately(profile[from + i].x, 0f);
                bool highPole = Mathf.Approximately(profile[from + i + 1].x, 0f);
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    int a = lowPole ? rings[i] : rings[i] + j;
                    int d = lowPole ? rings[i] : rings[i] + j1;
                    int b = highPole ? rings[i + 1] : rings[i + 1] + j;
                    int c = highPole ? rings[i + 1] : rings[i + 1] + j1;
                    // 輪郭を上る向き（a→b）と角度の増える向き（a→d）で、外が表になる並び。
                    if (!highPole)
                    {
                        AddFace(slot, a, b, c, inward);
                    }

                    if (!lowPole)
                    {
                        AddFace(slot, a, c, d, inward);
                    }
                }
            }
        }

        private static void AddFace(List<int> slot, int a, int b, int c, bool inward)
        {
            slot.AddRange(inward ? new[] { a, c, b } : new[] { a, b, c });
        }

        private void Quad(List<int> slot, Matrix4x4 place, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = _vertices.Count;
            _vertices.Add(place.MultiplyPoint3x4(a));
            _vertices.Add(place.MultiplyPoint3x4(b));
            _vertices.Add(place.MultiplyPoint3x4(c));
            _vertices.Add(place.MultiplyPoint3x4(d));
            slot.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        private void Tri(List<int> slot, Matrix4x4 place, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = _vertices.Count;
            _vertices.Add(place.MultiplyPoint3x4(a));
            _vertices.Add(place.MultiplyPoint3x4(b));
            _vertices.Add(place.MultiplyPoint3x4(c));
            slot.AddRange(new[] { i, i + 1, i + 2 });
        }
    }
}
