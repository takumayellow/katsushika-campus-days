"""講義棟 2F〜7F（plan_lecture_west・upper・atrium・rooms・esc）: 教室の表・扉・通り道・床・明かり・EV。"""

import math
import re

import pytest

from kcd_interior import plan_lecture_atrium as AT
from kcd_interior import plan_lecture_dims as D
from kcd_interior import plan_lecture_esc as ESC
from kcd_interior import plan_lecture_props as P
from kcd_interior import plan_lecture_rooms as R
from kcd_interior import plan_lecture_rooms_dims as RD
from kcd_interior import plan_lecture_shell as SH
from kcd_interior import plan_lecture_signs as SG
from kcd_interior import plan_lecture_upper as UP
from kcd_interior import plan_lecture_west as W
from kcd_interior import plan_lecture_wstair as WS
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib.mesh import MeshBuilder

from conftest import CAMPUS_JSON

ROOM_NOS = (("201", "202", "203") + tuple("3%02d" % i for i in range(1, 12))
            + ("402", "410", "502", "509", "602", "610"))
UPPER = RD.FLOORS[2:]     # 4F〜7F
FRAME_CLEAR = 0.04        # 扉の枠から室の内法まで
WALK_HW = 0.35            # 歩く箱の半幅（CharacterController の半径 + 余裕）
WALK_Z = (0.2, 2.0)       # 床からの歩く箱の高さ
# 三角形の上限は、この PR の実測（west 18,782・upper 2,514・atrium 278・rooms 37,348）に 1 割強の余裕
CAPS = {"west": 20800, "upper": 2800, "atrium": 320, "rooms": 41000}
LAMP_REACH = 9.0          # 点光源の範囲（InteriorStage.AddLamp）
LAMPS_PER_PART = 4        # URP Forward の 1 つの物に効く点光源の上限
UPPER_PART = re.compile(r"(floor|wall|furn)_lecture_(\d+_)?"
                        r"(corr[2-7]f_\w+|r\d{3}(_\d+)?|seev[4-7]f|esc_\w+)|escsteps_lecture_\w+")


@pytest.fixture(scope="module")
def lspec():
    return ispec.build_specs(ispec.load_campus(CAMPUS_JSON))["lecture"]


def _build(c):
    """plan_lecture.build の 2F〜7F の分（看板は除く）。戻り値は階 -> 点光源の数。"""
    lamps = W.build(c)
    UP.build(c, SG.hang_roots())
    AT.build(c)
    counts = R.build(c, lamps)
    P.corridors(c)
    ESC.build(c)
    W.ev_empties(c)
    return counts


@pytest.fixture(scope="module")
def upper(lspec):
    c = Ctx(lspec)
    return c, _build(c)


def _faces_in(c, box):
    """box = (x0, x1, y0, y1, z0, z1) に外接矩形が食い込む面の数。"""
    x0, x1, y0, y1, z0, z1 = box
    n = 0
    for mb in c.builders():
        for f in mb.faces:
            pts = [mb.verts[i] for i in f]
            if (max(p[0] for p in pts) > x0 and min(p[0] for p in pts) < x1
                    and max(p[1] for p in pts) > y0 and min(p[1] for p in pts) < y1
                    and max(p[2] for p in pts) > z0 and min(p[2] for p in pts) < z1):
                n += 1
    return n


def _walk(fl, x0, x1, y0, y1):
    z = RD.FLOOR_Z[fl]
    return (x0, x1, y0, y1, z + WALK_Z[0], z + WALK_Z[1])


def test_rooms_are_the_twenty_with_interiors():
    assert sorted(r[0] for r in RD.ROOMS) == sorted(ROOM_NOS)
    assert all(r[1] == "%dF" % int(r[0][0]) for r in RD.ROOMS)
    assert [r[0] for r in RD.ROOMS if r[6] == "B"] == ["306"]
    assert [r[0] for r in RD.ROOMS if r[6] == "C"] == ["410", "509", "610"]
    assert [r[0] for r in RD.ROOMS if r[6] == "L"] == ["310", "311"]


