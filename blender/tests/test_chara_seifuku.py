"""セーラー服の首まわり (#47)。

身頃を肩ヨークまで高く張ると首の横に白い布の壁が立ち、正面から見ると白い立ち襟に見える。
身頃は前を V に開けて首を出し、紺の前襟を身頃の面の上に張る（パーカーを羽織る sora は
パーカーの面の上）。前襟より外に身頃・袖・パーカー・肌の面があると、突き抜けた白や肌色が
ぎざぎざの斑になるので、前襟の面ごとに外から光線を飛ばし、前襟より手前で当たる面が無い
ことを確かめる。
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
    """キャラを組み、襟の部位で add_grid が張った格子の (先頭の頂点, 段数, 列数) を返す。"""
    grids = []
    orig = M.MeshBuilder.add_grid

    def add_grid(self, rings, mat, **kw):
        v0 = len(self.verts)
        out = orig(self, rings, mat, **kw)
        # 襟の部位の add_tube（白線）も add_grid で筒を張るので、開いた格子だけ拾う
        if self._stack and self._stack[-1] == "collar" and not kw.get("close_u", True):
            grids.append((v0, len(rings), len(rings[0])))
        return out

    M.MeshBuilder.add_grid = add_grid
    try:
        p = params.resolve(name)
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
        cloth.build_outfit(mb, p, a)
    finally:
        M.MeshBuilder.add_grid = orig
    return p, a, mb, grids


def _front_panels(name):
    """前襟 2 枚の格子（段, 列, 3）。最後の段は身頃の縁の内側への折り返し。"""
    _, _, mb, grids = _build(name)
    V = np.array(mb.verts)
    # 1 枚目は背面フラップ、続く 2 枚が前襟
    assert len(grids) == 3
    return [V[v0:v0 + rows * cols].reshape(rows, cols, 3) for v0, rows, cols in grids[1:]]


def _triangles(mb, keep):
    V = np.array(mb.verts)
    tri = [(f[0], f[k], f[k + 1]) for f, m in zip(mb.faces, mb.face_mat) if keep(f, m)
           for k in range(1, len(f) - 1)]
    return V[np.array(tri)]


@pytest.mark.parametrize("name", SEIFUKU)
def test_blouse_opens_below_chin(name):
    p, _, mb, _ = _build(name)
    V = np.array(mb.verts)[mb.part_indices("blouse")]
    front = V[(np.abs(V[:, 0]) < p["height"] * 0.01) & (V[:, 1] < 0)]
    # 前中心の身頃の上端（胸当て）は顎よりずっと下で、首と鎖骨が見える
    assert front[:, 2].max() < p["z"]["chin"] - p["head_h"] * 0.3
    # 胸当てが V の奥を塞ぎ、胸までは開かない
    assert front[:, 2].max() > p["z"]["bust"] + p["height"] * 0.04


@pytest.mark.parametrize("name", SEIFUKU)
def test_blouse_covers_shoulder(name):
    p, a, mb, _ = _build(name)
    V = np.array(mb.verts)[mb.part_indices("blouse")]
    side = V[np.abs(V[:, 0]) > a.shoulder[0] * 0.5]
    # V に開けても肩の上は身頃が覆う
    assert side[:, 2].max() > a.shoulder[2] + p["height"] * 0.02


@pytest.mark.parametrize("name", SEIFUKU)
def test_back_flap_corners_stay_inside_shoulders(name):
    _, a, mb, grids = _build(name)
    v0, rows, cols = grids[0]
    flap = np.array(mb.verts)[v0:v0 + rows * cols].reshape(rows, cols, 3)
    # 上辺の角が肩の外へ出ると、後ろから見て肩章のように見える
    assert np.all(np.abs(flap[0, [0, -1], 0]) < a.shoulder[0] * 0.62)
    # 背中へ下りるほど広がる
    assert np.all(np.abs(flap[-1, [0, -1], 0]) > np.abs(flap[0, [0, -1], 0]))


@pytest.mark.parametrize("name", SEIFUKU)
def test_collar_inner_edge_above_outer_edge(name):
    for g in _front_panels(name):
        assert np.all(g[-2, :, 2] >= g[0, :, 2] - 1e-9)


@pytest.mark.parametrize("name", SEIFUKU)
def test_nothing_pokes_through_front_collar(name):
    p, _, mb, _ = _build(name)
    panels = _front_panels(name)
    lo = np.min([g.reshape(-1, 3).min(axis=0) for g in panels], axis=0) - 0.05
    hi = np.max([g.reshape(-1, 3).max(axis=0) for g in panels], axis=0) + 0.05
    V = np.array(mb.verts)

    def near(f, m):
        c = V[list(f)].mean(axis=0)
        return bool(np.all((c > lo) & (c < hi))) and (
            m in ("cloth_blouse", "cloth_hoodie") or m.startswith("skin"))

    tri = _triangles(mb, near)
    reach = p["height"] * 0.03
    checked = 0
    for g in panels:
        # 折り返しの段（最後の 1 段）は身頃の内側へ潜るので除く
        for j in range(g.shape[0] - 2):
            for i in range(g.shape[1] - 1):
                q = np.array([g[j, i], g[j, i + 1], g[j + 1, i + 1], g[j + 1, i]])
                n = np.cross(q[2] - q[0], q[3] - q[1])
                if np.linalg.norm(n) < 1e-12:
                    continue
                n /= np.linalg.norm(n)
                c = q.mean(axis=0)
                t = first_hit(c + n * reach, -n, tri)
                checked += 1
                # 前襟より手前（外）で身頃・袖・パーカー・肌に当たらない
                assert t > reach - 1e-4, (name, c.round(3).tolist(), reach - t)
    assert checked > 200


HOODIE = [cid for cid in params.ALL_IDS
          if params.CHARACTERS[cid]["outfit"] == "seifuku_hoodie"]


@pytest.mark.parametrize("name", HOODIE)
def test_blouse_stays_well_inside_hoodie_sleeves(name):
    """パーカーの袖の面ごとに外から光線を飛ばし、ブラウスが袖の面より十分奥にある。

    ブラウスの半袖を張ると、肩口のふくらみがパーカーの袖とほぼ同じ太さ（面の中心で
    0.04 mm 外）になり、二の腕に白い帯がちらついて突き抜けた。肩関節より付け根側は
    身頃と襟に隠れるので数えない。"""
    p, a, mb, _ = _build(name)
    V = np.array(mb.verts)
    sleeve = set(mb.part_indices("sleeve_l", "sleeve_r").tolist())
    blouse = _triangles(mb, lambda f, m: m == "cloth_blouse")
    hoodie = _triangles(mb, lambda f, m: m == "cloth_hoodie")
    reach = p["height"] * 0.008
    gap = p["height"] * 0.002
    checked = 0
    for f, m in zip(mb.faces, mb.face_mat):
        if m != "cloth_hoodie" or not sleeve.issuperset(f) or len(f) != 4:
            continue
        q = V[list(f)]
        c = q.mean(axis=0)
        n = np.cross(q[2] - q[0], q[3] - q[1])
        if np.linalg.norm(n) < 1e-12:
            continue
        sgn = 1.0 if c[0] > 0 else -1.0
        sh, el = a.shoulder * [sgn, 1, 1], a.elbow * [sgn, 1, 1]
        ax = el - sh
        if np.dot(c - sh, ax) < 0:
            continue
        # 腕の軸から外へ向ける
        radial = (c - sh) - np.dot(c - sh, ax) / np.dot(ax, ax) * ax
        n = n / np.linalg.norm(n) * (1.0 if np.dot(n, radial) >= 0 else -1.0)
        t_blouse = first_hit(c + n * reach, -n, blouse)
        t_hoodie = first_hit(c + n * reach, -n, hoodie)
        checked += 1
        assert t_blouse > t_hoodie + gap, (name, c.round(3).tolist(), t_blouse - t_hoodie)
    assert checked > 50


@pytest.mark.parametrize("name", HOODIE)
def test_hood_fills_the_nape_below_the_hair(name):
    """後ろから見ると、毛先の高さの首の後ろはフードで埋まる。

    髪とフードは同じ水色で、境目には輪郭線しか出ない。毛先がフードの外へ出ると
    出た所の輪郭線だけが切れ切れの鉤になる。毛先とフードの間に肌や空がのぞくと、
    遠目では髪の裾とフードの縁の輪郭線が重なって黒い帯になる。"""
    p, a, mb, _ = _build(name)
    h = p["height"]
    V = np.array(mb.verts)
    c, r = cloth.hood_ellipsoid(p, a)

    def q(idx):
        return ((((V[idx] - c) / r) ** 2).sum(axis=1))

    def on_hood(f, m):
        return m == "cloth_hoodie" and bool((np.abs(q(list(f)) - 1.0) < 1e-9).all())

    # 楕円体はメッシュのフードの面そのもの（16 列 10 段）
    assert sum(on_hood(f, m) for f, m in zip(mb.faces, mb.face_mat)) >= 16 * 10
    hair = sorted({i for f, m in zip(mb.faces, mb.face_mat) if m == "hair" for i in f})
    tips = V[hair, 2].min()
    hood = _triangles(mb, on_hood)
    rest = _triangles(mb, lambda f, m: not on_hood(f, m))
    d = np.array([0.0, -1.0, 0.0])
    # 真後ろから、毛先の高さの上下 身長 × 0.6% を、肩幅の 7 割の幅で光線を並べる
    half = a.shoulder[0] * 0.7
    for zc in np.linspace(tips - h * 0.006, tips + h * 0.006, 6):
        for x in np.linspace(-half, half, 11):
            o = np.array([x, c[1] + 0.5, zc])
            assert first_hit(o, d, hood) < first_hit(o, d, rest), (name, round(x, 3), round(zc, 3))
