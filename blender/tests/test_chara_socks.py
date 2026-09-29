"""セーラー服の紺のハイソックス (#47)。

靴下は脚の筒のリングを同じ段・同じ列のまま少しだけ外へ押し出して張る。脚から離れると
ふくらはぎより太い筒になり、脚の外へ出た肌は靴下の上から透けて見える。横から光線を
飛ばし、靴下が脚のすぐ外で脚を覆っていることを確かめる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M
from raycast import first_hit

SEIFUKU = [cid for cid in params.ALL_IDS
           if params.CHARACTERS[cid]["outfit"].startswith("seifuku")]


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _triangles(mb, part):
    V = np.array(mb.verts)
    keep = set(mb.part_indices(part).tolist())
    tri = [(f[0], f[k], f[k + 1]) for f in mb.faces if all(i in keep for i in f)
           for k in range(1, len(f) - 1)]
    return V[np.array(tri)]


@pytest.mark.parametrize("name", SEIFUKU)
def test_shin_rings_are_fine_and_keep_the_leg_shape(name):
    p, a, _ = _build(name)
    for sgn in (-1, 1):
        path, radii = body.leg_tube(a, sgn)
        coarse = M.tube_rings(path, radii, n=body.LEG_SEG)
        rings = body.leg_rings(p, a, sgn)
        z = [r.mean(axis=0)[2] for r in rings]
        # 元のリングはそのまま残る
        for r in coarse:
            assert any(np.allclose(r, q) for q in rings)
        # 膝から下は SHIN_STEP 以下の段に割る
        knee = a.knee[2]
        steps = -np.diff(z)
        shin = steps[np.array(z[1:]) < knee - 1e-6]
        assert len(shin) >= 8
        assert shin.max() <= p["height"] * body.SHIN_STEP + 1e-9


@pytest.mark.parametrize("name", SEIFUKU)
def test_socks_hug_the_leg(name):
    p, a, mb = _build(name)
    h = p["height"]
    V = np.array(mb.verts)
    checked = 0
    for side, sgn in (("l", 1), ("r", -1)):
        sock = _triangles(mb, f"socks_{side}")
        leg = _triangles(mb, f"leg_{side}")
        rings = body.leg_rings(p, a, sgn)
        core = np.array([r.mean(axis=0) for r in rings])[::-1]
        zs = V[mb.part_indices(f"socks_{side}"), 2]
        # 足首のリングから口ゴムの手前まで
        for z in np.linspace(core[0, 2] + h * 0.004, zs.max() - h * 0.008, 12):
            c = np.array([np.interp(z, core[:, 2], core[:, k]) for k in range(3)])
            for ang in np.linspace(0, 2 * np.pi, 16, endpoint=False):
                d = np.array([np.cos(ang), np.sin(ang), 0.0])
                o = c + d * 0.2
                s = first_hit(o, -d, sock)
                k = first_hit(o, -d, leg)
                checked += 1
                # 靴下が脚より外にあり、脚から身長比 0.4% 以内に沿う
                assert s < k, (name, side, round(float(z), 3), round(float(np.degrees(ang))))
                assert k - s < h * 0.004, (name, side, round(float(z), 3), k - s)
    assert checked > 300
