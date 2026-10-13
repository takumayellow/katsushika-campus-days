"""東棟: 廊下・体育事務室・更衣室とシャワー室の扉・サブアリーナ。"""

from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S

H = D.C_EAST
WALL_X0, WALL_X1 = D.E_WALL


def _ex(y):
    """東壁（辺 4）の室内側の面の少し奥の X。仕切りを壁の中へ差し込む。"""
    return D.edge_point(D.E_WING_E, 1, y)[0] - 0.1


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
    # 天井の梁（床の上 3.2 m）と照明
    for k in range(5):
        y = y0 + 1.5 + k * 3.5
        kit.box(mb, x0, y, D.C_BEAM, x1 + 0.3, y + 0.3, H, "gym_beam_beige")
    pts = shell.ceiling_lights(mb, x0 + 1.0, y0 + 1.0, x1 - 0.5, y1 - 1.0, D.C_BEAM - 0.02,
                               sx=3.5, sy=3.5, w=0.2, l=1.6, mat="gym_light_linear", limit=20)
    c.lights_from(pts, D.C_BEAM, energy=300.0, step=3, radius=3.5)
    # 壁の羽目板（横張りの木）: 西・南・北・東の内面
    wood = "gym_wall_wood_horizontal"
    for g0, g1 in ((y0, D.SUB_DOORS[0][0]), (D.SUB_DOORS[0][1], D.SUB_DOORS[1][0]),
                   (D.SUB_DOORS[1][1], y1)):
        kit.box(mb, x0, g0, 0.0, x0 + 0.05, g1, 2.4, wood)
    S.xwall(mb, x0 + 0.16, _ex(y0), y0 + 0.16, y0 + 0.2, 0.0, 2.4, wood)
    S.xwall(mb, x0 + 0.16, _ex(y1), y1 - 0.04, y1, 0.0, 2.4, wood)
    a, b = D.edge_span(D.E_WING_E, 1, y0, y1)
    n = S._normal(a, b)
    pa = (a[0] + n[0] * shell.WALL, a[1] + n[1] * shell.WALL)
    pb = (b[0] + n[0] * shell.WALL, b[1] + n[1] * shell.WALL)
    kit.vplate(mb, pa, pb, 0.0, 2.4, wood, flip=True)
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
