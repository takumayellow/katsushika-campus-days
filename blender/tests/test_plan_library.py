"""図書館棟のプラン（kcd_interior.plan_library）: スタブで組み、Empty・座面・カメラ・外形・三角数を確かめる。"""

import math

import pytest

import stubs
from kcd_interior import plan_library_dims as D
from kcd_interior import plan_library_signs as SG
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 80000
TOP = 22.0
NOTCH = (41.3, 15.7, 49.7, 59.5)     # 右の切欠き（外）。東の壁は外面 X 41.3 の直線
BACK_Y = 66.1                        # 奥の張り出しの外（これより奥に立てる床が無い）
UP = 0.7                             # 法線の z がこれ以上なら立てる面


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
        c = Ctx(specs["library"], seed=SEED)
        registry.get("library").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def _faces(c):
    for mb in c.builders():
        for f in mb.faces:
            yield [mb.verts[i] for i in f]


def _normal_z(pts):
    nx = ny = nz = 0.0
    for a, b in zip(pts, pts[1:] + pts[:1]):
        nx += (a[1] - b[1]) * (a[2] + b[2])
        ny += (a[2] - b[2]) * (a[0] + b[0])
        nz += (a[0] - b[0]) * (a[1] + b[1])
    L = math.sqrt(nx * nx + ny * ny + nz * nz)
    return nz / L if L > 1e-12 else 0.0


def test_pois_are_in_table_order(built):
    names = [n for n, _ in _empties(built, "poi_library_")]
    assert names == ["poi_library_%s" % p[0] for p in D.POIS]
    for (_, pos), (_name, x, y, z) in zip(_empties(built, "poi_library_"), D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_library_")
    assert [n for n, _ in got] == ["npc_library_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def test_signs_are_in_table_order(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == 33
    assert ja[:3] == ["OPAC の使い方", "閲覧席", "受付"]
    assert ja[20:25] == ["1", "2", "3", "4", "5"]
    assert ja[25] == "黙 考 書 院"
    assert ja[-1] == "貸出・返却"
    assert [r["empty"] for r in built.signs] == ["sign_library_%d" % k for k in range(1, 34)]


def test_hanging_sign_roots_skip_the_ceiling_lights(built):
    roots = sorted((round(x, 3), round(y, 3)) for x, y, _z in SG.HANGS)
    hung = sorted((round(r["pos"]["x"], 3), round(r["pos"]["z"], 3)) for r in built.signs
                  if r["kind"] == "hanging")
    assert roots == pytest.approx(hung, abs=1e-3)


def test_sixteen_seats_in_table_order(built):
    got = [p for n, p in built.empties if n.startswith("seat_library_") and n.count("_") == 2]
    assert len(got) == 16
    for (x, y, _z), (sx, sy, sz, _ang) in zip(got, D.SEATS):
        assert (x, y) == pytest.approx((sx, sy), abs=0.3)
    for n, p in built.empties:
        if n.startswith("seat_library_") and n.count("_") == 2:
            k = int(n.rsplit("_", 1)[1]) - 1
            assert p[2] == pytest.approx(D.SEATS[k][2] - D.SEAT_H)


def test_six_cameras_inside_the_envelope(built):
    s = built.spec
    assert [cam[0] for cam in built.cams] == ["", "gallery", "upper", "overview", "hall", "foyer"]
    for _suffix, loc, target, _lens in built.cams:
        assert s.x0 < loc[0] < s.x1 and s.y_face < loc[1] < s.y_back
        assert 0.0 < loc[2] < TOP


def test_nothing_in_the_notch(built):
    x0, y0, x1, y1 = NOTCH
    bad = [p for f in _faces(built) for p in f
           if x0 + 0.01 < p[0] < x1 - 0.01 and y0 + 0.01 < p[1] < y1 - 0.01]
    assert bad == []


def test_nothing_standable_behind_the_bump(built):
    bad = []
    for f in _faces(built):
        cy = sum(p[1] for p in f) / len(f)
        if cy > BACK_Y and _normal_z(f) > UP:
            bad.append((round(cy, 2), round(f[0][2], 2)))
    assert bad == []


def test_triangles_fit_the_cap_and_top(built):
    assert built.tris() <= INT_CAP
    assert max(p[2] for f in _faces(built) for p in f) <= TOP + 1e-6
