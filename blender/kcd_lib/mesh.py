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
        """凹みうる 2D ポリゴンを水平面として三角形分割して追加する。"""
        poly2d = geom.dedup(poly2d)
        if len(poly2d) < 3:
            return
        if len(poly2d) <= 4:
            pts = [(p[0], p[1], z) for p in geom.ensure_ccw(poly2d)]
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
        """折れ線を幅 width の帯として敷く。thickness>0 なら側面も作る。"""
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
        for i in range(len(pts) - 1):
            a, b = left[i], left[i + 1]
            c, d2 = right[i + 1], right[i]
            self.add_quad((a[0], a[1], z), (b[0], b[1], z),
                          (c[0], c[1], z), (d2[0], d2[1], z), mat)
            if thickness > 0:
                self.add_quad((a[0], a[1], z - thickness), (b[0], b[1], z - thickness),
                              (b[0], b[1], z), (a[0], a[1], z), mat)
                self.add_quad((d2[0], d2[1], z), (c[0], c[1], z),
                              (c[0], c[1], z - thickness), (d2[0], d2[1], z - thickness), mat)

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
