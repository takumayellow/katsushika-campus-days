"""屋内の看板（kcd_interior.signs）: 全棟と寮をスタブで組み、看板の文言・向き・取り付けを確かめる。

三角形分割は Blender の delaunay_2d_cdt の代わりに輪郭ごとの耳切り（stubs.tessellate_polygon）を
使う。字の形は Blender で書き出したものと少し違うが、置く位置と向きは同じ。
"""

import json
import math
import os

import numpy as np
import pytest

import stubs
from kcd_interior import registry, shell, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext
from kcd_route import dorm as D
from raycast import first_hit

from conftest import CAMPUS_JSON, REPO

ROUTE_JSON = os.path.join(REPO, "data", "osm", "route.json")
SEED = 1709
DORM_SEED = 20250923
WALL_OUT = 0.06          # signs.wall の out の既定（プランは全部これを使う）
FRONT_CLEAR = 0.6        # 文字の前に空いているべき距離（m）
BACKED = 0.12            # 壁の看板の板の後ろにこの距離以内で面があること
ROD_TOL = 0.05           # 吊り看板の吊り元と天井の面のずれの許容（m）


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
    """{id: Ctx}（9 棟と寮）。"""
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        data = ispec.load_campus(CAMPUS_JSON)
        specs = ispec.build_specs(data)
        out = {}
        for bid in registry.ORDER:
            c = Ctx(specs[bid], seed=SEED)
            registry.get(bid).build(c)
            out[bid] = c
        with open(ROUTE_JSON, encoding="utf-8") as fp:
            dorm = json.load(fp)["dormitory"]
        dorm["footprint"] = [tuple(p) for p in dorm["footprint"]]
        c = Ctx(D.make_spec(dorm), seed=DORM_SEED)
        D.build(c)
        out["dorm"] = c
    return out


def _triangles(c):
    tris = []
    for mb in c.builders():
        v = mb.verts
        for f in mb.faces:
            for k in range(1, len(f) - 1):
                tris.append((v[f[0]], v[f[k]], v[f[k + 1]]))
    return np.asarray(tris, dtype=float)


@pytest.fixture(scope="module")
def geometry(built):
    return {bid: _triangles(c) for bid, c in built.items()}


def _frame(rec):
    """記録から (板の中心（建物座標）, 正面, 右（正面から見て）)。"""
    p = rec["pos"]
    f = rec["facing"]
    center = np.array([p["x"], p["z"], p["y"]])
    front = np.array([f["x"], f["z"], 0.0])
    right = np.array([front[1], -front[0], 0.0])
    return center, front, right


def _outside(tri, center, front, right, rec, back=0.01):
    """看板そのもの（板・枠・文字・吊り棒）の三角形を除いた三角形の束。"""
    w, h = rec["w"], rec["h"]
    rel = tri - center
    u = rel @ right
    f = rel @ front
    z = rel[..., 2]
    if rec["kind"] == "wall":
        inside = (np.abs(u) <= w * 0.5 + 0.06) & (f >= -back) & (f <= WALL_OUT + 0.02) \
            & (np.abs(z) <= h * 0.5 + 0.06)
    else:
        inside = (np.abs(u) <= w * 0.5 + 0.06) & (np.abs(f) <= shell.HANG_HALF + 0.03) \
            & (z >= -h * 0.5 - 0.06) & (z <= h * 0.5 + 1.0)
    return tri[~inside.all(axis=1)]


def _records(built):
    for bid, c in built.items():
        for rec in c.signs:
            yield bid, rec


def test_every_sign_empty_has_text(built):
    for bid, c in built.items():
        assert signs.unlabeled(c) == [], bid
        assert c.signs, "%s に看板が無い" % bid
        for rec in c.signs:
            assert rec["ja"].strip() and rec["en"].strip(), (bid, rec["empty"])
            assert rec["tris"] > 0, (bid, rec["ja"])
        assert c.text_tris() == sum(r["tris"] for r in c.signs)


def test_sign_empties_sit_on_the_sign(built):
    for bid, c in built.items():
        empties = dict(c.empties)
        for rec in c.signs:
            if rec["empty"] is None:
                continue
            x, y, z = empties[rec["empty"]]
            p = rec["pos"]
            assert (round(x, 3), round(z, 3), round(y, 3)) == (p["x"], p["y"], p["z"]), \
                (bid, rec["empty"])


def test_text_material_reads_against_its_plate(built):
    for bid, rec in _records(built):
        assert signs.contrast(rec["text"], rec["plate"]) >= 3.0, (bid, rec["ja"], rec["plate"])


