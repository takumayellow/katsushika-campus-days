"""樹木 3 種（ケヤキ・丸刈りの中木・クロマツ, #51）。

幹は回転体、樹冠は凸多面体の「かたまり」を重ねて作る。マテリアルは trunk と leaf の 2 つだけで、
葉の明暗は面ごとの頂点カラー（sRGB）に焼く。色は mats.PALETTE の leaf_dark → leaf →
leaf_light → leaf_top の 4 色の間を補間したもので、どこまで明るくするかは
「空がどれだけ見えるか（かたまり同士の遮蔽）」「上を向いているか」「樹冠の中の高さ」で決める。

Unity 側の KCD/Toon は明暗の境目（しきい値 0.1）がほぼ裏側にしか来ないので、太陽の向きでは
陰がつかない。かたまりの重なりの明暗は、ここで焼いた色がそのまま見える。

Unity の頂点数は面の角の数と同じになる（trees.fbx は weldVertices 0 で、法線も面ごと）。
1 本 600 以下に収める（MAX_CORNERS）。ほかのかたまりの中に丸ごと埋まった面は捨てる。
"""

import math
import random

from . import mats
from .mesh import MeshBuilder, _is_convex, _newell, _triangulate

# 1 本の木の Unity の頂点数（面の角の数）の上限
MAX_CORNERS = 600

# 幹から上を +x へ寄せる量 [m]（天辺で）。回転体のままだと site.py の Z 回転が輪郭を変えられず、
# 588 本中 384 本が「回しても同じ絵」の判子になっていた。樹冠のかたまりも同じ剪断で寄せる。
# 寄せ始めの z=2.0 は下げてはいけない。根元から傾けると身長 1.8 m での幹の最大半径が
# 0.292 → 0.357 m になり、Unity 側の幹カプセル（半径 0.3 m）を突き抜ける。
_LEAN = 0.35
_LEAN_Z = 2.0

# 葉の 4 色（リニア → sRGB）。PaletteAgreementTests が MaterialLibrary.CampusColors と突き合わせる。
_RAMP_NAMES = ("leaf_dark", "leaf", "leaf_light", "leaf_top")


def _to_srgb(c):
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1.0 / 2.4) - 0.055


_RAMP = [tuple(_to_srgb(v) for v in mats.PALETTE[n][0]) for n in _RAMP_NAMES]


def leaf_color(t):
    """0（leaf_dark）〜 1（leaf_top）の t を 4 色の折れ線で sRGB にする。"""
    t = min(1.0, max(0.0, t)) * (len(_RAMP) - 1)
    i = min(int(t), len(_RAMP) - 2)
    f = t - i
    a, b = _RAMP[i], _RAMP[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))


# --------------------------------------------------------------------------- #
#  ベクトルの小道具
# --------------------------------------------------------------------------- #
def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    n = math.sqrt(_dot(a, a))
    return (a[0] / n, a[1] / n, a[2] / n) if n > 1e-12 else (0.0, 0.0, 1.0)


def _centroid(pts):
    k = 1.0 / len(pts)
    return (sum(p[0] for p in pts) * k, sum(p[1] for p in pts) * k, sum(p[2] for p in pts) * k)


def _polar(deg, dist, z):
    a = math.radians(deg)
    return (dist * math.cos(a), dist * math.sin(a), z)


# --------------------------------------------------------------------------- #
#  幹（以前の回転体の幹の段と同じ頂点）
# --------------------------------------------------------------------------- #
def _lean_x(z, top_z):
    return _LEAN * max(0.0, (z - _LEAN_Z) / (top_z - _LEAN_Z))


