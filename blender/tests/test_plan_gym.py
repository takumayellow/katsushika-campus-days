"""体育館のプラン（kcd_interior.plan_gym）: スタブで組み、Empty・座面・看板・カメラ・三角数・外形を確かめる。"""

import json
import math
import re
from pathlib import Path

import pytest

import stubs
from kcd_interior import plan_gym_dims as D
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 21000
N_SIGNS = 13
STEP_MAX = 0.4         # Unity の stepOffset
TOP = D.C_HALL + 0.2   # 屋根の板より上に何も出さない

# 仕様（docs/ref/interiors/gym/spec.md）の値。作った面の頂点と材質をこれと比べる
SPEC_TRAIN_COL_X = (4.4, 6.6)          # §6 トレーニングルームの丸柱 2 列
SPEC_TRAIN_COL_Y = (15.5, 21.5, 27.5)
SPEC_COL_R = 0.30                      # φ0.6
SPEC_VENT_R, SPEC_VENT_STEP, SPEC_VENT_Z = 0.25, 1.55, 4.3   # §1-4 ギャラリーの鼻先の丸い吹出口
SPEC_RAIL_H = 1.10                     # §1-4 ギャラリーの手すり
SPEC_FOLD_Y = (10.6, 34.5)             # §6 壁 B の床際の折り畳みゴール
SPEC_BOARD_GLASS, SPEC_BOARD_FRAME = "glass_partition", "metal_white"   # §5 バックボードの板と枠
SPEC_RING = "gym_line_red"             # §5 リング
SPEC_RIM_Z = 3.05                      # §5 リングの高さ
SPEC_END_BOARD_Y = (6.65, 37.05)       # §5 W_S・W_N の鼻先の吊りゴールのボード面
SPEC_JOINT_PITCH = 2.4                 # §5 木の壁の金属の目地の間隔
SPEC_SPEAKER_Z = 9.0                   # §5 壁付けスピーカー 3 台の中心の高さ
GALLERY_EDGE_X = -25.4                 # ギャラリーの西の辺（手すりの線）
GALLERY_EDGE_Y = (6.7, 37.0)           # ギャラリーの南・北の辺


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
        c = Ctx(specs["gym"], seed=SEED)
        registry.get("gym").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def _faces(c):
    for mb in c.builders():
        for f in mb.faces:
            yield [mb.verts[i] for i in f]


def _in_polygon(poly, x, y):
    inside = False
    for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
        if (ay > y) != (by > y) and x < ax + (y - ay) * (bx - ax) / (by - ay):
            inside = not inside
    return inside


def test_spawn_and_exit_are_inside_the_vestibule(built):
    x0, x1, y0, y1 = D.VEST
    assert D.DOOR[0] < D.SPAWN[0] < D.DOOR[1]
    assert y0 < D.EXIT[1] < D.SPAWN[1] < y1


