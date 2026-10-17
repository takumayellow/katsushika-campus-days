"""寮の細部（bld_dorm_trim）の当たり判定メッシュ (#41)。

bld_dorm_trim の MeshCollider は route.fbx のメッシュそのものではなく、葉のサブメッシュを
落とした Assets/Models/Colliders/route_bld_dorm_trim.asset を指す。この asset は
SceneBuilder.BuildAll（RouteStage.Dress -> CampusStage.ColliderMesh）でしか書き直されない。
dorm.py を直して route.fbx だけ作り直すと asset は前の形のまま残り、前の位置に見えない壁が、
新しい位置には当たり判定の無い立体ができる。そこで asset の頂点を、dorm.build_exterior が
今出す bld_dorm_trim の頂点と両方向に突き合わせ、ずれていたら落とす。
"""

import os
import re
import struct

import pytest

from conftest import REPO
from kcd_route import dorm, ground

ASSET = os.path.join(REPO, "unity", "KatsushikaCampusDays", "Assets", "Models", "Colliders",
                     "route_bld_dorm_trim.asset")
VENDING = "vending_blue"
# CampusStage.IsFoliageMaterial と同じ接頭辞。葉の面は当たり判定に入らない
FOLIAGE = ("plant_green", "plant_yellow", "leaf")
CELL = 0.01     # 近くの点を引く格子の一辺（m）
TOL = 2e-3      # FBX の float32 と取り込みの丸めを吸収する許容（m）


def _unity(p):
    """Blender の (x, y, z) を、route.fbx を取り込んだ Unity のローカル座標 (-x, z, -y) に直す。"""
    return (-p[0], p[2], -p[1])


def _points(trim, faces):
    """faces の頂点を Unity のローカル座標で返す（共有された頂点は 1 つ）。"""
    ids = sorted({k for face in faces for k in face})
    return [_unity(trim.verts[k]) for k in ids]


def _cell(p):
    return tuple(round(c / CELL) for c in p)


def _missing(points, ref):
    """points のうち、ref に TOL 以内の点が無いもの。"""
    grid = {}
    for p in ref:
        grid.setdefault(_cell(p), []).append(p)
    out = []
    for q in points:
        cx, cy, cz = _cell(q)
        near = (r for dx in (-1, 0, 1) for dy in (-1, 0, 1) for dz in (-1, 0, 1)
                for r in grid.get((cx + dx, cy + dy, cz + dz), ()))
        if not any(max(abs(a - b) for a, b in zip(q, r)) <= TOL for r in near):
            out.append(q)
    return out


def _show(points):
    return ", ".join("(%.3f, %.3f, %.3f)" % p for p in points[:3]) + \
        (" ほか" if len(points) > 3 else "")


@pytest.fixture(scope="module")
def trim():
    return dorm.build_exterior(ground.load()["dormitory"])[1]


@pytest.fixture(scope="module")
def asset_points():
    assert os.path.isfile(ASSET), "%s が無い" % ASSET
    with open(ASSET, encoding="utf-8", newline="") as fp:
        text = fp.read()
    count = int(re.search(r"^\s+m_VertexCount: (\d+)", text, re.M).group(1))
    raw = bytes.fromhex(re.search(r"^\s+_typelessdata: ([0-9a-f]*)", text, re.M).group(1))
    assert len(raw) == count * 12, "頂点が位置（float32 x 3）だけの並びではない"
    return [struct.unpack_from("<3f", raw, 12 * i) for i in range(count)]


@pytest.fixture(scope="module")
def solid_points(trim):
    """当たり判定に入る面（葉でない面）の頂点。"""
    return _points(trim, [face for face, m in zip(trim.faces, trim.face_mat)
                          if not trim.mat_names[m].startswith(FOLIAGE)])


@pytest.fixture(scope="module")
def vending_points(trim):
    """自販機の箱の 8 隅（面ごとに別の頂点なので、位置で 1 つにまとめる）。"""
    mi = trim.mat_names.index(VENDING)
    pts = _points(trim, [face for face, m in zip(trim.faces, trim.face_mat) if m == mi])
    return sorted({tuple(round(c, 6) for c in p) for p in pts})


def test_collider_has_nothing_the_trim_no_longer_has(asset_points, trim):
    stale = _missing(asset_points, _points(trim, trim.faces))
    assert not stale, ("route_bld_dorm_trim.asset に今の bld_dorm_trim に無い頂点が %d 個ある"
                       "（SceneBuilder.BuildAll で作り直す）: %s" % (len(stale), _show(stale)))


def test_collider_has_every_solid_vertex_of_the_trim(asset_points, solid_points):
    gap = _missing(solid_points, asset_points)
    assert not gap, ("bld_dorm_trim の頂点 %d 個が route_bld_dorm_trim.asset に無い"
                     "（SceneBuilder.BuildAll で作り直す）: %s" % (len(gap), _show(gap)))


def test_collider_has_the_vending_machine(asset_points, vending_points):
    assert len(vending_points) == 8, "自販機は箱 1 つのはず: %d 頂点" % len(vending_points)
    gap = _missing(vending_points, asset_points)
    assert not gap, ("自販機の角 %d 個が route_bld_dorm_trim.asset に無い。体がめり込む: %s"
                     % (len(gap), _show(gap)))
