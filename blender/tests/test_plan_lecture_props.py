"""講義棟 2F〜7F の小物（plan_lecture_props）: 廊下の掲示板・消火器・巾木・扉の窓、教室のブラインド・後ろの掲示。"""

import pytest

from kcd_interior import plan_lecture_props as P
from kcd_interior import plan_lecture_rooms as R
from kcd_interior import plan_lecture_rooms_dims as RD
from kcd_interior import plan_lecture_west as W
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib.mesh import MeshBuilder

from conftest import CAMPUS_JSON

CORRIDOR_CAP = 3200       # 廊下の小物（6 階ぶん）の三角形の上限
ROOM_CAP = 2400           # 教室の小物（20 室ぶん）の三角形の上限
BOARD_CLEAR = 1.0         # 黒板の面から手前へ空ける幅（ストーリーの黒板の場面）
RESERVED_7F = (-23.6, -21.2)   # 7F の廊下の南の壁でストーリー担当が使う区間


@pytest.fixture(scope="module")
def lspec():
    return ispec.build_specs(ispec.load_campus(CAMPUS_JSON))["lecture"]


@pytest.fixture(scope="module")
def corr(lspec):
    """廊下の小物だけを積んだ Ctx。"""
    c = Ctx(lspec)
    P.corridors(c)
    return c


def _verts(c):
    return [v for mb in c.builders() for v in mb.verts]


def _mat_verts(mbs, mat):
    return [mb.verts[i] for mb in mbs for f, face in enumerate(mb.faces)
            if mb.mat_names[mb.face_mat[f]] == mat for i in face]


def _on_floor(vs, fl):
    z = RD.FLOOR_Z[fl]
    return [v for v in vs if z - 0.01 <= v[2] <= z + RD.CEIL_H + 0.01]


def _room_props(room):
    furn, walls = MeshBuilder("f"), MeshBuilder("w")
    P.room(furn, walls, room, RD.FLOOR_Z[room[1]])
    return furn, walls


def test_every_floor_has_boards_extinguishers_and_skirting_on_both_walls(corr):
    mbs = list(corr.builders())
    for fl in RD.FLOORS:
        for side, (y, _) in W.FACE.items():
            items = [k for k, _ in P.wall_items(fl, side)]
            assert items.count("board") == 2, (fl, side)
            assert items.count("ext") >= 1, (fl, side)
            near = [v for v in _on_floor(_mat_verts(mbs, "lecture_skirting"), fl) if abs(v[1] - y) < 0.01]
            assert near, (fl, side)
        assert _on_floor(_mat_verts(mbs, "lecture_cork"), fl), fl
        assert _on_floor(_mat_verts(mbs, "fire_red"), fl), fl


def test_wall_items_sit_in_the_free_spans(corr):
    """掲示板・消火器・消火栓は扉の開口・室名札・7F の予約区間にかからない。"""
    for fl in RD.FLOORS:
        for side in ("S", "N"):
            spans = P.free_spans(fl, side)
            for kind, x in P.wall_items(fl, side):
                hw = P.WIDTH[kind] * 0.5
                assert any(a - 1e-6 <= x - hw and x + hw <= b + 1e-6 for a, b in spans), (fl, side, kind, x)


def test_free_spans_avoid_doors_plates_and_the_reserved_wall():
    for fl in RD.FLOORS:
        for side in ("S", "N"):
            spans = P.free_spans(fl, side)
            for lo, hi in P.blocked(fl, side):
                assert not [s for s in spans if s[0] < hi and s[1] > lo], (fl, side, lo, hi)
            doors = W.room_doors(fl, side)
            reach = RD.DOOR_W * 0.5 + W.FRAME_OUT
            for d in doors:
                assert not [s for s in spans if s[0] < d + reach and s[1] > d - reach], (fl, d)
    lo, hi = RESERVED_7F
    assert not [s for s in P.free_spans(RD.TOP_FLOOR, "S") if s[0] < hi and s[1] > lo]


def test_nothing_is_put_on_the_reserved_7f_wall(corr):
    lo, hi = RESERVED_7F
    z = RD.FLOOR_Z[RD.TOP_FLOOR]
    bad = [v for v in _verts(corr) if lo - 0.05 < v[0] < hi + 0.05
           and W.FACE["S"][0] - 0.01 <= v[1] < W.FACE["S"][0] + 1.0 and z + 0.1 < v[2] < z + 2.8]
    assert bad == []


