"""樹木・ベンチ・照明柱などの小物。"""

import math
import random

from . import geom
from .mesh import MeshBuilder


def add_blob(mb, cx, cy, cz, rx, ry, rz, mat, seg=8, rings=4, noise=0.0, seed=0):
    """低ポリの楕円体（葉のかたまり）。

    mat は文字列、または上→下に並べたマテリアル名のリスト（リングごとに切り替える）。
    noise > 0 なら各リング頂点の半径を ±noise 倍だけ揺らし、輪郭をギザギザにする。
    """
    bands = [mat] if isinstance(mat, str) else list(mat)
    bands += [bands[-1]] * max(0, rings - len(bands))
    rng = random.Random(seed)
    jitter = [[1.0 + rng.uniform(-noise, noise) for _ in range(seg)]
              for _ in range(rings + 1)]
    prev = None
    for k in range(rings + 1):
        phi = math.pi * k / rings
        r = math.sin(phi)
        z = cz + rz * math.cos(phi)
        if k == 0 or k == rings:
            ring = [(cx, cy, z)] * seg
        else:
            ring = [(cx + rx * r * jitter[k][i] * math.cos(2 * math.pi * i / seg),
                     cy + ry * r * jitter[k][i] * math.sin(2 * math.pi * i / seg), z)
                    for i in range(seg)]
        if prev is not None:
            m = bands[min(k - 1, len(bands) - 1)]
            for i in range(seg):
                j = (i + 1) % seg
                a, b, c, d = prev[i], prev[j], ring[j], ring[i]
                # 巻き方向。以前は逆で法線が内向きになっており、Cull Back のトゥーン
                # シェーダが手前の面を捨てて奥側の面を描いていた。そのせいで幹の枝が
                # 樹冠を突き抜けて見え、陰影も裏返っていた（#51）。
                if k == rings:
                    mb.add_face([c, b, a], m)
                elif k == 1:
                    mb.add_face([d, c, a], m)
                else:
                    mb.add_quad(d, c, b, a, m)
        prev = ring


def add_cone(mb, cx, cy, z0, z1, r, mat, seg=8, noise=0.0, seed=0):
    """円錐（針葉樹の段）。noise > 0 で裾の半径と高さを少し乱す。"""
    rng = random.Random(seed)
    ring = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        j = 1.0 + rng.uniform(-noise, noise)
        dz = rng.uniform(-0.12, 0.12) * r if noise else 0.0
        ring.append((cx + r * j * math.cos(a), cy + r * j * math.sin(a), z0 + dz))
    tip = (cx, cy, z1)
    for i in range(seg):
        j = (i + 1) % seg
        mb.add_face([ring[i], ring[j], tip], mat)
    mb.add_face(list(reversed(ring)), mat)


# 樹冠の塗り分け（#51）。トゥーンは 2 段ランプしか持たないので、明暗はメッシュ側の
# マテリアルで作る。上面 leaf_top → leaf_light → leaf → 内側・底面 leaf_dark。
_CROWN_CORE_BANDS = ["leaf", "leaf", "leaf_dark"]


def _crown_bands(w):
    """w は樹冠内の上下位置（+1 が天、-1 が底）。上ほど明るく、底ほど濃く。"""
    if w > 0.40:
        return ["leaf_top", "leaf_light", "leaf"]
    if w > -0.40:
        return ["leaf_light", "leaf", "leaf"]
    return ["leaf", "leaf", "leaf_dark"]


def add_crown(mb, core, env, n, r_range, shell, seed, noise=0.14):
    """核の楕円体 1 個 + 枝先のかたまり n 個。

    core / env は (cx, cy, cz, rx, ry, rz)。かたまりは env の楕円殻の上に
    フィボナッチ球で配り、shell 倍だけ中心寄りに引き込む。乱数は seed 固定なので
    再ビルドしても同じ FBX になる。
    """
    cx, cy, cz, rx, ry, rz = core
    add_blob(mb, cx, cy, cz, rx, ry, rz, _CROWN_CORE_BANDS,
             seg=8, rings=3, noise=noise * 0.6, seed=seed * 7)
    ex, ey, ez, ax, ay, az = env
    rng = random.Random(seed)
    made = []
    for i in range(n):
        w = 2.0 * ((i + 0.5) / n) - 1.0
        rad = math.sqrt(max(0.0, 1.0 - w * w))
        a = math.pi * (3.0 - math.sqrt(5.0)) * i + rng.uniform(-0.35, 0.35)
        s = rng.uniform(*shell)
        made.append((ex + ax * rad * math.cos(a) * s,
                     ey + ay * rad * math.sin(a) * s,
                     ez + az * w * s, rng.uniform(*r_range), w))
    for i, (px, py, pz, rr, w) in enumerate(made):
        add_blob(mb, px, py, pz, rr, rr, rr * rng.uniform(0.74, 0.92),
                 _crown_bands(w), seg=6, rings=3, noise=noise, seed=seed * 131 + i)


