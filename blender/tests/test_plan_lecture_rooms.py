"""講義棟 2F・3F（plan_lecture_west・plan_lecture_rooms）: 教室の表・扉・通り道・床・明かり。"""

import re

import pytest

from kcd_interior import plan_lecture_dims as D
from kcd_interior import plan_lecture_rooms as R
from kcd_interior import plan_lecture_rooms_dims as RD
from kcd_interior import plan_lecture_west as W
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx

from conftest import CAMPUS_JSON

ROOM_NOS = ("201", "202", "203") + tuple("3%02d" % i for i in range(1, 12))
FRAME_CLEAR = 0.04        # 扉の枠から室の内法まで
WALK_HW = 0.35            # 歩く箱の半幅（CharacterController の半径 + 余裕）
WALK_Z = (0.2, 2.0)       # 床からの歩く箱の高さ
CAPS = {"west": 5000, "rooms": 27000}     # 現在の値に 1 割強の余裕
LAMP_REACH = 9.0          # 点光源の範囲（InteriorStage.AddLamp）
LAMPS_PER_PART = 4        # URP Forward の 1 つの物に効く点光源の上限


@pytest.fixture(scope="module")
def lspec():
    return ispec.build_specs(ispec.load_campus(CAMPUS_JSON))["lecture"]


@pytest.fixture(scope="module")
def upper(lspec):
    c = Ctx(lspec)
    counts = R.build(c, W.build(c))
    return c, counts


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


def test_rooms_are_the_fourteen_of_2f_and_3f():
    assert sorted(r[0] for r in RD.ROOMS) == sorted(ROOM_NOS)
    assert all(r[1] == "%dF" % int(r[0][0]) for r in RD.ROOMS)
    assert [r[0] for r in RD.ROOMS if r[6] == "B"] == ["306"]


def test_rooms_on_a_side_tile_west_to_east_without_overlap():
    for fl in ("2F", "3F"):
        for side in ("S", "N"):
            row = [r for r in RD.ROOMS if r[1] == fl and r[2] == side]
            for a, b in zip(row, row[1:]):
                assert a[4] == pytest.approx(b[3]), (a[0], b[0])
            if row:
                assert row[0][3] == D.X_W


def test_door_frames_sit_inside_the_room_faces():
    reach = RD.DOOR_W * 0.5 + W.FRAME_OUT
    for room in RD.ROOMS:
        xf0, xf1, _, _ = RD.room_faces(room)
        for d in room[5]:
            assert d - reach >= xf0 + FRAME_CLEAR - 1e-9, (room[0], d)
            assert d + reach <= xf1 - FRAME_CLEAR + 1e-9, (room[0], d)


def test_open_leaves_slide_clear_of_the_other_openings():
    hw = RD.DOOR_W * 0.5
    dx = RD.DOOR_W * RD.DOOR_OPEN
    for fl in ("2F", "3F"):
        for side in ("S", "N"):
            doors = W.room_doors(fl, side)
            for x in doors:
                s = 1.0 if W.slide_ang(x, doors) == 0.0 else -1.0
                lo, hi = sorted((x + s * (dx - hw), x + s * (dx + hw)))
                assert W.X_WALL0 <= lo and hi <= W.X_WALL1, (fl, side, x)
                assert not [d for d in doors if d != x and lo < d + hw and hi > d - hw], (fl, x)


def test_wall_pieces_never_cut_through_an_opening():
    hw = RD.DOOR_W * 0.5 + W.FRAME_OUT
    for fl in ("2F", "3F"):
        for side in ("S", "N"):
            doors = W.room_doors(fl, side)
            for _k, a, b in W.pieces(W.X_WALL0, W.X_WALL1, doors):
                assert not [d for d in doors if d - hw < a < d + hw or d - hw < b < d + hw]


