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
            assert sorted(items) == sorted(k for k, _ in P.wants(fl, side)), (fl, side)   # 全部置けた
            assert items.count("board") + items.count("case") >= 1, (fl, side)
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


def _floor_signature(fl):
    """階の廊下の小物の並び（掲示の種類と X、ベンチ、ゴミ箱の数、ラックの有無）。"""
    notices = tuple((side, k, round(x, 1)) for side in ("S", "N") for k, x in P.wall_items(fl, side)
                    if k in ("board", "case"))
    return notices, P.FLOOR_BENCHES[fl], len(P.bins_x(fl)), fl in P.RACK_FLOORS


def test_no_two_floors_have_the_same_corridor_props():
    """#423: 2F〜7F の廊下がどの階も同じに見えないよう、掲示・ベンチ・ゴミ箱・ラックの組を階ごとに変える。"""
    sigs = [_floor_signature(fl) for fl in RD.FLOORS]
    assert len(set(sigs)) == len(RD.FLOORS)


def test_benches_and_bins_stay_clear_of_core_doors():
    """コアの北の面のベンチは多目的トイレの扉（X -17.6）、南の面のゴミ箱はトイレの扉（X -7.8・14.0）にかからず、
    どちらもコアの箱の面の中に収まる。"""
    hw = P.BENCH_W * 0.5
    for fl in RD.FLOORS:
        for x in P.FLOOR_BENCHES[fl]:
            assert abs(x - (-17.6)) > hw + 0.6 + P.CLEAR, (fl, x)
            assert (W.CORE_W[0] <= x - hw and x + hw <= W.CORE_W[2]) or                 (W.CORE_M[0] <= x - hw and x + hw <= W.CORE_M[2]), (fl, x)
        for x in P.bins_x(fl):
            assert W.CORE_W[0] <= x - 0.15 and x + 0.15 <= -7.8 - 0.5 - P.CLEAR, (fl, x)


def test_rack_sits_on_the_lobby_wall_clear_of_the_signs_and_the_stair():
    y, w, _ = P.RACK
    lo, hi = y - w * 0.5, y + w * 0.5
    sign_lo = RD.LOBBY_SIGN_Y - RD.LOBBY_DIRECTORY[0] * 0.5
    assert W.FACE["S"][0] + P.CLEAR <= lo and hi + P.CLEAR <= sign_lo
    for fl in P.RACK_FLOORS:
        for a, b in P.WS.halves(fl):
            assert hi < a, fl



def _case_faces():
    from kcd_interior.kit import T
    mb = MeshBuilder("case")
    P.case(mb, T())
    return [(mb.mat_names[m], [mb.verts[i] for i in f]) for f, m in zip(mb.faces, mb.face_mat)]


def test_case_posters_sit_behind_the_frame():
    """掲示ケースのコルクとポスターは枠の前の面より奥（壁の側）にある。"""
    faces = _case_faces()
    inner = [pts for mat, pts in faces if mat in (P.CORK, P.PAPER) + P.POSTERS]
    assert inner
    for pts in inner:
        assert all(0.0 < y < P.CASE_DEPTH * 0.5 for _x, y, _z in pts), pts
    metal_y = [y for mat, pts in faces if mat == P.METAL for _x, y, _z in pts]
    assert max(metal_y) == pytest.approx(P.CASE_DEPTH)
    assert {mat for mat, _pts in faces} <= {P.METAL, P.CORK, P.PAPER, P.GLASS} | set(P.POSTERS)


def test_case_glass_covers_the_posters_inside_the_frame():
    """ガラスは 1 枚で、ポスターより手前・枠の前の面より奥にあり、ポスターを全部覆う。"""
    faces = _case_faces()
    glass = [pts for mat, pts in faces if mat == P.GLASS]
    assert len(glass) == 1
    gx = [x for x, _y, _z in glass[0]]
    gz = [z for _x, _y, z in glass[0]]
    assert all(y == pytest.approx(P.CASE_GLASS) for _x, y, _z in glass[0])
    inner = [pts for mat, pts in faces if mat in (P.PAPER,) + P.POSTERS]
    for pts in inner:
        assert all(y < P.CASE_GLASS < P.CASE_DEPTH for _x, y, _z in pts)
        assert all(min(gx) <= x <= max(gx) and min(gz) <= z <= max(gz) for x, _y, z in pts)
    # 廊下の側（+y）を向く: 面の法線の y が正
    (ax, ay, az), (bx, by, bz), (cx, cy, cz) = glass[0][:3]
    ny = (bz - az) * (cx - ax) - (bx - ax) * (cz - az)
    assert ny > 0


def test_box_open_drops_only_the_named_faces():
    from kcd_interior.kit import T
    for skip, n in (((), 5), (("top",), 4), (("-x", "+x"), 3), (("-y",), 4)):
        mb = MeshBuilder("b")
        T(1.0, 2.0, 0.5, 0.3).box_open(mb, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, "m", skip=skip)
        assert len(mb.faces) == n, skip
    with pytest.raises(ValueError):
        T().box_open(MeshBuilder("b"), 0, 0, 0, 1, 1, 1, "m", skip=("bottom",))


def test_box_open_matches_box_nb_without_skips():
    """skip が無ければ box_nb と同じ頂点の並び（面の向きも同じ）。"""
    from kcd_interior.kit import T
    t = T(1.0, 2.0, 0.5, 0.7)
    a, b = MeshBuilder("a"), MeshBuilder("b")
    t.box_open(a, -0.2, -0.1, 0.0, 0.3, 0.4, 0.5, "m")
    t.box_nb(b, -0.2, -0.1, 0.0, 0.3, 0.4, 0.5, "m")
    fa = sorted(tuple(tuple(round(c, 9) for c in a.verts[i]) for i in f) for f in a.faces)
    fb = sorted(tuple(tuple(round(c, 9) for c in b.verts[i]) for i in f) for f in b.faces)
    assert fa == fb