def _add_trunk(mb, levels, top_z, seg, arms, phase, noise, seed):
    """幹を回転体で引き、天辺の輪をふさぐ。

    levels は下から (半径, z, 腕の張り出し)。腕のある段は半径を
    r + arm * ((1 + cos(arms*(theta - phase))) / 2) ** 1.5 にして arms 本に割る（ケヤキの三又）。
    乱数の引き方と top_z（以前の樹冠の天辺）は以前の回転体と同じにしてあるので、幹の頂点は
    13ef5dc までと 1 点も変わらない（Issue #51 の「幹・枝の形は変えない」）。
    """
    rng = random.Random(seed)
    rings = []
    for r, z, arm in levels:
        lx = _lean_x(z, top_z)
        pts = []
        for i in range(seg):
            th = 2.0 * math.pi * i / seg
            w = (0.5 * (1.0 + math.cos(arms * (th - phase)))) ** 1.5 if arm > 0 else 0.0
            rr = (r + arm * w) * (1.0 + rng.uniform(-noise, noise))
            pts.append((lx + rr * math.cos(th), rr * math.sin(th), z))
        rings.append(pts)
    for lo, hi in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            mb.add_quad(lo[i], lo[j], hi[j], hi[i], "trunk")
    for face in _cap(rings[-1]):
        mb.add_face(face, "trunk")


def _merge(a, b):
    """辺を 1 本共有する 2 つの多角形（向きはそろっている）を 1 つにする。共有しなければ None。"""
    na, nb = len(a), len(b)
    for i in range(na):
        p, q = a[i], a[(i + 1) % na]
        for j in range(nb):
            if b[j] == q and b[(j + 1) % nb] == p:
                # a を q から一周して p まで、続けて b を p の次から q の手前まで
                return ([a[(i + 1 + k) % na] for k in range(na)]
                        + [b[(j + 2 + k) % nb] for k in range(nb - 2)])
    return None


def _cap(ring):
    """天辺の輪をふさぐ面。凹なら三角形に割り、凸のままつなげられる隣どうしをつなぐ。

    ケヤキの三又の輪（12 角）は三角形 10 枚（角 30）のままだと重いので、凸の多角形数枚にする。
    """
    if _is_convex(ring):
        return [ring]
    polys = [list(t) for t in _triangulate(ring)]
    joined = True
    while joined:
        joined = False
        for i in range(len(polys)):
            for j in range(i + 1, len(polys)):
                m = _merge(polys[i], polys[j])
                if m is not None and _is_convex(m):
                    polys[i] = m
                    del polys[j]
                    joined = True
                    break
            if joined:
                break
    return polys


# --------------------------------------------------------------------------- #
#  かたまり（凸多面体）と枝（角柱）
# --------------------------------------------------------------------------- #
class _Piece:
    """外向きの面の並び。leaf=True のかたまりは凸で、面の平面で内外を判定できる。"""

    def __init__(self, faces, leaf, tone=0.0):
        self.faces = faces
        self.leaf = leaf
        self.tone = tone
        self.planes = []
        for f in faces:
            n = _norm(_newell(f))
            self.planes.append((n, _dot(n, _centroid(f))))
        pts = [p for f in faces for p in f]
        self.center = _centroid(pts)
        self.radius = max(math.sqrt(_dot(_sub(p, self.center), _sub(p, self.center))) for p in pts)

    def contains(self, p, margin=0.01):
        """p がすべての面の内側にあるか（凸でなくても、この判定が真なら中にある）。"""
        return all(_dot(n, p) <= d - margin for n, d in self.planes)

    def hit(self, p, d, tmax):
        """p から d 方向の線分（長さ tmax）がこのかたまりに入るか。"""
        oc = _sub(p, self.center)
        b = _dot(oc, d)
        if _dot(oc, oc) - b * b > self.radius * self.radius or b > self.radius:
            return False
        t0, t1 = 0.0, tmax
        for n, dd in self.planes:
            den = _dot(n, d)
            dist = dd - _dot(n, p)
            if abs(den) < 1e-9:
                if dist < 0.0:
                    return False
                continue
            t = dist / den
            if den > 0.0:
                t1 = min(t1, t)
            else:
                t0 = max(t0, t)
            if t0 > t1:
                return False
        return True


_H5 = 1.0 / math.sqrt(5.0)       # 正二十面体の上下の輪の高さ
_R5 = 2.0 / math.sqrt(5.0)       # 同じく輪の半径


