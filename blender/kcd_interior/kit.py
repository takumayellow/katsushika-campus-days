"""インテリア生成の最小プリミティブ（箱・回転フレーム・円柱）。

MeshBuilder は add_box / add_prism を持つが、軸に対して回転した小物を
大量に置くのでローカル座標 -> ワールドの写像をまとめた T を用意する。
座標はすべて「建物ローカル」（+Y が入口から奥へ、+Z が上）。
"""

import math

TAU = math.pi * 2.0


def box(mb, x0, y0, z0, x1, y1, z1, mat, top=None, bottom=None):
    """軸平行な直方体（6 面）。top/bottom を指定すると天面・底面だけ別マテリアル。"""
    if x1 < x0:
        x0, x1 = x1, x0
    if y1 < y0:
        y0, y1 = y1, y0
    if z1 < z0:
        z0, z1 = z1, z0
    poly = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    mb.add_prism(poly, z0, z1, mat, top or mat, bottom or mat)


def box_nb(mb, x0, y0, z0, x1, y1, z1, mat):
    """底面を省いた直方体（大量配置する小物用。1 個あたり 2 三角形を節約）。"""
    if x1 < x0:
        x0, x1 = x1, x0
    if y1 < y0:
        y0, y1 = y1, y0
    if z1 < z0:
        z0, z1 = z1, z0
    mb.add_prism([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z0, z1, mat, mat, None)


def box_c(mb, cx, cy, z0, sx, sy, z1, mat, top=None):
    box(mb, cx - sx * 0.5, cy - sy * 0.5, z0, cx + sx * 0.5, cy + sy * 0.5, z1,
        mat, top)


def plate(mb, x0, y0, x1, y1, z, mat, flip=False):
    """水平な 1 枚板（厚みなし）。flip=True で下向き（天井用）。"""
    pts = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
    if flip:
        pts.reverse()
    mb.add_face(pts, mat)


def vplate(mb, a, b, z0, z1, mat, flip=False):
    """2 点間の垂直な板（厚みなし）。a, b は 2D。"""
    pts = [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)]
    if flip:
        pts.reverse()
    mb.add_face(pts, mat)


def cyl(mb, cx, cy, z0, z1, r, mat, seg=10, cap_top=True, cap_bottom=False):
    mb.add_cylinder(cx, cy, z0, z1, r, mat, seg=seg,
                    cap_top=cap_top, cap_bottom=cap_bottom)


def tube(mb, a, b, r, mat, seg=6):
    """任意方向の細い円筒（手すり・パイプ・トラス用）。a, b は 3D。"""
    ax, ay, az = a
    bx, by, bz = b
    dx, dy, dz = bx - ax, by - ay, bz - az
    L = math.sqrt(dx * dx + dy * dy + dz * dz)
    if L < 1e-6:
        return
    d = (dx / L, dy / L, dz / L)
    up = (0.0, 0.0, 1.0) if abs(d[2]) < 0.9 else (1.0, 0.0, 0.0)
    e1 = (d[1] * up[2] - d[2] * up[1], d[2] * up[0] - d[0] * up[2],
          d[0] * up[1] - d[1] * up[0])
    n = math.sqrt(sum(c * c for c in e1)) or 1.0
    e1 = (e1[0] / n, e1[1] / n, e1[2] / n)
    e2 = (d[1] * e1[2] - d[2] * e1[1], d[2] * e1[0] - d[0] * e1[2],
          d[0] * e1[1] - d[1] * e1[0])
    ring_a = []
    ring_b = []
    for i in range(seg):
        t = TAU * i / seg
        ox = r * (math.cos(t) * e1[0] + math.sin(t) * e2[0])
        oy = r * (math.cos(t) * e1[1] + math.sin(t) * e2[1])
        oz = r * (math.cos(t) * e1[2] + math.sin(t) * e2[2])
        ring_a.append((ax + ox, ay + oy, az + oz))
        ring_b.append((bx + ox, by + oy, bz + oz))
    for i in range(seg):
        j = (i + 1) % seg
        mb.add_quad(ring_a[i], ring_a[j], ring_b[j], ring_b[i], mat)