def test_pois_are_in_table_order(built):
    got = _empties(built, "poi_gym_")
    assert [n for n, _ in got] == ["poi_gym_%s" % p[0] for p in D.POIS]
    for (_, pos), (_name, x, y, z) in zip(got, D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_gym_")
    assert [n for n, _ in got] == ["npc_gym_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def test_seats_follow_the_table(built):
    names = [n for n, _ in built.empties if re.fullmatch(r"seat_gym_\d\d", n)]
    assert names == ["seat_gym_%02d" % k for k in range(1, len(D.SEATS) + 1)]
    assert built.seats == len(D.SEATS) == 10
    pos = dict(built.empties)
    for k, (_kind, x, y, z) in enumerate(D.SEATS, 1):
        name = "seat_gym_%02d" % k
        assert pos[name] == pytest.approx((x, y, z), abs=0.02)
        assert name + "_a0" in pos


def test_signs_are_in_table_order(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == N_SIGNS
    assert ja[:3] == ["メインアリーナ", "メインアリーナ", "トレーニングルーム"]
    assert ja[-1] == "AED"
    assert [r["empty"] for r in built.signs if r["empty"]] == [
        "sign_gym_%d" % k for k in range(1, N_SIGNS + 1)]
    assert all(r["en"] for r in built.signs)


def test_six_cameras(built):
    assert [cam[0] for cam in built.cams] == [c[0] for c in D.CAMS]
    assert len(built.cams) == 6


def test_triangles_fit_the_cap_and_top(built):
    assert built.tris() <= INT_CAP
    assert max(p[2] for f in _faces(built) for p in f) <= TOP


def test_everything_stays_on_the_footprint(built):
    """全頂点が外形（FOOT）の外へ 0.35 m 以上はみ出さない（壁の厚み分は許す）。"""
    foot = [tuple(p) for p in D.FOOT]
    xs = [p[0] for p in foot]
    ys = [p[1] for p in foot]
    tol = 0.35
    for mb in built.builders():
        for v in mb.verts:
            assert min(xs) - tol <= v[0] <= max(xs) + tol, v
            assert min(ys) - tol <= v[1] <= max(ys) + tol, v


def test_pois_npcs_and_cameras_are_inside_the_walls(built):
    foot = [tuple(p) for p in D.FOOT]
    pts = [p[1:3] for p in D.POIS] + [n[:2] for n in D.NPCS] + [s[1:3] for s in D.SEATS]
    pts += [cam[1][:2] for cam in D.CAMS]
    assert [p for p in pts if not _in_polygon(foot, *p)] == []


def test_stair_steps_are_climbable():
    rise = D.Z2 / D.STAIR_N
    assert 0.0 < rise <= STEP_MAX
    assert D.STAIR_TREAD >= 0.25


def test_stand_tiers_step_by_climbable_rises():
    tops = [t[2] for t in D.TIERS]
    levels = [D.Z_FRONT] + tops + [D.Z2]
    for a, b in zip(levels, levels[1:]):
        assert 0.0 <= b - a <= 2 * 0.21 + 1e-6, (a, b)


def test_quest_and_collectible_destinations_exist(built):
    data = Path(CAMPUS_JSON).parent.parent.parent / "unity" / "KatsushikaCampusDays" / "Assets" / "Data"
    coll = json.loads((data / "Collectibles" / "collectibles.json").read_text(encoding="utf-8"))
    targets = {i["position"] for i in coll["collectibles"]
               if i.get("building") == "gym" and str(i.get("position", "")).startswith("poi_")}
    names = {n for n, _ in built.empties}
    assert targets and targets <= names, targets - names


# ---- 作った面の頂点と材質から測る ----

def _mfaces(c):
    """全部の面を (片の名前, 材質, 頂点の並び) で返す。"""
    for mb in c.builders():
        for f, m in zip(mb.faces, mb.face_mat):
            yield mb.name, mb.mat_names[m], [mb.verts[i] for i in f]


def _wall_faces(c):
    return [(mat, pts) for name, mat, pts in _mfaces(c) if name == c.wall.name]


def _nrm(pts):
    """面の単位法線（Newell 法）。"""
    nx = ny = nz = 0.0
    for (x0, y0, z0), (x1, y1, z1) in zip(pts, pts[1:] + pts[:1]):
        nx += (y0 - y1) * (z0 + z1)
        ny += (z0 - z1) * (x0 + x1)
        nz += (x0 - x1) * (y0 + y1)
    n = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
    return (nx / n, ny / n, nz / n)


def _ctr(pts):
    return tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))


def _bbox(pts):
    return (min(p[0] for p in pts), min(p[1] for p in pts), min(p[2] for p in pts),
            max(p[0] for p in pts), max(p[1] for p in pts), max(p[2] for p in pts))


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _floor_tops(c, z0, z1):
    """floor_gym の上向きの面で高さが z0〜z1 のもの: [(z, 材質, xy の多角形)]。"""
    out = []
    for name, mat, pts in _mfaces(c):
        if name == c.floor.name and _nrm(pts)[2] > 0.99 and z0 < pts[0][2] < z1:
            out.append((pts[0][2], mat, [p[:2] for p in pts]))
    return out


def _floor_at(tops, x, y):
    """(x, y) で一番上に見える床の材質（無ければ None）。"""
    hits = [(z, mat) for z, mat, poly in tops if _in_polygon(poly, x, y)]
    return max(hits)[1] if hits else None


def _train_columns(c):
    """トレーニングルームの中の gym_column_concrete の面を柱ごとにまとめ、(cx, cy, r, z0, z1) を返す。"""
    x0, y0, x1, y1 = D.TRAIN
    groups = []
    for _name, mat, pts in _mfaces(c):
        cx, cy, _ = _ctr(pts)
        if mat != "gym_column_concrete" or not (x0 < cx < x1 and y0 < cy < y1):
            continue
        for seed, vs in groups:
            if math.hypot(seed[0] - cx, seed[1] - cy) < 1.0:
                vs.extend(pts)
                break
        else:
            groups.append(((cx, cy), list(pts)))
    out = []
    for _seed, vs in groups:
        gx = sum(p[0] for p in vs) / len(vs)
        gy = sum(p[1] for p in vs) / len(vs)
        r = sum(math.hypot(p[0] - gx, p[1] - gy) for p in vs) / len(vs)
        out.append((gx, gy, r, min(p[2] for p in vs), max(p[2] for p in vs)))
    return sorted(out)


def _plane_gaps(pts, faces):
    """faces のうち面 pts と同じ向きで中心の投影を含む面について、pts の中心がその面より前に出る距離。"""
    c, n = _ctr(pts), _nrm(pts)
    out = []
    for q in faces:
        if _dot(_nrm(q), n) < 0.99:
            continue
        d = _dot([c[i] - q[0][i] for i in range(3)], n)
        p = [c[i] - d * n[i] for i in range(3)]
        b = _bbox(q)
        if all(b[i] - 1e-6 <= p[i] <= b[i + 3] + 1e-6 for i in range(3)):
            out.append(d)
    return out


def _backed_by(pts, faces, dmax):
    """面 pts の中心の dmax 以内の後ろに、同じ向きで中心の投影を含む面が faces にあるか。"""
    return any(0.0 < d <= dmax for d in _plane_gaps(pts, faces))


def test_training_room_floor_is_yellow_resin(built):
    """トレーニングルームの床は黄色の樹脂。ゴムのマットはパワーラックの足元だけ（spec §5）。"""
    tops = _floor_tops(built, D.Z2, D.Z2 + 0.05)
    x0, y0, x1, y1 = D.TRAIN
    got = [_floor_at(tops, x0 + 0.3 + (x1 - x0 - 0.6) * i / 8, y0 + 0.3 + (y1 - y0 - 0.6) * j / 16)
           for i in range(9) for j in range(17)]
    assert set(got) <= {"gym_floor_resin_yellow", "gym_rubber_black"}, set(got)
    assert got.count("gym_floor_resin_yellow") >= 0.9 * len(got)
    # パワーラック（高さ 2 m 以上の支柱）の足元はゴム。ゴムはその 1 m の外へ出ない
    posts = [p for name, mat, pts in _mfaces(built)
             if name.endswith("_train") and mat == "gym_rail_metal"
             and max(q[2] for q in pts) - min(q[2] for q in pts) >= 2.0 for p in pts]
    assert posts
    bx0, by0, _, bx1, by1, _ = _bbox(posts)
    for x, y in ((bx0 + 0.03, by0 + 0.03), (bx1 - 0.03, by0 + 0.03),
                 (bx0 + 0.03, by1 - 0.03), (bx1 - 0.03, by1 - 0.03)):
        assert _floor_at(tops, x, y) == "gym_rubber_black", (x, y)
    rubber = [p for _z, mat, poly in tops if mat == "gym_rubber_black" for p in poly
              if x0 <= p[0] <= x1 and y0 <= p[1] <= y1]
    assert rubber
    assert bx0 - 1.0 <= min(p[0] for p in rubber) and max(p[0] for p in rubber) <= bx1 + 1.0
    assert by0 - 1.0 <= min(p[1] for p in rubber) and max(p[1] for p in rubber) <= by1 + 1.0


def test_sub_arena_floor_is_wood(built):
    """サブアリーナの床はメープルの木（spec §5 gym_floor_wood）。"""
    tops = _floor_tops(built, 0.005, 0.05)
    x0, y0, x1, y1 = D.SUB
    got = {_floor_at(tops, x0 + (x1 - x0) * fx, y0 + (y1 - y0) * fy)
           for fx in (0.2, 0.5, 0.8) for fy in (0.2, 0.5, 0.8)}
    assert got == {"gym_floor_wood"}


def test_west_wall_upper_part_is_fair_faced_concrete(built):
    """西壁 G の室内側の面は、z 3.0 から上が打放しのパネル（spec §5 gym_concrete_panel）。"""
    mats = {mat for mat, pts in _wall_faces(built)
            if _nrm(pts)[0] > 0.99 and min(p[2] for p in pts) >= 3.0 - 1e-6
            and all(abs(p[0] - D.west_x(p[1])) < 0.02 for p in pts)}
    assert mats == {"gym_concrete_panel"}


def test_north_wall_is_wood_on_the_arena_side(built):
    """W_N のアリーナ側は z 3.0 までが木の下部、その上が木の上部。用具庫側は打放し（spec §1-6・§5）。"""
    def along(y, pts):
        return all(abs(p[1] - y) < 0.005 and -27.81 <= p[0] <= D.ARENA[2] + 0.01 for p in pts)

    wall = _wall_faces(built)
    south = [(mat, pts) for mat, pts in wall if _nrm(pts)[1] < -0.99 and along(D.ARENA[3], pts)]
    lower = {mat for mat, pts in south if max(p[2] for p in pts) <= 3.0 + 1e-6}
    upper = {mat for mat, pts in south if min(p[2] for p in pts) >= 3.0 - 1e-6}
    assert lower == {"gym_wall_wood_lower"}
    assert upper == {"gym_wall_wood_upper"}
    assert {mat for mat, _ in south} == lower | upper
    store = {mat for mat, pts in wall if _nrm(pts)[1] > 0.99 and along(D.STORE[1], pts)}
    assert store == {"gym_concrete_panel"}


def test_north_passage_far_wall_is_dark_behind_the_opening(built):
    """W_N の東の開口（北の通路へ）の先に見える奥の壁は暗い色。P1 では開口の中が暗く写る（spec §1-6）。"""
    x0, _y0, x1, y1 = D.PASS
    far = [(mat, pts) for mat, pts in _wall_faces(built)
           if _nrm(pts)[1] < -0.99 and all(abs(p[1] - y1) < 0.005 for p in pts)
           and all(x0 - 0.01 <= p[0] <= x1 + 0.01 for p in pts)]
    assert far
    assert {mat for mat, _ in far} == {"gym_wall_darkgrey"}
    xs = [p[0] for _, pts in far for p in pts]
    assert min(xs) <= D.N_OPEN[0] and max(xs) >= D.N_OPEN[1]


def _north_wall_parts(c, mat):
    """W_N のアリーナ側の面から手前（-Y）へ 0.5 m 以内に出ている mat の面。"""
    yn = D.ARENA[3]
    return [pts for m, pts in _wall_faces(c)
            if m == mat and all(yn - 0.5 <= p[1] <= yn + 1e-6 for p in pts)]


def _north_joints(c):
    """W_N の金属の目地の、アリーナを向く面（-Y）。"""
    return [pts for pts in _north_wall_parts(c, "gym_joint_metal") if _nrm(pts)[1] < -0.99]


def test_north_wall_has_metal_joints_every_2_4_m(built):
    """W_N の木の壁に、金属の目地が 2.4 m おきに縦に通る。上部は全幅、下部は扉の開口を避ける（spec §5）。"""
    joints = _north_joints(built)
    upper = sorted(min(p[0] for p in pts) for pts in joints if min(p[2] for p in pts) >= 3.0 - 1e-6)
    lower = [pts for pts in joints if max(p[2] for p in pts) <= 3.0 + 1e-6]
    assert len(upper) >= 8
    assert all(abs(b - a - SPEC_JOINT_PITCH) < 0.01 for a, b in zip(upper, upper[1:])), upper
    assert lower
    gaps = [D.N_STORE_CLOSED, D.N_STORE_OPEN, D.N_OPEN]
    for pts in lower:
        x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
        assert any(abs(x0 - u) < 0.01 for u in upper), x0
        assert not any(x1 > g0 and x0 < g1 for g0, g1 in gaps), (x0, x1)


def test_north_wall_has_three_speakers_between_the_joints(built):
    """W_N の木の壁の高い所に、壁付けスピーカーが 3 台、中心の高さ z 9.0 で並び、目地にかからない（spec §5）。"""
    speakers = [pts for pts in _north_wall_parts(built, "gym_vent_dark")
                if _nrm(pts)[1] < -0.99 and 8.0 < _ctr(pts)[2] < 10.0]
    assert len(speakers) == 3
    for pts in speakers:
        assert _ctr(pts)[2] == pytest.approx(SPEC_SPEAKER_Z, abs=0.01)
        assert not any(min(p[0] for p in j) < max(p[0] for p in pts)
                       and max(p[0] for p in j) > min(p[0] for p in pts)
                       for j in _north_joints(built)), _ctr(pts)


def test_north_wall_has_a_round_clock_above_the_gallery(built):
    """W_N の木の壁に、白い丸い時計がアリーナを向いて掛かる。P1 では観覧席の手すりよりずっと上に写る（spec §5）。
    壁 B のホワイエ側へ向けて掛けていた時計は無い。"""
    dials = [pts for pts in _north_wall_parts(built, "plastic_white")
             if _nrm(pts)[1] < -0.99 and len(pts) >= 8]
    assert len(dials) == 1
    c = _ctr(dials[0])
    assert all(abs(math.dist(p, c) - 0.3) < 0.01 for p in dials[0]), c
    assert D.Z2 + SPEC_RAIL_H + 1.0 < c[2] < D.C_HALL - 1.0
    assert -27.8 < c[0] < D.WALL_B[0]
    assert not [1 for mat, pts in _wall_faces(built)      # 3 頂点の面は看板の文字
                if mat == "plastic_white" and len(pts) >= 4
                and all(abs(p[0] - D.WALL_B[1]) < 0.1 for p in pts)]


def test_training_east_wall_is_orange_with_dark_bands_by_the_columns(built):
    """トレーニングルームの東壁はオレンジで、東の列の柱の後ろはダークグレー（spec §5・§6）。"""
    def x_in(y):
        return D.edge_point(8, 1, y)[0] - 0.30

    wall = _wall_faces(built)
    y_a, y_b = D.FOOT[8][1], D.TRAIN[3]
    east = [(mat, pts) for mat, pts in wall
            if _nrm(pts)[0] < -0.99 and max(p[2] for p in pts) > D.Z2 + 0.01
            and all(abs(p[0] - x_in(p[1])) < 0.02 and y_a - 0.01 <= p[1] <= y_b + 0.01
                    for p in pts)]
    assert {mat for mat, _ in east} == {"gym_accent_orange", "gym_wall_darkgrey"}
    cols = _train_columns(built)
    assert cols
    east_cols = [col for col in cols if col[0] > max(c[0] for c in cols) - 0.1]
    assert len(east_cols) >= 2
    for _cx, cy, r, _z0, _z1 in east_cols:
        assert any(mat == "gym_wall_darkgrey" and min(p[1] for p in pts) <= cy - r
                   and max(p[1] for p in pts) >= cy + r and min(p[2] for p in pts) <= D.Z2
                   and max(p[2] for p in pts) >= D.C_2F - 0.01 for mat, pts in east), cy
    # 南の端（連絡通路の上、y 11.9〜14.8）の室内側もオレンジ
    conn = {mat for mat, pts in wall
            if _nrm(pts)[0] < -0.99
            and all(abs(p[0] - D.TRAIN[2]) < 0.005 and p[2] >= D.Z2 - 0.01
                    and D.TRAIN[1] - 1e-6 <= p[1] <= y_a + 1e-6 for p in pts)}
    assert conn == {"gym_accent_orange"}


def test_training_room_has_two_rows_of_round_columns(built):
    """φ0.6 の丸柱が 2 列 × 3 本、床から天井まで立ち、器具・照明・NPC・POI・カメラの線に掛からない（spec §6）。"""
    cols = _train_columns(built)
    want = sorted((x, y) for x in SPEC_TRAIN_COL_X for y in SPEC_TRAIN_COL_Y)
    assert len(cols) == len(want)
    for (cx, cy, r, z0, z1), xy in zip(cols, want):
        assert (cx, cy) == pytest.approx(xy, abs=0.02)
        assert r == pytest.approx(SPEC_COL_R, abs=0.02)
        assert (z0, z1) == pytest.approx((D.Z2, D.C_2F), abs=0.02)
    x0, y0, x1, y1 = D.TRAIN
    others = [_bbox(pts) for name, mat, pts in _mfaces(built)
              if name.endswith("_train") or (mat == "gym_light_linear"
                                             and x0 < _ctr(pts)[0] < x1 and y0 < _ctr(pts)[1] < y1)]
    for cx, cy, r, z0, z1 in cols:
        hit = [b for b in others if b[0] < cx + r and b[3] > cx - r and b[1] < cy + r
               and b[4] > cy - r and b[2] < z1 and b[5] > z0]
        assert hit == [], (cx, cy)
    people = [p for n, p in built.empties if n.startswith(("npc_gym_", "poi_gym_"))
              and x0 < p[0] < x1 and y0 < p[1] < y1 and abs(p[2] - D.Z2) < 0.05]
    assert people
    for cx, cy, r, _z0, _z1 in cols:
        assert min(math.hypot(p[0] - cx, p[1] - cy) for p in people) >= r + 0.3, (cx, cy)
    (ax, ay, _), (bx, by, _) = next(cam[1:3] for cam in built.cams if cam[0] == "training")
    for cx, cy, r, _z0, _z1 in cols:
        t = ((cx - ax) * (bx - ax) + (cy - ay) * (by - ay)) / ((bx - ax) ** 2 + (by - ay) ** 2)
        t = max(0.0, min(1.0, t))
        assert math.hypot(ax + (bx - ax) * t - cx, ay + (by - ay) * t - cy) >= r + 0.2, (cx, cy)


def test_gallery_nose_has_round_vents_on_each_side(built):
    """ギャラリーの鼻先の南・西・北に、φ0.5 の丸い吹出口が 1.55 m おきに中心 z 4.3 で並ぶ（spec §1-4・§5）。"""
    wall = _wall_faces(built)
    vents = [pts for mat, pts in wall
             if mat == "gym_vent_dark" and all(3.8 < p[2] < D.Z2 for p in pts)]
    nose = [pts for mat, pts in wall if mat == "gym_concrete_nose"]
    assert vents
    rows = {}
    for pts in vents:
        c = _ctr(pts)
        assert all(abs(math.dist(p, c) - SPEC_VENT_R) < 0.01 for p in pts), c
        assert c[2] == pytest.approx(SPEC_VENT_Z, abs=0.01)
        assert _backed_by(pts, nose, 0.02), c
        rows.setdefault(tuple(round(v) for v in _nrm(pts)), []).append(c)
    assert set(rows) == {(0, 1, 0), (1, 0, 0), (0, -1, 0)}
    for n, cs in rows.items():
        s = sorted(c[1] if n[0] else c[0] for c in cs)
        assert len(s) >= 2
        assert all(abs(b - a - SPEC_VENT_STEP) < 0.01 for a, b in zip(s, s[1:])), (n, s)


def _folded_goals(c):
    """床から 2.85 m より下に収まるゴールの面（＝たたんだゴール）を、SPEC_FOLD_Y の近い方ごとにまとめる。"""
    low = [(mat, pts) for name, mat, pts in _mfaces(c)
           if name.endswith("_hoops") and max(p[2] for p in pts) < 2.85]
    return {fy: [(mat, pts) for mat, pts in low
                 if min(SPEC_FOLD_Y, key=lambda v: abs(v - _ctr(pts)[1])) == fy]
            for fy in SPEC_FOLD_Y}


def _yz_overlap(pts, rect):
    """面 pts の (y, z) の外接矩形と rect = (y0, z0, y1, z1) が重なる面積。"""
    b = _bbox(pts)
    dy = min(b[4], rect[2]) - max(b[1], rect[0])
    dz = min(b[5], rect[3]) - max(b[2], rect[1])
    return max(0.0, dy) * max(0.0, dz)


def test_folded_goals_stand_against_wall_b(built):
    """壁 B の床際に、たたんだゴールが 2 基ある（spec §6 ゴール）。"""
    goals = _folded_goals(built)
    xs = [p[0] for grp in goals.values() for _, pts in grp for p in pts]
    assert xs
    assert D.WALL_B[0] - 1.2 <= min(xs) and max(xs) <= D.WALL_B[0] + 1e-6
    for fy, grp in goals.items():
        ys = [p[1] for _, pts in grp for p in pts]
        assert grp and min(ys) < fy < max(ys) and max(ys) - min(ys) < 2.5, fy
        assert min(p[2] for _, pts in grp for p in pts) < 0.1
        assert max(ys) < D.B_DOOR_S[0] or min(ys) > D.B_DOOR_S[1]


def test_folded_goal_board_is_framed_glass_and_the_ring_is_red(built):
    """たたんだゴールの板は白い金属の枠に入ったガラスで、コートから向こうが透けて見える。
    板の表より前に出るのは赤いリングと付け根、白い網だけ（spec §5）。"""
    for fy, grp in _folded_goals(built).items():
        court = [(mat, pts) for mat, pts in grp if _nrm(pts)[0] < -0.99]   # コート（-X）向きの面
        glass = [pts for mat, pts in court if mat == SPEC_BOARD_GLASS]
        frame = [pts for mat, pts in court if mat == SPEC_BOARD_FRAME]
        assert glass and frame, fy
        g = _bbox([p for pts in glass for p in pts])
        f = _bbox([p for pts in frame for p in pts])
        assert f[1] < g[1] < g[4] < f[4] and f[2] < g[2] < g[5] < f[5], (fy, g, f)
        assert g[4] - g[1] > 1.5 and g[5] - g[2] > 0.8, (fy, g)
        # コートから見てガラスの範囲を隠す不透明な面（前でも後ろでも）は 4 分の 1 未満
        rect = (g[1], g[2], g[4], g[5])
        hidden = sum(_yz_overlap(pts, rect) for mat, pts in court if mat != SPEC_BOARD_GLASS)
        assert hidden < 0.25 * (rect[2] - rect[0]) * (rect[3] - rect[1]), (fy, hidden)
        face_x = min(p[0] for pts in frame for p in pts)          # 板の表の面
        ahead = {mat for mat, pts in grp if _ctr(pts)[0] < face_x - 1e-6}
        assert ahead == {SPEC_RING, "net_white"}, (fy, ahead)


def _hung_boards(c):
    """吊りゴールのバックボードのガラスのうち、コートの中央を向く表の面（床から 2.85 m より上のもの）。"""
    out = []
    for name, mat, pts in _mfaces(c):
        if not name.endswith("_hoops") or mat != SPEC_BOARD_GLASS:
            continue
        n, (cx, cy, cz), b = _nrm(pts), _ctr(pts), _bbox(pts)
        wide = math.hypot(b[3] - b[0], b[4] - b[1]) > 1.5
        toward_court = n[0] * (D.COURT_C[0] - cx) + n[1] * (D.COURT_C[1] - cy) > 0
        if cz > 2.85 and abs(n[2]) < 0.01 and wide and toward_court:
            out.append(pts)
    return out


def _on_board(pts, n, o):
    """面 pts の頂点を、板の中心 o・向き n から測った (前, 幅, 高さ) にする。前はコート側が正。"""
    return [((p[0] - o[0]) * n[0] + (p[1] - o[1]) * n[1],
             (p[0] - o[0]) * -n[1] + (p[1] - o[1]) * n[0],
             p[2]) for p in pts]


def test_eight_goals_hang_at_both_ends_and_both_sides(built):
    """吊りゴールは 8 基: コートの南北の端に 1 基ずつ、G 側と B 側の横向きに 3 基ずつ（spec §5 ゴール）。
    spec のどれにも当たらない高さ 6.8 m のゴールは無い。南北の端は観覧席の前縁（y 6.7 / 37.0）に
    ぶつからない分だけ、spec の板の面（6.65 / 37.05）より 0.3 m 以内でコート寄りに置く。"""
    cx = D.COURT_C[0]
    want = [(cx, SPEC_END_BOARD_Y[0], 0, 1), (cx, SPEC_END_BOARD_Y[1], 0, -1)]
    want += [(D.HOOP_G_X, y, 1, 0) for y in D.CROSS_Y] + [(D.HOOP_B_X, y, -1, 0) for y in D.CROSS_Y]
    boards = _hung_boards(built)
    assert len(boards) == len(want) == 8
    for x, y, nx, ny in want:
        hits = [pts for pts in boards
                if math.hypot(_ctr(pts)[0] - x, _ctr(pts)[1] - y) < 0.3
                and nx * _nrm(pts)[0] + ny * _nrm(pts)[1] > 0.99]
        assert len(hits) == 1, (x, y)


def test_hung_goal_board_is_framed_glass_and_the_ring_is_red(built):
    """吊りゴールの板は白い金属の枠に入ったガラスで、コートから向こうが透けて見える。
    板の表より前に出るのは赤いリングと付け根、白い網だけで、リングは高さ 3.05 m（spec §5）。
    不透明な白い板とオレンジのリングはどのゴールにも残らない。"""
    mats = {mat for name, mat, _ in _mfaces(built) if name.endswith("_hoops")}
    assert mats == {SPEC_BOARD_GLASS, SPEC_BOARD_FRAME, SPEC_RING, "net_white", "gym_truss_steel"}
    hoops = [(mat, pts) for name, mat, pts in _mfaces(built)
             if name.endswith("_hoops") and max(p[2] for p in pts) >= 2.85]
    for glass in _hung_boards(built):
        n, o = _nrm(glass), _ctr(glass)
        g = _bbox(_on_board(glass, n, o))
        assert g[4] - g[1] > 1.5 and g[5] - g[2] > 0.8, (o, g)
        assert o[2] == pytest.approx(SPEC_RIM_Z + 0.375, abs=0.02), o      # リングは板の下端から 0.15 m 上
        local = []
        for mat, pts in hoops:
            q = _on_board(pts, n, o)
            cd, cu, cz = _ctr(q)
            if abs(cu) <= 1.2 and -0.3 <= cd <= 1.0 and abs(cz - o[2]) <= 1.0:
                local.append((mat, q, _dot(_nrm(pts), n)))
        court = [(mat, q) for mat, q, d in local if d > 0.99]               # コート向きの面
        frame = [q for mat, q in court if mat == SPEC_BOARD_FRAME and abs(_ctr(q)[0] - 0.01) < 0.005]
        assert len(frame) == 4, o
        f = _bbox([p for q in frame for p in q])
        assert f[1] < g[1] < g[4] < f[4] and f[2] < g[2] < g[5] < f[5], (o, g, f)
        # コートから見てガラスの範囲を隠す不透明な面（前でも後ろでも）は 4 分の 1 未満
        rect = (g[1], g[2], g[4], g[5])
        hidden = sum(_yz_overlap(q, rect) for mat, q in court if mat != SPEC_BOARD_GLASS)
        assert hidden < 0.25 * (rect[2] - rect[0]) * (rect[3] - rect[1]), (o, hidden)
        face_d = max(p[0] for q in frame for p in q)                        # 板の表の面
        ahead = [(mat, q) for mat, q, _d in local if _ctr(q)[0] > face_d + 1e-6]
        assert {mat for mat, _ in ahead} == {SPEC_RING, "net_white"}, (o, ahead)
        ring = [p for mat, q in ahead if mat == SPEC_RING for p in q]
        assert max(p[0] for p in ring) - face_d > 0.4, o                    # リングは板から張り出す
        assert all(abs(p[2] - SPEC_RIM_Z) < 0.12 for p in ring), o


def test_sub_arena_beams_cross_diagonally_with_downlights_between(built):
    """サブアリーナの梁は幅広の斜め交差で、ダウンライトは梁の間の天井にある（spec §5・§6）。"""
    x0, y0, x1, y1 = D.SUB
    faces = [(mat, pts) for _name, mat, pts in _mfaces(built)
             if all(x0 - 1e-6 <= p[0] <= x1 + 0.3 + 1e-6 and y0 - 1e-6 <= p[1] <= y1 + 1e-6
                    for p in pts)]
    beams = [pts for mat, pts in faces if mat == "gym_beam_beige"]
    sides = []
    for pts in beams:
        n = _nrm(pts)
        u = (-n[1], n[0])
        span = [p[0] * u[0] + p[1] * u[1] for p in pts]
        if abs(n[2]) < 0.01 and max(span) - min(span) >= 3.0 and min(abs(u[0]), abs(u[1])) > 0.2:
            sides.append((n, pts))
    assert {math.copysign(1.0, n[0] * n[1]) for n, _ in sides} == {1.0, -1.0}
    for n, pts in sides:
        behind = [-_dot([q[0][i] - pts[0][i] for i in range(3)], n) for q in beams
                  if _dot(_nrm(q), n) < -0.999]
        assert min([d for d in behind if d > 1e-6], default=math.inf) >= 1.2
    bottoms = [[p[:2] for p in pts] for pts in beams if _nrm(pts)[2] < -0.99]
    lights = [_ctr(pts) for mat, pts in faces if mat == "gym_light_linear"
              and _nrm(pts)[2] < -0.99 and _ctr(pts)[2] > D.C_BEAM + 0.1]
    assert len(lights) >= 8
    for c in lights:
        assert not any(_in_polygon(b, c[0], c[1]) for b in bottoms), c


def test_sub_arena_wood_panels_reach_near_the_ceiling(built):
    """サブアリーナの横張りの木は四方の壁で天井の近くまで張り、扉の上にも続く（spec §6）。
    木の面は後ろの壁の面と同じ平面に重ねず、手前に出す（重なると壁の色が出てちらつく）。
    南と北の板は西の板の面から東の板の面まで通し、隅に後ろの壁を見せない。"""
    x0, y0, x1, y1 = D.SUB
    wood = [pts for _name, mat, pts in _mfaces(built) if mat == "gym_wall_wood_horizontal"
            and all(x0 - 0.5 <= p[0] <= x1 + 0.5 and y0 - 0.5 <= p[1] <= y1 + 0.5 for p in pts)]
    others = [pts for mat, pts in _wall_faces(built) if mat != "gym_wall_wood_horizontal"]
    for axis, sign in ((0, 1), (1, 1), (1, -1), (0, -1)):
        tops = [max(p[2] for p in pts) for pts in wood if _nrm(pts)[axis] * sign > 0.99]
        assert tops and max(tops) >= D.C_EAST - 0.2, (axis, sign)
    for pts in wood:
        assert all(abs(d) >= 0.005 for d in _plane_gaps(pts, others)), (_ctr(pts), _nrm(pts))
    west = [pts for pts in wood if _nrm(pts)[0] > 0.99]
    for g0, g1 in D.SUB_DOORS:
        assert any(min(p[1] for p in pts) <= g0 and max(p[1] for p in pts) >= g1
                   and min(p[2] for p in pts) >= D.DOOR_H - 1e-6 for pts in west), (g0, g1)
    west_x = max(p[0] for pts in west if _ctr(pts)[0] < x0 + 0.5 for p in pts)
    east_x = min(p[0] for pts in wood if _nrm(pts)[0] < -0.99 and _ctr(pts)[0] > x1 - 0.5
                 for p in pts)
    for sign, yw in ((1, y0), (-1, y1)):
        xs = [p[0] for pts in wood if _nrm(pts)[1] * sign > 0.99 and abs(_ctr(pts)[1] - yw) < 0.5
              for p in pts]
        assert xs, sign
        assert min(xs) <= west_x + 1e-6 and max(xs) >= east_x - 1e-6, (sign, min(xs), max(xs))


def test_gallery_railing_is_1_10_high(built):
    """ギャラリーの手すりは高さ 1.10 m（spec §1-4）。西の辺の支柱の天面で測る。"""
    tops = [pts[0][2] for mat, pts in _wall_faces(built)
            if mat == "gym_rail_metal" and _nrm(pts)[2] > 0.99
            and all(abs(p[0] - GALLERY_EDGE_X) < 0.05
                    and GALLERY_EDGE_Y[0] - 0.05 <= p[1] <= GALLERY_EDGE_Y[1] + 0.05 for p in pts)
            and max(p[0] for p in pts) - min(p[0] for p in pts) < 0.1
            and max(p[1] for p in pts) - min(p[1] for p in pts) < 0.1]
    assert tops
    assert all(z == pytest.approx(D.Z2 + SPEC_RAIL_H, abs=0.005) for z in tops), sorted(set(tops))
