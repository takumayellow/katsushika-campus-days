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


def vertex_normals(verts, faces, by_angle: bool = False) -> np.ndarray:
    """頂点法線。既定は面の面積で重み付けする（多角形の面の法線は Newell 法）。

    by_angle なら、面の単位法線を角の角度で重み付けする（Blender の頂点法線と同じ）。
    面積の重みでは、大きな面に接した細い面の頂点の法線が大きな面の向きに引かれる。
    """
    v = np.asarray(verts, dtype=float)
    out = np.zeros_like(v)
    for f in faces:
        q = v[list(f)]
        n = np.cross(q, np.roll(q, -1, axis=0)).sum(axis=0)
        if by_angle:
            length = np.linalg.norm(n)
            if length < 1e-30:
                continue
            e0, e1 = np.roll(q, 1, axis=0) - q, np.roll(q, -1, axis=0) - q
            den = np.linalg.norm(e0, axis=1) * np.linalg.norm(e1, axis=1)
            cos = (e0 * e1).sum(axis=1) / np.maximum(den, 1e-30)
            ang = np.where(den > 1e-30, np.arccos(np.clip(cos, -1.0, 1.0)), 0.0)
            n = ang[:, None] * (n / length)
        out[list(f)] += n
    length = np.linalg.norm(out, axis=1, keepdims=True)
    return out / np.maximum(length, 1e-12)


def merge_normals(custom, index, auto) -> np.ndarray:
    """custom を index で引いた法線。ゼロ（決めていない）のところは auto を使う。"""
    out = np.asarray(custom, dtype=float)[index]
    unset = np.linalg.norm(out, axis=1) == 0
    out[unset] = np.asarray(auto, dtype=float).reshape(-1, 3)[unset]
    return out


def faces_within(faces, idx) -> list:
    """頂点がすべて idx に入る面。"""
    keep = set(np.asarray(idx).tolist())
    return [f for f in faces if all(i in keep for i in f)]


def triangles(faces, idx) -> np.ndarray:
    """頂点がすべて idx に入る面を扇形に三角形へ割った (T, 3) の頂点番号。"""
    out = [(f[0], f[k], f[k + 1]) for f in faces_within(faces, idx)
           for k in range(1, len(f) - 1)]
    return np.array(out, dtype=int).reshape(-1, 3)


def first_hits(origins, dirs, verts, tris, reach: float, chunk: int = 64) -> np.ndarray:
    """光線 origins + t·dirs (0 < t < reach) が最初に当たる三角形（Möller–Trumbore）。

    dirs は長さ 1 のベクトル（t と reach を長さとして比べる）。
    返すのは (N, 4) の [三角形番号, u, v, t]。当たりの点は
    verts[a]·(1-u-v) + verts[b]·u + verts[c]·v。当たらない光線は三角形番号が -1。
    """
    V = np.asarray(verts, dtype=float)
    T = np.asarray(tris, dtype=int).reshape(-1, 3)
    O = np.asarray(origins, dtype=float).reshape(-1, 3)
    D = np.asarray(dirs, dtype=float).reshape(-1, 3)
    res = np.full((len(O), 4), -1.0)
    if len(T) == 0:
        return res
    A = V[T[:, 0]]
    e1, e2 = V[T[:, 1]] - A, V[T[:, 2]] - A
    for s in range(0, len(O), chunk):
        o, d = O[s:s + chunk, None, :], D[s:s + chunk, None, :]
        pv = np.cross(d, e2[None])
        det = np.einsum("rtk,tk->rt", pv, e1)
        ok = np.abs(det) > 1e-14
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        tv = o - A[None]
        u = np.einsum("rtk,rtk->rt", tv, pv) * inv
        qv = np.cross(tv, e1[None])
        v = np.einsum("rtk,rk->rt", qv, d[:, 0]) * inv
        t = np.einsum("tk,rtk->rt", e2, qv) * inv
        hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0) & (t < reach)
        t = np.where(hit, t, np.inf)
        j = np.argmin(t, axis=1)
        rows = np.arange(len(j))
        good = np.isfinite(t[rows, j])
        res[s:s + chunk][good] = np.stack(
            [j, u[rows, j], v[rows, j], t[rows, j]], axis=1)[good]
    return res