def test_wall_props_stay_off_the_walking_line(corr):
    """廊下の通り道（Y 21.0・34.0 の ±0.35、床から 0.2〜2.0）と開いた扉の口には何も出さない。"""
    for fl in RD.FLOORS:
        z = RD.FLOOR_Z[fl]
        body = [v for v in _verts(corr) if z + 0.2 < v[2] < z + 2.0]
        assert not [v for v in body if 20.65 <= v[1] <= 21.35 or 33.65 <= v[1] <= 34.35], fl
        for side in ("S", "N"):
            y = RD.corridor_y(side)[0]
            for d in W.room_doors(fl, side):
                hit = [v for v in body if abs(v[0] - d) < 0.8 and abs(v[1] - y) < 2.0
                       and v[2] > z + 0.15]
                assert hit == [], (fl, side, d)


def test_closed_doors_get_a_window(corr):
    mbs = list(corr.builders())
    glass = _mat_verts(mbs, "lecture_door_glass")
    for room in RD.CLOSED:
        z = RD.FLOOR_Z[room[1]]
        y, _ = W.FACE[room[2]]
        for x in room[5]:
            near = [v for v in glass if abs(v[0] - x) < RD.DOOR_W * 0.5 and abs(v[1] - y) < 0.1
                    and z + 1.0 < v[2] < z + RD.DOOR_H]
            assert near, (room[0], x)


def test_corridor_props_fit_the_cap(lspec):
    c = Ctx(lspec)
    P.corridors(c)
    assert 0 < c.tris() <= CORRIDOR_CAP


def test_room_props_fit_the_cap():
    total = 0
    for room in RD.ROOMS:
        furn, walls = _room_props(room)
        total += len([1 for mb in (furn, walls) for f in mb.faces for _ in range(len(f) - 2)])
    assert 0 < total <= ROOM_CAP


def test_room_props_keep_the_board_clear():
    """黒板・白板の面とその手前 1 m（黒板の幅、床から 2.4 m まで）には小物を置かない。"""
    for room in RD.ROOMS:
        if room[6] not in ("A", "B", "C"):
            continue
        z = RD.FLOOR_Z[room[1]]
        _, xf1, yf0, yf1 = RD.room_faces(room)
        cy = R.C_RING_Y if room[6] == "C" else (yf0 + yf1) * 0.5
        for mb in _room_props(room):
            bad = [v for v in mb.verts if v[0] > xf1 - BOARD_CLEAR and abs(v[1] - cy) < 3.7
                   and z < v[2] < z + 2.4]
            assert bad == [], room[0]


def test_type_a_and_b_have_a_back_board_clear_of_the_clock():
    for room in RD.ROOMS:
        if room[6] not in ("A", "B"):
            continue
        xf0, _, yf0, yf1 = RD.room_faces(room)
        cy = (yf0 + yf1) * 0.5
        _, walls = _room_props(room)
        cork = _mat_verts([walls], "lecture_cork")
        assert cork, room[0]
        assert min(v[0] for v in cork) == pytest.approx(xf0), room[0]
        assert max(v[0] for v in cork) - xf0 < 0.06, room[0]
        ys = [v[1] for v in cork]
        assert max(ys) < cy - 0.4 or min(ys) > cy + 0.4, room[0]


def test_every_room_has_blinds_on_its_window_wall():
    for room in RD.ROOMS:
        z = RD.FLOOR_Z[room[1]]
        _, walls = _room_props(room)
        vs = _mat_verts([walls], "lecture_blind")
        assert vs, room[0]
        assert max(v[2] for v in vs) <= z + RD.CEIL_H, room[0]
        assert min(v[2] for v in vs) >= z + P.SILL, room[0]
        if room[2] == "S":
            assert max(v[1] for v in vs) < RD.Y_IN_S + 0.2, room[0]
        else:
            assert min(v[1] for v in vs) > 47.0, room[0]


def test_room_props_stay_inside_the_room():
    for room in RD.ROOMS:
        xf0, xf1, yf0, yf1 = RD.room_faces(room)
        for mb in _room_props(room):
            for v in mb.verts:
                assert xf0 - 1e-6 <= v[0] <= xf1 + 1e-6, room[0]
                assert yf0 - 1e-6 <= v[1] <= max(yf1, yf0) + 0.5, room[0]
