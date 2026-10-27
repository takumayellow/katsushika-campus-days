"""講義棟 2F〜7F の小物（#279）: 人が使っている建物に見せるための掲示・防災設備・仕上げ。

廊下（corridors）:
    南北の廊下の壁の掲示板（コルクとポスター）・消火器・消火栓、壁の足元の巾木、
    中を作らない室の閉じた扉の窓、コアの南の面のゴミ箱・北の面のベンチ、西ロビーと南東の階段の誘導灯、
    天井の空調の吹き出し口。plan_lecture_west の廊下の部品（帯 s / n と区画 k）へ積む。
教室（room）:
    窓のブラインド、型 A・B の西の壁（後ろ）の掲示板、型 A の教卓のチョーク箱と黒板消し・
    黒板の上の巻き上げスクリーンの箱、天井の空調の吹き出し口。

Unity の MaterialLibrary は内装に画像のテクスチャを持たないので、掲示物は色違いの紙の板で見せる。
空けておく所（ストーリーの黒板の場面 #389）:
    型 A・B・C の黒板・白板の面とその手前 1 m（床から 2.4 m まで）、黒板の上の壁、
    7F の廊下の南の壁の X -23.6〜-21.2（RESERVED_7F）。
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_west as W
from . import plan_lecture_wstair as WS

CORK = "lecture_cork"
POSTERS = ("lecture_poster_red", "lecture_poster_blue", "lecture_poster_yellow",
           "lecture_poster_green")
PAPER = "paper_white"
SKIRTING = "lecture_skirting"
BLIND = "lecture_blind"
DIFFUSER = "lecture_diffuser"
VENT = "lecture_vent_dark"
DOOR_GLASS = "lecture_door_glass"
METAL = "metal_gray"
RED = "fire_red"
EXIT = "sign_exit_green"
BENCH_TOP = "desk_wood"
ERASER = "lecture_seat_navy"

RESERVED_7F = (-24.1, -20.7)          # 7F の廊下の南の壁（ストーリー担当の場面。-23.6〜-21.2 に 0.5 m の余白を足す）
WIDTH = {"board": 1.5, "ext": 0.35, "hydrant": 0.8}
WANT = {"S": (("board", -12.0), ("ext", -24.0), ("board", 4.0), ("ext", 20.0)),
        "N": (("board", -10.0), ("hydrant", -24.0), ("ext", 2.0), ("board", 12.0))}
CLEAR = 0.3                           # 扉の枠・札・他の小物から空ける幅
SKIRT_H = 0.08
SKIRT_OUT = 0.003                     # 壁の面から巾木の板まで
DIFFUSER_X = (-22.5, -10.5, 1.5, 13.5)   # 廊下の吹き出し口（ダウンライトの間）
DIFFUSER_Y = (21.65, 33.3)
BINS_X = (-19.9, -19.5, -19.1)        # コアの南の面のゴミ箱（缶・ペットボトル・燃えるごみ）
BIN_Y = (D.Y_CORE0 - 0.32, D.Y_CORE0 - 0.02)
BENCH_X, BENCH_W = -11.0, 1.8         # コアの北の面のベンチ
BENCH_Y = (D.Y_CORE1 + 0.05, D.Y_CORE1 + 0.45)
SILL = 0.95                           # 窓の下端（床から。plan_lecture_rooms._outer の SH.wall と同じ）
BLIND_W = 2.2                         # ブラインド 1 枚の幅の目安
BLIND_OUT = 0.08                      # 窓の内面からブラインドまで
BLIND_DROP = (0.35, 1.5, 0.8, 1.2, 0.5, 1.45)   # 下げた量（窓の上端から）を順に回す
BACK_BOARD_DY = -3.0                  # 後ろの掲示板の中心（室の奥行きの中央から。時計を避ける）
FACE_E, FACE_W = -math.pi * 0.5, math.pi * 0.5


# ---- 廊下の壁の空き ----
def _face_signs(floor, side):
    """floor の階の side の廊下の面に貼った札 (x, 幅)。"""
    y, _ = W.FACE[side]
    z = RD.FLOOR_Z[floor]
    out = [(r[5], r[3]) for r in D.SIGNS + RD.SIGNS
           if r[0] == "wall" and abs(r[6] - y) < 0.05 and z <= r[7] < z + RD.CEIL_H]
    if floor == RD.TOP_FLOOR:
        out += [(RD.upper_plate_x(r), 0.42) for r in RD.CLOSED if r[1] == floor and r[2] == side]
    if floor == "2F" and side == "N":
        out += [(x, 0.42) for x in RD.PLATES_2F_N]
    return out


def _closed_doors(floor, side):
    return [x for r in RD.CLOSED if r[1] == floor and r[2] == side for x in r[5]]


def _small_doors(floor, side):
    """幅 1.0 の閉じた扉（2F の北の室・4F〜7F の南東の階段）。"""
    if floor == "2F" and side == "N":
        return list(RD.DOORS_2F_N)
    if floor in W.UPPER and side == "S":
        return [W.STAIR_DOOR_X]
    return []


def _openings(floor, side):
    """壁の開口と扉の枠 (西端, 東端)。巾木はここを抜く。"""
    big = RD.DOOR_W * 0.5 + W.FRAME_OUT
    small = 0.5 + W.FRAME_OUT
    out = [(d - big, d + big) for d in W.room_doors(floor, side) + _closed_doors(floor, side)]
    out += [(d - small, d + small) for d in _small_doors(floor, side)]
    return sorted(out + list(W.halls(floor, side)))


def blocked(floor, side):
    """廊下の壁のうち小物を置かない区間 (lo, hi): 扉の開口と枠・札・EV ホールの口・7F の予約。"""
    out = [(lo - CLEAR, hi + CLEAR) for lo, hi in _openings(floor, side)]
    out += [(x - w * 0.5 - 0.25, x + w * 0.5 + 0.25) for x, w in _face_signs(floor, side)]
    if floor == RD.TOP_FLOOR and side == "S":
        out.append(RESERVED_7F)
    return sorted(out)


def _subtract(spans, cuts):
    for lo, hi in cuts:
        nxt = []
        for a, b in spans:
            if hi <= a or lo >= b:
                nxt.append((a, b))
                continue
            if lo > a:
                nxt.append((a, lo))
            if hi < b:
                nxt.append((hi, b))
        spans = nxt
    return spans


def _wall_x(floor):
    """廊下の壁の面の西端・東端（西の外周壁の内面から、東の壁の内面まで）。"""
    return RD.X_IN_W, W.x_wall1(floor) - shell.PART * 0.5


def free_spans(floor, side):
    """小物を置ける区間 (lo, hi) の並び（西から順）。"""
    x0, x1 = _wall_x(floor)
    return _subtract([(x0 + CLEAR, x1 - CLEAR)], blocked(floor, side))


def wall_items(floor, side):
    """廊下の壁に置く (種類, 中心 X)。WANT の X に一番近い空きへ順に置く。"""
    spans = free_spans(floor, side)
    out = []
    for kind, want in WANT[side]:
        hw = WIDTH[kind] * 0.5
        best = None
        for a, b in spans:
            if b - a < hw * 2:
                continue
            x = min(max(want, a + hw), b - hw)
            if best is None or abs(x - want) < abs(best - want):
                best = x
        if best is None:
            continue
        out.append((kind, best))
        spans = _subtract(spans, [(best - hw - CLEAR, best + hw + CLEAR)])
    return out


# ---- 形（ローカル +y が廊下の側、z は床から） ----
def board(mb, t, seed=0):
    """コルクの掲示板と、色の帯の付いたポスター 3 枚。"""
    t.box_nb(mb, -0.75, 0.0, 1.0, 0.75, 0.025, 1.9, CORK)
    for k, (cx, w, z0, z1) in enumerate(((-0.45, 0.36, 1.12, 1.62), (0.0, 0.40, 1.30, 1.80),
                                         (0.45, 0.32, 1.08, 1.52))):
        color = POSTERS[(seed + k) % len(POSTERS)]
        t.vplate(mb, cx + w * 0.5, 0.027, cx - w * 0.5, 0.027, z0, z1, PAPER)
        t.vplate(mb, cx + w * 0.5, 0.028, cx - w * 0.5, 0.028, z1 - (z1 - z0) * 0.35, z1 - 0.02,
                 color)


def extinguisher(mb, t):
    """床に置いた消火器と、壁の赤い表示の板。"""
    t.box_nb(mb, -0.08, 0.02, 0.0, 0.08, 0.17, 0.55, RED)
    t.box_nb(mb, -0.03, 0.07, 0.55, 0.03, 0.12, 0.63, METAL)
    t.vplate(mb, 0.12, 0.003, -0.12, 0.003, 1.55, 1.75, RED)


def hydrant(mb, t):
    """壁付けの消火栓の箱、扉の赤い札と上の赤い灯。"""
    t.box_nb(mb, -0.4, 0.0, 0.3, 0.4, 0.1, 1.5, METAL)
    t.vplate(mb, 0.3, 0.101, -0.3, 0.101, 1.1, 1.35, RED)
    t.box_nb(mb, -0.05, 0.0, 1.55, 0.05, 0.05, 1.65, RED)


SHAPES = {"board": board, "ext": extinguisher, "hydrant": hydrant}


def _wall_props(bs, floor):
    z = RD.FLOOR_Z[floor]
    for n, side in enumerate(("S", "N")):
        y, ang = W.FACE[side]
        for k, (kind, x) in enumerate(wall_items(floor, side)):
            t = kit.T(x, y, z, ang)
            mb = bs.at(x, y)
            if kind == "board":
                board(mb, t, RD.FLOORS.index(floor) + n * 2 + k)
            else:
                SHAPES[kind](mb, t)


def _skirting(bs, floor):
    """廊下の壁の足元の巾木（開口と扉の枠を抜き、区画の境で切る）。"""
    z = RD.FLOOR_Z[floor]
    x0, x1 = _wall_x(floor)
    for side in ("S", "N"):
        y, _ = W.FACE[side]
        yo = y + SKIRT_OUT if side == "S" else y - SKIRT_OUT
        cuts = [(b, b) for b in W.SPLIT_X]
        for a, b in _subtract([(x0, x1)], _openings(floor, side) + cuts):
            if b - a < 0.05:
                continue
            p, q = ((b, yo), (a, yo)) if side == "S" else ((a, yo), (b, yo))
            kit.vplate(bs.at((a + b) * 0.5, y), p, q, z, z + SKIRT_H, SKIRTING)


def _door_windows(bs, floor):
    """中を作らない室の閉じた扉の、取っ手と反対の側の縦長の窓（廊下の側の面）。"""
    z = RD.FLOOR_Z[floor]
    hw = RD.DOOR_W * 0.5
    for room in RD.CLOSED:
        if room[1] != floor:
            continue
        y, ang = W.FACE[room[2]]
        for x in room[5]:
            t = kit.T(x, y, z, ang)
            t.vplate(bs.at(x, y), -hw + 0.45, 0.026, -hw + 0.2, 0.026, 1.1, 1.9, DOOR_GLASS)


def _core_props(bs, floor):
    """コアの南の面のゴミ箱 3 つと、北の面のベンチ。"""
    z = RD.FLOOR_Z[floor]
    y0, y1 = BIN_Y
    for k, x in enumerate(BINS_X):
        mb = bs.at(x, y0)
        kit.box_nb(mb, x - 0.15, y0, z, x + 0.15, y1, z + 0.7, METAL)
        kit.plate(mb, x - 0.12, y0 + 0.03, x + 0.12, y1 - 0.03, z + 0.701, POSTERS[(k + 1) % 4])
    b0, b1 = BENCH_Y
    mb = bs.at(BENCH_X, b0)
    hw = BENCH_W * 0.5
    kit.box_nb(mb, BENCH_X - hw, b0, z + 0.40, BENCH_X + hw, b1, z + 0.44, BENCH_TOP)
    for dx in (-hw + 0.15, hw - 0.15):
        kit.box_nb(mb, BENCH_X + dx - 0.03, b0 + 0.05, z, BENCH_X + dx + 0.03, b1 - 0.05, z + 0.40,
                   METAL)


def _exit_signs(bs, floor):
    """西ロビーの階段の口の上と、4F〜7F の南東の階段の扉の上の誘導灯。"""
    z = RD.FLOOR_Z[floor]
    ys = WS.halves(floor)
    y = (ys[0][0] + ys[-1][1]) * 0.5
    x = RD.X_IN_W + 0.4
    shell.exit_sign(bs.at(x, y), x, y, z + RD.CEIL_H - 0.26, ang=FACE_E)
    if floor in W.UPPER:
        y = W.FACE["S"][0]
        x = W.STAIR_DOOR_X
        kit.box_nb(bs.at(x, y), x - 0.2, y, z + 2.25, x + 0.2, y + 0.03, z + 2.41, EXIT)


def ceiling_vent(mb, x, y, zc):
    """天井の空調の吹き出し口（明るい枠と暗い格子。下向きの板）。"""
    kit.plate(mb, x - 0.25, y - 0.25, x + 0.25, y + 0.25, zc - 0.006, DIFFUSER, flip=True)
    kit.plate(mb, x - 0.15, y - 0.15, x + 0.15, y + 0.15, zc - 0.008, VENT, flip=True)


def corridors(c):
    """2F〜7F の廊下の小物。plan_lecture_west.build の後に呼ぶ（同じ区画の部品へ積む）。"""
    for floor in RD.FLOORS:
        bs = W.bands(c, "wall", floor)
        _wall_props(bs, floor)
        _skirting(bs, floor)
        _door_windows(bs, floor)
        _core_props(bs, floor)
        _exit_signs(bs, floor)
        zc = RD.FLOOR_Z[floor] + RD.CEIL_H
        for x in DIFFUSER_X:
            for y in DIFFUSER_Y:
                ceiling_vent(bs.at(x, y), x, y, zc)


# ---- 教室 ----
def _window_top(floor):
    """窓の上端（床から）。plan_lecture_rooms._outer の垂れ壁と同じ。"""
    header = 0.6 if floor == RD.TOP_FLOOR else 1.4
    return min(RD.CEIL_H - 0.03, RD.next_z(floor) - RD.FLOOR_Z[floor] - header)


def _blinds(walls, room, z):
    """窓の帯に沿うブラインド。約 BLIND_W ごとの板で、下げた量を変える。上に金属の箱の帯。"""
    xf0, xf1, _, _ = RD.room_faces(room)
    a, b = xf0 + 0.1, xf1 - 0.1
    top = _window_top(room[1])
    n = max(1, int(round((b - a) / BLIND_W)))
    off = shell.WALL + BLIND_OUT
    if room[2] == "S":
        def at(x):
            return (x, D.Y_S + off)
        order = (b, a)              # +X から -X へ引くと室の側（+Y）を向く
    else:
        (xa, ya), (xb, yb) = D.N_WALL
        L = math.hypot(xb - xa, yb - ya)
        nx, ny = (yb - ya) / L, -(xb - xa) / L

        def at(x):
            return (x + nx * off, D.north_y(x) + ny * off)
        order = (a, b)              # -X から +X へ引くと室の側（-Y）を向く
    seed = RD.ROOMS.index(room)
    step = (order[1] - order[0]) / n
    for k in range(n):
        p, q = at(order[0] + step * k + math.copysign(0.02, step)), at(order[0] + step * (k + 1))
        drop = min(BLIND_DROP[(seed + k) % len(BLIND_DROP)], top - SILL)
        kit.vplate(walls, p, q, z + top - drop, z + top, BLIND)
    kit.vplate(walls, at(order[0]), at(order[1]), z + top, z + top + 0.08, METAL)


def _back_board(walls, room, z):
    """後ろ（西の壁）の掲示板。時計（中央）を避けて奥行きの中央から BACK_BOARD_DY。"""
    xf0, _, yf0, yf1 = RD.room_faces(room)
    by = (yf0 + yf1) * 0.5 + BACK_BOARD_DY
    board(walls, kit.T(xf0, by, z, FACE_E), RD.ROOMS.index(room))


def _podium_props(furn, room, z):
    """型 A: 教卓の上のチョーク箱と黒板消し、黒板の上の天井際の巻き上げスクリーンの箱。"""
    _, xf1, yf0, yf1 = RD.room_faces(room)
    cy = (yf0 + yf1) * 0.5
    t = kit.T(xf1 - 1.3, cy, z, FACE_W)
    t.box_nb(furn, -0.50, -0.08, 1.12, -0.36, 0.0, 1.17, PAPER)
    t.box_nb(furn, -0.34, -0.02, 1.12, -0.22, 0.06, 1.15, ERASER)
    zc = z + RD.CEIL_H
    kit.box_nb(furn, xf1 - 0.30, cy - 1.9, zc - 0.14, xf1 - 0.12, cy + 1.9, zc - 0.01, METAL)


def _room_vents(walls, room, z):
    """天井の吹き出し口。照明の板の格子の間に、投影機から 0.8 m より離して置く。"""
    from .plan_lecture_rooms import _grid       # plan_lecture_rooms がこのモジュールを読むので関数の中で読む
    xf0, xf1, yf0, yf1 = RD.room_faces(room)
    xs, ys = _grid(xf0, xf1), _grid(yf0, yf1)
    mx = [(p + q) * 0.5 for p, q in zip(xs[:-1], xs[1:])][::2] or [(xf0 + xf1) * 0.5]
    my = [(p + q) * 0.5 for p, q in zip(ys[:-1], ys[1:])]
    my = my[1::2] or my
    proj = (xf1 - 0.4 * (xf1 - xf0), (yf0 + yf1) * 0.5)
    for x in mx:
        for y in my:
            if room[6] == "A" and math.hypot(x - proj[0], y - proj[1]) < 0.8:
                continue
            ceiling_vent(walls, x, y, z + RD.CEIL_H)


def room(furn, walls, room_, z):
    """室の小物。plan_lecture_rooms.build が家具の後に、仮の mb（furn / walls）へ積む。"""
    _blinds(walls, room_, z)
    if room_[6] in ("A", "B"):
        _back_board(walls, room_, z)
    if room_[6] == "A":
        _podium_props(furn, room_, z)
    _room_vents(walls, room_, z)
