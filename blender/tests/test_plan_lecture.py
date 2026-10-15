"""講義棟のプラン（kcd_interior.plan_lecture_*）: 寸法の表と躯体をスタブで組んで確かめる。"""

import pytest

import stubs
from kcd_interior import plan_lecture_coop as COOP
from kcd_interior import plan_lecture_core as CORE
from kcd_interior import plan_lecture_dims as D
from kcd_interior import plan_lecture_east as EAST
from kcd_interior import plan_lecture_esc as ESC
from kcd_interior import plan_lecture_hall as HL
from kcd_interior import plan_lecture_lounge as LOUNGE
from kcd_interior import plan_lecture_north as NORTH
from kcd_interior import plan_lecture_101 as R101
from kcd_interior import plan_lecture_rooms_dims as RD
from kcd_interior import plan_lecture_shell as SH
from kcd_interior import plan_lecture_signs as SG
from kcd_interior import plan_lecture_upper as UP
from kcd_interior import shell as ishell
from kcd_interior import signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import entrances, geom, signtext

from conftest import CAMPUS_JSON

SHELL_CAP = 8500          # spec §7 の外殻の配分
STEP_OFFSET = 0.40        # Unity の CharacterController が上れる段
KEPT_POIS = ("hall_stage", "hall_booth", "hall_seats", "grand_stair", "atrium", "lecture_room",
             "foyer_counter", "foyer", "seminar")      # 名前を変えずに残す 9 個
NEW_POIS = ("coop", "lounge", "corridor", "health")
MARGIN = ishell.WALL      # 外周壁の厚み
HALL_SEATS, HALL_DESKS = 270, 70                       # spec §4
SEATS_DESKS_CAP = 12720   # spec §4 の見積もり（座席 270 x 44 + 机）
REGISTERS = COOP.REGISTERS
ESC_CAP = 4500           # エスカレータ 10 区間（1F→6F）。実測 4,012 に 1 割強の余裕
UNDER_CLEAR = 1.6        # spec §1-3: 斜路の下でこれより低い所には入れない
HEAD_CLEAR = 2.1         # 段の上面から上の区間の腹まで


def _inside(x, y):
    """外周壁の内側か（101 の東の段差・北の斜めの壁・生協の張り出しを含む外形）。"""
    if not (D.X_W + MARGIN < x < D.X_E - MARGIN and y > D.Y_S + MARGIN):
        return False
    if x > D.X_STEP - MARGIN and y < D.Y_EAST_S + MARGIN:
        return False
    top = D.Y_N_BUMP if x > D.X_BUMP_W + MARGIN else D.north_y(x)
    return y < top - MARGIN


@pytest.fixture(scope="module")
def lspec():
    return ispec.build_specs(ispec.load_campus(CAMPUS_JSON))["lecture"]


def _ear_candidates(contours):
    out = []
    for c in contours:
        try:
            idx = stubs.tessellate_polygon([c])
        except ValueError:
            idx = [(0, k, k + 1) for k in range(1, len(c) - 1)]
        out.extend(tuple(c[i] for i in t) for t in idx)
    return out


@pytest.fixture(scope="module")
def signed(lspec):
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        c = Ctx(lspec)
        SG.build(c, c.furn("signs"), REGISTERS)
        signs.check(c)
    return c


@pytest.fixture(scope="module")
def hall(lspec):
    c = Ctx(lspec)
    n = HL.build_seats(c, c.furn("hall_seats"), c.furn("hall_desks"))
    return c, n


@pytest.fixture(scope="module")
def shell(lspec):
    c = Ctx(lspec)
    SH.build(c)
    return c


def test_bands_run_south_to_north():
    ys = (D.Y_S, D.Y_101_N, D.Y_ROOM_S1, D.Y_CORE0, D.Y_CORE1, D.Y_ROOM_N0, D.N_WALL[1][1],
          D.Y_N_BUMP)
    assert list(ys) == sorted(set(ys))


def test_hall_rows_are_low_steps():
    assert D.HALL_RISE < STEP_OFFSET
    assert D.HALL_ROWS * D.HALL_RISE == pytest.approx(1.2)
    assert D.DIRECT_RISE < STEP_OFFSET and D.ESC_RISE < STEP_OFFSET


def test_direct_stair_is_low_steps_and_reaches_2f():
    assert D.DIRECT_RISE <= STEP_OFFSET
    assert D.DIRECT_RISE * D.DIRECT_STEPS * 2 == pytest.approx(D.F2)
    assert EAST.ST_LAND[1] <= D.DIRECT_STAIR_Y[1] - D.DIRECT_STEPS * D.DIRECT_RUN + 1e-6