def test_rooms_on_a_side_tile_west_to_east_without_overlap():
    every = list(RD.ROOMS) + [r + (None,) for r in RD.CLOSED]
    for fl in RD.FLOORS:
        for side in ("S", "N"):
            row = sorted((r for r in every if r[1] == fl and r[2] == side), key=lambda r: r[3])
            for a, b in zip(row, row[1:]):
                assert a[4] == pytest.approx(b[3]), (a[0], b[0])
            if row:
                assert row[0][3] == D.X_W
    for fl in UPPER:
        assert len([r for r in every if r[1] == fl]) == 11, fl


def test_door_frames_sit_inside_the_room_faces():
    reach = RD.DOOR_W * 0.5 + W.FRAME_OUT
    for room in list(RD.ROOMS) + [r + (None,) for r in RD.CLOSED]:
        xf0, xf1, _, _ = RD.room_faces(room)
        for d in room[5]:
            assert d - reach >= xf0 + FRAME_CLEAR - 1e-9, (room[0], d)
            assert d + reach <= xf1 - FRAME_CLEAR + 1e-9, (room[0], d)


def test_open_leaves_slide_clear_of_the_other_openings():
    hw = RD.DOOR_W * 0.5
    dx = RD.DOOR_W * RD.DOOR_OPEN
    for fl in RD.FLOORS:
        x1 = W.x_wall1(fl)
        for side in ("S", "N"):
            doors = W.room_doors(fl, side)
            for x in doors:
                s = 1.0 if W.slide_ang(x, doors, x1) == 0.0 else -1.0
                lo, hi = sorted((x + s * (dx - hw), x + s * (dx + hw)))
                assert W.X_WALL0 <= lo and hi <= x1, (fl, side, x)
                assert not [d for d in doors if d != x and lo < d + hw and hi > d - hw], (fl, x)
                assert not [g for g in W.halls(fl, side) if lo < g[1] and hi > g[0]], (fl, x)


def test_wall_pieces_never_cut_through_an_opening():
    hw = RD.DOOR_W * 0.5 + W.FRAME_OUT
    for fl in RD.FLOORS:
        for side in ("S", "N"):
            doors = W.room_doors(fl, side)
            holes = W.openings(doors, W.halls(fl, side))
            for _k, a, b in W.pieces(W.X_WALL0, W.x_wall1(fl), doors, W.halls(fl, side)):
                assert not [h for h in holes if h[0] < a < h[1] or h[0] < b < h[1]], (fl, a, b)
    assert all(d - hw > W.SEEV_GAP[1] or d + hw < W.SEEV_GAP[0]
               for fl in UPPER for d in W.room_doors(fl, "S"))


def test_each_room_is_reached_through_every_door(upper):
    """廊下から扉を入り、室の中の通路を次の扉まで歩いて、また廊下へ出られる。"""
    c, _ = upper
    blocked = []
    for room in RD.ROOMS:
        no, fl, side, _, _, doors, _ = room
        yc, yi = RD.corridor_y(side)
        lo_y, hi_y = sorted((yc, yi))
        for d in doors:
            if _faces_in(c, _walk(fl, d - WALK_HW, d + WALK_HW, lo_y, hi_y)):
                blocked.append((no, d))
        if _faces_in(c, _walk(fl, doors[0] - WALK_HW, doors[-1] + WALK_HW, yi - WALK_HW, yi + WALK_HW)):
            blocked.append((no, "aisle"))
    assert blocked == []


def test_corridors_are_open_from_the_west_lobby_to_the_east_end(upper):
    c, _ = upper
    blocked = []
    for fl in RD.FLOORS:
        for y in (21.0, 34.0):
            box = _walk(fl, RD.X_IN_W + 0.6, UP.x_east(fl) - 0.6, y - WALK_HW, y + WALK_HW)
            if _faces_in(c, box):
                blocked.append((fl, y))
    assert blocked == []


def test_south_east_ev_hall_is_open_from_the_corridor(upper):
    """4F〜7F: 廊下の南の壁の口から EV ホールの奥（動かない EV の扉の前）まで歩ける。"""
    c, _ = upper
    cx = (W.SEEV_GAP[0] + W.SEEV_GAP[1]) * 0.5
    blocked = [fl for fl in UPPER
               if _faces_in(c, _walk(fl, cx - WALK_HW, cx + WALK_HW, W.SEEV[1] + 1.4, 21.0))]
    assert blocked == []


