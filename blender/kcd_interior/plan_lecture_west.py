"""講義棟 2F〜7F の中廊下・西ロビー・コア・南東の EV ホール（spec §1-4〜§1-6・§1-11）。

西の外周壁から X 16.4 までの廊下の床と明るい灰色の天井、廊下の南北の壁（西端から 2F・3F は X 35.7、
4F〜7F は東の外壁 X 35.8 まで。教室の扉は開いた引き戸、中を作らない室は閉じた扉と室名札）、
黒いコアの箱 2 つ（EV・トイレ。東の箱は 1F のトイレの箱とガラスの仕切りを 1 つにまとめた形 = 推定）、
西ロビーの外周壁と西の階段（plan_lecture_wstair）、3F〜6F の男子トイレの前のロッカー、4F〜7F の南東の EV ホールと階段の扉、
7F のエスカレータの上の黒い箱。X 16.4 より東の床・天井とエスカレータの穴は plan_lecture_upper、
吹き抜けの縁は plan_lecture_atrium、教室の中は plan_lecture_rooms が作る。

メッシュは階・南北の帯・東西の区画ごとに分ける（URP は 1 つのメッシュに効く点光源の数に上限がある。spec §1-13）:
    floor_lecture_corr<階>_<帯><k> / wall_lecture_corr<階>_<帯><k>
        帯は s（南の廊下とコアの南面）・n（北）・c（コアの帯）、k は SPLIT_X で切った区画の番号。
        西ロビー（k = 0）は帯を分けず corr<階>_0。plan_lecture_upper・plan_lecture_atrium も同じ部品へ積む
    floor_lecture_seev<階> / wall_lecture_seev<階>   4F〜7F の南東の EV ホール
    lamp_lecture_<階>_<n>   廊下の天井の点光源
"""

import math

from . import furniture as F
from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH
from . import plan_lecture_wstair as WS

X_CORR_E = D.UPPER_X0                 # この区画の廊下の床・天井の東端（16.4）
X_WALL0 = RD.X_IN_W - 0.1             # 廊下の壁の西端（西の外周壁の中まで）
X_WALL1 = RD.X_311_E                  # 2F・3F の廊下の壁の東端（35.7）
UPPER = RD.FLOORS[2:]                 # 4F〜7F（3F と同じ間仕切りで、東端が東の外壁）
SPLIT_X = (D.WEST_LOBBY_X1, -3.5, 7.0, X_CORR_E, D.X_STEP)   # 区画の境（開口にかかれば開口の西端へずらす）
CORE_W = (D.CORE_EV[0], D.Y_CORE0, D.ATRIUM[0], D.Y_CORE1)        # EV・女子トイレ（-21.0〜-2.8）
CORE_M = (D.CORE_WC_M[0], D.Y_CORE0, D.SCREEN_X[1], D.Y_CORE1)    # 男子トイレ（6.6〜18.0）
CORE_ESC = (D.SCREEN_X[1], D.Y_CORE0, D.ESC_X[1], D.Y_CORE1)      # 7F のエスカレータの上の箱（18.0〜33.8）
Y_MID = (D.Y_CORE0 + D.Y_CORE1) * 0.5    # 南の帯と北の帯の境（27.5）
STRIP = 0.06                          # コアの天井際の光の帯の出
FRAME_OUT = 0.07                      # 扉の枠の見付（shell.door）
DOWN = 0.30                           # ダウンライトの一辺
FACE = {"S": (D.Y_ROOM_S1, 0.0), "N": (D.Y_ROOM_N0, math.pi)}   # 廊下の面の Y と、そこに貼る扉の向き

# 南東の EV ホール（4F〜7F。§1-6）: x01 の東の境と階段の西の壁の間、南の室の奥行きの途中まで
SEEV = (RD.X_01_E, 13.5, 32.0, RD.Y_S_ROOM1)
SEEV_GAP = (SEEV[0] + RD.T * 0.5, SEEV[2] - RD.T * 0.5)    # 廊下の南の壁の開口（扉は無い）
SEEV_GAP_H = 2.6                      # 開口の上の垂れ壁の下端（床から）
STAIR_DOOR_X = 33.9                   # 南東の階段の閉じた扉（廊下の南の面）
EV_POINT = (D.CORE_EV[0] - 1.4, sum(D.EV_DOORS_Y) * 0.5)  # EV の乗り場（西の面の 2 基の扉の間の 1.4 m 前の床。1F〜7F 共通）
LOCKERS = (7.0, 12.8, D.Y_CORE0 - 0.23, 1.8)   # 男子トイレの南の面のロッカー（3F〜6F）: x0, x1, 中心 y, 高さ
LOCKER_FLOORS = RD.FLOORS[1:-1]

