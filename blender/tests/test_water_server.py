"""furn_research2.water_server: 給茶機・給水機が白一色の箱でなく、注ぎ口・ボタン・水受けを持つ。"""

import pytest

from kcd_interior import furn_research2 as FR
from kcd_lib.mesh import MeshBuilder


def _build(kind):
    mb = MeshBuilder("t")
    FR.water_server(mb, 0.0, 0.0, 0.0, 0.0, kind)
    return mb


def _faces_by_mat(mb):
    out = {}
    for mi in mb.face_mat:
        name = mb.mat_names[mi]
        out[name] = out.get(name, 0) + 1
    return out


@pytest.mark.parametrize("kind", ["tea", "water"])
def test_has_spout_tray_grating_and_display(kind):
    mats = _faces_by_mat(_build(kind))

    assert mats["research2_chrome"] > 0                # 注ぎ口とノズル
    assert mats["research2_stainless"] > 0             # 水受け皿と背板
    assert mats["research2_grille_black"] > 0          # グレーチング
    assert mats["research2_display_blue"] > 0          # 表示窓
    assert mats["research2_kiosk_black"] > 0           # 黒樹脂の頭部


@pytest.mark.parametrize("kind", ["tea", "water"])
def test_is_not_a_single_white_box(kind):
    mats = _faces_by_mat(_build(kind))

    assert len(mats) >= 5


def test_tea_and_water_differ_by_buttons():
    assert _faces_by_mat(_build("tea")) != _faces_by_mat(_build("water"))


def test_stays_within_a_few_hundred_triangles():
    mb = _build("tea")
    tris = sum(len(f) - 2 for f in mb.faces)

    assert 100 < tris <= 400


def test_cup_recess_is_open_between_tray_and_head():
    mb = _build("tea")
    zs = [v[2] for f in mb.faces for v in (mb.verts[i] for i in f)
          if -0.2 < v[0] < 0.2 and 0.05 < v[1] < 0.2]

    assert zs
    # 水受けの上（0.095）から注ぎ口の先（0.29）までは何も無い
    assert not [z for z in zs if 0.1 < z < 0.28]


def _normal(face):
    n = [0.0, 0.0, 0.0]
    for i in range(len(face)):
        p, q = face[i], face[(i + 1) % len(face)]
        n[0] += (p[1] - q[1]) * (p[2] + q[2])
        n[1] += (p[2] - q[2]) * (p[0] + q[0])
        n[2] += (p[0] - q[0]) * (p[1] + q[1])
    return n


@pytest.mark.parametrize("ang,front_y", [(0.0, 1.0), (3.141592653589793, -1.0)])
def test_display_window_faces_the_front(ang, front_y):
    mb = MeshBuilder("t")
    FR.water_server(mb, 0.0, 0.0, 0.0, ang, "tea")
    blue = [i for i, mi in enumerate(mb.face_mat) if mb.mat_names[mi] == "research2_display_blue"]

    assert blue
    for i in blue:
        n = _normal([mb.verts[j] for j in mb.faces[i]])
        assert n[1] * front_y > 0.0


def test_stands_on_the_counter_top():
    mb = _build("tea")

    assert min(v[2] for v in mb.verts) == pytest.approx(0.0)