def test_poi_names_keep_the_nine_and_add_four():
    names = [p[0] for p in D.POIS]
    assert len(names) == len(set(names))
    assert sorted(names) == sorted(KEPT_POIS + NEW_POIS)


def test_pois_npcs_and_cameras_are_inside_the_walls():
    pts = [p[1:3] for p in D.POIS] + [n[:2] for n in D.NPCS] + [cam[1][:2] for cam in D.CAMS]
    outside = [p for p in pts if not _inside(*p)]
    assert outside == []


def test_sign_table():
    assert len(D.SIGNS) == 36
    hang = [s for s in D.SIGNS if s[0] == "hang"]
    assert len(hang) == 8 and all(s[7] == D.CEIL_CORR for s in hang)
    assert [s[10]["register"] for s in D.SIGNS if "register" in s[10]] == [0, 1, 2, 3]
    assert all(s[1].strip() and s[2].strip() for s in D.SIGNS)


def test_shell_fits_its_share_of_the_budget(shell):
    assert 0 < shell.tris() <= SHELL_CAP


def test_shell_stays_on_the_footprint(shell, lspec):
    vs = [v for mb in shell.builders() for v in mb.verts]
    tol = 0.01
    assert min(v[0] for v in vs) >= lspec.x0 - tol and max(v[0] for v in vs) <= lspec.x1 + tol
    assert min(v[1] for v in vs) >= lspec.y_face - tol and max(v[1] for v in vs) <= lspec.y_back + tol


def test_shell_places_the_entrance(shell):
    west = {}
    for door, y, _w in D.WEST_DOORS:
        west["spawn_" + door] = (D.WEST_SPAWN_X, y, 0.0)
        west["exit_" + door] = (D.WEST_EXIT_X, y, 0.0)
    assert dict(shell.empties) == {"spawn_lecture": D.SPAWN, "exit_lecture": D.EXIT,
                                   "spawn_lecture_se": D.SE_SPAWN,
                                   "exit_lecture_se": D.SE_EXIT, **west}
    assert len(shell.empties) == 4 + len(west)
    # meta の spawn（最初の spawn_*）は南の入口のまま
    assert shell.empties[0][0] == "spawn_lecture"
    assert tuple(shell.door_gap) == D.DOOR_GAP


def test_signs_are_all_placed_with_text(signed):
    rows = D.SIGNS + RD.SIGNS
    assert [r["ja"] for r in signed.signs] == [row[1] for row in rows]
    assert all(r["tris"] > 0 for r in signed.signs)
    anchored = [row for row in rows if row[10].get("anchor", True)]
    assert len([r for r in signed.signs if r["empty"]]) == len(anchored)


def test_register_plates_go_where_the_coop_puts_them(signed):
    regs = [r for r in signed.signs if r["ja"] in ("1", "2", "3", "4")]
    assert [(r["pos"]["x"], r["pos"]["z"]) for r in regs] == list(REGISTERS)


def test_register_count_is_checked(lspec):
    with pytest.raises(ValueError):
        SG.build(Ctx(lspec), None, REGISTERS[:3])


def test_hang_roots_are_the_eight_hanging_signs():
    roots = SG.hang_roots()
    assert len(roots) == 8 and all(z == D.CEIL_CORR for _, _, z in roots)


def test_hall_has_270_seats_at_70_desks():
    rs = HL.rows()
    assert len(rs) == D.HALL_ROWS
    assert all(len(r.desks) == 7 and sum(d[2] for d in r.desks) == 27 for r in rs)
    assert len(HL.seat_points(rs)) == HALL_SEATS
    assert sum(len(r.desks) for r in rs) == HALL_DESKS


def test_hall_desks_fill_each_block_from_wall_to_aisle():
    desks = HL.rows()[0].desks
    for bx0, bx1 in D.HALL_BLOCKS:
        mine = [d for d in desks if bx0 - 1e-6 <= d[0] and d[1] <= bx1 + 1e-6]
        assert mine[0][0] == pytest.approx(bx0) and mine[-1][1] == pytest.approx(bx1)
        assert all(0 < d[2] <= HL.DESK_SEATS for d in mine)
    assert [len([d for d in desks if bx0 - 1e-6 <= d[0] and d[1] <= bx1 + 1e-6])
            for bx0, bx1 in D.HALL_BLOCKS] == [2, 3, 2]


