"""脚の筒と、その上に着るもの (#47)。

セーラー服の紺のハイソックスは、脚の筒のリングを同じ段・同じ列のまま少しだけ外へ
押し出して張り、ウェイトも内側の脚の面から写す。脚から離れるとふくらはぎより太い筒に
なり、脚の外へ出た肌は靴下の上から透けて見える。横から光線を飛ばし、靴下が脚の
すぐ外で脚を覆っていることを確かめる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
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


@pytest.mark.parametrize("name", params.ALL_IDS)
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
        # 膝から下（元は 2 段）は 3 cm 以下の段に割る
        knee = a.knee[2]
        steps = -np.diff(z)
        shin = steps[np.array(z[1:]) < knee - 1e-6]
        assert len(shin) > 2
        assert shin.max() <= 0.03


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


@pytest.mark.parametrize("name", SEIFUKU)
def test_sock_weights_follow_the_skin(name):
    """靴下の各頂点のウェイトは、すぐ内側の脚の面のウェイトと同じ。

    脚の頂点に、高さに比例する骨と周の向きで変わる骨を混ぜたウェイトを付け、
    靴下へ写した値がその頂点の高さ・向きの値と合うかを見る。列を取り違えると
    向きの骨が、段を取り違えると高さの骨がずれる。5 本目の骨は落ちる。
    """
    p, a, mb = _build(name)
    pts = np.array(mb.verts)
    seg = body.LEG_SEG
    for side in ("l", "r"):
        sk = mb.part_indices(f"leg_{side}")
        grid = sk[:len(sk) - 2].reshape(-1, seg)
        z0, z1 = pts[grid, 2].min(), pts[grid, 2].max()
        ang = np.linspace(0.0, 2.0 * np.pi, seg, endpoint=False)
        c = 0.5 + 0.5 * np.cos(ang)
        # Hips がどの頂点でもいちばん小さい 5 本目になる
        W = [[{"UpperLeg": 0.01 + 0.97 * u * (1 - c[j]),
               "LowerLeg": 0.01 + 0.97 * u * c[j],
               "Foot": 0.005 + 0.97 * (1 - u), "Toes": 0.004, "Hips": 0.001}
              for j, u in enumerate((pts[row, 2] - z0) / (z1 - z0))]
             for row in grid]
        idx = mb.part_indices(f"socks_{side}")
        out = rig._skin_blend(pts, idx, grid, W)
        assert len(out) == len(idx)
        for n, (i, w) in enumerate(zip(idx.tolist(), out)):
            assert len(w) == 4 and "Hips" not in w
            assert abs(sum(w.values()) - 1.0) < 1e-9
            # 脚の最下段より下の靴下の縁は、最下段の値をそのまま持つ
            u = np.clip((pts[i, 2] - z0) / (z1 - z0), 0.0, 1.0)
            cj = c[n % seg]
            assert abs(w["LowerLeg"] - (0.01 + 0.97 * u * cj) / 0.999) < 0.02, (name, side, n)
            assert abs(w["Foot"] - (0.005 + 0.97 * (1 - u)) / 0.999) < 0.02, (name, side, n)


def test_geta_hakama_covers_the_shin():
    """高下駄の坊っちゃんは、足首から上の脚の肌を全部袴の布にする。

    袴の裾は足の甲のすぐ上までしか無く、歩きで後ろへ振った脛が裾の下から覗く。
    """
    p, a, mb = _build("botchan")
    h = p["height"]
    V = np.array(mb.verts)
    leg = set(mb.part_indices("leg_l", "leg_r").tolist())
    above = [fi for fi, f in enumerate(mb.faces)
             if all(v in leg for v in f) and V[f, 2].mean() > a.ankle[2] + h * 0.01]
    assert len(above) > 200
    assert all(mb.face_mat[fi] == "cloth_hakama_blue" for fi in above)
