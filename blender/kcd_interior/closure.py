"""屋内の外周が人の通れない壁で閉じているかを測る（#45）。

Unity の CharacterController（PhysX）は MeshCollider の三角形を片面でしか受け止めない。
進む向きと同じ側を向いた面（裏面）はすり抜けるので、外向きの面しか持たない窓ガラスは
室内から外へ素通りになる。ここでは外周の壁厚の帯へ室内側から水平にレイを撃ち、
**室内を向いた面**（法線が撃った向きと逆）に当たったときだけ「塞がっている」とみなす。

外周の内面から PROBE_IN 内側の線を step 刻みでなぞり、各点で真上から下へレイを撃って
立てる面（上向きで、上に身長ぶんの空きがある面）を全部拾う。立てる高さ zf ごとに
zf 〜 zf + JUMP + BODY_H を step 刻みで外へ撃ち、身長ぶん連続して空いていて、
その下端がジャンプで届く高さ（zf + JUMP 以下）にあれば、その点は「抜けられる」。
抜けられる点が外周に沿って続く区間を穴として返す。

座標は Blender の建物ローカル（+Y が入口から奥、+Z が上）。FBX を読み戻して測るので、
Unity に渡る形そのものを見ている。
"""

import math

from mathutils import Vector
from mathutils.bvhtree import BVHTree

# プレイヤーのカプセル（ActorFactory.BodyHeight / BodyRadius, PlayerController._jumpHeight）
BODY_H = 1.60
BODY_R = 0.28
JUMP = 1.10
PROBE_IN = 0.30      # 外周の内面から、カプセルの中心線までの距離（半径 + 余裕）
RAY_IN = 0.05        # 横レイの始点は内面からこれだけ室内側
RAY_OUT = 0.25       # 外面からこれだけ外まで撃つ

EDGE_NAMES = ("入口(-Y)", "右(+X)", "奥(+Y)", "左(-X)")


def _gather(objects, name_filter=None):
    """メッシュをワールド座標の頂点・面・法線に展開する。"""
    verts, polys, normals = [], [], []
    for o in objects:
        if o.type != "MESH":
            continue
        if name_filter and not name_filter(o.name):
            continue
        mw = o.matrix_world
        base = len(verts)
        verts.extend(mw @ v.co for v in o.data.vertices)
        for p in o.data.polygons:
            idx = [base + i for i in p.vertices]
            polys.append(idx)
            # 法線は巻き順から出す（PhysX も三角形の巻き順で表裏を決める）
            a, b, c = verts[idx[0]], verts[idx[1]], verts[idx[2]]
            n = (b - a).cross(c - a)
            if len(idx) > 3:
                n = Vector((0.0, 0.0, 0.0))
                for k in range(len(idx)):
                    p0, p1 = verts[idx[k]], verts[idx[(k + 1) % len(idx)]]
                    n.x += (p0.y - p1.y) * (p0.z + p1.z)
                    n.y += (p0.z - p1.z) * (p0.x + p1.x)
                    n.z += (p0.x - p1.x) * (p0.y + p1.y)
            normals.append(n.normalized() if n.length > 1e-12 else n)
    return verts, polys, normals


class Scene:
    """1 棟ぶんのレイ判定。structural は床・壁（floor_ / wall_）、solid は家具も含む全部。"""

    def __init__(self, objects):
        def structural(name):
            low = name.lower()
            return low.startswith("floor_") or low.startswith("wall_")

        v, p, n = _gather(objects, structural)
        self.wall_bvh = BVHTree.FromPolygons(v, p)
        self.wall_n = n
        v, p, n = _gather(objects)
        self.all_bvh = BVHTree.FromPolygons(v, p)
        self.all_n = n
        self.z_top = max((q.z for q in v), default=10.0) + 1.0

    def hits(self, bvh, normals, origin, direction, dist):
        """origin から direction へ dist まで、当たった面を近い順に全部返す [(t, 法線)]。"""
        out = []
        o = Vector(origin)
        d = Vector(direction)
        t0 = 0.0
        for _ in range(64):
            loc, _nrm, idx, t = bvh.ray_cast(o, d, dist - t0)
            if loc is None:
                break
            out.append((t0 + t, normals[idx]))
            step = t + 1e-4
            t0 += step
            o = o + d * step
            if t0 >= dist:
                break
        return out

    def blocked(self, origin, direction, dist, two_sided):
        """横レイが室内を向いた面に当たるか（two_sided なら向きを問わない）。"""
        o = Vector(origin)
        d = Vector(direction)
        t0 = 0.0
        for _ in range(64):
            loc, _nrm, idx, t = self.wall_bvh.ray_cast(o, d, dist - t0)
            if loc is None:
                return False
            if two_sided or self.wall_n[idx].dot(d) < -1e-6:
                return True
            t0 += t + 1e-4
            o = o + d * (t + 1e-4)
            if t0 >= dist:
                return False
        return False

    def levels(self, x, y):
        """(x, y) で立てる高さ。上向きの面で、真上に身長ぶんの空きがあるもの。"""
        hs = self.hits(self.all_bvh, self.all_n, (x, y, self.z_top),
                       (0.0, 0.0, -1.0), self.z_top + 50.0)
        out = []
        above = None
        for t, n in hs:
            z = self.z_top - t
            if n.z > 0.7 and (above is None or above - z >= BODY_H):
                out.append(z)
            above = z
        return out