def blob(mb, cx, cy, cz, rx, ry, rz, mat, seg=6, rings=3):
    """低ポリの楕円体（観葉植物の葉のかたまりなど）。"""
    prev = None
    for k in range(rings + 1):
        phi = math.pi * k / rings
        r = math.sin(phi)
        z = cz + rz * math.cos(phi)
        if k == 0 or k == rings:
            ring = [(cx, cy, z)] * seg
        else:
            ring = [(cx + rx * r * math.cos(TAU * i / seg),
                     cy + ry * r * math.sin(TAU * i / seg), z) for i in range(seg)]
        if prev is not None:
            for i in range(seg):
                j = (i + 1) % seg
                a, b, c, d = prev[i], prev[j], ring[j], ring[i]
                if k == rings:
                    mb.add_face([a, b, c], mat)
                elif k == 1:
                    mb.add_face([a, c, d], mat)
                else:
                    mb.add_quad(a, b, c, d, mat)
        prev = ring


class T:
    """回転 + 平行移動のローカルフレーム。家具 1 個ぶんの座標系。

    ang は +X 軸からの回転（rad）。家具は「自分の +Y が正面」を向くように
    原点まわりでモデリングし、T が向きと位置を与える。
    """

    __slots__ = ("x", "y", "z", "c", "s")

    def __init__(self, x=0.0, y=0.0, z=0.0, ang=0.0):
        self.x = x
        self.y = y
        self.z = z
        self.c = math.cos(ang)
        self.s = math.sin(ang)

    def p(self, dx, dy, dz=0.0):
        return (self.x + self.c * dx - self.s * dy,
                self.y + self.s * dx + self.c * dy,
                self.z + dz)

    def p2(self, dx, dy):
        return (self.x + self.c * dx - self.s * dy,
                self.y + self.s * dx + self.c * dy)

    def box(self, mb, x0, y0, z0, x1, y1, z1, mat, top=None):
        if x1 < x0:
            x0, x1 = x1, x0
        if y1 < y0:
            y0, y1 = y1, y0
        if z1 < z0:
            z0, z1 = z1, z0
        a = self.p2(x0, y0)
        b = self.p2(x1, y0)
        c = self.p2(x1, y1)
        d = self.p2(x0, y1)
        mb.add_prism([a, b, c, d], self.z + z0, self.z + z1, mat,
                     top or mat, top or mat)

    def box_nb(self, mb, x0, y0, z0, x1, y1, z1, mat):
        """底面なしの直方体（椅子・本など大量配置用）。"""
        if x1 < x0:
            x0, x1 = x1, x0
        if y1 < y0:
            y0, y1 = y1, y0
        if z1 < z0:
            z0, z1 = z1, z0
        poly = [self.p2(x0, y0), self.p2(x1, y0), self.p2(x1, y1), self.p2(x0, y1)]
        mb.add_prism(poly, self.z + z0, self.z + z1, mat, mat, None)

    def box_c(self, mb, cx, cy, z0, sx, sy, z1, mat, top=None):
        self.box(mb, cx - sx * 0.5, cy - sy * 0.5, z0,
                 cx + sx * 0.5, cy + sy * 0.5, z1, mat, top)

    def plate(self, mb, x0, y0, x1, y1, z, mat, flip=False):
        pts = [self.p(x0, y0, z), self.p(x1, y0, z),
               self.p(x1, y1, z), self.p(x0, y1, z)]
        if flip:
            pts.reverse()
        mb.add_face(pts, mat)

    def vplate(self, mb, x0, y0, x1, y1, z0, z1, mat, flip=False):
        """(x0,y0)-(x1,y1) を底辺とする垂直な板。"""
        pts = [self.p(x0, y0, z0), self.p(x1, y1, z0),
               self.p(x1, y1, z1), self.p(x0, y0, z1)]
        if flip:
            pts.reverse()
        mb.add_face(pts, mat)

    def cyl(self, mb, cx, cy, z0, z1, r, mat, seg=8, cap_top=True):
        p = self.p2(cx, cy)
        mb.add_cylinder(p[0], p[1], self.z + z0, self.z + z1, r, mat,
                        seg=seg, cap_top=cap_top)


def lift(mb, base, dz):
    """base 番目以降に積んだ頂点をまとめて dz だけ持ち上げる。

    上階の什器は 2F の床スラブ上に置きたいが、家具ヘルパの多くは z=0 前提で
    組み立てる。呼ぶ前に base = len(mb.verts) を控えておき、組み終わってから
    これを呼ぶと上階ぶんだけを平行移動できる。
    """
    if dz == 0.0:
        return
    for i in range(base, len(mb.verts)):
        x, y, z = mb.verts[i]
        mb.verts[i] = (x, y, z + dz)
