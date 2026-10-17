"""食堂のプラン（kcd_interior.plan_cafeteria）: スタブで組み、Empty・看板・カメラ・三角数を確かめる。"""

import math

import pytest

import stubs
from kcd_interior import furn_research2 as FR
from kcd_interior import plan_research2_1f as P1
from kcd_interior import plan_research2_2f as P2
from kcd_interior import plan_research2_dims as D
from kcd_interior import plan_research2_finish as FIN
from kcd_interior import plan_research2_poly as P
from kcd_interior import plan_research2_shell as S
from kcd_interior import plan_research2_signs as PS
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext
from kcd_lib.mesh import MeshBuilder

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 62000
TEXT_CAP = 17500                    # spec の看板の文字の配分
N_SIGNS = 33
CAMS = ["", "ticket", "=interior_cafeteria", "upper", "top"]
DOOR = "research2_door_steel"
NEW_MATS = ("research2_floor_1f_honey", "research2_floor_2f_honey", "research2_grille_black",
            "research2_cassette_white", "research2_roll_white", "research2_floor_mat_black")
HOLES = D.HOLES


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
def built():
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        specs = ispec.build_specs(ispec.load_campus(CAMPUS_JSON))
        c = Ctx(specs["cafeteria"], seed=SEED)
        registry.get("cafeteria").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def _faces_of(c, mat):
    for mb in c.builders():
        for f, m in zip(mb.faces, mb.face_mat):
            if mb.mat_names[m] == mat:
                yield [mb.verts[i] for i in f]


def test_spawn_and_exit(built):
    names = [n for n, _ in built.empties]
    assert names.count("spawn_cafeteria") == 1
    assert names.count("exit_cafeteria") == 1