def _parts(c, pattern):
    return [mb for mb in c.builders() if re.fullmatch(pattern, mb.name)]


def test_every_room_has_its_own_floor_wall_and_furniture(upper):
    c, _ = upper
    names = [mb.name for mb in c.builders()]
    assert len(names) == len(set(names))
    for room in RD.ROOMS:
        no, fl = room[0], room[1]
        floors = _parts(c, r"floor_lecture_r%s(_\d+)?" % no)
        vs = [v for mb in floors for v in mb.verts]
        bx0, by0, bx1, by1 = R.bounds(room)
        assert min(v[0] for v in vs) == pytest.approx(bx0) and max(v[0] for v in vs) == pytest.approx(bx1), no
        assert min(v[1] for v in vs) == pytest.approx(by0) and max(v[1] for v in vs) == pytest.approx(by1), no
        assert max(v[2] for v in vs) == pytest.approx(RD.FLOOR_Z[fl]), no
        assert [mb for mb in _parts(c, r"wall_lecture_r%s(_\d+)?" % no) if mb.faces], no
        assert [mb for mb in _parts(c, r"furn_lecture_\d+_r%s(_\d+)?" % no) if mb.faces], no


def _room_furn(c, no):
    return _parts(c, r"furn_lecture_\d+_r%s(_\d+)?" % no)


def _mat_faces(mbs, mat):
    return sum(1 for mb in mbs for f in range(len(mb.faces)) if mb.mat_names[mb.face_mat[f]] == mat)


def _mat_verts(mbs, mat):
    """材質 mat の面の頂点。"""
    return [mb.verts[i] for mb in mbs for f, face in enumerate(mb.faces)
            if mb.mat_names[mb.face_mat[f]] == mat for i in face]


def _mat_xs(mbs, mat):
    """材質 mat の面の頂点の X。"""
    return [v[0] for v in _mat_verts(mbs, mat)]


def _mid(vs, k):
    """頂点 vs の座標 k の幅の中央。"""
    return (min(v[k] for v in vs) + max(v[k] for v in vs)) * 0.5


def _row_fronts(xs, gap=0.5):
    """X の並びを gap より広い隙間で列に分け、各列の東の端を東から順に返す。"""
    xs = sorted(xs)
    return [a for a, b in zip(xs, xs[1:] + [math.inf]) if b - a > gap][::-1]


BOARDS = {"A": "lecture_board_green", "B": "board_white", "C": "board_white"}


def test_boards_hang_on_the_east_wall(upper):
    """型 A の黒板と型 B・C の白板は、東の壁（金町駅の側）に掛かる。"""
    c, _ = upper
    for room in RD.ROOMS:
        mat = BOARDS.get(room[6])
        if mat is None:
            continue
        _, xf1, _, _ = RD.room_faces(room)
        xs = _mat_xs(_room_furn(c, room[0]), mat)
        assert xs, room[0]
        assert xf1 - 0.2 <= min(xs) and max(xs) <= xf1, room[0]



def _board_empties(c):
    return dict((n, p) for n, p in c.empties if n.startswith("board_lecture_"))


def _front(boards, name):
    """板の面から _f への水平のベクトル。"""
    p, f = boards[name], boards[name + "_f"]
    assert f[2] == pytest.approx(p[2])
    return f[0] - p[0], f[1] - p[1]


def test_every_board_has_an_empty_on_its_face(upper):
    """黒板・白板のある室には、板の面の中央に board_lecture_<室> の Empty がある（Unity が板書を貼る）。"""
    c, _ = upper
    boards = _board_empties(c)
    for room in RD.ROOMS:
        name = "board_lecture_" + room[0]
        if room[6] not in BOARDS:
            assert name not in boards, room[0]
            continue
        _, xf1, yf0, yf1 = RD.room_faces(room)
        x, y, z = boards[name]
        cy = R.C_RING_Y if room[6] == "C" else (yf0 + yf1) * 0.5
        assert xf1 - 0.1 < x < xf1, room[0]
        assert y == pytest.approx(cy), room[0]
        assert z == pytest.approx(RD.FLOOR_Z[room[1]] + R.BOARD_Z), room[0]
        assert _front(boards, name) == pytest.approx((-1.0, 0.0)), room[0]   # 室の中（西）を向く