def test_hall_rows_step_down_from_the_back_aisle_to_the_board():
    rs = HL.rows()
    assert rs[0].y0 == pytest.approx(D.HALL_FLAT_Y[1])
    assert rs[0].z == pytest.approx(D.HALL_RISE - D.HALL_DROP)
    assert rs[-1].y1 == pytest.approx(D.HALL_BACK_Y[0]) and rs[-1].z == pytest.approx(0.0)
    for a, b in zip(rs, rs[1:]):
        assert b.y0 == pytest.approx(a.y1) and b.z - a.z == pytest.approx(D.HALL_RISE)
    # 黒板は平場の南の壁（扉は後ろの通路の北の壁）
    assert all(D.HALL_Y[0] <= y <= D.HALL_FLAT_Y[1] for y in (D.HALL_LECTERN[1], D.HALL_RACK[1]))


def test_hall_seats_and_desks_stay_on_their_tread(hall):
    c, _n = hall
    rs = HL.rows()
    stray = []
    for mb in c.builders():
        for x, y, z in mb.verts:
            r = next((r for r in rs if r.y0 - 1e-6 <= y <= r.y1 + 1e-6), None)
            if r is None or not (r.z - 1e-6 <= z <= r.z + 1.0):
                stray.append((x, y, z))
    assert stray == []


def test_hall_seats_and_desks_fit_the_estimate(hall):
    c, n = hall
    assert n == HALL_SEATS and c.seats == HALL_SEATS
    assert c.tris() <= SEATS_DESKS_CAP


def test_hall_tiers_run_wall_to_wall_without_gaps():
    ts = HL.tiers()
    assert len(ts) == D.HALL_ROWS
    assert all(t[0] < D.R101_X[0] and t[2] > D.R101_X[1] for t in ts)
    assert ts[0][1] == pytest.approx(D.HALL_FLAT_Y[1]) and ts[-1][3] == pytest.approx(D.HALL_BACK_Y[0])
    for a, b in zip(ts, ts[1:]):
        assert b[1] == pytest.approx(a[3])
        assert 0 <= b[4] - a[4] < STEP_OFFSET
    assert ts[0][4] + D.HALL_DROP < STEP_OFFSET and ts[-1][4] == pytest.approx(0.0)


def test_hall_floor_is_cheap(lspec):
    c = Ctx(lspec)
    HL.build_floor(c)
    assert 0 < c.tris() <= 200


@pytest.fixture(scope="module")
def esc(lspec):
    c = Ctx(lspec)
    ESC.build(c)
    return c


def test_escalator_steps_are_climbable():
    assert ESC.RISE <= STEP_OFFSET
    for f in ESC.flights():
        assert f.n * ESC.RISE == pytest.approx(f.zb - f.za)
        assert 0.33 <= abs(f.run) <= 0.40


def test_moving_steps_reach_both_floors_after_one_step():
    """止まっているとき 1 段目は下の床の高さ、1 段ぶん動くと最上段が上の端の床にそろう。"""
    for f in ESC.flights():
        x0, x1, z = f.tread(1)
        assert z == pytest.approx(f.za)
        assert sorted((x0, x1)) == pytest.approx(sorted((f.xa - f.run, f.xa)))
        x0, x1, z = f.tread(f.n)
        assert z + ESC.RISE == pytest.approx(f.zb)
        assert sorted((x0 + f.dir * abs(f.run), x1 + f.dir * abs(f.run))) == pytest.approx(
            sorted((f.xb - f.run, f.xb)))


def _empty(c, name):
    found = [p for n, p in c.empties if n == name]
    assert len(found) == 1, name
    return found[0]


def test_escalator_ends_are_empties_on_the_step_midline(esc):
    """esc_lecture_<区間>_lo / _hi: 踏面の中央を結ぶ線の両端（下の端は 1 段目の中央）、列の幅の中央。"""
    names = [n for n, _ in esc.empties
             if n.startswith("esc_lecture_") and not n.endswith("_down")]
    assert len(names) == 2 * len(ESC.flights())
    for f in ESC.flights():
        yc = (f.y0 + f.y1) * 0.5
        lo = _empty(esc, "esc_lecture_%s_lo" % f.name)
        hi = _empty(esc, "esc_lecture_%s_hi" % f.name)
        assert lo == pytest.approx((f.xa - f.dir * abs(f.run) * 0.5, yc, f.za))
        assert hi == pytest.approx((f.xb - f.dir * abs(f.run) * 0.5, yc, f.zb))
        x0, x1, z = f.tread(1)
        assert (x0 + x1) * 0.5 == pytest.approx(lo[0]) and z == pytest.approx(lo[2])