def _puff(center, rx, rz, yaw, rng, ry=None, jitter=0.08, tone=0.0):
    """底を平らにした正二十面体（三角形 15 枚 + 底の五角形 1 枚、角は 50）。

    極を z 軸に向け、上下の輪を 36 度ずらす。頂点は ±jitter で半径と高さを揺らして
    かたまりごとに形を変える。底の輪は高さを揺らさない（五角形を平らに保つ）。
    """
    cx, cy, cz = center
    ry = rx if ry is None else ry
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def put(lx, ly, lz):
        return (cx + c * lx * rx - s * ly * ry, cy + s * lx * rx + c * ly * ry, cz + lz * rz)

    def ring(offset, z, shake_z):
        out = []
        for i in range(5):
            a = math.radians(offset + 72.0 * i)
            k = 1.0 + rng.uniform(-jitter, jitter)
            zz = z * (1.0 + rng.uniform(-jitter, jitter)) if shake_z else z
            out.append(put(_R5 * k * math.cos(a), _R5 * k * math.sin(a), zz))
        return out

    up = ring(0.0, _H5, True)
    lo = ring(36.0, -_H5, False)
    top = put(0.0, 0.0, 1.0 + rng.uniform(-jitter, jitter))
    faces = []
    for i in range(5):
        k = (i + 1) % 5
        faces.append([up[i], up[k], top])
        faces.append([lo[i], up[k], up[i]])
        faces.append([lo[i], lo[k], up[k]])
    faces.append(list(reversed(lo)))
    return _Piece(faces, leaf=True, tone=tone)


def _tube(points, radii, sides, ref, spin=0.0):
    """折れ線に沿った角柱（両端は開いたまま。幹かかたまりの中に埋める）。"""
    rings = []
    for i, p in enumerate(points):
        a = points[max(i - 1, 0)]
        b = points[min(i + 1, len(points) - 1)]
        t = _norm(_sub(b, a))
        u = _norm(_sub(ref, tuple(x * _dot(ref, t) for x in t)))
        v = _cross(t, u)
        ring = []
        for k in range(sides):
            ang = math.radians(spin) + 2.0 * math.pi * k / sides
            ca, sa = math.cos(ang) * radii[i], math.sin(ang) * radii[i]
            ring.append((p[0] + ca * u[0] + sa * v[0], p[1] + ca * u[1] + sa * v[1],
                         p[2] + ca * u[2] + sa * v[2]))
        rings.append(ring)
    faces = []
    for lo, hi in zip(rings, rings[1:]):
        for k in range(sides):
            j = (k + 1) % sides
            faces.append([lo[k], lo[j], hi[j], hi[k]])
    return _Piece(faces, leaf=False)


# --------------------------------------------------------------------------- #
#  葉の色
# --------------------------------------------------------------------------- #
def _hemisphere(n):
    """+z を中心にした余弦重みの半球の方向（黄金角で散らした固定の n 本）。"""
    golden = math.pi * (3.0 - math.sqrt(5.0))
    out = []
    for i in range(n):
        u = (i + 0.5) / n
        r = math.sqrt(u)
        out.append((r * math.cos(i * golden), r * math.sin(i * golden), math.sqrt(1.0 - u)))
    return out


_RAYS = _hemisphere(32)
# 遮るもののない真上向きの面が受ける空の光（光線の上向き成分の和）。_sky_light をこれで割る。
_SKY_FULL = sum(r[2] for r in _RAYS)


def _sky_light(p, n, others, reach):
    """面の中心 p が受ける空の光（0〜1）。

    法線 n の半球へ光線を飛ばし、上へ向かってほかのかたまりに遮られない光線の上向き成分を足す。
    遮るもののない面では、真上向き 1、真横向き約 0.3、下向き 0 になる。
    """
    ref = (1.0, 0.0, 0.0) if abs(n[0]) < 0.9 else (0.0, 1.0, 0.0)
    t1 = _norm(_cross(n, ref))
    t2 = _cross(n, t1)
    start = (p[0] + n[0] * 0.02, p[1] + n[1] * 0.02, p[2] + n[2] * 0.02)
    light = 0.0
    for x, y, z in _RAYS:
        d = (t1[0] * x + t2[0] * y + n[0] * z, t1[1] * x + t2[1] * y + n[1] * z,
             t1[2] * x + t2[2] * y + n[2] * z)
        if d[2] > 0.0 and not any(o.hit(start, d, reach) for o in others):
            light += d[2]
    return light / _SKY_FULL