def test_hall_board_hangs_on_the_7f_corridor_wall(upper):
    """7F の廊下の南の壁に白板と board_lecture_7F がある。705 の扉と室名札に重ならない。"""
    c, _ = upper
    boards = _board_empties(c)
    x, y, z = boards["board_lecture_7F"]
    assert _front(boards, "board_lecture_7F") == pytest.approx((0.0, 1.0))   # 廊下（北）を向く
    assert x == pytest.approx(R.HALL_BOARD_X)
    assert D.Y_ROOM_S1 < y < D.Y_ROOM_S1 + 0.1
    assert z == pytest.approx(RD.FLOOR_Z["7F"] + R.BOARD_Z)
    hw = R.HALL_BOARD_W * 0.5 + 0.04
    room = next(r for r in RD.CLOSED if r[0] == "705")
    plate = RD.upper_plate_x(room + (None,))
    for lo, hi in [(d - 0.87, d + 0.87) for d in room[5]] + [(plate - 0.21, plate + 0.21)]:
        assert hi < x - hw or x + hw < lo, (lo, hi)
    zf = RD.FLOOR_Z["7F"]
    assert _faces_in(c, (x - 0.5, x + 0.5, D.Y_ROOM_S1 + 0.06, D.Y_ROOM_S1 + 0.08,
                         zf + 1.0, zf + 2.0))


def test_hall_board_leaves_the_corridor_walkable(upper):
    c, _ = upper
    x = R.HALL_BOARD_X
    assert _faces_in(c, _walk("7F", x - 1.5, x + 1.5, D.Y_ROOM_S1 + 0.3, D.Y_ROOM_S1 + 1.3)) == 0

def test_type_a_desks_face_the_east_board(upper):
    """型 A の最前列の天板の前の縁は東の壁から FRONT0 にあり、長椅子は机の西の側にある（席は東を向く）。"""
    c, _ = upper
    for room in RD.ROOMS:
        if room[6] != "A":
            continue
        _, xf1, _, _ = RD.room_faces(room)
        mbs = _room_furn(c, room[0])
        tops, seats = _mat_xs(mbs, "lecture_desk_ivory"), _mat_xs(mbs, "chair_grey")
        assert max(tops) == pytest.approx(xf1 - R.FRONT0), room[0]
        assert max(seats) < max(tops) and min(seats) < min(tops), room[0]


def test_type_a_rooms_have_rows_that_leave_a_back_aisle(upper):
    """型 A は 2 列以上あり、最後の列の長椅子の背から西の壁まで 1.4 m 以上の通路が残る。"""
    c, _ = upper
    for room in RD.ROOMS:
        if room[6] != "A":
            continue
        assert R.rows(room) >= 2, room[0]
        backs = _mat_xs(_room_furn(c, room[0]), "lecture_panel_dark")
        assert min(backs) - RD.room_faces(room)[0] >= 1.4, room[0]


def test_type_a_podium_projector_and_clock_follow_the_east_board(upper):
    """型 A の教卓は黒板と最前列の間、プロジェクターは天井の下でレンズを東へ向け、時計は西の壁で東を向く。"""
    c, _ = upper
    for room in RD.ROOMS:
        if room[6] != "A":
            continue
        no, z = room[0], RD.FLOOR_Z[room[1]]
        xf0, _, yf0, yf1 = RD.room_faces(room)
        mbs = _room_furn(c, no)
        podium = _mat_verts(mbs, "counter_wood")
        assert max(_mat_xs(mbs, "lecture_desk_ivory")) < min(v[0] for v in podium), no
        assert max(v[0] for v in podium) < min(_mat_xs(mbs, "lecture_board_green")), no
        assert _mid(podium, 1) == pytest.approx((yf0 + yf1) * 0.5), no
        west = xf0 + 0.5                     # これより西は西の壁の時計
        whites, blacks = _mat_verts(mbs, "plastic_white"), _mat_verts(mbs, "plastic_black")
        body = [v for v in whites if v[0] > west]
        lens = [v for v in blacks if v[0] > west and v[2] > z + 2.0]    # 教卓のマイクは z + 2.0 より下
        assert z + 2.0 < min(v[2] for v in body) and max(v[2] for v in body) <= z + RD.CEIL_H, no
        assert _mid(lens, 0) > _mid(body, 0), no
        clock = [v for v in whites if v[0] < west]
        hands = [v for v in blacks if v[0] < west]
        assert min(v[0] for v in clock) == pytest.approx(xf0), no
        assert min(v[0] for v in hands) >= max(v[0] for v in clock) - 1e-6, no


