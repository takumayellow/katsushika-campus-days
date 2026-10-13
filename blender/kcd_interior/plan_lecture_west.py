"""講義棟 2F・3F の中廊下・西ロビー・コア（spec §1-4・§1-5・§1-11）。

西の外周壁から X 16.4 までの廊下の床と暗い天井、2F・3F の廊下の南北の壁（西端から X 35.7 まで。
教室の扉は開いた引き戸、2F の北の室は閉じた扉）、黒いコアの箱 2 つ（EV・トイレ。東の箱は
1F のトイレの箱とガラスの仕切りを 1 つにまとめた形 = 推定）、西ロビーの外周壁と天井の明かり。
X 16.4 より東の床・天井とエスカレータの穴は plan_lecture_upper、教室の中は plan_lecture_rooms が作る。

メッシュは階・南北の帯・東西の区画ごとに分ける（URP は 1 つのメッシュに効く点光源の数に上限がある。spec §1-13）:
    floor_lecture_corr<階>_<帯><k> / wall_lecture_corr<階>_<帯><k>
        帯は s（南の廊下とコアの南面）・n（北）・c（コアの箱の東西の端の面）、k は SPLIT_X で切った区画の番号。
        西ロビー（k = 0）は帯を分けず corr<階>_0
    lamp_lecture_<階>_<n>   廊下の天井の点光源
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH

X_CORR_E = D.UPPER_X0                 # この区画の廊下の床・天井の東端（16.4）
X_WALL0 = RD.X_IN_W - 0.1             # 廊下の壁の西端（西の外周壁の中まで）
X_WALL1 = RD.X_311_E                  # 廊下の壁の東端（35.7）
SPLIT_X = (D.WEST_LOBBY_X1, -3.5, 7.0, X_CORR_E, D.X_STEP)   # 区画の境（扉の開口にかかれば開口の西端へずらす）
CORE_W = (D.CORE_EV[0], D.Y_CORE0, D.ATRIUM[0], D.Y_CORE1)        # EV・女子トイレ（-21.0〜-2.8）
CORE_M = (D.CORE_WC_M[0], D.Y_CORE0, D.SCREEN_X[1], D.Y_CORE1)    # 男子トイレ（6.6〜18.0）
Z_SLAB2_UNDER = D.F2 - D.SLAB         # コアの箱の下端（2F の床スラブの下面）
Z_SLAB3_UNDER = D.F3 - D.SLAB         # 2F の壁の上端（3F の床スラブの下面）
WALL_TOP = {"2F": Z_SLAB3_UNDER, "3F": RD.Z_TOP}
CORE_Z = {"2F": (Z_SLAB2_UNDER, D.F3), "3F": (D.F3, RD.Z_TOP)}   # コアの箱の各階の分
Y_MID = (D.Y_CORE0 + D.Y_CORE1) * 0.5    # 南の帯と北の帯の境（27.5）
STRIP = 0.06                          # コアの天井際の光の帯の出
FRAME_OUT = 0.07                      # 扉の枠の見付（shell.door）
DOWN = 0.30                           # ダウンライトの一辺

CORR = "lecture_concrete"
CORE = "lecture_core_black"
CEIL = "lecture_ceiling_dark"
CARPET = "lecture_carpet_corridor"
COOL = "lecture_light_cool"
DOOR_WOOD = "lecture_door_wood"
FRAME = "metal_dark"
WHITE = "lecture_sign_white"


def lifted(mb, z, fn, *args, **kw):
    """z = 0 で組む家具ヘルパ fn を、床の高さ z へ持ち上げて置く。"""
    base = len(mb.verts)
    out = fn(mb, *args, **kw)
    kit.lift(mb, base, z)
    return out


def segment(x):
    """X が入る区画の番号（SPLIT_X の境で切る）。"""
    return sum(1 for b in SPLIT_X if x >= b)


def room_doors(floor, side):
    """floor の階の side（"S" / "N"）の並びの教室の扉の中心 X（西から順）。"""
    return sorted(d for r in RD.ROOMS if r[1] == floor and r[2] == side for d in r[5])


def slide_ang(x, doors, x1=X_WALL1):
    """扉 x の戸の向き。+X へ開けた戸（x + 0.60〜x + 2.16）が隣の開口か壁の端にかかれば -X へ。"""
    hw = RD.DOOR_W * 0.5
    dx = RD.DOOR_W * RD.DOOR_OPEN
    lo, hi = x + dx - hw + 0.02, x + dx + hw - 0.02
    reach = hw + FRAME_OUT
    hit = hi > x1 or any(lo < d + reach and hi > d - reach for d in doors if d != x)
    return math.pi if hit else 0.0


def pieces(x0, x1, doors):
    """x0〜x1 の壁を SPLIT_X で切った (区画の番号, 西端, 東端)。境が開口にかかれば開口の西端へ。"""
    hw = RD.DOOR_W * 0.5 + FRAME_OUT
    cuts = []
    for b in SPLIT_X:
        for d in doors:
            if d - hw < b < d + hw:
                b = d - hw
        if x0 < b < x1:
            cuts.append(b)
    xs = [x0] + cuts + [x1]
    return [(segment((a + b) * 0.5), a, b) for a, b in zip(xs[:-1], xs[1:])]


def _door(mb, x, y, z, ang, w=RD.DOOR_W, h=RD.DOOR_H, open_=0.0):
    lifted(mb, z, shell.door, x, y, ang=ang, w=w, h=h, leaf=DOOR_WOOD, frame=FRAME,
           open_=open_)


class _Walls:
    """SH.part / SH.wall に渡す c の代わり（c.wall の代わりに区画のメッシュへ積む）。"""

    def __init__(self, mb):
        self.wall = mb


def band(y):
    """Y が入る帯（"s" / "n"）。"""
    return "s" if y < Y_MID else "n"


class _Bands:
    """ある階の floor_ か wall_ の区画のメッシュ。使うときに作る（西ロビーは帯を分けない）。"""

    def __init__(self, c, role, floor):
        self.c, self.role, self.tag = c, role, "corr" + floor.lower()
        self.parts = {}

    def get(self, side, k):
        key = "%s%d" % (side, k) if k else "0"
        if key not in self.parts:
            self.parts[key] = self.c.part(self.role, "%s_%s" % (self.tag, key))
        return self.parts[key]

    def at(self, x, y):
        return self.get(band(y), segment(x))


def corridor_wall(bands, floor, yc, doors, z1, top):
    """廊下の壁 1 本（中心 yc、X_WALL0〜X_WALL1）。doors の扉は開いた引き戸と垂れ壁。"""
    z0 = RD.FLOOR_Z[floor]
    hw = RD.DOOR_W * 0.5
    for k, a, b in pieces(X_WALL0, X_WALL1, doors):
        mb = bands.get(band(yc), k)
        mine = [d for d in doors if a <= d < b]
        gaps = [(d - hw - a, d + hw - a) for d in mine]
        SH.part(_Walls(mb), (a, yc), (b, yc), z0, z1, mat=CORR, gaps=gaps, top=top)
        for d in mine:
            kit.box(mb, d - hw, yc - RD.T * 0.5, z0 + RD.DOOR_H, d + hw, yc + RD.T * 0.5, z1, CORR)
            _door(mb, d, yc, z0, slide_ang(d, doors), open_=RD.DOOR_OPEN)


def _corridor_walls(bands, floor):
    top = floor == "3F"
    z1 = WALL_TOP[floor]
    corridor_wall(bands, floor, RD.Y_CS, room_doors(floor, "S"), z1, top)
    corridor_wall(bands, floor, RD.Y_CN, room_doors(floor, "N"), z1, top)
    if floor != "2F":
        return
    # 2F の北の室（中は作らない）: 閉じた扉と、文字の無い室名札の白い板
    z0 = D.F2
    for x in RD.DOORS_2F_N:
        _door(bands.at(x, D.Y_ROOM_N0), x, D.Y_ROOM_N0, z0, math.pi, w=1.0, h=2.1)
    for x in RD.PLATES_2F_N:
        shell.wall_sign(bands.at(x, D.Y_ROOM_N0), x, D.Y_ROOM_N0, z0 + 1.8, math.pi,
                        w=0.42, h=0.9, mat=WHITE)


def _floor_rects():
    """廊下の床の矩形（区画の番号, x0, y0, x1, y1）。南北の帯と西ロビー。コアと吹き抜けは抜く。"""
    y0, y1 = RD.Y_S_ROOM1, RD.Y_N_ROOM0
    out = [(0, RD.X_IN_W, y0, D.WEST_LOBBY_X1, y1)]
    xs = (D.WEST_LOBBY_X1, D.ATRIUM[0], D.ATRIUM[2], X_CORR_E)
    for a, b in zip(xs[:-1], xs[1:]):
        k = segment((a + b) * 0.5)
        out.append((k, a, y0, b, D.Y_CORE0))
        out.append((k, a, D.Y_CORE1, b, y1))
    return out


def _floors(c, floor):
    z = RD.FLOOR_Z[floor]
    bands = _Bands(c, "floor", floor)
    for k, x0, y0, x1, y1 in _floor_rects():
        mb = bands.get(band((y0 + y1) * 0.5), k)
        shell.floor(mb, x0, y0, x1, y1, z, CARPET, thickness=D.SLAB, side_mat=CORR)


def downlight_centers():
    """廊下の天井のダウンライトの中心 (x, y)（2F・3F 共通）。"""
    pts = [(-27.0 + 3.0 * i, y) for y in (21.65, 33.3) for i in range(15)]
    return pts + [(x, y) for x in (-26.5, -23.5) for y in (25.5, 29.5)]


def _ceilings(bands, floor):
    """廊下の暗い天井（床から 3.0）。コアの帯（2 つのコアの箱と吹き抜け）は抜く（箱の天面は廊下から見えない）。"""
    z = RD.FLOOR_Z[floor] + RD.CEIL_H
    hole = (CORE_W[0], D.Y_CORE0, X_CORR_E, D.Y_CORE1)
    xs = (RD.X_IN_W,) + SPLIT_X[:3] + (X_CORR_E,)
    for a, b in zip(xs[:-1], xs[1:]):
        k = segment((a + b) * 0.5)
        for y0, y1 in ((D.Y_ROOM_S1, Y_MID), (Y_MID, D.Y_ROOM_N0)):
            shell.ceiling(bands.get(band(y0), k), a, y0, b, y1, z, CEIL, holes=(hole,))
    h = DOWN * 0.5
    for x, y in downlight_centers():
        kit.plate(bands.at(x, y), x - h, y - h, x + h, y + h, z - 0.005, "light_panel", flip=True)


def _core_side(bands, p, q, z0, z1, strip):
    """コアの箱の 1 面（p から q、外向きは進む向きの右）。strip なら天井際に光の帯。

    南・北の面は区画の境で切ってその帯へ、東・西の端の面は帯 c（コアの帯）へ入れる。
    """
    (px, py), (qx, qy) = p, q
    if py == qy:
        cuts = sorted({px, qx} | {b for b in SPLIT_X if min(px, qx) < b < max(px, qx)})
        cuts = cuts if px < qx else cuts[::-1]
        segs = [((a, py), (b, py)) for a, b in zip(cuts[:-1], cuts[1:])]
    else:
        segs = [(p, q)]
    nx, ny = (qy - py, px - qx)
    s = STRIP / math.hypot(nx, ny)
    for a, b in segs:
        mx = (a[0] + b[0]) * 0.5
        mb = bands.at(mx, py) if py == qy else bands.get("c", segment(mx))
        kit.vplate(mb, a, b, z0, z1, CORE)
        if strip:
            xs, ys = (a[0], b[0], a[0] + nx * s, b[0] + nx * s), (a[1], b[1], a[1] + ny * s, b[1] + ny * s)
            kit.box(mb, min(xs), min(ys), z1 - 0.10, max(xs), max(ys), z1 - 0.04, COOL)


def _core_box(bands, floor, box, west):
    """コアの箱の側面のこの階の分。光の帯は南・北の面と、吹き抜けに面さない端の面（西の箱は西、東の箱は東）。"""
    x0, y0, x1, y1 = box
    z0, z1 = CORE_Z[floor]
    h = RD.FLOOR_Z[floor] + RD.CEIL_H
    lit = {"s": True, "n": True, "w": west, "e": not west}
    for side, p, q in (("s", (x0, y0), (x1, y0)), ("e", (x1, y0), (x1, y1)),
                       ("n", (x1, y1), (x0, y1)), ("w", (x0, y1), (x0, y0))):
        _core_side(bands, p, q, z0, h, lit[side])
        if z1 > h:
            _core_side(bands, p, q, h, z1, False)


def _core(bands, floor):
    """各階の EV 2 基・多目的トイレ・女子・男子トイレの入口（1F と同じ位置）。"""
    z = RD.FLOOR_Z[floor]
    y0, y1 = D.Y_CORE0, D.Y_CORE1
    lifted(bands.at(CORE_W[0] + 0.1, y0), z, shell.elevator_bank, D.CORE_EV[0] + 1.6, y0,
           count=2, ang=0.0, pitch=3.6)
    _door(bands.at(-17.6, y1), -17.6, y1 + 0.05, z, 0.0, w=1.2, h=2.1)
    for x in (-7.8, 14.0):
        _door(bands.at(x, y0), x, y0 - 0.05, z, math.pi, w=1.0, h=2.1)
    _core_box(bands, floor, CORE_W, True)
    _core_box(bands, floor, CORE_M, False)


def _west_wall(bands, floor):
    """西ロビーの西の外周壁（廊下の壁の間）と、学生食堂の上の階への閉じた扉。"""
    z0 = RD.FLOOR_Z[floor]
    top = floor == "3F"
    z1 = RD.Z_TOP if top else D.F3
    mb = bands.get("s", 0)
    SH.wall(_Walls(mb), ((D.X_W, RD.Y_CN), (D.X_W, RD.Y_CS)), z0, z1, top=top)
    _door(mb, RD.X_IN_W + 0.05, RD.WEST_DOOR_Y, z0, -math.pi * 0.5, w=1.8)


def lamps(c, floor):
    """廊下・西ロビー・他の区画の天井の点光源の Empty（Unity は 1 つずつ吊る）と、プレビューの明かり。"""
    z = RD.FLOOR_Z[floor]
    pts = list(RD.LAMPS_CORRIDOR) + list(RD.LAMPS_EXTRA[floor])
    for n, (x, y) in enumerate(pts, 1):
        c._put("lamp_%s_%s_%d" % (c.spec.id, floor, n), x, y, z + RD.LAMP_DZ)
    c.lights_from(downlight_centers(), z + RD.CEIL_H, step=2)
    return len(pts)


def build(c):
    """2F・3F の廊下・コア・西ロビー。戻り値は階 -> 置いた点光源の数（教室の明かりはその続きの番号）。"""
    count = {}
    for floor in ("2F", "3F"):
        walls = _Bands(c, "wall", floor)
        _floors(c, floor)
        _ceilings(walls, floor)
        _corridor_walls(walls, floor)
        _core(walls, floor)
        _west_wall(walls, floor)
        count[floor] = lamps(c, floor)
    return count