# JSPE p3・p4 の乗り口・降り口の表記（spec §1-3）: (階, 列, 端の X, "へ" か "から", 行き先・来た階)
JSPE_LABELS = (
    (1, "north", ESC.X_E, "へ", 2), (1, "south", ESC.X_W, "から", 2),
    (3, "north", ESC.X_W, "へ", 2), (3, "north", ESC.X_E, "へ", 4),
    (3, "south", ESC.X_E, "から", 2), (3, "south", ESC.X_W, "から", 4),
)


def test_escalator_directions_follow_the_jspe_labels():
    """「N Fへ」は乗り口、「N Fから」は降り口。乗る向きが区間の上り・下りと合う。"""
    by_name = {f.name: f for f in ESC.flights()}
    for floor, column, x, way, other in JSPE_LABELS:
        f = by_name["%s_%df" % (column, min(floor, other))]
        at_lower = floor < other
        assert (f.xa if at_lower else f.xb) == pytest.approx(x), (floor, column, x)
        boards = way == "へ"
        # 下の端から乗る（下の端が乗り口）のは上り、上の端から乗るのは下り
        assert f.down == (boards != at_lower), (floor, column, x, way, other)


def test_each_pair_of_floors_has_one_up_and_one_down():
    for lower in range(1, ESC.TOP):
        pair = [f for f in ESC.flights() if f.name.endswith("_%df" % lower)]
        assert sorted(f.down for f in pair) == [False, True], (lower, [(f.name, f.down) for f in pair])


def test_down_flights_carry_a_down_marker(esc):
    """下りの区間だけに Empty esc_lecture_<区間>_down（上の端）。Unity はこれで運ぶ向きを逆にする。"""
    downs = {n for n, _ in esc.empties if n.startswith("esc_lecture_") and n.endswith("_down")}
    assert downs == {"esc_lecture_%s_down" % f.name for f in ESC.flights() if f.down}
    for f in ESC.flights():
        if f.down:
            assert _empty(esc, "esc_lecture_%s_down" % f.name) == _empty(esc, "esc_lecture_%s_hi" % f.name)


def test_moving_steps_are_their_own_part_per_flight(esc):
    """段は区間ごとの escsteps_lecture_<区間>（Unity が当たり判定を付けずに動かす）。止まった部分には段が無い。"""
    parts = {mb.name: mb for mb in esc.parts}
    for f in ESC.flights():
        steps = parts["escsteps_lecture_%s" % f.name]
        zs = [v[2] for v in steps.verts]
        assert min(zs) == pytest.approx(f.za - ESC.RISE)          # 1 段目の蹴込みも RISE の高さ
        assert max(zs) == pytest.approx(f.zb - ESC.RISE + ESC.LIFT)
        static = parts["wall_lecture_esc_%s" % f.name]
        assert static.faces
        assert "lecture_tactile_yellow" not in _materials(static)
        assert "lecture_tactile_yellow" in _materials(steps)


def _materials(mb):
    return {mb.mat_names[i] for i in mb.face_mat}


def test_escalator_ends_meet_floors_and_landings():
    for f in ESC.flights():
        assert min(abs(f.za - z) for z in D.FL) < 1e-9
        assert min(abs(f.zb - z) for z in D.FL) < 1e-9
        assert sorted((f.xa, f.xb)) == [D.ESC_LAND_W[1], D.ESC_LAND_E[0]]
        assert (f.y0, f.y1) in (D.ESC_S_Y, D.ESC_N_Y)
        assert (f.xb > f.xa) == ((f.y0, f.y1) == D.ESC_S_Y)   # 南の列は東へ、北の列は西へ上る


def test_escalator_rows_stack_with_headroom():
    fl = ESC.flights()
    n = ESC.TOP - 1
    assert len(fl) == 2 * n
    for lo, hi in [(fl[r + i], fl[r + i + 1]) for r in (0, n) for i in range(n - 1)]:
        assert (lo.y0, lo.y1, lo.xa, lo.xb) == (hi.y0, hi.y1, hi.xa, hi.xb)
        for x in (lo.xa, (lo.xa + lo.xb) * 0.5, lo.xb):
            gap = hi.line(x) - lo.line(x)
            assert 4.2 - 1e-9 <= gap <= 4.6 + 1e-9
            assert hi.line(x) - ESC.DROP - (lo.line(x) + ESC.RISE) >= HEAD_CLEAR