def test_type_b_pc_desks_face_the_east_board(upper):
    """306 の PC の机は東の壁から PC_FRONT0 を最前列に PC_PITCH おき、椅子は机の西の側（席は東を向く）、先生の机は白板と最前列の間。"""
    c, _ = upper
    room = next(r for r in RD.ROOMS if r[6] == "B")
    xf0, xf1, yf0, yf1 = RD.room_faces(room)
    mbs = _room_furn(c, room[0])
    panels, seats = _mat_xs(mbs, "lecture_panel_dark"), _mat_xs(mbs, "chair_grey")
    assert _row_fronts(panels) == pytest.approx([xf1 - R.PC_FRONT0 - k * R.PC_PITCH for k in range(R.PC_ROWS)])
    assert max(seats) < max(panels) and min(seats) < min(panels)
    assert min(seats) - xf0 >= 1.4
    teacher = [v for v in _mat_verts(mbs, "desk_white") if v[0] > max(panels) + 1e-6]
    assert teacher
    assert max(v[0] for v in teacher) < min(_mat_xs(mbs, "board_white"))
    assert _mid(teacher, 1) == pytest.approx((yf0 + yf1) * 0.5)


def test_lounges_have_sofas_to_sit_on_and_311_sells_bento(upper):
    """310・311 はラウンジ: 西の壁ぎわのソファに座れる。311 だけが弁当の保温ケースを置き、黒板は無い。"""
    c, _ = upper
    for no in ("310", "311"):
        mbs = _room_furn(c, no)
        assert sum(len(getattr(mb, "seats", ())) for mb in mbs) == len(R.SOFA_Y), no
        assert _mat_faces(mbs, "lecture_board_green") == 0, no
    assert _mat_faces(_room_furn(c, "311"), "stainless") > 0
    assert _mat_faces(_room_furn(c, "310"), "stainless") == 0


def test_lounge_furniture_stays_inside_the_room(upper):
    c, _ = upper
    for room in RD.ROOMS:
        if room[6] != "L":
            continue
        xf0, xf1, yf0, yf1 = RD.room_faces(room)
        vs = [v for mb in _room_furn(c, room[0]) for v in mb.verts]
        assert min(v[0] for v in vs) >= xf0 - 1e-6 and max(v[0] for v in vs) <= xf1 + 1e-6, room[0]
        assert min(v[1] for v in vs) >= yf0 and max(v[1] for v in vs) <= yf1, room[0]
        assert min(v[2] for v in vs) >= RD.FLOOR_Z[room[1]] - 1e-6, room[0]


def test_walls_are_lidded_where_no_room_is_built_above():
    # 上の階の同じ所に室を作らないと、壁の上端に立てて外周の検査に穴として出る（3F の 305・301 の上は閉じた室）
    lidded = {r[0] for r in RD.ROOMS if R._lidded(r)}
    assert {"305", "301"} <= lidded
    assert "402" not in lidded


def test_desks_stay_off_the_inner_aisles():
    s_end = max(R.DESK_Y["S"]) + R.DESK_W * 0.5
    n_start = min(R.DESK_Y["N"]) - R.DESK_W * 0.5
    assert s_end < RD.corridor_y("S")[1] - WALK_HW
    assert n_start > RD.corridor_y("N")[1] + WALK_HW


def test_lamps_are_numbered_without_gaps_per_floor(upper):
    c, counts = upper
    for fl in RD.FLOORS:
        nums = sorted(int(n.rsplit("_", 1)[1]) for n, _ in c.empties
                      if n.startswith("lamp_lecture_%s_" % fl))
        assert nums == list(range(1, counts[fl] + 1)), fl
        rooms = sum(len(RD.lamp_points(r)) for r in RD.ROOMS if r[1] == fl)
        assert counts[fl] == len(RD.LAMPS_CORRIDOR) + len(RD.LAMPS_EXTRA[fl]) + rooms