# 樹冠の寸法。外形（幅・高さ）は現行の blob 配置とほぼ同じに合わせてあるので、
# 並木の見え方と Unity 側の幹カプセル（半径 0.3 m / 高さ 6 m）は変わらない。
_KEYAKI_CROWN = dict(core=(0.0, 0.0, 8.05, 2.45, 2.45, 1.67),
                     env=(0.0, 0.0, 8.10, 2.88, 2.88, 1.72),
                     n=9, r_range=(1.21, 1.67), shell=(0.45, 0.80), seed=11)
# 丸木は枝先を核より外へ出すのが肝。核 1.95 に対し、いちばん内寄り・いちばん小さい
# かたまりでも 1.42 * 0.66 + 1.30 = 2.24 > 1.95 で必ず顔を出す。ここを核より内側にすると
# 輪郭が核の八角形そのものになり、遠景で「緑の六角形」に見えてしまう（#51 の実測）。
_ROUND_CROWN = dict(core=(0.0, 0.0, 4.15, 1.95, 1.95, 1.88),
                    env=(0.0, 0.0, 4.22, 1.42, 1.42, 1.55),
                    n=5, r_range=(1.30, 1.62), shell=(0.66, 1.00), seed=23)


def tree_keyaki(name="tree_mesh_keyaki"):
    """ケヤキ風。杯状に枝分かれし、上が広い。高さ 9.5 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 3.4, 0.26, "trunk", seg=8, cap_top=False)
    for i in range(3):
        a = 2 * math.pi * i / 3 + 0.4
        dx, dy = math.cos(a) * 1.3, math.sin(a) * 1.3
        tri = [(0.20 * math.cos(a), 0.20 * math.sin(a), 3.0),
               (dx, dy, 6.2), (dx * 0.5, dy * 0.5, 3.1)]
        # 枝は板 1 枚で、法線は (-sin a, cos a, 0) の片面だけ。Cull Back の
        # トゥーンでは反対側から見たときに枝が丸ごと消える（#51）。表裏 2 枚にする。
        # ただし同じ位置に重ねると MeshBuilder.to_object の remove_doubles
        # （しきい値 1e-4）が片方を消すので、法線方向に半分ずつずらして厚み 0.03 m の
        # 板にする。+1 tri / 枝（= +3 tri / 本）。
        ox, oy = -math.sin(a) * 0.015, math.cos(a) * 0.015
        mb.add_face([(p[0] + ox, p[1] + oy, p[2]) for p in tri], "trunk")
        mb.add_face([(p[0] - ox, p[1] - oy, p[2]) for p in reversed(tri)], "trunk")
    add_crown(mb, **_KEYAKI_CROWN)
    return mb


def tree_round(name="tree_mesh_round"):
    """丸い樹冠の中木。高さ 6.5 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 2.4, 0.20, "trunk", seg=8, cap_top=False)
    add_crown(mb, **_ROUND_CROWN)
    return mb


# (z0, z1, 裾半径, マテリアル)。段を 3 → 4 に増やして輪郭を階段状にする。
_PINE_TIERS = [(1.35, 4.55, 2.40, "leaf_dark"), (3.10, 6.10, 1.92, "leaf"),
               (4.60, 7.25, 1.42, "leaf_light"), (5.80, 8.05, 0.90, "leaf_top")]


