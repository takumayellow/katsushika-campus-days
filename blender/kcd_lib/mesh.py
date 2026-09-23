"""面を貯めてから 1 つの Blender オブジェクトに落とす軽量ビルダー。

bmesh の inset/extrude を都度回すより、頂点座標を直接計算して積む方が
キャンパス規模（数十万面）では桁で速い。重複頂点は最後に一括マージする。
"""

import math

import bmesh
import bpy
from mathutils.geometry import tessellate_polygon

from . import geom, mats


class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []
        self.face_mat = []
        self.mat_names = []
        self._mat_index = {}

    # ---- マテリアル ----
    def mat(self, name):
        i = self._mat_index.get(name)
        if i is None:
            i = len(self.mat_names)
            self._mat_index[name] = i
            self.mat_names.append(name)
        return i

    # ---- 低レベル ----
    def add_face(self, pts, mat):
        """pts: [(x,y,z), ...] 凸または平面の n 角形。"""
        if len(pts) < 3:
            return
        base = len(self.verts)
        self.verts.extend(pts)
        self.faces.append(tuple(range(base, base + len(pts))))
        self.face_mat.append(self.mat(mat) if isinstance(mat, str) else mat)

    def add_quad(self, a, b, c, d, mat):
        self.add_face([a, b, c, d], mat)

    def add_ngon_flat(self, poly2d, z, mat, flip=False):
        """凹みうる 2D ポリゴンを水平面として三角形分割して追加する。

        面は上（+z）を向く（flip=True なら下）。入力の巻き方向には依らない: tessellate_polygon は
        入力と同じ巻き方向で三角形を返すので、時計回りのまま渡すと 5 角以上の面だけ下を向く。"""
        poly2d = geom.ensure_ccw(geom.dedup(poly2d))
        if len(poly2d) < 3:
            return
        if len(poly2d) <= 4:
            pts = [(p[0], p[1], z) for p in poly2d]
            if flip:
                pts.reverse()
            self.add_face(pts, mat)
            return
        pts3 = [(p[0], p[1], 0.0) for p in poly2d]
        try:
            tris = tessellate_polygon([pts3])
        except Exception:
            tris = [(0, i, i + 1) for i in range(1, len(poly2d) - 1)]
        for tri in tris:
            p = [(poly2d[i][0], poly2d[i][1], z) for i in tri]
            if flip:
                p.reverse()
            self.add_face(p, mat)

    # ---- プリミティブ ----
    def add_prism(self, poly2d, z0, z1, side_mat, top_mat=None, bottom_mat=None):
        poly = geom.ensure_ccw(geom.dedup(poly2d))
        n = len(poly)
        if n < 3:
            return
        for i in range(n):
            a = poly[i]
            b = poly[(i + 1) % n]
            self.add_quad((a[0], a[1], z0), (b[0], b[1], z0),
                          (b[0], b[1], z1), (a[0], a[1], z1), side_mat)
        if top_mat:
            self.add_ngon_flat(poly, z1, top_mat)
        if bottom_mat:
            self.add_ngon_flat(poly, z0, bottom_mat, flip=True)

    def add_slab(self, poly2d, z0, z1, mat):
        self.add_prism(poly2d, z0, z1, mat, mat, mat)

    def add_box(self, x0, y0, z0, x1, y1, z1, mat, top_mat=None):
        self.add_slab([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z0, z1, mat)
        if top_mat:
            self.add_ngon_flat([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z1, top_mat)

    def add_box_c(self, cx, cy, z0, sx, sy, z1, mat, top_mat=None):
        self.add_box(cx - sx * 0.5, cy - sy * 0.5, z0,
                     cx + sx * 0.5, cy + sy * 0.5, z1, mat, top_mat)

    def add_cylinder(self, cx, cy, z0, z1, r, mat, seg=10, cap_top=True, cap_bottom=False):
        ring = [(cx + r * math.cos(2 * math.pi * i / seg),
                 cy + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
        self.add_prism(ring, z0, z1, mat,
                       mat if cap_top else None, mat if cap_bottom else None)

    def add_ribbon(self, polyline, width, z, mat, thickness=0.0):
        """折れ線を幅 width の帯として敷く。thickness>0 なら側面と両端の小口も作る。

        面の向き: 天面は上 (+z)、側面と小口は帯の外を向く。Unity は裏面を描かず、
        CharacterController も裏面を床として扱わない（とみなす）ので、ここを逆にすると
        道路の天面が消えて奥の側面だけが細い線に見え、足元の判定も穴になる（#30）。"""
        pts = geom.dedup(polyline)
        if len(pts) < 2:
            return
        hw = width * 0.5
        left = []
        right = []
        for i, p in enumerate(pts):
            if i == 0:
                d = geom.normalize(geom.sub(pts[1], pts[0]))
            elif i == len(pts) - 1:
                d = geom.normalize(geom.sub(pts[-1], pts[-2]))
            else:
                d = geom.normalize(geom.add(
                    geom.normalize(geom.sub(pts[i], pts[i - 1])),
                    geom.normalize(geom.sub(pts[i + 1], pts[i]))))
            nrm = (-d[1], d[0])
            left.append(geom.add(p, geom.mul(nrm, hw)))
            right.append(geom.add(p, geom.mul(nrm, -hw)))
        zb = z - thickness
        for i in range(len(pts) - 1):
            a, b = left[i], left[i + 1]
            c, d2 = right[i + 1], right[i]
            # 天面: 右 → 左の順に回すと上から見て反時計回り（法線 +z）
            self.add_quad((d2[0], d2[1], z), (c[0], c[1], z),
                          (b[0], b[1], z), (a[0], a[1], z), mat)
            if thickness > 0:
                # 左の側面（法線は +nrm 側）と右の側面（-nrm 側）
                self.add_quad((a[0], a[1], z), (b[0], b[1], z),
                              (b[0], b[1], zb), (a[0], a[1], zb), mat)
                self.add_quad((d2[0], d2[1], zb), (c[0], c[1], zb),
                              (c[0], c[1], z), (d2[0], d2[1], z), mat)
        if thickness > 0:
            # 小口: 始点は進行方向の逆、終点は進行方向を向く
            l0, r0, l1, r1 = left[0], right[0], left[-1], right[-1]
            self.add_quad((l0[0], l0[1], z), (l0[0], l0[1], zb),
                          (r0[0], r0[1], zb), (r0[0], r0[1], z), mat)
            self.add_quad((r1[0], r1[1], z), (r1[0], r1[1], zb),
                          (l1[0], l1[1], zb), (l1[0], l1[1], z), mat)

    # ---- 後処理 ----
    def split_by_grid(self, cell, eps=1e-3):
        """全ての面を x = k*cell / y = k*cell の直線で切り分ける（面の向きとマテリアルは保つ）。

        外周の地面（700 m 四方）が 990 m の三角形 2 枚になると、PhysX が 500 m 超の三角形を
        警告し、長い三角形の上では接地判定も不安定になる（#30）。どの面も同じ格子で切るので、
        隣り合う面は同じ点で割れ、T 字の継ぎ目は出ない（切り口の点は to_object の
        remove_doubles で 1 点にまとまる）。凸でない多角形は先に三角形に割ってから切る。
        格子の線から eps 以内の頂点は「線の上」とみなし、細い切れ端を作らない。
        戻り値: (切った面の数, 切った後の面の数)。"""
        verts, faces, fmat = self.verts, self.faces, self.face_mat
        new_verts, new_faces, new_mat = [], [], []
        n_split = 0

        def emit(poly, mi):
            base = len(new_verts)
            new_verts.extend(poly)
            new_faces.append(tuple(range(base, base + len(poly))))
            new_mat.append(mi)

        for face, mi in zip(faces, fmat):
            poly = [verts[k] for k in face]
            xs = [p[0] for p in poly]
            ys = [p[1] for p in poly]
            lines = [(0, k * cell) for k in _grid_range(min(xs), max(xs), cell, eps)]
            lines += [(1, k * cell) for k in _grid_range(min(ys), max(ys), cell, eps)]
            if not lines:
                emit(poly, mi)
                continue
            n_split += 1
            pieces = [poly] if _is_convex(poly) else _triangulate(poly)
            for axis, c in lines:
                nxt = []
                for pc in pieces:
                    s = [p[axis] - c for p in pc]
                    if min(s) < -eps and max(s) > eps:
                        for side in (-1.0, 1.0):
                            q = _clip_half(pc, axis, c, side, eps)
                            if len(q) >= 3 and _area3(q) > 1e-8:
                                nxt.append(q)
                    else:
                        nxt.append(pc)
                pieces = nxt
            for pc in pieces:
                emit(pc, mi)
        self.verts, self.faces, self.face_mat = new_verts, new_faces, new_mat
        return n_split, len(new_faces)

    # ---- 出力 ----
    def stats(self):
        return len(self.verts), len(self.faces)

    def to_object(self, collection=None, merge=True, shade_flat=True):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata(self.verts, [], [poly for poly in self.faces])
        me.update()
        for name in self.mat_names:
            me.materials.append(mats.get(name))
        for poly, mi in zip(me.polygons, self.face_mat):
            poly.material_index = mi
        obj = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(obj)
        if merge and len(self.verts) > 0:
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
            bm.to_mesh(me)
            bm.free()
            me.update()
        if shade_flat:
            for poly in me.polygons:
                poly.use_smooth = False
        return obj


# ---- split_by_grid の下請け ----
def _grid_range(lo, hi, cell, eps):
    """lo..hi の内側（両端から eps 以上離れて）を通る格子線の番号。"""
    k0 = int(math.floor((lo + eps) / cell)) + 1
    k1 = int(math.ceil((hi - eps) / cell)) - 1
    return [k for k in range(k0, k1 + 1) if lo + eps < k * cell < hi - eps]


def _newell(poly):
    nx = ny = nz = 0.0
    n = len(poly)
    for i in range(n):
        x0, y0, z0 = poly[i]
        x1, y1, z1 = poly[(i + 1) % n]
        nx += (y0 - y1) * (z0 + z1)
        ny += (z0 - z1) * (x0 + x1)
        nz += (x0 - x1) * (y0 + y1)
    return nx, ny, nz


def _area3(poly):
    nx, ny, nz = _newell(poly)
    return 0.5 * math.sqrt(nx * nx + ny * ny + nz * nz)


def _is_convex(poly):
    if len(poly) <= 3:
        return True
    nx, ny, nz = _newell(poly)
    n = len(poly)
    for i in range(n):
        a, b, c = poly[i - 1], poly[i], poly[(i + 1) % n]
        u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
        v = (c[0] - b[0], c[1] - b[1], c[2] - b[2])
        cx = u[1] * v[2] - u[2] * v[1]
        cy = u[2] * v[0] - u[0] * v[2]
        cz = u[0] * v[1] - u[1] * v[0]
        if cx * nx + cy * ny + cz * nz < -1e-12:
            return False
    return True


def _triangulate(poly):
    """凸でない多角形を三角形に割る。各三角形の向きは元の多角形にそろえる。"""
    nrm = _newell(poly)
    try:
        tris = tessellate_polygon([poly])
    except Exception:
        tris = [(0, i, i + 1) for i in range(1, len(poly) - 1)]
    out = []
    for tri in tris:
        t = [poly[i] for i in tri]
        tn = _newell(t)
        if tn[0] * nrm[0] + tn[1] * nrm[1] + tn[2] * nrm[2] < 0:
            t.reverse()
        out.append(t)
    return out


def _clip_half(poly, axis, c, side, eps):
    """Sutherland-Hodgman で poly の side 側（side*(p[axis]-c) >= 0）を残す。"""
    out = []
    n = len(poly)
    for i in range(n):
        p = poly[i]
        q = poly[(i + 1) % n]
        sp = side * (p[axis] - c)
        sq = side * (q[axis] - c)
        if sp >= -eps:
            out.append(p)
        if (sp > eps and sq < -eps) or (sp < -eps and sq > eps):
            t = sp / (sp - sq)
            r = [p[k] + (q[k] - p[k]) * t for k in range(3)]
            r[axis] = c
            out.append(tuple(r))
    # 連続する同じ点を落とす
    dedup = []
    for p in out:
        if not dedup or max(abs(p[k] - dedup[-1][k]) for k in range(3)) > 1e-9:
            dedup.append(p)
    if len(dedup) > 1 and max(abs(dedup[0][k] - dedup[-1][k]) for k in range(3)) <= 1e-9:
        dedup.pop()
    return dedup