def test_lamps_hang_below_the_ceiling(upper):
    c, _ = upper
    for n, (_x, _y, z) in c.empties:
        if n.startswith("lamp_lecture_"):
            fl = n.split("_")[2]
            assert RD.FLOOR_Z[fl] + 2.0 < z < RD.FLOOR_Z[fl] + RD.CEIL_H, n


def test_ev_stops_on_every_floor_with_a_facing(upper):
    c, _ = upper
    ev = dict((n, p) for n, p in c.empties if n.startswith("ev_lecture_"))
    assert len(ev) == 2 * len(D.FL)
    x, y = W.EV_POINT
    for i, z in enumerate(D.FL):
        p, f = ev["ev_lecture_%dF" % (i + 1)], ev["ev_lecture_%dF_f" % (i + 1)]
        assert p == pytest.approx((x, y, z)) and f[2] == pytest.approx(z)
        assert math.hypot(f[0] - x, f[1] - y) == pytest.approx(1.0)
        assert f[0] < x                       # EV の扉（コアの西の面）に背を向けて西ロビーを向く


def test_ev_landings_are_clear_on_the_upper_floors(upper):
    c, _ = upper
    x, y = W.EV_POINT
    blocked = [fl for fl in RD.FLOORS
               if _faces_in(c, _walk(fl, x - 1.0 - WALK_HW, x + WALK_HW, y - WALK_HW, y + WALK_HW))]
    assert blocked == []


def test_ev_doors_face_west_toward_the_lobby():
    """EV 2 基の扉はコアの西の面（X -21.0）に並び、乗り場はその 1.4 m 西（ユーザーの実地の確認）。"""
    x, y = W.EV_POINT
    assert x == pytest.approx(D.CORE_EV[0] - 1.4)
    assert min(D.EV_DOORS_Y) - 1.26 > D.Y_CORE0 and max(D.EV_DOORS_Y) + 1.26 < D.Y_CORE1
    mb = MeshBuilder("ev")
    SH.ev_bank(mb)
    xs = [v[0] for v in mb.verts]
    assert min(xs) == pytest.approx(D.CORE_EV[0] - 0.10) and max(xs) <= D.CORE_EV[0] + 0.07


def _stair_path(fl):
    """fl から上の階までの西の階段の踏面 (x, y, 踏面の高さ) の並び（南の段・踊り場・北の段）。"""
    z0, h = RD.FLOOR_Z[fl], WS.rise(fl)
    ys = (WS.IY0 + WS.YM) * 0.5, (WS.YM + WS.IY1) * 0.5
    up = [(WS.X1 - (k - 0.5) * WS.RUN, ys[0], z0 + k * h) for k in range(1, WS.RISERS)]
    zm = z0 + WS.RISERS * h
    land = [((WS.IX0 + WS.XL) * 0.5, y, zm) for y in ys]
    back = [(WS.XL + (k - 0.5) * WS.RUN, ys[1], zm + k * h) for k in range(1, WS.RISERS)]
    return up + land + back


def test_west_stair_steps_stay_under_the_step_offset():
    """西の階段は 1 階ぶんを 24 段で上り、蹴上げは CharacterController の stepOffset 0.40 より低い。"""
    for fl in RD.FLOORS[:-1]:
        assert 2 * WS.RISERS * WS.rise(fl) == pytest.approx(RD.next_z(fl) - RD.FLOOR_Z[fl])
        assert WS.rise(fl) <= 0.2
    assert WS.IX0 < WS.XL - 1.2, "踊り場の奥行きが 1.2 m より狭い"


def test_west_stair_is_walkable_from_each_lobby_to_the_next(upper):
    """2F〜6F の西ロビーから南の段を上り、踊り場で折り返して北の段で上の階の西ロビーへ出られる（扉は無い）。"""
    c, _ = upper
    hw, head = 0.1, 1.7
    blocked = []
    for fl in RD.FLOORS[:-1]:
        for x, y, z in _stair_path(fl):
            box = (x - hw, x + hw, y - WALK_HW, y + WALK_HW, z + 0.05, z + head)
            if _faces_in(c, box):
                blocked.append((fl, round(x, 2), round(z, 2)))
    for fl in RD.FLOORS:
        for y0, y1 in WS.halves(fl):
            ym = (y0 + y1) * 0.5
            if _faces_in(c, _walk(fl, WS.X1 - 0.12, RD.X_IN_W + 1.0, ym - WALK_HW, ym + WALK_HW)):
                blocked.append((fl, "口", round(ym, 2)))
    assert blocked == []