def tree_pine(name="tree_mesh_pine"):
    """常緑の円錐樹。高さ 8 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 1.6, 0.25, "trunk", seg=8, cap_top=False)
    for i, (z0, z1, r, mat) in enumerate(_PINE_TIERS):
        add_cone(mb, 0, 0, z0, z1, r, mat, seg=8, noise=0.22, seed=41 + i)
    return mb


TREE_SPECIES = [
    ("keyaki", tree_keyaki),
    ("round", tree_round),
    ("pine", tree_pine),
]


def add_bench(mb, x, y, ang, back=True):
    """ベンチ。ローカル +v 側に背もたれ（back=True）。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)

    # 座面
    mb.add_quad(P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(-0.9, -0.25, 0.38), P(-0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.38), P(0.9, -0.25, 0.38), "wood")
    mb.add_quad(P(-0.9, 0.25, 0.38), P(0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(0.9, -0.25, 0.38), P(-0.9, -0.25, 0.38),
                P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45), "wood")
    # 脚
    for du in (-0.7, 0.7):
        p = P(du, 0, 0)
        mb.add_box_c(p[0], p[1], 0.0, 0.10, 0.55, 0.40, "metal_grey")
    if back:
        add_box_rot(mb, x, y, ang, -0.9, 0.22, 0.50, 0.9, 0.28, 0.92, "wood")
        for du in (-0.7, 0.7):
            add_box_rot(mb, x, y, ang, du - 0.03, 0.22, 0.40, du + 0.03, 0.28, 0.50, "metal_grey")


def add_lamp(mb, x, y, h=4.0):
    mb.add_cylinder(x, y, 0.0, h, 0.09, "metal_white", seg=8, cap_top=False)
    mb.add_box_c(x, y, h, 0.55, 0.30, h + 0.22, "metal_white", top_mat="metal_white")
    mb.add_box_c(x, y, h - 0.08, 0.45, 0.22, h, "line_white")


def scatter_positions(polys, density, blocked, rng, jitter=1.0, limit=100000):
    """ポリゴン内に density [本/m^2] でランダム散布する（blocked(x,y)==True は除外）。"""
    out = []
    for poly in polys:
        x0, y0, x1, y1 = geom.bbox(poly)
        area = abs(geom.poly_area(poly))
        n = int(area * density)
        tries = 0
        placed = 0
        while placed < n and tries < n * 12 and len(out) < limit:
            tries += 1
            px = rng.uniform(x0, x1)
            py = rng.uniform(y0, y1)
            if not geom.point_in_poly((px, py), poly):
                continue
            if blocked(px, py):
                continue
            out.append((px, py))
            placed += 1
    return out


def line_positions(a, b, step, blocked, offset=0.0):
    """a→b に等間隔で並べる（offset は左手側へのずらし）。"""
    d = geom.sub(b, a)
    L = geom.length(d)
    if L < step:
        return []
    e = geom.mul(d, 1.0 / L)
    nrm = (-e[1], e[0])
    out = []
    n = int(L / step)
    for i in range(n + 1):
        p = geom.add(geom.add(a, geom.mul(e, i * step)), geom.mul(nrm, offset))
        if not blocked(p[0], p[1]):
            out.append(p)
    return out