class Tone:
    """葉の色の決め方。t = sky*空の光 + height*樹冠の中の高さ + bias（0 で leaf_dark、1 で leaf_top）。

    bias の既定 0.20 は、樹冠の面の平均の明るさを芝（grass #6FA84A）と同じくらいにする値。
    かたまりを重ねると遮蔽で平均が下がり、0 のままでは樹冠が芝より暗い「穴」に見える。
    """

    def __init__(self, bias=0.20, sky_w=0.85, height_w=0.30, spread=0.05, reach=5.0):
        self.bias = bias
        self.sky_w = sky_w
        self.height_w = height_w
        self.spread = spread
        self.reach = reach


def _shade(pieces, tone, rng):
    """leaf のかたまりの面ごとに (面, t) を返す。ほかのかたまりに埋まった面は捨てる。"""
    leaves = [p for p in pieces if p.leaf]
    z0 = min(v[2] for p in leaves for f in p.faces for v in f)
    z1 = max(v[2] for p in leaves for f in p.faces for v in f)
    out = []
    for piece in leaves:
        others = [o for o in leaves if o is not piece]
        for face in piece.faces:
            if any(all(o.contains(v) for v in face) for o in others):
                continue
            c = _centroid(face)
            n = _norm(_newell(face))
            t = (tone.sky_w * _sky_light(c, n, others, tone.reach)
                 + tone.height_w * (c[2] - z0) / max(z1 - z0, 1e-6)
                 + tone.bias + piece.tone + rng.uniform(-tone.spread, tone.spread))
            out.append((face, t))
    return out


def _emit(mb, pieces, tone, top_z, seed):
    """かたまりと枝を剪断（_LEAN）して mb に積む。"""
    rng = random.Random(seed)

    def lean(face):
        return [(x + _lean_x(z, top_z), y, z) for x, y, z in face]

    for piece in pieces:
        if not piece.leaf:
            for face in piece.faces:
                mb.add_face(lean(face), "trunk")
    for face, t in _shade(pieces, tone, rng):
        mb.add_face(lean(face), "leaf", leaf_color(t))


def corners(mb):
    """Unity の頂点数（面の角の数）。"""
    return sum(len(f) for f in mb.faces)


def _finish(mb):
    n = corners(mb)
    if n > MAX_CORNERS:
        raise ValueError("%s: 頂点 %d が上限 %d を超えた" % (mb.name, n, MAX_CORNERS))
    return mb


# --------------------------------------------------------------------------- #
#  ケヤキ
# --------------------------------------------------------------------------- #
# 箒を逆さにした扇形。幹は 3.5 m から三又に割れ、斜め上へ開く太枝の先に葉のかたまりが載る。
# 幹のテーパー 0.280 → 0.215 は、身長 1.8 m 以下の最大半径が 0.292 m に収まるように
# 決めてある（Unity の幹カプセルは半径 0.3 m）。(半径, z, 腕の張り出し) を下から。
_KEYAKI_TRUNK = [
    (0.280, 0.00, 0.00),
    (0.270, 2.60, 0.00),
    (0.245, 3.50, 0.20),   # ここから三又が開きはじめる
    (0.215, 4.50, 0.50),
    (0.260, 5.35, 0.85),   # 三又の天辺。ここから上は太枝とかたまり
]
_KEYAKI_TOP = 9.95         # 剪断の基準（以前の樹冠の天辺）
_KEYAKI_FORK = 0.4         # 三又の向き [rad]
_KEYAKI_SEED = 11