def closest_points(points, verts, tris, chunk: int = 64) -> np.ndarray:
    """各点にいちばん近い三角形の上の点（Ericson, Real-Time Collision Detection 5.1.5）。

    返すのは (N, 4) の [三角形番号, u, v, 距離]。近い点は
    verts[a]·(1-u-v) + verts[b]·u + verts[c]·v（first_hits と同じ並び）。
    tris が空なら三角形番号が -1。
    """
    V = np.asarray(verts, dtype=float)
    T = np.asarray(tris, dtype=int).reshape(-1, 3)
    P = np.asarray(points, dtype=float).reshape(-1, 3)
    res = np.full((len(P), 4), -1.0)
    if len(T) == 0:
        return res
    A = V[T[:, 0]]
    ab, ac = V[T[:, 1]] - A, V[T[:, 2]] - A
    for s in range(0, len(P), chunk):
        ap = P[s:s + chunk, None, :] - A[None]
        d1 = np.einsum("tk,rtk->rt", ab, ap)
        d2 = np.einsum("tk,rtk->rt", ac, ap)
        d3 = d1 - np.einsum("tk,tk->t", ab, ab)          # ab·(p - b)
        d4 = d2 - np.einsum("tk,tk->t", ac, ab)          # ac·(p - b)
        d5 = d1 - np.einsum("tk,tk->t", ab, ac)          # ab·(p - c)
        d6 = d2 - np.einsum("tk,tk->t", ac, ac)          # ac·(p - c)
        va, vb, vc = d3 * d6 - d5 * d4, d5 * d2 - d1 * d6, d1 * d4 - d3 * d2
        with np.errstate(divide="ignore", invalid="ignore"):
            den = va + vb + vc
            u, v = vb / den, vc / den                     # 面の内側
            w_bc = (d4 - d3) / ((d4 - d3) + (d5 - d6))
            w_ac = d2 / (d2 - d6)
            w_ab = d1 / (d1 - d3)
        # 上から順に優先する領域（頂点 → 辺 → 面の内側）。後ろから上書きする。
        regions = (
            ((d1 <= 0) & (d2 <= 0), 0.0, 0.0),                           # a
            ((d3 >= 0) & (d4 <= d3), 1.0, 0.0),                          # b
            ((vc <= 0) & (d1 >= 0) & (d3 <= 0), w_ab, 0.0),              # 辺 ab
            ((d6 >= 0) & (d5 <= d6), 0.0, 1.0),                          # c
            ((vb <= 0) & (d2 >= 0) & (d6 <= 0), 0.0, w_ac),              # 辺 ac
            ((va <= 0) & (d4 - d3 >= 0) & (d5 - d6 >= 0), 1.0 - w_bc, w_bc),  # 辺 bc
        )
        for mask, uu, vv in reversed(regions):
            u, v = np.where(mask, uu, u), np.where(mask, vv, v)
        q = A[None] + ab[None] * u[..., None] + ac[None] * v[..., None]
        dist = np.linalg.norm(P[s:s + chunk, None, :] - q, axis=2)
        dist = np.where(np.isfinite(dist), dist, np.inf)  # 面積 0 の三角形
        j = np.argmin(dist, axis=1)
        rows = np.arange(len(j))
        res[s:s + chunk] = np.stack([j, u[rows, j], v[rows, j], dist[rows, j]], axis=1)
    return res


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
        self._normals: list[tuple[int, np.ndarray]] = []

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

    # -- 陰の法線 ----------------------------------------------------------
    def set_normals(self, start: int, normals) -> None:
        """start から並ぶ頂点の、陰に使う法線を決める（Blender のカスタム法線）。"""
        n = np.asarray(normals, dtype=float).reshape(-1, 3)
        length = np.linalg.norm(n, axis=1, keepdims=True)
        # 長さゼロの法線は決めなかったことにする（割ると NaN になる）
        self._normals.append((start, n / np.maximum(length, 1e-12)))

    def normal_array(self) -> np.ndarray:
        """頂点ごとの陰の法線。決めていない頂点はゼロ（面から求める法線のまま）。"""
        out = np.zeros((len(self.verts), 3))
        for start, n in self._normals:
            out[start:start + len(n)] = n
        return out

    def share_normals(self, v0: int, f0: int, tol: float = 1e-6) -> None:
        """v0 番以降で同じ位置に重なった頂点へ、1 本にそろえた陰の法線を決める。

        別々の add_grid で張った面の継ぎ目では、位置が同じでも頂点の番号が別なので、
        FBX の角の法線が面ごとに分かれる。Unity の輪郭線は角の法線の向きへ押し出した
        殻なので、継ぎ目で殻が裂ける。重なった頂点には、f0 番以降の面から求めた
        それぞれの頂点法線の平均を渡す。重ならない頂点の法線は決めない。
        """
        self.set_normals(v0, self._pooled_normals(v0, f0, None, tol))

    def shade_as(self, v0: int, f0: int, at, tol: float = 1e-6) -> None:
        """v0 番以降の頂点の陰の法線を、f0 番以降の面を at の位置へ置いたときの頂点法線にする。

        形（シルエットと輪郭線の殻の位置）はそのままで、陰だけを別の形（陰の代理形状）に
        従わせる。at は v0 番以降の頂点と同じ並びの位置で、法線を求める面は f0 番以降の
        本物の面。share_normals と違い、重ならない頂点にも法線を決める。今の位置で
        重なった頂点には、それぞれの法線の平均を渡す。平均が打ち消し合う組（表と裏の
        面が重なる所）は、それぞれの頂点の法線のままにする。
        """
        at = np.asarray(at, dtype=float).reshape(-1, 3)
        if len(at) != len(self.verts) - v0:
            raise ValueError(f"at は {len(self.verts) - v0} 点のはずが {len(at)} 点です")
        self.set_normals(v0, self._pooled_normals(v0, f0, at, tol))

    def coincident(self, v0: int, tol: float = 1e-6) -> np.ndarray:
        """v0 番以降の頂点ごとの組の番号。tol の格子で同じ位置に丸まる頂点が同じ組になる。"""
        V = np.asarray(self.verts[v0:], dtype=float)
        _, group = np.unique(np.round(V / tol).astype(np.int64), axis=0,
                             return_inverse=True)
        return group.reshape(-1)

    def _pooled_normals(self, v0: int, f0: int, at, tol: float) -> np.ndarray:
        """f0 番以降の面から求めた v0 番以降の頂点法線。位置 at が None なら今の位置で求め、
        重ならない頂点はゼロ（決めない）にする。at を渡すとその位置で求め、どの頂点にも
        法線を入れる。今の位置で重なった頂点は、和が打ち消し合わなければその和にそろえる。"""
        V = np.asarray(self.verts[v0:], dtype=float)
        if any(i < v0 for f in self.faces[f0:] for i in f):
            raise ValueError(f"f0={f0} 以降の面が v0={v0} より前の頂点を使っています")
        faces = [[i - v0 for i in f] for f in self.faces[f0:]]
        # 陰の代理形状の法線は Blender と同じ角の角度の重みで求める。面積の重みだと、
        # 生え際の細い面の頂点が大きな面の向きになり、輪郭線の殻が細い面で折れ返る。
        vn = (vertex_normals(V, faces) if at is None
              else vertex_normals(at, faces, by_angle=True))
        group = self.coincident(v0, tol)
        out = np.zeros_like(V) if at is None else vn.copy()
        for g in np.flatnonzero(np.bincount(group) > 1):
            members = np.flatnonzero(group == g)
            n = vn[members].sum(axis=0)
            if np.linalg.norm(n) > 1e-9:
                out[members] = n
        return out

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
                 cap_end: bool = False, flip: bool = False,
                 min_area: float = 0.0) -> int:
        """リング列を筒状に張る。rings は同じ頂点数の (n,3) 配列のリスト。

        mat は文字列、または `f(i, j) -> マテリアル名` の callable。
        min_area > 0 なら、面積がそれより小さい四角形を張らない。隣り合うリングが
        重なって潰れた面は Blender では法線が決まらず（頂点の位置を正規化した
        向きになる）、Unity の輪郭線の殻がそこでめくれて線になる。
        """
        rings = [np.asarray(r, dtype=float) for r in rings]
        n = len(rings[0])
        m = len(rings)
        pts = np.concatenate(rings, axis=0)
        base = self.add_verts(pts)
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
                if min_area > 0.0:
                    P = pts[[a - base, b - base, c - base, d - base]]
                    if 0.5 * np.linalg.norm(np.cross(P[2] - P[0], P[3] - P[1])) < min_area:
                        continue
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
        # どの面も外から見て反時計回りに張り、法線を箱の外へ向ける。輪郭線の殻は
        # 法線の向きへ押し出すので、内を向いた面の殻は箱の中へ縮み、表の面の際に黒い筋が出る
        f = [(0, 1, 2, 3)[::-1], (4, 7, 6, 5)[::-1],
             (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
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
        if self._normals:
            # 決めていない頂点は、Blender が面から求めた角ごとの法線をそのまま渡す。
            # 頂点単位でゼロを渡すと、フラットな面の角まで頂点の平均法線に変わる。
            n_loops = len(me.loops)
            loop_v = np.empty(n_loops, dtype=np.int32)
            me.loops.foreach_get("vertex_index", loop_v)
            auto = np.empty(n_loops * 3, dtype=np.float32)
            me.corner_normals.foreach_get("vector", auto)
            corner = merge_normals(self.normal_array(), loop_v, auto)
            me.normals_split_custom_set(corner.tolist())
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