def _edges(env, wall):
    """外周 4 辺: (辺番号, 内面上の始点, 終点, 外向き単位ベクトル)。"""
    x0, x1 = env["x0"] + wall, env["x1"] - wall
    y0, y1 = env["y_face"] + wall, env["y_back"] - wall
    return [
        (0, (x0, y0), (x1, y0), (0.0, -1.0)),
        (1, (x1, y0), (x1, y1), (1.0, 0.0)),
        (2, (x1, y1), (x0, y1), (0.0, 1.0)),
        (3, (x0, y1), (x0, y0), (-1.0, 0.0)),
    ]


def open_at(scene, px, py, out, zf, wall, two_sided, step):
    """内面上の点 (px, py) で、外周の帯が床の高さ zf から抜けられるか。"""
    ox, oy = px - out[0] * RAY_IN, py - out[1] * RAY_IN
    dist = RAY_IN + wall + RAY_OUT
    n = int(math.ceil((JUMP + BODY_H) / step)) + 1
    need = int(math.ceil(BODY_H / step))
    free_run = 0
    for k in range(n):
        z = zf + step * 0.5 + step * k
        if scene.blocked((ox, oy, z), (out[0], out[1], 0.0), dist, two_sided):
            free_run = 0
            continue
        free_run += 1
        # 空きの下端 z - (free_run - 1) * step がジャンプで届く高さなら抜けられる
        if free_run >= need and z - (free_run - 1) * step <= zf + JUMP + 1e-6:
            return True
    return False


def measure(objects, meta, two_sided=False, step=0.05):
    """外周の穴を返す。[{edge, name, z, s0, s1, width, a, b}]（a, b は内面上の端点）。"""
    scene = Scene(objects)
    wall = float(meta.get("wall_thickness", 0.30))
    env = meta["envelope"]
    gaps = []
    for ei, a, b, out in _edges(env, wall):
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
        runs = {}          # 床の高さ（0.1 m で丸め）-> 開いている s の列
        s = step * 0.5
        while s < L:
            # 内面から PROBE_IN 内側（カプセルの中心が壁に触れる位置）
            px = a[0] + d[0] * s - out[0] * PROBE_IN
            py = a[1] + d[1] * s - out[1] * PROBE_IN
            qx, qy = a[0] + d[0] * s, a[1] + d[1] * s
            for zf in scene.levels(px, py):
                if open_at(scene, qx, qy, out, zf, wall, two_sided, step):
                    runs.setdefault(round(zf, 1) + 0.0, []).append(s)
            s += step
        for zk, ss in sorted(runs.items()):
            start = prev = ss[0]
            for v in ss[1:] + [None]:
                if v is not None and v - prev <= step * 1.5:
                    prev = v
                    continue
                s0, s1 = start - step * 0.5, prev + step * 0.5
                gaps.append({
                    "edge": ei, "name": EDGE_NAMES[ei], "z": zk,
                    "s0": round(s0, 3), "s1": round(s1, 3),
                    "width": round(s1 - s0, 3),
                    "a": (round(a[0] + d[0] * s0, 2), round(a[1] + d[1] * s0, 2)),
                    "b": (round(a[0] + d[0] * s1, 2), round(a[1] + d[1] * s1, 2)),
                })
                if v is not None:
                    start = prev = v
    return gaps