def _keyaki_pieces(rng):
    fork = math.degrees(_KEYAKI_FORK)
    pieces = []
    for k in range(3):
        lobe = fork + 120.0 * k
        gap = lobe + 60.0
        # 太枝: 三又の腕の中から斜め外へ、下段のかたまりの中へ
        pieces.append(_tube([_polar(lobe, 0.62, 5.15), _polar(lobe, 1.85, 6.70)],
                            [0.20, 0.10], 3, (0.0, 0.0, 1.0), spin=rng.uniform(0, 120)))
        # 細枝: 三又の谷から、上段のかたまりの中へ（下段のかたまりの間から見える）
        pieces.append(_tube([_polar(gap, 0.08, 5.20), _polar(gap, 1.20, 7.75)],
                            [0.14, 0.07], 3, (0.0, 0.0, 1.0), spin=rng.uniform(0, 120)))
    # 下段 3 つは太枝の先で外へ張り出し、上段 3 つはその間で内寄りに高く、樹冠の天辺を作る。
    # 隣どうしを大きく重ねて 1 つの広い扇形にまとめ、下から見ると下段の間に上段の底と枝がのぞく。
    for k in range(3):
        lobe = fork + 120.0 * k
        pieces.append(_puff(_polar(lobe, 2.00, 6.75), 1.60, 1.00, lobe + rng.uniform(-12, 12), rng,
                            ry=1.35, tone=rng.uniform(-0.06, 0.06)))
        pieces.append(_puff(_polar(lobe + 60.0, 1.15, 8.05), 1.80, 1.25, rng.uniform(0, 72), rng,
                            tone=rng.uniform(-0.04, 0.06)))
    return pieces


def tree_keyaki(name="tree_mesh_keyaki"):
    """ケヤキ風。三又から扇形に開き、上が広い。高さ約 9.9 m。"""
    mb = MeshBuilder(name)
    _add_trunk(mb, _KEYAKI_TRUNK, _KEYAKI_TOP, seg=12, arms=3, phase=_KEYAKI_FORK,
               noise=0.05, seed=_KEYAKI_SEED)
    rng = random.Random(_KEYAKI_SEED + 1000)
    _emit(mb, _keyaki_pieces(rng), Tone(), _KEYAKI_TOP, _KEYAKI_SEED + 2000)
    return _finish(mb)


# --------------------------------------------------------------------------- #
#  丸刈りの中木（玉仕立て）
# --------------------------------------------------------------------------- #
# 刈り込んだ丸いドームに浅いこぶが並ぶ。樹冠の下端は目線 1.6 m より上（約 2.9 m）。
_ROUND_TRUNK = [
    (0.230, 0.00, 0.00),
    (0.210, 1.95, 0.00),
    (0.340, 2.55, 0.05),
]
_ROUND_TOP = 6.20
_ROUND_PHASE = 0.6
_ROUND_SEED = 23


def _round_pieces(rng):
    base = math.degrees(_ROUND_PHASE)
    # 芯のドームに、下の輪 3 つと 60 度ずらした上の輪 3 つの大きなこぶを半分ほど埋める。
    # こぶの芯に埋まった面は _shade が捨てる。
    pieces = [_tube([(0.0, 0.0, 2.55), (0.0, 0.0, 3.70)], [0.26, 0.20], 5, (1.0, 0.0, 0.0)),
              _puff((0.0, 0.0, 4.40), 1.65, 1.70, rng.uniform(0, 72), rng, jitter=0.05)]
    for k in range(3):
        a = base + 120.0 * k
        pieces.append(_puff(_polar(a, 1.05, 3.75), 1.20, 0.95, a + rng.uniform(-15, 15), rng,
                            ry=1.05, tone=rng.uniform(-0.05, 0.05)))
        pieces.append(_puff(_polar(a + 60.0, 0.90, 4.95), 1.10, 0.95, rng.uniform(0, 72), rng,
                            tone=rng.uniform(-0.03, 0.06)))
    return pieces