CORR = "lecture_concrete"
CORE = "lecture_core_black"
CEIL = "lecture_ceiling_upper"
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


def x_wall1(floor):
    """floor の階の廊下の壁の東端。"""
    return RD.X_UPPER_E if floor in UPPER else X_WALL1


def room_doors(floor, side):
    """floor の階の side（"S" / "N"）の並びの教室の開いた扉の中心 X（西から順）。"""
    return sorted(d for r in RD.ROOMS if r[1] == floor and r[2] == side for d in r[5])


def halls(floor, side):
    """扉の無い開口 (西端, 東端) の列（4F〜7F の南の壁の EV ホールの口）。"""
    return (SEEV_GAP,) if floor in UPPER and side == "S" else ()


def slide_ang(x, doors, x1=X_WALL1):
    """扉 x の戸の向き。+X へ開けた戸（x + 0.60〜x + 2.16）が隣の開口か壁の端にかかれば -X へ。"""
    hw = RD.DOOR_W * 0.5
    dx = RD.DOOR_W * RD.DOOR_OPEN
    lo, hi = x + dx - hw + 0.02, x + dx + hw - 0.02
    reach = hw + FRAME_OUT
    hit = hi > x1 or any(lo < d + reach and hi > d - reach for d in doors if d != x)
    return math.pi if hit else 0.0


def openings(doors, extra=()):
    """扉の枠を含めた開口 (西端, 東端) と、扉の無い開口 extra。"""
    hw = RD.DOOR_W * 0.5 + FRAME_OUT
    return [(d - hw, d + hw) for d in doors] + list(extra)


def pieces(x0, x1, doors, extra=()):
    """x0〜x1 の壁を SPLIT_X で切った (区画の番号, 西端, 東端)。境が開口にかかれば開口の西端へ。"""
    holes = openings(doors, extra)
    cuts = []
    for b in SPLIT_X:
        for lo, hi in holes:
            if lo < b < hi:
                b = lo
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


def bands(c, role, floor):
    """floor の階の role（"floor" / "wall"）の区画のメッシュ。同じ c の中ではモジュールをまたいで 1 つ。"""
    cache = getattr(c, "lecture_bands", None)
    if cache is None:
        cache = {}
        c.lecture_bands = cache
    if (role, floor) not in cache:
        cache[(role, floor)] = _Bands(c, role, floor)
    return cache[(role, floor)]


def corridor_wall(bs, floor, yc, doors, z1, top, x1=X_WALL1, extra=()):
    """廊下の壁 1 本（中心 yc、X_WALL0〜x1）。doors の扉は開いた引き戸と垂れ壁、extra は扉の無い開口。"""
    z0 = RD.FLOOR_Z[floor]
    hw = RD.DOOR_W * 0.5
    t = RD.T * 0.5
    for k, a, b in pieces(X_WALL0, x1, doors, extra):
        mb = bs.get(band(yc), k)
        mine = [d for d in doors if a <= d < b]
        gaps = [(lo, hi) for lo, hi in extra if a <= lo < b]
        SH.part(_Walls(mb), (a, yc), (b, yc), z0, z1, mat=CORR, top=top,
                gaps=[(d - hw - a, d + hw - a) for d in mine] + [(lo - a, hi - a) for lo, hi in gaps])
        for d in mine:
            kit.box(mb, d - hw, yc - t, z0 + RD.DOOR_H, d + hw, yc + t, z1, CORR)
            _door(mb, d, yc, z0, slide_ang(d, doors, x1), open_=RD.DOOR_OPEN)
        for lo, hi in gaps:
            kit.box(mb, lo, yc - t, z0 + SEEV_GAP_H, hi, yc + t, z1, CORR)


