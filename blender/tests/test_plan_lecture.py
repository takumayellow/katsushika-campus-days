"""講義棟のプラン（kcd_interior.plan_lecture_*）: 寸法の表と躯体をスタブで組んで確かめる。"""

import pytest

import stubs
from kcd_interior import plan_lecture_dims as D
from kcd_interior import plan_lecture_hall as HL
from kcd_interior import plan_lecture_shell as SH
from kcd_interior import plan_lecture_signs as SG
from kcd_interior import shell as ishell
from kcd_interior import signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SHELL_CAP = 8500          # spec §7 の外殻の配分
STEP_OFFSET = 0.40        # Unity の CharacterController が上れる段
KEPT_POIS = ("hall_stage", "hall_booth", "hall_seats", "grand_stair", "atrium", "lecture_room",
             "foyer_counter", "foyer", "seminar")      # 名前を変えずに残す 9 個
NEW_POIS = ("coop", "lounge", "corridor", "health")
MARGIN = ishell.WALL      # 外周壁の厚み
HALL_SEATS, HALL_DESKS = 270, 70                       # spec §4
SEATS_DESKS_CAP = 12720   # spec §4 の見積もり（座席 270 x 44 + 机）
REGISTERS = ((36.6, 39.0), (37.6, 39.0), (38.6, 39.0), (39.6, 39.0))   # 生協のモジュールが決めるまでの仮置き


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
    assert dict(shell.empties) == {"spawn_lecture": D.SPAWN, "exit_lecture": D.EXIT}
    assert tuple(shell.door_gap) == D.DOOR_GAP


def test_signs_are_all_placed_with_text(signed):
    assert [r["ja"] for r in signed.signs] == [row[1] for row in D.SIGNS]
    assert all(r["tris"] > 0 for r in signed.signs)
    anchored = [row for row in D.SIGNS if row[10].get("anchor", True)]
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


def test_hall_rows_climb_to_the_back_aisle():
    rs = HL.rows()
    assert rs[0].y1 == pytest.approx(D.HALL_FLAT_Y[0])
    assert rs[-1].y0 == pytest.approx(D.HALL_BACK_Y[1]) and rs[-1].z == pytest.approx(1.2)
    for a, b in zip(rs, rs[1:]):
        assert b.y1 == pytest.approx(a.y0) and b.z - a.z == pytest.approx(D.HALL_RISE)


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