def test_nothing_blocks_the_text(built, geometry):
    """文字の正面（吊り看板は両面）に壁や棚が無い。壁の方を向いた看板・壁にめり込んだ看板を拾う。"""
    bad = []
    for bid, rec in _records(built):
        center, front, right = _frame(rec)
        tri = _outside(geometry[bid], center, front, right, rec)
        face = WALL_OUT if rec["kind"] == "wall" else shell.HANG_HALF
        for side in ((1,) if rec["faces"] == 1 else (1, -1)):
            d = front * side
            for du in (-rec["w"] / 3.0, 0.0, rec["w"] / 3.0):
                o = center + right * du + d * (face + signtext.LIFT + 0.01)
                t = first_hit(o, d, tri)
                if t < FRONT_CLEAR:
                    bad.append("%s %s side=%d u=%+.2f: %.2f m" % (bid, rec["ja"], side, du, t))
    assert bad == []


def test_wall_signs_are_backed_by_a_surface(built, geometry):
    """壁の看板の板の後ろに壁（か棚の側板）がある。宙に浮いた板を拾う。"""
    bad = []
    for bid, rec in _records(built):
        if rec["kind"] != "wall":
            continue
        center, front, right = _frame(rec)
        tri = _outside(geometry[bid], center, front, right, rec, back=0.002)
        hu, hz = rec["w"] * 0.5 - 0.05, rec["h"] * 0.5 - 0.03
        samples = [(0.0, 0.0), (-hu, -hz), (hu, -hz), (-hu, hz), (hu, hz)]
        backed = 0
        for du, dz in samples:
            o = center + right * du + np.array([0.0, 0.0, dz]) + front * 0.01
            if first_hit(o, -front, tri) <= BACKED:
                backed += 1
        if backed < 4:
            bad.append("%s %s: %d / %d" % (bid, rec["ja"], backed, len(samples)))
    assert bad == []


def test_hanging_signs_hang_from_a_ceiling(built, geometry):
    """板の上端から真上に drop 行った所（吊り元）に天井の面がある。宙で止まった吊り棒を拾う。"""
    bad = []
    for bid, rec in _records(built):
        if rec["kind"] != "hanging":
            continue
        center, _front, _right = _frame(rec)
        o = center + np.array([0.0, 0.0, rec["h"] * 0.5 + 0.001])
        t = first_hit(o, np.array([0.0, 0.0, 1.0]), geometry[bid])
        if abs(t - rec["drop"]) > ROD_TOL:
            bad.append("%s %s: drop %.2f / ceiling %.2f m" % (bid, rec["ja"], rec["drop"], t))
    assert bad == []


def test_library_counter_sign_hangs_over_the_counter(built):
    """#181 その3: 貸出・返却の看板は吊り看板で、両面に文字がある。"""
    recs = [r for r in built["library"].signs if r["ja"] == "貸出・返却"]
    assert len(recs) == 1
    rec = recs[0]
    assert rec["kind"] == "hanging" and rec["faces"] == 2
    assert rec["en"] == "Circulation Desk"


def test_dorm_signs_do_not_name_the_university(built):
    """寮は共立メンテナンスの運営で大学の直営ではない（kcd_route/dorm.py の NOT_UNIVERSITY）。"""
    words = ("理科", "大学", "TUS", "Tokyo University", "University", "Campus")
    for rec in built["dorm"].signs:
        text = rec["ja"] + "\n" + rec["en"]
        assert not [w for w in words if w in text], text


def test_text_triangles_count_against_the_building(built):
    for bid, c in built.items():
        assert 0 < c.text_tris() < c.tris(), bid


def test_seminar_and_lab_signs_are_numbered(built):
    lecture = [r["ja"] for r in built["lecture"].signs if r["ja"].startswith("演習室")]
    assert lecture == ["演習室 %d" % (i + 1) for i in range(len(lecture))] and lecture
    for bid in ("lab1", "lab2"):
        labs = [r["ja"] for r in built[bid].signs if r["ja"].startswith("実験室")]
        assert labs == ["実験室 %d" % (i + 1) for i in range(len(labs))] and labs, bid
        assert any(r["ja"] == "準備室" for r in built[bid].signs), bid


def test_facing_matches_the_angle_convention():
    """記録の facing は kit.T の正面 (-sin ang, cos ang) を Unity の並び（y が上）にしたもの。"""
    class _C:
        signs = []
    rec = signs._record(_C, None, "wall", "a", "b", 1, (1.0, 2.0, 3.0), math.pi * 0.5,
                        1.0, 0.5, 1, "sign_plate_blue", "plastic_white", "center")
    assert rec["pos"] == {"x": 1.0, "y": 3.0, "z": 2.0}
    assert rec["facing"] == {"x": -1.0, "y": 0.0, "z": 0.0}