def _closed_rooms(bs, floor):
    """中を作らない室の閉じた扉（廊下の面）。7F の室名札は番号の無い白い板（4F〜6F は看板の表）。"""
    z = RD.FLOOR_Z[floor]
    for room in RD.CLOSED:
        if room[1] != floor:
            continue
        y, ang = FACE[room[2]]
        for x in room[5]:
            _door(bs.at(x, y), x, y, z, ang)
        if floor == RD.TOP_FLOOR:
            x, yp, zp, ap = RD.plate_at(room[0], RD.upper_plate_x(room), room[2])
            shell.wall_sign(bs.at(x, yp), x, yp, zp, ap, w=0.42, h=0.9, mat=WHITE)


def _corridor_walls(bs, floor):
    top = floor == RD.TOP_FLOOR
    z1 = RD.wall_top(floor)
    x1 = x_wall1(floor)
    for side, yc in (("S", RD.Y_CS), ("N", RD.Y_CN)):
        corridor_wall(bs, floor, yc, room_doors(floor, side), z1, top, x1, halls(floor, side))
    _closed_rooms(bs, floor)
    if floor == "2F":
        # 2F の北の室（中は作らない）: 閉じた扉と、文字の無い室名札の白い板
        z0 = D.F2
        for x in RD.DOORS_2F_N:
            _door(bs.at(x, D.Y_ROOM_N0), x, D.Y_ROOM_N0, z0, math.pi, w=1.0, h=2.1)
        for x in RD.PLATES_2F_N:
            shell.wall_sign(bs.at(x, D.Y_ROOM_N0), x, D.Y_ROOM_N0, z0 + 1.8, math.pi,
                            w=0.42, h=0.9, mat=WHITE)


def _east_end(bs, floor):
    """4F〜7F: 廊下の東の外壁（X 35.8）と南東の階段の閉じた扉。"""
    z0, z1 = RD.FLOOR_Z[floor], RD.wall_top(floor)
    x = RD.X_UPPER_E
    SH.part(_Walls(bs.get("c", segment(x))), (x, RD.Y_CS), (x, RD.Y_CN), z0, z1, mat=CORR,
            top=floor == RD.TOP_FLOOR)
    y = D.Y_ROOM_S1
    _door(bs.at(STAIR_DOOR_X, y), STAIR_DOOR_X, y, z0, 0.0, w=1.0, h=2.1)


def _seev(c, floor):
    """4F〜7F の南東の EV ホール: 床・三方の壁・明るい灰色の天井とダウンライト・奥の EV の扉（動かない）。"""
    x0, y0, x1, y1 = SEEV
    z0, z1 = RD.FLOOR_Z[floor], RD.wall_top(floor)
    top = floor == RD.TOP_FLOOR
    tag = "seev" + floor.lower()
    shell.floor(c.part("floor", tag), x0, y0, x1, y1, z0, CARPET, thickness=D.SLAB, side_mat=CORR)
    mb = c.part("wall", tag)
    w = _Walls(mb)
    t = RD.T * 0.5
    SH.part(w, (x0, y0 - t), (x0, RD.Y_CS), z0, z1, mat=CORR, top=top)
    SH.part(w, (x1, y0 - t), (x1, RD.Y_CS), z0, z1, mat=CORR, top=top)
    SH.part(w, (x0 + t, y0), (x1 - t, y0), z0, z1, mat=CORR, top=top)
    zc = z0 + RD.CEIL_H
    shell.ceiling(mb, x0 + t, y0 + t, x1 - t, y1, zc, CEIL)
    cx = (x0 + x1) * 0.5
    h = DOWN * 0.5
    for y in (15.2, 18.0):
        kit.plate(mb, cx - h, y - h, cx + h, y + h, zc - 0.005, "light_panel", flip=True)
    lifted(mb, z0, shell.elevator_bank, cx, y0 + t, count=1, ang=math.pi)