# --------------------------------------------------------------------------- #
#  外構小物（看板・駐輪場・自販機・ゴミ箱）
# --------------------------------------------------------------------------- #
def _local(x, y, ang):
    """(x, y) を原点に ang 回転したローカル座標 (du, dv, z) → ワールド 3D 点。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)
    return P


def add_box_rot(mb, x, y, ang, du0, dv0, z0, du1, dv1, z1, mat, top=True):
    """ローカル座標 (du, dv) で指定した直方体を ang 回転して置く。"""
    P = _local(x, y, ang)
    poly = [P(du0, dv0, 0)[:2], P(du1, dv0, 0)[:2], P(du1, dv1, 0)[:2], P(du0, dv1, 0)[:2]]
    mb.add_prism(poly, z0, z1, mat, mat if top else None, None)


def add_pylon_sign(mb, x, y, ang, zc, w=2.4, h=1.2):
    """建物名の立て看板。ローカル +u が板の正面（法線）。文字は Unity 側で貼る。"""
    hw = w * 0.5
    band = 0.28
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc - h * 0.5, 0.05, hw, zc + h * 0.5 - band,
                "sign_plate")
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc + h * 0.5 - band, 0.05, hw, zc + h * 0.5,
                "tus_green")
    P = _local(x, y, ang)
    for dv in (-hw + 0.25, hw - 0.25):
        p = P(0.0, dv, 0.0)
        mb.add_cylinder(p[0], p[1], 0.0, zc - h * 0.5, 0.05, "metal_grey", seg=8,
                        cap_top=False)


def _beam_uz(mb, P, du0, z0, du1, z1, w, hv, mat, dvc=0.0):
    """ローカルの u-z 平面で (du0, z0)-(du1, z1) を結ぶ、断面 w × 2*hv の角棒。

    add_box_rot は軸に平行な直方体しか作れないので、斜めの棒（自転車のダウンチューブ
    やフォーク）はここで面を直接積む。dvc は v 方向の中心で、車輪の板（v = 0）から
    逃がしたいチェーンステーとフォークに使う。
    巻き方: 断面は u-z で時計回りに並べる。こうすると dvc+hv 側の面の法線が +v、
    dvc-hv 側が -v、側面が断面の外を向く（Cull Back で消えない）。"""
    ddu, dz = du1 - du0, z1 - z0
    ln = math.hypot(ddu, dz)
    if ln < 1e-6:
        return
    nu, nz = -dz / ln * w * 0.5, ddu / ln * w * 0.5
    sec = [(du0 + nu, z0 + nz), (du1 + nu, z1 + nz),
           (du1 - nu, z1 - nz), (du0 - nu, z0 - nz)]
    back = [P(du, dvc + hv, z) for du, z in sec]
    front = [P(du, dvc - hv, z) for du, z in sec]
    mb.add_face(back, mat)
    mb.add_face(list(reversed(front)), mat)
    for i in range(4):
        j = (i + 1) % 4
        mb.add_quad(front[i], front[j], back[j], back[i], mat)


def _disc_uz(mb, P, du, z, r, hv, mat, seg=8, dvc=0.0):
    """ローカルの u-z 平面に立つ、厚み 2*hv の n 角形の円盤。自転車の車輪。

    以前は車輪が 0.66 × 0.66 の箱で、真横から見るとただの四角だった（#46）。"""
    ring = [(du + r * math.cos(-2.0 * math.pi * i / seg),
             z + r * math.sin(-2.0 * math.pi * i / seg)) for i in range(seg)]
    back = [P(u, dvc + hv, zz) for u, zz in ring]
    front = [P(u, dvc - hv, zz) for u, zz in ring]
    mb.add_face(back, mat)
    mb.add_face(list(reversed(front)), mat)
    for i in range(seg):
        j = (i + 1) % seg
        mb.add_quad(front[i], front[j], back[j], back[i], mat)


def _wedge_uv(mb, P, du0, du1, dv0, dv1, z_at_dv0, z_at_dv1, t, mat):
    """ローカルの u-v 矩形を、v 方向に傾けた厚み t の板にする。片流れの屋根と梁。

    上面の高さは dv0 で z_at_dv0、dv1 で z_at_dv1（その間は線形）。下面は t だけ下。
    add_prism は天地が水平な柱しか作れないので、ここも面を直接積む。"""
    corners = [(du0, dv0), (du1, dv0), (du1, dv1), (du0, dv1)]   # 上から見て反時計回り

    def zt(dv):
        s = 0.0 if abs(dv1 - dv0) < 1e-9 else (dv - dv0) / (dv1 - dv0)
        return z_at_dv0 + (z_at_dv1 - z_at_dv0) * s
    top = [P(du, dv, zt(dv)) for du, dv in corners]
    bot = [P(du, dv, zt(dv) - t) for du, dv in corners]
    mb.add_face(top, mat)
    mb.add_face(list(reversed(bot)), mat)
    for i in range(4):
        j = (i + 1) % 4
        mb.add_quad(bot[i], bot[j], top[j], top[i], mat)


