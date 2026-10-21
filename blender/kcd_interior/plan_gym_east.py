"""東棟: 廊下・体育事務室・更衣室とシャワー室の扉・サブアリーナ。"""

import math

from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S

H = D.C_EAST
WALL_X0, WALL_X1 = D.E_WALL
BEAM_W = 1.4                               # サブアリーナの梁の幅
BEAM_OFF = (-6.75, -2.25, 2.25, 6.75)      # 室の中心から梁の芯まで（梁に直交する向き）
DL_CENTERS = (-9.0, -4.5, 0.0, 4.5, 9.0)   # 梁の間の菱形の中心（梁と同じ座標）
DL_R = 0.15                                # ダウンライトの半径
DL_CLEAR = 1.0                             # ダウンライトの中心から梁の芯までの最小の距離


def _ex(y):
    """東壁（辺 4）の室内側の面の少し奥の X。仕切りを壁の中へ差し込む。"""
    return D.edge_point(D.E_WING_E, 1, y)[0] - 0.1


def _beam_strips(x0, y0, x1, y1, xc, yc):
    """45° と -45° に交差する梁の帯を室の矩形で切り、(向きの番号, 多角形) で返す。"""
    r = math.sqrt(0.5)
    out = []
    for fam, (d, n) in enumerate((((r, r), (-r, r)), ((-r, r), (-r, -r)))):
        for o in BEAM_OFF:
            pts = [(xc + n[0] * (o + w) + d[0] * s, yc + n[1] * (o + w) + d[1] * s)
                   for w, s in ((-BEAM_W / 2, -30.0), (-BEAM_W / 2, 30.0),
                                (BEAM_W / 2, 30.0), (BEAM_W / 2, -30.0))]
            poly = S._clip(pts, x0, y0, x1, y1)
            if len(poly) >= 3:
                out.append((fam, poly))
    return out