def _floor_rects(floor):
    """廊下の床の矩形（帯, 区画の番号, x0, y0, x1, y1）。南北の帯と西ロビー。コアと吹き抜けは抜く。

    7F は吹き抜けの上を床で塞ぐ（吹き抜けの頂部 25.1 の天井は plan_lecture_atrium）。その床は
    コアの帯の部品に入れる（南北の帯へ入れると、反対側の廊下の点光源まで届く大きさになる）。"""
    y0, y1 = RD.Y_S_ROOM1, RD.Y_N_ROOM0
    out = [("s", 0, RD.X_IN_W, y0, D.WEST_LOBBY_X1, y1)]
    xs = (D.WEST_LOBBY_X1, D.ATRIUM[0], D.ATRIUM[2], X_CORR_E)
    for a, b in zip(xs[:-1], xs[1:]):
        k = segment((a + b) * 0.5)
        out.append(("s", k, a, y0, b, D.Y_CORE0))
        out.append(("n", k, a, D.Y_CORE1, b, y1))
    if floor == RD.TOP_FLOOR:
        ax0, ay0, ax1, ay1 = D.ATRIUM
        out.append(("c", segment((ax0 + ax1) * 0.5), ax0, ay0, ax1, ay1))
    return out


def _floors(c, floor):
    z = RD.FLOOR_Z[floor]
    bs = bands(c, "floor", floor)
    for b, k, x0, y0, x1, y1 in _floor_rects(floor):
        thick = 0.0 if b == "c" else D.SLAB
        shell.floor(bs.get(b, k), x0, y0, x1, y1, z, CARPET, thickness=thick, side_mat=CORR)


def downlight_centers():
    """廊下の天井のダウンライトの中心 (x, y)（2F〜7F 共通）。"""
    pts = [(-27.0 + 3.0 * i, y) for y in (21.65, 33.3) for i in range(15)]
    return pts + [(x, y) for x in (-26.5, -23.5) for y in (25.5, 29.5)]


def _ceilings(bs, floor):
    """廊下の明るい灰色の天井（床から 3.0）。コアの帯は抜く（箱の天面は廊下から見えない）。
    7F は吹き抜けの上にも張る（コアの帯の部品。床と同じ理由）。"""
    z = RD.FLOOR_Z[floor] + RD.CEIL_H
    hole = (CORE_W[0], D.Y_CORE0, X_CORR_E, D.Y_CORE1)
    xs = (RD.X_IN_W,) + SPLIT_X[:3] + (X_CORR_E,)
    for a, b in zip(xs[:-1], xs[1:]):
        k = segment((a + b) * 0.5)
        for y0, y1 in ((D.Y_ROOM_S1, Y_MID), (Y_MID, D.Y_ROOM_N0)):
            shell.ceiling(bs.get(band(y0), k), a, y0, b, y1, z, CEIL, holes=(hole,))
    if floor == RD.TOP_FLOOR:
        ax0, ay0, ax1, ay1 = D.ATRIUM
        shell.ceiling(bs.get("c", segment((ax0 + ax1) * 0.5)), ax0, ay0, ax1, ay1, z, CEIL)
    h = DOWN * 0.5
    for x, y in downlight_centers():
        kit.plate(bs.at(x, y), x - h, y - h, x + h, y + h, z - 0.005, "light_panel", flip=True)


def _core_side(bs, p, q, z0, z1, strip):
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
        mb = bs.at(mx, py) if py == qy else bs.get("c", segment(mx))
        kit.vplate(mb, a, b, z0, z1, CORE)
        if strip:
            xs, ys = (a[0], b[0], a[0] + nx * s, b[0] + nx * s), (a[1], b[1], a[1] + ny * s, b[1] + ny * s)
            kit.box(mb, min(xs), min(ys), z1 - 0.10, max(xs), max(ys), z1 - 0.04, COOL)


def core_z(floor):
    """コアの箱のこの階の分の下端と上端。2F は 2F の床スラブの下面から、7F は天井まで。"""
    z = RD.FLOOR_Z[floor]
    return (z - D.SLAB if floor == "2F" else z), RD.next_z(floor)