# 駐輪場の寸法。日本の大学の屋根付き駐輪場（片流れ + 門形フレーム + 前輪差し込み式
# ラック + 三方開放）に合わせた（#46 の実測と修正計画 D）。
RACK_PITCH = 0.45        # ラックのピッチ。実物は 0.40〜0.50
RACK_TOP = 0.30          # ラックの天端。プレイヤーの stepOffset 0.40 より低くして跨げるようにする
POST_SPAN_MAX = 2.8      # 柱の最大スパン。実物は 2.0〜3.0 m ごとの門形フレーム
BIKE_PITCH = 0.90        # 自転車はラック 2 本に 1 台
SHED_EAVE_FRONT = 2.15   # 開口側（-v）の軒高
SHED_EAVE_REAR = 2.60    # 背面側（+v）の軒高。勾配 = (後 - 前) / depth
SHED_ROOF_T = 0.06       # 屋根の厚み
SHED_STOP_H = 0.15       # 背面の車止めの高さ。以前はここに高さ 1.0 m の腰壁があった


def add_bike(mb, x, y, ang):
    """自転車（前後輪 + ダイヤモンドフレーム + サドル + ハンドル）。ローカル +u が前。

    車輪は 8 角形の円盤（半径 0.335 = 26 インチ相当、ホイールベース 1.05 m）。
    フレームの棒は車輪の円盤と交わらない線形に引いてある。以前はトップチューブ・
    シートチューブ・フォークの 3 本が前後輪の箱の中に埋まっていた（#46）。
    前ホークとチェーンステーだけは車輪をまたぐので v 方向に 0.055 逃がす
    （タイヤの厚みは ±0.025 なので触れない）。"""
    P = _local(x, y, ang)
    r, hub = 0.335, 0.525
    _disc_uz(mb, P, -hub, r, r, 0.025, "bike_tire", seg=8)
    _disc_uz(mb, P, hub, r, r, 0.025, "bike_tire", seg=8)
    bb = (-0.06, 0.27)            # ボトムブラケット
    head_lo, head_hi = (0.47, 0.76), (0.40, 1.00)   # ヘッドチューブ（フォークの上）
    seat_top = (-0.16, 0.86)
    _beam_uz(mb, P, bb[0], bb[1], 0.455, 0.80, 0.055, 0.022, "bike_frame")      # ダウンチューブ
    _beam_uz(mb, P, seat_top[0], 0.845, 0.445, 0.82, 0.045, 0.020, "bike_frame")  # トップチューブ
    _beam_uz(mb, P, bb[0], bb[1], seat_top[0], seat_top[1], 0.05, 0.022, "bike_frame")
    _beam_uz(mb, P, head_lo[0], head_lo[1], head_hi[0], head_hi[1], 0.05, 0.022, "bike_frame")
    for dv in (-0.055, 0.055):
        _beam_uz(mb, P, hub, r, head_lo[0], head_lo[1], 0.035, 0.014, "bike_frame", dvc=dv)
        _beam_uz(mb, P, -hub, r, bb[0], bb[1], 0.035, 0.014, "bike_frame", dvc=dv)
    add_box_rot(mb, x, y, ang, -0.25, -0.055, 0.88, -0.07, 0.055, 0.94, "bike_tire")
    add_box_rot(mb, x, y, ang, 0.36, -0.25, 0.97, 0.42, 0.25, 1.02, "bike_frame")