def test_escalator_underside_is_closed_where_low():
    for f in ESC.flights():
        if f.za > 0.0:
            continue
        xo = f.open_x()
        assert min(f.xa, f.xb) < xo < max(f.xa, f.xb)
        assert f.line(xo) - ESC.DROP == pytest.approx(ESC.CLEAR)
        assert ESC.CLEAR >= UNDER_CLEAR


def test_hole_rails_clear_the_escalator_cladding():
    post = 0.03                                    # shell.railing の柱の半幅
    assert UP.RAIL_OUT > post
    x0, y0, x1, y1 = UP.HOLE
    assert (y0, y1) == (D.ESC_S_Y[0], D.ESC_N_Y[1])
    assert (x0, x1) == (D.ESC_LAND_W[1], D.ESC_LAND_E[0])


def test_no_point_light_next_to_the_escalator_hole(lspec):
    c = Ctx(lspec)
    UP.build(c, SG.hang_roots())
    assert c.lights
    assert not [p for p in c.lights if UP.near_hole(p[0], p[1])]
    x0, y0, x1, y1 = UP.HOLE
    assert UP.near_hole((x0 + x1) * 0.5, y0 - 1.0)


def test_escalator_fits_its_budget(esc):
    assert 0 < esc.tris() <= ESC_CAP


# ---- 区画ごと: 三角形の上限と、頂点が footprint の内側にあること ----

def _build_east(c):
    EAST.build(c)


def _build_coop(c):
    COOP.build(c, c.furn("coop"))


def _build_lounge(c):
    LOUNGE.build(c, c.furn("lounge"))


def _build_north(c):
    NORTH.build(c, c.furn("north"))


def _build_core(c):
    CORE.build(c, {k: c.furn(k) for k in ("corridor", "lobby")}, SG.hang_roots())


def _build_r101(c):
    R101.build(c.furn("hall_front"))


# 区画: (組み立て, 三角形の上限)。上限は現在の値に 1 割強の余裕を見たもの。
SECTIONS = {
    "east": (_build_east, 1300),
    "coop": (_build_coop, 1900),
    "lounge": (_build_lounge, 4000),
    "north": (_build_north, 4700),
    "core": (_build_core, 3700),
    "r101": (_build_r101, 2700),      # 前と後ろの壁の両方に木のパネル
}


@pytest.fixture(scope="module", params=sorted(SECTIONS))
def section(request, lspec):
    build, cap = SECTIONS[request.param]
    c = Ctx(lspec)
    build(c)
    return request.param, c, cap


def test_section_fits_its_triangle_cap(section):
    name, c, cap = section
    assert 0 < c.tris() <= cap, name


def test_section_stays_on_the_footprint(section, lspec):
    name, c, _ = section
    vs = [v for mb in c.builders() for v in mb.verts]
    tol = 0.01
    assert min(v[0] for v in vs) >= lspec.x0 - tol and max(v[0] for v in vs) <= lspec.x1 + tol, name
    assert min(v[1] for v in vs) >= lspec.y_face - tol and max(v[1] for v in vs) <= lspec.y_back + tol, name


def test_lounge_has_no_zero_length_wall():
    """弦の側の壁は、南の端が弧の端と一致するとき作らない。"""
    walls = LOUNGE.chord_walls()
    assert len(walls) == 1
    assert all(abs(b[1] - a[1]) > 0.1 for a, b in walls)


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


def test_se_door_walks_from_the_hall_through_the_vestibule_to_outside(lspec):
    """南東の扉: 風除室の中に立ち、外の扉の開口と内の扉を歩いて通れる。"""
    x0, y0, x1, y1 = D.SE_VESTIBULE
    sx, sy, _ = D.SE_SPAWN
    ex, ey, _ = D.SE_EXIT
    assert x0 < sx < x1 and y0 < ey < sy < y1
    assert sx == ex and _inside(sx, sy) and _inside(ex, ey)
    c = Ctx(lspec)
    SH.build(c)
    for build, _cap in SECTIONS.values():
        build(c)
    hw = 0.45
    path = (sx - hw, sx + hw, D.Y_EAST_S - 0.3, y1 + 1.0, 0.2, 2.0)
    assert _faces_in(c, path) == 0