def test_pois_are_in_table_order(built):
    got = _empties(built, "poi_cafeteria_")
    assert [n for n, _ in got] == ["poi_cafeteria_%s" % p[0] for p in D.POIS]
    assert {"poi_cafeteria_ticket", "poi_cafeteria_tray_return",
            "poi_cafeteria_counter", "poi_cafeteria_hall"} <= {n for n, _ in got}
    for (_, pos), (_name, x, y, z) in zip(got, D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_cafeteria_")
    assert [n for n, _ in got] == ["npc_cafeteria_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def test_every_sign_has_text_and_a_name(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == N_SIGNS
    assert all(s.strip() for s in ja)
    assert all(r["en"].strip() for r in built.signs)
    assert ja[0] == "2F フードコート"
    for must in ("食器返却口", "トレー", "本日のランチ"):
        assert must in ja
    named = [r["empty"] for r in built.signs if r["empty"]]
    assert named == ["sign_cafeteria_%d" % k for k in range(1, len(named) + 1)]
    assert named == [n for n, _ in _empties(built, "sign_cafeteria_")]
    assert len(named) >= 20


def test_kiosk_signs_do_not_carry_a_real_shop_name(built):
    kiosk = [r["ja"] for r in built.signs if "券売機" in r["ja"]]
    assert "【1F 専用券売機】\nキャッシュレス決済のみ" in kiosk
    assert "【1F 専用券売機】" in kiosk
    assert not [s for s in kiosk if "葛飾店" in s]
    assert not [r for r in built.signs if "葛飾店" in r["ja"]]


def test_hanging_sign_roots_match_the_table(built):
    roots = sorted((round(x, 3), round(y, 3)) for x, y, _z in PS.HANGS)
    hung = sorted((round(r["pos"]["x"], 3), round(r["pos"]["z"], 3)) for r in built.signs
                  if r["kind"] == "hanging")
    assert roots == pytest.approx(hung, abs=1e-3)


def test_window_labels_hang_single_sided_under_the_band(built):
    names = {ja for _x, ja, _en in PS.WINDOW_LABELS}
    labels = [r for r in built.signs if r["kind"] == "hanging" and r["ja"] in names]
    assert len(labels) == len(PS.WINDOW_LABELS)
    for r in labels:
        assert r["faces"] == 1, r["ja"]
        assert r["pos"]["y"] + r["h"] * 0.5 + r["drop"] == pytest.approx(D.BAND1[0], abs=1e-6)


def test_five_cameras_inside_the_building(built):
    assert [cam[0] for cam in built.cams] == CAMS
    for suffix, loc, _target, _lens in built.cams:
        assert P.inside(loc[:2], D.INNER), suffix
        if suffix != "top":
            assert 0.0 < loc[2] < D.TOP, suffix


def test_triangles_fit_the_caps(built):
    assert built.tris() <= INT_CAP
    assert built.text_tris() <= TEXT_CAP


def _center(f):
    return tuple(sum(p[k] for p in f) / len(f) for k in range(3))


def _in_rect(p, r):
    return r[0] < p[0] < r[2] and r[1] < p[1] < r[3]


def test_every_new_material_is_used(built):
    for mat in NEW_MATS:
        assert next(_faces_of(built, mat), None) is not None, mat


def test_seat_count_matches_the_tables(built):
    seats1 = sum(n for *_, n in P1.RUNS1) * 4 + len(P1.ROUNDS1) * 3 + len(D.STOOLS_1F)
    seats2 = (sum(n for *_, n in P2.RUNS2) * 4 + len(P2.ROUNDS2) * 4 + len(D.BENCHES2) * P2.BENCH_SEATS
              + len(D.HIGH_CHAIRS))
    assert (seats1, seats2) == (392, 370)
    assert built.seats == seats1 + seats2


@pytest.mark.parametrize("mats, z, floor", [
    ((FIN.DARK, FIN.HONEY), D.Z1, S.FLOOR1),
    ((FIN.OAK2,), D.Z2, D.FLOOR2),
])
def test_floor_boards_lie_on_the_floor_and_skip_the_holes(built, mats, z, floor):
    n = 0
    for mat in mats:
        for f in _faces_of(built, mat):
            n += 1
            assert all(p[2] == pytest.approx(z + FIN.BOARD_UP) for p in f)
            c = _center(f)
            assert P.inside(c[:2], floor) and P.inside(c[:2], D.INNER)
            assert not any(_in_rect(c, h) for h in HOLES)
    assert n > 300


def test_2f_boards_keep_off_the_glass(built):
    segs = list(zip(D.GLASS2, D.GLASS2[1:]))
    for f in _faces_of(built, FIN.OAK2):
        for x, y, _z in f:
            assert min(P.seg_dist((x, y), a, b) for a, b in segs) >= FIN.GLASS_GAP2 - 0.01


def test_cassettes_sit_under_the_flat_ceilings(built):
    pts1, pts2 = FIN.cassettes_1f(PS.HANGS), FIN.cassettes_2f(PS.HANGS)
    assert len(pts1) >= 20 and len(pts2) >= 10
    for pts, cols in ((pts1, D.COLS1), (pts2, D.COLS2)):
        for x, y in pts:
            assert P.inside((x, y), D.INNER)
            assert not any(_in_rect((x, y), h) for h in HOLES)
            assert all(math.hypot(x - cx, y - cy) >= 1.2 for cx, cy in cols)
    for f in _faces_of(built, FIN.CASSETTE):
        z = _center(f)[2]
        assert D.C1 - 0.04 < z < D.C1 or D.C2 - 0.04 < z < D.C2


def test_roll_screens_hang_inside_the_1f_glass(built):
    faces = list(_faces_of(built, FIN.ROLL))
    assert len(faces) > 30
    for f in faces:
        for x, y, z in f:
            assert P.inside((x, y), D.INNER)
            assert x > D.CLIP_X and y > D.CLIP_Y
            assert FIN.ROLL_Z[0] - 1e-6 <= z <= D.SOFFIT_Z + 1e-6


def test_entrance_mat_is_at_the_spawn(built):
    faces = list(_faces_of(built, FIN.MAT))
    assert len(faces) == 1
    x0, y0, x1, y1 = FIN.VEST_MAT
    assert x0 < D.SPAWN[0] < x1 and y0 < D.SPAWN[1] < y1
    assert all(p[2] == pytest.approx(D.Z1 + 0.004) for p in faces[0])


def test_stair_is_the_same_shape_along_x_and_y():
    a, b = MeshBuilder("a"), MeshBuilder("b")
    FR.stair(a, 0.0, 1.0, 1.2, 4.0, 0.0, 2.0, 10, "+y", side=None)
    FR.stair(b, 1.0, 0.0, 4.0, 1.2, 0.0, 2.0, 10, "+x", side=None)
    assert len(a.faces) == len(b.faces)
    key = sorted((round(y, 9), round(x, 9), round(z, 9)) for x, y, z in a.verts)
    assert key == sorted((round(x, 9), round(y, 9), round(z, 9)) for x, y, z in b.verts)