def tree_round(name="tree_mesh_round"):
    """丸く刈り込んだ中木。高さ約 6.2 m。"""
    mb = MeshBuilder(name)
    _add_trunk(mb, _ROUND_TRUNK, _ROUND_TOP, seg=10, arms=5, phase=_ROUND_PHASE,
               noise=0.06, seed=_ROUND_SEED)
    rng = random.Random(_ROUND_SEED + 1000)
    _emit(mb, _round_pieces(rng), Tone(bias=0.24), _ROUND_TOP, _ROUND_SEED + 2000)
    return _finish(mb)


# --------------------------------------------------------------------------- #
#  クロマツ（庭木の棚仕立て）
# --------------------------------------------------------------------------- #
# 少し曲がった幹から水平の枝が出て、枝先に平たい葉の棚（ポンポン）が載る。
# いちばん下の棚の底は z=3.1 前後。目線 1.6 m の頭上にあり、真下に立っても棚の間に空が見える。
_PINE_TRUNK = [
    (0.270, 0.00, 0.00),
    (0.250, 2.95, 0.00),
]
_PINE_TOP = 8.10
_PINE_SEED = 41

# 棚: (方位 [度], 幹からの距離, 中心 z, 枝に沿った半径, 枝に直交する半径, 厚みの半分, 枝を出すか)
_PINE_PADS = [
    (20.0, 1.55, 3.45, 1.65, 1.20, 0.55, True),
    (145.0, 1.50, 3.70, 1.60, 1.15, 0.55, True),
    (265.0, 1.60, 3.40, 1.65, 1.20, 0.55, True),
    (85.0, 1.25, 4.80, 1.50, 1.10, 0.52, True),
    (215.0, 1.20, 4.90, 1.45, 1.10, 0.52, True),
    (330.0, 0.85, 5.95, 1.30, 1.05, 0.50, False),
    (160.0, 0.80, 6.15, 1.25, 1.00, 0.50, False),
    (60.0, 0.25, 7.20, 1.20, 1.05, 0.62, False),
]


# 幹の続き（z=2.95 の天辺から天辺の棚の中まで）の折れ線。途中で +x 側へ振れて戻る。
_PINE_STEM = [(0.0, 0.0, 2.95), (0.28, -0.05, 5.20), (0.02, -0.10, 7.40)]


def _pine_axis(z):
    """幹の続きの折れ線の、高さ z での中心 (x, y)。"""
    for a, b in zip(_PINE_STEM, _PINE_STEM[1:]):
        if z <= b[2] or b is _PINE_STEM[-1]:
            f = max(0.0, min(1.0, (z - a[2]) / (b[2] - a[2])))
            return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)
    return _PINE_STEM[-1][:2]


def _pine_pieces(rng):
    stem = _PINE_STEM
    pieces = [_tube(stem, [0.21, 0.15, 0.08], 5, (1.0, 0.0, 0.0))]
    for deg, dist, z, ru, rv, rz, branch in _PINE_PADS:
        ax, ay = _pine_axis(z)
        cx, cy, _ = _polar(deg, dist, z)
        pieces.append(_puff((ax + cx, ay + cy, z), ru, rz, deg, rng, ry=rv, jitter=0.10,
                            tone=rng.uniform(-0.05, 0.05)))
        if branch:
            root = (ax, ay, z - 0.25)
            tip = (ax + cx * 0.75, ay + cy * 0.75, z - 0.05)
            pieces.append(_tube([root, tip], [0.11, 0.07], 3, (0.0, 0.0, 1.0),
                                spin=rng.uniform(0, 120)))
    return pieces


def tree_pine(name="tree_mesh_pine"):
    """クロマツ風。曲がった幹と水平の棚。高さ約 8.1 m。"""
    mb = MeshBuilder(name)
    _add_trunk(mb, _PINE_TRUNK, _PINE_TOP, seg=10, arms=5, phase=0.25, noise=0.05,
               seed=_PINE_SEED)
    rng = random.Random(_PINE_SEED + 1000)
    _emit(mb, _pine_pieces(rng), Tone(bias=0.08), _PINE_TOP, _PINE_SEED + 2000)
    return _finish(mb)


TREE_SPECIES = [
    ("keyaki", tree_keyaki),
    ("round", tree_round),
    ("pine", tree_pine),
]
