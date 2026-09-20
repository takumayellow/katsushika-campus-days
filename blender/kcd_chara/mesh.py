"""低レベルのメッシュ組み立てユーティリティ。

bpy.ops に一切依存しない（headless で context に悩まされないため）。
頂点/面/マテリアル名/スムーズフラグを Python 側で積み上げ、最後に一度だけ
`bpy.data.meshes` を作る。部位（part）ごとの頂点インデックス範囲を覚えておき、
リグのウェイト調整や Shape Key で参照する。
"""

from __future__ import annotations

import math
from contextlib import contextmanager

import numpy as np

# --------------------------------------------------------------------------
# 数学ヘルパ
# --------------------------------------------------------------------------


def smoothstep(edge0: float, edge1: float, x):
    t = np.clip((np.asarray(x, dtype=float) - edge0) / (edge1 - edge0 + 1e-12), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def normalize(v):
    v = np.asarray(v, dtype=float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def bezier3(p0, p1, p2, p3, n: int) -> np.ndarray:
    """3 次ベジエを n 点にサンプリングする（髪の房の芯線に使う）。"""
    t = np.linspace(0.0, 1.0, n).reshape(-1, 1)
    p0, p1, p2, p3 = (np.asarray(p, dtype=float) for p in (p0, p1, p2, p3))
    mt = 1.0 - t
    return mt**3 * p0 + 3 * mt**2 * t * p1 + 3 * mt * t**2 * p2 + t**3 * p3


def ring(n: int, rx: float, ry: float, *, power: float = 2.0,
         cx: float = 0.0, cy: float = 0.0, z: float = 0.0,
         phase: float = 0.0) -> np.ndarray:
    """XY 平面のスーパー楕円リング。power=2 で真円、大きいほど角ばる。"""
    a = np.linspace(0.0, 2.0 * math.pi, n, endpoint=False) + phase
    ca, sa = np.cos(a), np.sin(a)
    e = 2.0 / power
    x = np.sign(ca) * np.abs(ca) ** e * rx + cx
    y = np.sign(sa) * np.abs(sa) ** e * ry + cy
    return np.stack([x, y, np.full(n, float(z))], axis=1)


def _rotate_about(v, axis, angle):
    axis = normalize(axis)
    c, s = math.cos(angle), math.sin(angle)
    return v * c + np.cross(axis, v) * s + axis * np.dot(axis, v) * (1.0 - c)


def frames(path: np.ndarray):
    """平行移動フレーム（parallel transport）。ねじれの無い押し出し用。"""
    p = np.asarray(path, dtype=float)
    k = len(p)
    t = np.zeros_like(p)
    t[1:-1] = p[2:] - p[:-2]
    t[0] = p[1] - p[0]
    t[-1] = p[-1] - p[-2]
    t /= np.linalg.norm(t, axis=1, keepdims=True) + 1e-12

    ref = np.array([1.0, 0.0, 0.0])
    if abs(float(np.dot(ref, t[0]))) > 0.9:
        ref = np.array([0.0, 1.0, 0.0])
    n0 = normalize(np.cross(t[0], ref))
    normals = [n0]
    for i in range(1, k):
        v = np.cross(t[i - 1], t[i])
        s = float(np.linalg.norm(v))
        if s < 1e-9:
            nn = normals[-1].copy()
        else:
            ang = math.atan2(s, float(np.dot(t[i - 1], t[i])))
            nn = _rotate_about(normals[-1], v / s, ang)
        nn = nn - float(np.dot(nn, t[i])) * t[i]
        normals.append(normalize(nn))
    N = np.array(normals)
    B = np.cross(t, N)
    return t, N, B


def tube_rings(path, radii, n: int = 12, power: float = 2.0,
               twist: float = 0.0) -> list[np.ndarray]:
    """芯線に沿った一般化円柱のリング列を返す。

    radii: 長さ len(path) の [(rx, ry), ...] もしくはスカラー列。
    """
    p = np.asarray(path, dtype=float)
    _, N, B = frames(p)
    out = []
    k = len(p)
    for i in range(k):
        r = radii[i]
        rx, ry = (r, r) if np.isscalar(r) else (float(r[0]), float(r[1]))
        a = np.linspace(0.0, 2.0 * math.pi, n, endpoint=False) + twist * (i / max(1, k - 1))
        ca, sa = np.cos(a), np.sin(a)
        e = 2.0 / power
        lx = np.sign(ca) * np.abs(ca) ** e * rx
        ly = np.sign(sa) * np.abs(sa) ** e * ry
        pts = p[i][None, :] + lx[:, None] * N[i][None, :] + ly[:, None] * B[i][None, :]
        out.append(pts)
    return out


# --------------------------------------------------------------------------
# MeshBuilder
# --------------------------------------------------------------------------


class MeshBuilder:
    """頂点と面を貯めてから一度だけ bpy のメッシュを作るビルダー。"""

    def __init__(self) -> None:
        self.verts: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, ...]] = []
        self.face_mat: list[str] = []
        self.face_smooth: list[bool] = []
        self.parts: dict[str, list[tuple[int, int]]] = {}
        self._stack: list[str] = []

    # -- 部位の記録 --------------------------------------------------------
    @contextmanager
    def part(self, name: str):
        start = len(self.verts)
        self._stack.append(name)
        try:
            yield
        finally:
            self._stack.pop()
            end = len(self.verts)
            if end > start:
                self.parts.setdefault(name, []).append((start, end))

    def part_indices(self, *names: str) -> np.ndarray:
        idx: list[int] = []
        for name in names:
            for a, b in self.parts.get(name, []):
                idx.extend(range(a, b))
        return np.array(sorted(set(idx)), dtype=int)

    # -- 低レベル ----------------------------------------------------------
    def add_verts(self, pts) -> int:
        base = len(self.verts)
        for p in np.asarray(pts, dtype=float).reshape(-1, 3):
            self.verts.append((float(p[0]), float(p[1]), float(p[2])))
        return base

    def add_face(self, idx, mat: str, smooth: bool = True) -> None:
        if len(set(idx)) < 3:
            return
        self.faces.append(tuple(int(i) for i in idx))
        self.face_mat.append(mat)
        self.face_smooth.append(bool(smooth))

    # -- 中レベル ----------------------------------------------------------
    def add_grid(self, rings, mat, *, smooth: bool = True,
                 close_u: bool = True, cap_start: bool = False,
                 cap_end: bool = False, flip: bool = False) -> int:
        """リング列を筒状に張る。rings は同じ頂点数の (n,3) 配列のリスト。

        mat は文字列、または `f(i, j) -> マテリアル名` の callable。
        """
        rings = [np.asarray(r, dtype=float) for r in rings]
        n = len(rings[0])
        m = len(rings)
        base = self.add_verts(np.concatenate(rings, axis=0))
        pick = mat if callable(mat) else (lambda i, j: mat)
        for j in range(m - 1):
            for i in range(n):
                i2 = (i + 1) % n
                if not close_u and i == n - 1:
                    continue
                a = base + j * n + i
                b = base + j * n + i2
                c = base + (j + 1) * n + i2
                d = base + (j + 1) * n + i
                quad = [a, b, c, d]
                if flip:
                    quad = quad[::-1]
                self.add_face(quad, pick(i, j), smooth)
        fallback = mat if isinstance(mat, str) else pick(0, 0)
        if cap_start:
            self._cap(base, n, rings[0], fallback, smooth, reverse=not flip)
        if cap_end:
            self._cap(base + (m - 1) * n, n, rings[-1], fallback, smooth,
                      reverse=flip)
        return base

    def _cap(self, base: int, n: int, ringpts, mat: str, smooth: bool,
             reverse: bool) -> None:
        c = self.add_verts([np.asarray(ringpts, dtype=float).mean(axis=0)])
        for i in range(n):
            i2 = (i + 1) % n
            tri = [c, base + i, base + i2]
            if reverse:
                tri = tri[::-1]
            self.add_face(tri, mat, smooth)

    def add_tube(self, path, radii, mat: str, *, n: int = 12, power: float = 2.0,
                 smooth: bool = True, cap_start: bool = True, cap_end: bool = True,
                 twist: float = 0.0) -> int:
        rings = tube_rings(path, radii, n=n, power=power, twist=twist)
        return self.add_grid(rings, mat, smooth=smooth, cap_start=cap_start,
                             cap_end=cap_end)

    def add_quad_strip(self, left, right, mat: str, *, smooth: bool = True) -> int:
        """2 本の点列の間を帯で張る（リボン・紐・襟などの板状パーツ）。"""
        left = np.asarray(left, dtype=float)
        right = np.asarray(right, dtype=float)
        k = len(left)
        base = self.add_verts(np.concatenate([left, right], axis=0))
        for i in range(k - 1):
            self.add_face([base + i, base + i + 1, base + k + i + 1, base + k + i],
                          mat, smooth)
        return base

    def add_box(self, center, size, mat: str, *, smooth: bool = False,
                rot_z: float = 0.0) -> int:
        cx, cy, cz = center
        sx, sy, sz = (s * 0.5 for s in size)
        pts = []
        for dz in (-sz, sz):
            for dx, dy in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)):
                c, s = math.cos(rot_z), math.sin(rot_z)
                pts.append((cx + dx * c - dy * s, cy + dx * s + dy * c, cz + dz))
        base = self.add_verts(pts)
        f = [(0, 1, 2, 3)[::-1], (4, 7, 6, 5)[::-1],
             (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        for quad in f:
            self.add_face([base + i for i in quad], mat, smooth)
        return base

    def add_rounded_box(self, center, size, mat: str, *, radius: float = 0.25,
                        seg: int = 6, smooth: bool = True, rot_z: float = 0.0) -> int:
        """角を丸めた箱（靴・鞄・下駄などに使う）。radius は size の比率。"""
        cx, cy, cz = center
        sx, sy, sz = (s * 0.5 for s in size)
        rings = []
        for j in range(seg + 1):
            t = j / seg
            ang = t * math.pi
            zz = -math.cos(ang)
            shrink = math.sin(ang) ** (1.0 / 2.6)
            rr = ring(16, sx * shrink, sy * shrink, power=3.4, z=zz * sz)
            rings.append(rr)
        pts = np.concatenate(rings, axis=0)
        c, s = math.cos(rot_z), math.sin(rot_z)
        rx = pts[:, 0] * c - pts[:, 1] * s
        ry = pts[:, 0] * s + pts[:, 1] * c
        pts = np.stack([rx + cx, ry + cy, pts[:, 2] + cz], axis=1)
        rings = [pts[i * 16:(i + 1) * 16] for i in range(seg + 1)]
        _ = radius
        return self.add_grid(rings, mat, smooth=smooth, cap_start=True, cap_end=True)

    def add_sphere(self, center, radii, mat: str, *, nu: int = 16, nv: int = 10,
                   smooth: bool = True, deform=None) -> int:
        cx, cy, cz = center
        rx, ry, rz = radii if not np.isscalar(radii) else (radii, radii, radii)
        rings = []
        for j in range(nv + 1):
            v = j / nv * math.pi
            sv, cv = math.sin(v), math.cos(v)
            a = np.linspace(0.0, 2.0 * math.pi, nu, endpoint=False)
            pts = np.stack([np.cos(a) * sv * rx,
                            np.sin(a) * sv * ry,
                            np.full(nu, cv * rz)], axis=1)
            if deform is not None:
                pts = deform(pts, v)
            rings.append(pts + np.array([cx, cy, cz]))
        return self.add_grid(rings, mat, smooth=smooth, flip=True)

    # -- 出力 --------------------------------------------------------------
    def stats(self) -> tuple[int, int]:
        tris = sum(len(f) - 2 for f in self.faces)
        return len(self.verts), tris

    def to_object(self, name: str, material_map: dict):
        import bpy

        mat_names: list[str] = []
        for m in self.face_mat:
            if m not in mat_names:
                mat_names.append(m)

        me = bpy.data.meshes.new(name)
        me.from_pydata(self.verts, [], [f for f in self.faces])
        me.update()

        for mn in mat_names:
            mat = material_map.get(mn)
            if mat is None:
                raise KeyError(f"マテリアル '{mn}' が未定義です")
            me.materials.append(mat)
        index_of = {mn: i for i, mn in enumerate(mat_names)}

        mi = np.array([index_of[m] for m in self.face_mat], dtype=np.int32)
        sm = np.array([1 if s else 0 for s in self.face_smooth], dtype=np.int32)
        me.polygons.foreach_set("material_index", mi)
        me.polygons.foreach_set("use_smooth", sm)
        me.update()

        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def planar_uv(obj, x0: float, x1: float, z0: float, z1: float,
              layer_name: str = "UVMap") -> None:
    """顔前面の平面投影 UV。全ループに同じ規則で貼る（face 系マテリアルが使う）。"""
    me = obj.data
    uvl = me.uv_layers.get(layer_name) or me.uv_layers.new(name=layer_name)
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    u = (co[:, 0] - x0) / (x1 - x0)
    v = (co[:, 2] - z0) / (z1 - z0)
    loop_v = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", loop_v)
    uv = np.stack([u[loop_v], v[loop_v]], axis=1).astype(np.float32)
    uvl.data.foreach_set("uv", uv.ravel())
    me.update()