def _downlights(mb, x0, y0, x1, y1, xc, yc, z):
    """梁の間の天井に下を向く 8 角形のダウンライトを置く（菱形ごとに中心と十字の 4 か所。
    矩形 x0..x1, y0..y1 の外と、梁の芯から DL_CLEAR より近いものは置かない）。"""
    r = math.sqrt(0.5)
    for cu in DL_CENTERS:
        for cv in DL_CENTERS:
            for du, dv in ((0, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                u, v = cu + du, cv + dv
                if min(min(abs(u - o), abs(v - o)) for o in BEAM_OFF) < DL_CLEAR:
                    continue
                x, y = xc + r * (u - v), yc + r * (u + v)
                if not (x0 <= x <= x1 and y0 <= y <= y1):
                    continue
                mb.add_face([(x + DL_R * math.cos(t), y - DL_R * math.sin(t), z)
                             for t in (2 * math.pi * j / 8 for j in range(8))], "gym_light_linear")


def partitions(c):
    mb = c.wall
    # 廊下の東壁（事務室・サブアリーナの戸口を抜く）
    gaps = [(11.45, 12.55), D.SUB_DOORS[0], D.SUB_DOORS[1]]
    S.ywall(mb, WALL_X0, WALL_X1, 3.8, 41.4, 0.0, H, S.WHITE, gaps=gaps, lintel=D.DOOR_H)
    # 室の仕切り（事務室・更衣室・シャワー室・サブアリーナ）
    for y in (9.7, 14.3, 21.4, 38.8):
        S.xwall(mb, WALL_X1, _ex(y), y, y + 0.16, 0.0, H, S.WHITE)
    # 扉（閉じているもの）
    for y, _name in D.ROOM_DOORS:
        if _name == "office":
            continue
        kit.box(mb, WALL_X0 - 0.05, y - 0.5, 0.0, WALL_X0, y + 0.5, 2.1, "gym_door_dark")
    # 戸口の枠
    for g in (D.SUB_DOORS[0], D.SUB_DOORS[1], (11.45, 12.55)):
        for y in g:
            kit.box(mb, WALL_X0 - 0.03, y - 0.04, 0.0, WALL_X1 + 0.03, y + 0.04, D.DOOR_H, S.BLACK)
        kit.box(mb, WALL_X0 - 0.03, g[0], D.DOOR_H - 0.06, WALL_X1 + 0.03, g[1], D.DOOR_H, S.BLACK)


def corridor(c):
    mb = c.wall
    pts = shell.ceiling_lights(mb, 13.6, 5.0, 15.4, 40.0, H - 0.01, sx=2.0, sy=4.0, w=0.3, l=1.6,
                               mat="light_panel", limit=12)
    c.lights_from(pts, H, energy=200.0, step=2, radius=3.0)
    m = c.furn("corridor")
    shell.fire_extinguisher(m, 14.0, 5.0)
    kit.box(m, 13.5, 30.0, 0.0, 14.0, 31.0, 0.45, "gym_bench_wood")
    kit.box(m, 13.5, 33.0, 0.0, 14.0, 34.0, 0.45, "gym_bench_wood")


def office(c):
    mb = c.furn("office")
    x0, y0, x1, y1 = D.OFFICE
    # 受付カウンターと机
    kit.box(mb, x0 + 0.1, 11.0, 0.0, x0 + 0.7, 13.0, 1.05, "desk_wood")
    kit.box(mb, 18.0, 10.3, 0.0, 19.8, 11.1, 0.72, "desk_wood")
    kit.box(mb, 18.0, 12.8, 0.0, 19.8, 13.6, 0.72, "desk_wood")
    for x, y in ((18.9, 11.7), (18.9, 12.1)):
        kit.box(mb, x - 0.2, y - 0.2, 0.0, x + 0.2, y + 0.2, 0.45, "plastic_black")
    kit.box(mb, 24.0, 9.9, 0.0, 26.0, 10.4, 1.8, "metal_gray")
    kit.box(mb, 24.0, 13.8, 0.0, 26.0, 14.2, 1.2, "metal_gray")
    kit.box(mb, 22.0, 9.9, 1.2, 23.4, 9.95, 2.0, "paper_white")


def sub_arena(c):
    mb = c.wall
    x0, y0, x1, y1 = D.SUB
    xc, yc = (x0 + x1) / 2, (y0 + y1) / 2
    # 斜めに交差する下がり梁（下端は床の上 3.2 m。交差で底面が重ならないよう向きごとに 5 mm ずらす）。
    # 南は仕切りの室内側の面で止め、東は外周壁の中まで通す
    for fam, poly in _beam_strips(x0, y0 + 0.16, x1 + 0.3, y1, xc, yc):
        mb.add_prism(poly, D.C_BEAM + 0.005 * fam, H, "gym_beam_beige", None, "gym_beam_beige")
    # 梁の間の天井のダウンライトと、菱形の中心の点光源 3 個
    _downlights(mb, x0 + 0.4, y0 + 0.4, x1 - 0.4, y1 - 0.4, xc, yc, H - 0.005)
    c.lights_from([(xc, yc + k * 4.5 * math.sqrt(2)) for k in (-1, 0, 1)], D.C_BEAM,
                  energy=300.0, step=1, radius=3.5)
    # 壁の羽目板（横張りの木）: 西・南・北・東の内面に天井の近くまで張り、西は扉の上にも続く。
    # 西は南の仕切りの室内側の面から始める
    wood = "gym_wall_wood_horizontal"
    zt = H - 0.1
    ys = y0 + 0.16
    for g0, g1 in ((ys, D.SUB_DOORS[0][0]), (D.SUB_DOORS[0][1], D.SUB_DOORS[1][0]),
                   (D.SUB_DOORS[1][1], y1)):
        kit.box(mb, x0, g0, 0.0, x0 + 0.05, g1, D.DOOR_H, wood)
    kit.box(mb, x0, ys, D.DOOR_H, x0 + 0.05, y1, zt, wood)
    # 南と北は西の板の面から外周壁の中まで通す
    S.xwall(mb, x0 + 0.05, _ex(y0), ys, ys + 0.04, 0.0, zt, wood)
    S.xwall(mb, x0 + 0.05, _ex(y1), y1 - 0.04, y1, 0.0, zt, wood)
    # 東は外周壁の室内側の面より 2 cm 手前に張る
    a, b = D.edge_span(D.E_WING_E, 1, y0, y1)
    n = S._normal(a, b)
    off = shell.WALL + 0.02
    pa = (a[0] + n[0] * off, a[1] + n[1] * off)
    pb = (b[0] + n[0] * off, b[1] + n[1] * off)
    kit.vplate(mb, pa, pb, 0.0, zt, wood, flip=True)
    # 床の吊りゴールと用具
    m = c.furn("sub")
    kit.box(m, 26.4, 36.0, 0.0, 27.2, 37.0, 0.3, "gym_mat_dark")
    kit.box(m, 16.2, 22.0, 0.0, 16.9, 23.4, 0.9, "gym_cart_blue")
    kit.box(m, 16.2, 24.0, 0.0, 16.9, 25.4, 0.9, "gym_cart_blue")
    shell.fire_extinguisher(m, 26.8, 22.5)


def build(c):
    partitions(c)
    corridor(c)
    office(c)
    sub_arena(c)
