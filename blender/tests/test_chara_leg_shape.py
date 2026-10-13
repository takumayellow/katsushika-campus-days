"""制服の 4 体の脚の太さと、左右の脚の間 (#47)。

膝とふくらはぎが細いと、スカートの下の脚が膝から下で棒に見える。太らせるときは
左右の脚の芯も外へ離す。離さないと腿と膝がくっつき、2 本の脚が 1 本の柱に見える。
幅は顔の幅（頭の横幅）に対する比で測る。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M

SLIM = [name for name in params.CHARACTERS
        if params.resolve(name)["build"] == "slim"]


@functools.cache
def _legs(name):
    """(p, 顔の幅, 高さ -> 脚の幅, 高さ -> 脚の内側の x)。脚は +X 側の 1 本。"""
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a = body.build_base(mb, p)[0]
    head = np.asarray(mb.verts)[mb.part_indices("head")]
    face = float(head[:, 0].max() - head[:, 0].min())
    rings = body.leg_rings(p, a, 1)
    z = np.array([r[:, 2].mean() for r in rings])
    order = np.argsort(z)
    width = np.array([r[:, 0].max() - r[:, 0].min() for r in rings])
    inner = np.array([r[:, 0].min() for r in rings])

    def at(arr):
        return lambda zz: float(np.interp(zz, z[order], arr[order]))

    return p, face, at(width), at(inner)


def _heights(p):
    z = p["z"]
    return {
        "hem": z["crotch"] - (z["crotch"] - z["knee"]) * 0.46,  # スカートの裾
        "knee": z["knee"],
        "calf": z["knee"] + (z["ankle"] - z["knee"]) * 0.36,
    }


def test_slim_characters_exist():
    assert set(SLIM) >= {"mirai", "inari", "kaname", "sora"}


@pytest.mark.parametrize("name", SLIM)
@pytest.mark.parametrize("where,least", [("hem", 0.43), ("knee", 0.37), ("calf", 0.38)])
def test_leg_is_not_a_stick(name, where, least):
    p, face, width, _ = _legs(name)
    ratio = width(_heights(p)[where]) / face
    assert ratio > least, f"{name} {where}: {ratio:.3f}"


@pytest.mark.parametrize("name", SLIM)
def test_calf_is_not_thinner_than_knee(name):
    p, _, width, _ = _legs(name)
    zz = _heights(p)
    assert width(zz["calf"]) >= width(zz["knee"])


@pytest.mark.parametrize("name", SLIM)
@pytest.mark.parametrize("where", ["hem", "knee"])
def test_legs_do_not_touch(name, where):
    p, _, _, inner = _legs(name)
    gap = 2 * inner(_heights(p)[where])
    assert gap > p["height"] * 0.005, f"{name} {where}: {gap * 1000:.1f} mm"