def add_bike_shed(mb, x, y, ang, length=14.0, depth=2.4, bikes=True):
    """屋根付き駐輪場。ローカル u = 長手、v = 奥行（-v 側が開口、+v 側が背面）。

    片流れの屋根を門形フレームで受け、前輪差し込み式のラックを並べた三方開放の形。
    背面は高さ SHED_STOP_H の車止めだけにしてある。以前はここに高さ 1.0 m の腰壁が
    あり、屋根（下端 2.30）との間で跳べる高さが 2.30 - 1.62 = 0.68 m しかなくて
    中から出られず、しかもラックが厚み 0.04 / ピッチ 0.60（隙間 0.56 m < カプセル
    直径 0.616 m）かつ天端 0.45 m > stepOffset 0.40 m で通ることも跨ぐこともできず、
    入ると詰んでいた（#46）。"""
    P = _local(x, y, ang)
    hl, hd = length * 0.5, depth * 0.5
    slope = (SHED_EAVE_REAR - SHED_EAVE_FRONT) / max(depth, 1e-6)

    def eave(dv):
        """その v での軒（屋根の上面）の高さ。dv = -hd で SHED_EAVE_FRONT。"""
        return SHED_EAVE_FRONT + (dv + hd) * slope

    # 屋根。妻の出 0.20、前後の出 0.25。
    ru0, ru1 = -hl - 0.20, hl + 0.20
    rv0, rv1 = -hd - 0.25, hd + 0.25
    _wedge_uv(mb, P, ru0, ru1, rv0, rv1, eave(rv0), eave(rv1), SHED_ROOF_T, "metal_grey")
    # 鼻隠し。前の軒先に見付け 0.10 を垂らす。
    add_box_rot(mb, x, y, ang, ru0, rv0, eave(rv0) - SHED_ROOF_T - 0.10,
                ru1, rv0 + 0.05, eave(rv0) - SHED_ROOF_T, "metal_grey")
    # 門形フレーム。スパンが POST_SPAN_MAX を超えないように割る（前は 4 隅の 4 本だけ）。
    pu, pv = hl - 0.25, hd - 0.15
    nbay = max(1, int(math.ceil(2.0 * pu / POST_SPAN_MAX)))
    for i in range(nbay + 1):
        du = -pu + 2.0 * pu * i / nbay
        for dv in (-pv, pv):
            p = P(du, dv, 0.0)
            mb.add_cylinder(p[0], p[1], 0.0, eave(dv) - SHED_ROOF_T, 0.045,
                            "metal_grey", seg=6, cap_top=False)
        _wedge_uv(mb, P, du - 0.05, du + 0.05, -pv, pv,
                  eave(-pv) - SHED_ROOF_T, eave(pv) - SHED_ROOF_T, 0.09, "metal_grey")
    # 母屋 2 本。屋根の裏に沿わせる。
    for dv in (-hd * 0.45, hd * 0.45):
        add_box_rot(mb, x, y, ang, -hl, dv - 0.04, eave(dv) - SHED_ROOF_T - 0.06,
                    hl, dv + 0.04, eave(dv) - SHED_ROOF_T, "metal_grey")
    # 背面の車止め（腰壁の代わり）。
    add_box_rot(mb, x, y, ang, -hl, hd - 0.12, 0.0, hl, hd, SHED_STOP_H, "concrete_grey")
    # ラックの基礎レールと、前輪差し込み式のラック（千鳥に 0.05 ずらす）。
    add_box_rot(mb, x, y, ang, -hl + 0.3, -0.15, 0.0, hl - 0.3, 0.60, 0.06, "concrete_grey")
    n = max(1, int((length - 0.6) / RACK_PITCH))
    du0 = -(n - 1) * RACK_PITCH * 0.5
    for i in range(n):
        du = du0 + i * RACK_PITCH
        dv = -0.10 + (0.05 if i % 2 else 0.0)
        add_box_rot(mb, x, y, ang, du - 0.02, dv, 0.06, du + 0.02, dv + 0.65,
                    RACK_TOP, "metal_grey")
        # 自転車はラックの刃ではなく溝に入れる（半ピッチずらす）。ノーズは奥（+v）。
        # 何台か抜いて、満車に見えないようにする。
        if bikes and i % 2 == 0 and (i // 2) % 5 != 3:
            p = P(du + RACK_PITCH * 0.5, -0.10, 0.0)
            add_bike(mb, p[0], p[1], ang + math.pi * 0.5)


def add_vending(mb, x, y, ang, color="vending_red"):
    """自販機 1.0 (幅) × 0.75 (奥行) × 1.85 m。ローカル +u が正面。"""
    add_box_rot(mb, x, y, ang, -0.375, -0.5, 0.0, 0.375, 0.5, 1.85, color)
    P = _local(x, y, ang)
    mb.add_quad(P(0.39, -0.42, 0.95), P(0.39, 0.42, 0.95),
                P(0.39, 0.42, 1.75), P(0.39, -0.42, 1.75), "glass_dark")
    mb.add_quad(P(0.39, -0.42, 0.25), P(0.39, 0.42, 0.25),
                P(0.39, 0.42, 0.60), P(0.39, -0.42, 0.60), "metal_grey")


def add_trash_can(mb, x, y):
    mb.add_cylinder(x, y, 0.0, 0.85, 0.28, "bin_green", seg=10, cap_top=True)
    mb.add_cylinder(x, y, 0.85, 0.92, 0.30, "metal_grey", seg=10, cap_top=True)
