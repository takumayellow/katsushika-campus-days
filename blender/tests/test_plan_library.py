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


def test_seventeen_seats_in_table_order(built):
    got = [p for n, p in built.empties if n.startswith("seat_library_") and n.count("_") == 2]
    assert len(got) == 17
    for (x, y, _z), (sx, sy, sz, _ang) in zip(got, D.SEATS):
        assert (x, y) == pytest.approx((sx, sy), abs=0.3)
    for n, p in built.empties:
        if n.startswith("seat_library_") and n.count("_") == 2:
            k = int(n.rsplit("_", 1)[1]) - 1
            if k < len(D.SEATS):
                assert p[2] == pytest.approx(D.SEATS[k][2] - D.SEAT_H)


def test_piano_stool_is_the_last_seat_and_has_keys(built):
    empties = dict(built.empties)
    stool = empties["seat_library_17"]
    keys = empties["seat_library_17_piano"]
    assert "seat_library_17_a0" in empties and "seat_library_17_f" in empties
    assert stool[2] == pytest.approx(D.STAGE[4])
    assert keys[2] == pytest.approx(D.STAGE[4] + 0.70)
    # 鍵盤は椅子から 0.33 m 先（ピアノのローカルで +Y が +X 方向）
    assert (keys[0] - stool[0], keys[1] - stool[1]) == pytest.approx((0.33, 0.0), abs=1e-6)


def _stage_faces(c):
    for name, mb in c.furns:
        if name.endswith("_stage"):
            for f, m in zip(mb.faces, mb.face_mat):
                yield [mb.verts[i] for i in f], mb.mat_names[m]


def test_piano_stands_on_the_audience_left_of_the_stage(built):
    """ピアノは客席から見て左（下手, -X）。鍵盤は -X 側、尾は舞台の中央（+X）へ向く。袖の扉と演台を避ける。"""
    empties = dict(built.empties)
    stool = empties["seat_library_17"]
    x0, y0, x1, _y1, _z = D.STAGE
    assert stool[0] < D.HALL_AXIS
    assert stool[0] == pytest.approx(-12.25, abs=0.01)
    assert stool[0] - x0 > 1.5            # 袖の扉 (x0, 61.0) の前の通り道が空く
    lecterns = [(x, y) for x, y, *_ in [(D.HALL_AXIS, 57.0), (7.3, 57.2)]]
    assert all(abs(x - stool[0]) > 1.5 for x, _y in lecterns)


def test_piano_faces_the_audience_with_lid_prop_and_bass_in_place(built):
    """FBX の座標で、演奏の向きを確かめる（奏者は椅子から鍵盤の側を向く）。"""
    empties = dict(built.empties)
    stool = empties["seat_library_17"]
    keys = empties["seat_library_17_piano"]
    fx, fy = keys[0] - stool[0], keys[1] - stool[1]
    # 奏者の右手側 = 正面を時計回りに 90 度（Z 上の右手系で (fy, -fx)）。客席は舞台の前縁 y0 より小さい Y
    right = (fy, -fx)
    assert right[1] < 0, "奏者の右手側が客席を向いていない"
    zf = D.STAGE[4]
    py = stool[1]
    # 屋根の支え棒（stainless の細い棒。シャンデリアの棒は z が高いので除く）
    props = [f for f, m in _stage_faces(built)
             if m == "stainless" and all(zf + 0.8 < p[2] < zf + 1.6 for p in f) and all(abs(p[0] - (stool[0] + 1.45)) < 0.3 for p in f)]
    assert props
    prop_y = sum(p[1] for f in props for p in f) / sum(len(f) for f in props)
    assert prop_y < py, "屋根の支え棒が客席側にない"
    # 黒鍵の天面。低音側は黒鍵の間隔が「2 つ → 1 つ」の並びの 2 つ飛びの端
    tops = []
    for f, m in _stage_faces(built):
        if m == "library_counter_black" and len(f) == 4 and all(abs(p[2] - (zf + 0.725)) < 1e-6 for p in f)                 and all(abs(p[0] - stool[0]) < 1.5 for p in f):
            ys = [p[1] for p in f]
            if max(ys) - min(ys) < 0.02:
                tops.append(sum(ys) / 4)
    tops = sorted(tops)
    assert len(tops) == 36
    low_gap, high_gap = tops[1] - tops[0], tops[-1] - tops[-2]
    unit = 1.44 / 52
    # 白鍵 2 つぶんの間隔が開いている端が低音（A#0 と C#1）
    bass_is_high_y = high_gap > low_gap
    assert max(low_gap, high_gap) == pytest.approx(2 * unit, abs=1e-4)
    assert min(low_gap, high_gap) == pytest.approx(unit, abs=1e-4)
    assert bass_is_high_y, "低音側が袖（奥, +Y）の側にない"
    assert D.STAGE[3] > 0 and tops[-1] > py


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


def test_each_entrance_gate_lane_leaves_room_for_the_player(built):
    """入館ゲートの 3 レーンは、ガラスのフラップを含めても有効幅 0.70 m 以上（プレイヤー直径 0.56 m + 両側 7 cm）。"""
    g0 = D.GATES_Y[0]
    flaps = []
    for mb in built.builders():
        for f, m in zip(mb.faces, mb.face_mat):
            pts = [mb.verts[i] for i in f]
            if mb.mat_names[m] == "glass_partition" and all(abs(p[1] - (g0 + 0.5)) < 0.05 and 0.7 < p[2] < 1.05 and abs(p[0]) < 2.3 for p in pts):
                flaps.append((min(p[0] for p in pts), max(p[0] for p in pts)))
    pillars = (-2.0, -0.65, 0.65, 2.0)
    assert flaps
    for xa, xb in zip(pillars, pillars[1:]):
        lo, hi = xa + 0.15, xb - 0.15
        inside = [(a, b) for a, b in flaps if lo - 1e-6 <= a and b <= hi + 1e-6]
        left = max([b for a, b in inside if a < (lo + hi) / 2] + [lo])
        right = min([a for a, b in inside if a > (lo + hi) / 2] + [hi])
        assert right - left >= 0.70, f"レーン {xa}..{xb} の有効幅 {right - left:.2f} m"