def test_west_stair_top_floor_closes_the_south_half(upper):
    """7F は上る段が無いので、南の半分の口は西ロビーの外壁でふさぐ。2F の北の半分は 2F の床のまま。"""
    c, _ = upper
    ym = (WS.IY0 + WS.YM) * 0.5
    assert _faces_in(c, _walk("7F", D.X_W - 0.05, RD.X_IN_W + 0.05, ym - WALK_HW, ym + WALK_HW))
    assert WS.halves("7F") == (WS.halves("6F")[1],)
    y = (WS.YM + WS.IY1) * 0.5
    assert not _faces_in(c, _walk("2F", WS.XL + 0.3, WS.X1 - 0.3, y - WALK_HW, y + WALK_HW))


def test_west_stair_sits_inside_the_west_projection(upper, lspec):
    """階段室は外形の西の張り出し（X -33.18〜-28.6、Y 28.73〜33.62）の中に収まる。"""
    c, _ = upper
    vs = [v for mb in c.builders() for v in mb.verts if v[0] < D.X_W - 0.01]
    assert vs
    x0, y0, _x1, y1 = D.WEST_STAIR
    assert min(v[0] for v in vs) >= max(x0, lspec.x0) - 1e-6
    assert min(v[1] for v in vs) >= y0 - 1e-6 and max(v[1] for v in vs) <= y1 + 1e-6


@pytest.mark.parametrize("name", sorted(CAPS))
def test_upper_floors_fit_their_triangle_caps(lspec, name):
    c = Ctx(lspec)
    counts = W.build(c)
    before = c.tris()
    if name == "west":
        before = 0
    elif name == "upper":
        UP.build(c, SG.hang_roots())
    elif name == "atrium":
        AT.build(c)
    else:
        R.build(c, counts)
    assert 0 < c.tris() - before <= CAPS[name]


def test_upper_floors_stay_on_the_footprint(upper, lspec):
    c, _ = upper
    vs = [v for mb in c.builders() for v in mb.verts]
    tol = 0.01
    assert min(v[0] for v in vs) >= lspec.x0 - tol and max(v[0] for v in vs) <= lspec.x1 + tol
    assert min(v[1] for v in vs) >= lspec.y_face - tol and max(v[1] for v in vs) <= lspec.y_back + tol


def _lamps_near(mb, lamps):
    """mb の外接枠を XY で LAMP_REACH 広げた中にある、mb と同じ階の点光源の数（spec §1-13 の数え方）。"""
    xs, ys, zs = (sorted(v[i] for v in mb.verts) for i in range(3))
    n = 0
    for x, y, z in lamps:
        floor_z = z - RD.LAMP_DZ
        if not floor_z - 0.6 <= zs[0] < floor_z + RD.CEIL_H:
            continue
        dx = max(xs[0] - x, 0.0, x - xs[-1])
        dy = max(ys[0] - y, 0.0, y - ys[-1])
        n += dx <= LAMP_REACH and dy <= LAMP_REACH
    return n


def test_every_upper_part_sees_at_most_four_lamps(upper):
    """2F〜7F の廊下・コア・教室・EV ホール・エスカレータの部品は、同じ階の点光源が 4 つまでしか届かない。"""
    c, _ = upper
    lamps = [p for n, p in c.empties if n.startswith("lamp_lecture_")]
    parts = [mb for mb in c.builders() if mb.verts]
    assert all(UPPER_PART.fullmatch(mb.name) for mb in parts), \
        [mb.name for mb in parts if not UPPER_PART.fullmatch(mb.name)]
    over = [(mb.name, _lamps_near(mb, lamps)) for mb in parts]
    assert max(o[1] for o in over) > 0, "どの部品にも点光源が数えられていない"
    assert [o for o in over if o[1] > LAMPS_PER_PART] == []