def test_se_door_is_the_second_campus_door_of_the_lecture_building(lspec):
    """キャンパスの扉 lecture_se は講義棟の外壁に付き、屋内の南東の風除室の前に来る。"""
    assert entrances.building_of(D.SE_DOOR) == "lecture"
    assert entrances.building_of("lecture") == "lecture"
    assert entrances.building_of("cafeteria") == "cafeteria"
    assert entrances.HOSTS[D.SE_DOOR] == "lecture"
    data = ispec.load_campus(CAMPUS_JSON)
    frame = ispec.campus_frame(data)
    doors = {dr["id"]: dr for dr in entrances.plan(data, frame, {})}
    u, v = frame.uv(doors[D.SE_DOOR]["origin"])
    wu, wv = lspec.to_world_uv((D.SE_SPAWN[0], D.Y_EAST_S))
    assert abs(u - wu) < 0.05 and abs(v - wv) < 0.6


def _campus_doors():
    data = ispec.load_campus(CAMPUS_JSON)
    frame = ispec.campus_frame(data)
    doors = {dr["id"]: dr for dr in entrances.plan(data, frame, {})}
    loop = next(b["footprint"] for b in data["buildings"] if b["id"] == "lecture")
    return frame, doors, geom.ensure_ccw(geom.dedup(loop))


@pytest.mark.parametrize("door,y,w", D.WEST_DOORS)
def test_west_door_walks_from_the_lobby_through_the_glass_door_to_outside(lspec, door, y, w):
    """西の学生食堂側の扉: 西ロビーに立ち、開けたガラスの引き戸と外周壁の開口を歩いて通れる。"""
    assert _inside(D.WEST_SPAWN_X, y) and _inside(D.WEST_EXIT_X, y)
    assert D.WEST_EXIT_X < D.WEST_SPAWN_X < CORE.X_IN_W + 2.0
    c = Ctx(lspec)
    SH.build(c)
    for build, _cap in SECTIONS.values():
        build(c)
    hw = min(0.35, w * 0.5 - 0.1)
    path = (D.X_W - 0.5, D.WEST_SPAWN_X, y - hw, y + hw, 0.2, 2.0)
    assert _faces_in(c, path) == 0, door


@pytest.mark.parametrize("door,y,w", D.WEST_DOORS)
def test_west_door_opens_on_the_lobby_clear_of_the_west_stair(door, y, w):
    """開口（枠を含む）は西ロビーの南北の室の間にあり、1F で閉じている西の階段の張り出しにかからない。"""
    y0, y1 = y - w * 0.5 - 0.07, y + w * 0.5 + 0.07
    assert D.Y_ROOM_S1 < y0 and y1 < D.Y_ROOM_N0, door
    assert y1 < D.WEST_STAIR[1] or y0 > D.WEST_STAIR[3], door


@pytest.mark.parametrize("door,y,w", D.WEST_DOORS)
def test_west_door_is_a_campus_door_of_the_lecture_building(lspec, door, y, w):
    """キャンパスの扉 lecture_ws・lecture_wn は講義棟の西の外壁に付き、屋内の扉の外に来る。幅も同じ。"""
    assert entrances.building_of(door) == "lecture"
    assert entrances.HOSTS[door] == "lecture"
    frame, doors, _loop = _campus_doors()
    dr = doors[door]
    u, v = frame.uv(dr["origin"])
    wu, wv = lspec.to_world_uv((D.X_W, y))
    assert abs(v - wv) < 0.05 and abs(u - wu) < 0.6, door
    assert abs(dr["W"] * 2.0 - w) < 1e-6 and dr["H"] == D.WEST_DOOR_H, door


@pytest.mark.parametrize("door,y,w", D.WEST_DOORS)
def test_west_campus_door_stays_off_the_building(door, y, w):
    """扉の風除室・庇・足元の石張りが講義棟の外形（西の階段の張り出し）に食い込まない。"""
    _frame, doors, loop = _campus_doors()
    dr = doors[door]
    half = max(dr["WO"], dr["APRON_S"], dr["CAN_S"] if dr.get("CANOPY", True) else 0.0)
    far = max(dr["D"], dr["APRON_D"], dr["CAN_D"] if dr.get("CANOPY", True) else 0.0)
    for s in (-half, half):
        for k in range(1, 11):
            p = entrances.local(dr, s, far * k / 10.0)
            assert not geom.point_in_poly(p, loop), (door, s, far * k / 10.0)