def _core_box(bs, floor, box, lit, faces="senw"):
    """コアの箱の側面のこの階の分（faces の面だけ）。lit は光の帯を付ける面（"s"・"e"・"n"・"w"）。"""
    x0, y0, x1, y1 = box
    z0, z1 = core_z(floor)
    h = RD.FLOOR_Z[floor] + RD.CEIL_H
    for side, p, q in (("s", (x0, y0), (x1, y0)), ("e", (x1, y0), (x1, y1)),
                       ("n", (x1, y1), (x0, y1)), ("w", (x0, y1), (x0, y0))):
        if side not in faces:
            continue
        _core_side(bs, p, q, z0, h, side in lit)
        if z1 > h:
            _core_side(bs, p, q, h, z1, False)


def _core(bs, floor):
    """各階の EV 2 基・多目的トイレ・女子・男子トイレの入口（1F と同じ位置）とロッカー。

    光の帯は南・北の面と、吹き抜けに面さない端の面（西の箱は西、東の箱は東）。7F は吹き抜けを
    床で塞ぐので、エスカレータの上の箱（西の面は男子トイレの箱の東の面）を足す。"""
    z = RD.FLOOR_Z[floor]
    y0, y1 = D.Y_CORE0, D.Y_CORE1
    lifted(bs.at(CORE_W[0] - 0.1, Y_MID), z, SH.ev_bank)
    _door(bs.at(-17.6, y1), -17.6, y1 + 0.05, z, 0.0, w=1.2, h=2.1)
    for x in (-7.8, 14.0):
        _door(bs.at(x, y0), x, y0 - 0.05, z, math.pi, w=1.0, h=2.1)
    _core_box(bs, floor, CORE_W, "snw")
    if floor == RD.TOP_FLOOR:
        _core_box(bs, floor, CORE_M, "sn")
        _core_box(bs, floor, CORE_ESC, "sne", faces="sen")
    else:
        _core_box(bs, floor, CORE_M, "sne")
    if floor in LOCKER_FLOORS:
        x0, x1, y, h = LOCKERS
        lifted(bs.at((x0 + x1) * 0.5, y), z, F.locker_bank, x0, x1, y, ang=math.pi, h=h)


def _west_wall(bs, floor):
    """西ロビーの西の外周壁（廊下の壁の間。西の階段室への口は扉を付けず、天井の高さまで開ける）と階段室。"""
    z0 = RD.FLOOR_Z[floor]
    top = floor == RD.TOP_FLOOR
    mb = bs.get("s", 0)
    SH.wall(_Walls(mb), ((D.X_W, RD.Y_CN), (D.X_W, RD.Y_CS)), z0, RD.next_z(floor), top=top,
            gaps=WS.wall_gaps(floor), gap_top=z0 + RD.CEIL_H)
    WS.build_floor(mb, floor)


def lamps(c, floor):
    """廊下・西ロビー・他の区画の天井の点光源の Empty（Unity は 1 つずつ吊る）と、プレビューの明かり。"""
    z = RD.FLOOR_Z[floor]
    pts = list(RD.LAMPS_CORRIDOR) + list(RD.LAMPS_EXTRA[floor])
    for n, (x, y) in enumerate(pts, 1):
        c._put("lamp_%s_%s_%d" % (c.spec.id, floor, n), x, y, z + RD.LAMP_DZ)
    c.lights_from(downlight_centers(), z + RD.CEIL_H, step=2)
    return len(pts)


def ev_empties(c):
    """1F〜7F の EV の乗り場 ev_lecture_<階> と、着いたときに向く先 ev_lecture_<階>_f（扉に背を向け、西を向く）。"""
    x, y = EV_POINT
    for i, z in enumerate(D.FL):
        c._put("ev_%s_%dF" % (c.spec.id, i + 1), x, y, z)
        c._put("ev_%s_%dF_f" % (c.spec.id, i + 1), x - 1.0, y, z)


def build(c):
    """2F〜7F の廊下・コア・西ロビー。戻り値は階 -> 置いた点光源の数（教室の明かりはその続きの番号）。"""
    count = {}
    for floor in RD.FLOORS:
        walls = bands(c, "wall", floor)
        _floors(c, floor)
        _ceilings(walls, floor)
        _corridor_walls(walls, floor)
        _core(walls, floor)
        _west_wall(walls, floor)
        if floor in UPPER:
            _east_end(walls, floor)
            _seev(c, floor)
        count[floor] = lamps(c, floor)
    return count