def test_each_room_is_reached_through_every_door(upper):
    """廊下から扉を入り、室の中の通路を次の扉まで歩いて、また廊下へ出られる。"""
    c, _ = upper
    blocked = []
    for room in RD.ROOMS:
        no, fl, side, _, _, doors, _ = room
        z = RD.FLOOR_Z[fl]
        yc, yi = RD.corridor_y(side)
        za, zb = z + WALK_Z[0], z + WALK_Z[1]
        lo_y, hi_y = sorted((yc, yi))
        for d in doors:
            if _faces_in(c, (d - WALK_HW, d + WALK_HW, lo_y, hi_y, za, zb)):
                blocked.append((no, d))
        aisle = (doors[0] - WALK_HW, doors[-1] + WALK_HW, yi - WALK_HW, yi + WALK_HW, za, zb)
        if _faces_in(c, aisle):
            blocked.append((no, "aisle"))
    assert blocked == []


def test_corridors_are_open_from_the_west_lobby_to_the_east(upper):
    c, _ = upper
    blocked = []
    for fl in ("2F", "3F"):
        z = RD.FLOOR_Z[fl]
        for y in (21.0, 34.0):
            box = (RD.X_IN_W + 0.6, W.X_CORR_E, y - WALK_HW, y + WALK_HW, z + WALK_Z[0], z + WALK_Z[1])
            if _faces_in(c, box):
                blocked.append((fl, y))
    assert blocked == []


def _part(c, name):
    found = [mb for mb in c.builders() if mb.name == name]
    assert len(found) == 1, name
    return found[0]


def test_every_room_has_its_own_floor_wall_and_furniture(upper):
    c, _ = upper
    names = [mb.name for mb in c.builders()]
    assert len(names) == len(set(names))
    for room in RD.ROOMS:
        no, fl = room[0], room[1]
        floor = _part(c, "floor_lecture_r" + no)
        bx0, by0, bx1, by1 = R.bounds(room)
        xs, ys = [v[0] for v in floor.verts], [v[1] for v in floor.verts]
        assert min(xs) == pytest.approx(bx0) and max(xs) == pytest.approx(bx1), no
        assert min(ys) == pytest.approx(by0) and max(ys) == pytest.approx(by1), no
        assert max(v[2] for v in floor.verts) == pytest.approx(RD.FLOOR_Z[fl]), no
        assert _part(c, "wall_lecture_r" + no).faces, no
        furn = [mb for mb in c.builders() if re.fullmatch(r"furn_lecture_\d+_r" + no, mb.name)]
        assert len(furn) == 1 and furn[0].faces, no


def test_type_a_rooms_have_rows_that_leave_a_back_aisle():
    for room in RD.ROOMS:
        if room[6] != "A":
            continue
        xf0, xf1, _, _ = RD.room_faces(room)
        n = R.rows(room)
        assert n >= 2, room[0]
        back = xf0 + R.FRONT0 + R.DESK_DEPTH + (n - 1) * R.ROW_PITCH + 0.52
        assert xf1 - back >= 1.4, room[0]


def test_desks_stay_off_the_inner_aisles():
    s_end = max(R.DESK_Y["S"]) + R.DESK_W * 0.5
    n_start = min(R.DESK_Y["N"]) - R.DESK_W * 0.5
    assert s_end < RD.corridor_y("S")[1] - WALK_HW
    assert n_start > RD.corridor_y("N")[1] + WALK_HW


def test_lamps_are_numbered_without_gaps_per_floor(upper):
    c, counts = upper
    for fl in ("2F", "3F"):
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


@pytest.mark.parametrize("name", sorted(CAPS))
def test_upper_floors_fit_their_triangle_caps(lspec, name):
    c = Ctx(lspec)
    counts = W.build(c)
    if name == "rooms":
        c = Ctx(lspec)
        R.build(c, counts)
    assert 0 < c.tris() <= CAPS[name]


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


def test_corridor_and_core_parts_see_at_most_four_lamps(upper):
    """廊下の帯・西ロビー・コアの部品は、同じ階の点光源が 4 つまでしか届かない（教室の部品は第 2 段で切る）。"""
    c, _ = upper
    lamps = [p for n, p in c.empties if n.startswith("lamp_lecture_")]
    over = [(mb.name, _lamps_near(mb, lamps)) for mb in c.builders()
            if re.fullmatch(r"(floor|wall)_lecture_corr[23]f_\w+", mb.name)]
    assert over and [o for o in over if o[1] > LAMPS_PER_PART] == []
