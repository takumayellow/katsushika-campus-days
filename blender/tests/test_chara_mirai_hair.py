"""mirai の前髪とヘアピンの形 (#47)。

Unity の輪郭線は、面を法線の向きへ押し出した殻の裏面。前髪を兜と別の部品で重ねると、
重なりの段や板の端ごとに殻が線を引き、頭頂を横切る亀裂やこめかみの縦の弧になる。
前髪は兜の外殻をそのまま正面で下ろしたものなので、その殻が 0.9 m と 3.2 m の
輪郭線の太さで折れ返らないことと、潰れた面を張っていないことを確かめる。

ヘアピンは髪の下に埋もれると見えず、髪から浮くと殻の線がピンの下に回り込む。頭の
中心から頂点へ光線を飛ばし、その先で一番遠くに当たる髪の面との差で、どれだけ外へ
出ているかを測る。
"""

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M


@pytest.fixture(scope="module")
def mirai():
    """mirai の髪を組み、add_grid で張った面を部位ごとに呼び出し順で返す。"""
    grids = {}
    helmets = []
    orig_grid, orig_helmet = M.MeshBuilder.add_grid, hair.build_helmet

    def add_grid(self, rings, mat, **kw):
        v0, f0 = len(self.verts), len(self.faces)
        out = orig_grid(self, rings, mat, **kw)
        part = self._stack[-1] if self._stack else ""
        grids.setdefault(part, []).append(dict(
            v0=v0, f0=f0, f1=len(self.faces), rows=len(rings), cols=len(rings[0])))
        return out

    def build_helmet(*args, **kw):
        helmets.append(kw)
        return orig_helmet(*args, **kw)

    M.MeshBuilder.add_grid = add_grid
    hair.build_helmet = build_helmet
    try:
        p = params.resolve("mirai")
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
    finally:
        M.MeshBuilder.add_grid = orig_grid
        hair.build_helmet = orig_helmet
    assert len(helmets) == 1
    return dict(p=p, mb=mb, head=head, V=np.asarray(mb.verts, dtype=float),
                grids=grids, nv=helmets[0]["nv"])


def _area_vec(P):
    """多角形 P の面積ベクトル（向きは頂点の並びで決まる法線、長さは面積の 2 倍）。"""
    c = P.mean(axis=0)
    return np.cross(P - c, np.roll(P, -1, axis=0) - c).sum(axis=0)


def _vertex_normals(V, faces):
    """角の角度で重み付けした頂点法線（Blender の Smooth の頂点法線と同じ）。"""
    VN = np.zeros_like(V)
    for f in faces:
        P = V[f]
        n = _area_vec(P)
        L = np.linalg.norm(n)
        if L < 1e-30:
            continue
        for k in range(len(f)):
            e0, e1 = P[k - 1] - P[k], P[(k + 1) % len(f)] - P[k]
            c = e0 @ e1 / (np.linalg.norm(e0) * np.linalg.norm(e1))
            VN[f[k]] += n / L * np.arccos(np.clip(c, -1.0, 1.0))
    return VN / np.maximum(np.linalg.norm(VN, axis=1, keepdims=True), 1e-30)


def _tris(mb, V, names):
    keep = set(mb.part_indices(*names).tolist())
    tris = [(f[0], f[k], f[k + 1]) for f in mb.faces if f[0] in keep
            for k in range(1, len(f) - 1)]
    return V[np.array(tris)]


def _far_hit(tris, origin, dirs):
    """origin から dirs の向きへ飛ばした光線が tris に当たる一番遠い距離。当たらなければ nan。"""
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    e1, e2 = b - a, c - a
    s = origin - a
    q = np.cross(s, e1)
    out = []
    for d in dirs:
        h = np.cross(d, e2)
        det = (e1 * h).sum(axis=1)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        u = (s * h).sum(axis=1) * inv
        v = (q @ d) * inv
        t = (e2 * q).sum(axis=1) * inv
        hit = ok & (u >= 0.0) & (v >= 0.0) & (u + v <= 1.0) & (t > 0.0)
        out.append(t[hit].max() if hit.any() else np.nan)
    return np.array(out)


def _excess(head, pts, tris):
    """pts の各点が、同じ向きで一番外にある tris の面より何 m 外にあるか。"""
    x = pts - head.center
    r = np.linalg.norm(x, axis=1)
    return r - _far_hit(tris, head.center, x / r[:, None])


def test_mirai_bangs_are_the_helmet_shell(mirai):
    """前髪は別の部品にせず、兜の外殻が頭頂から途切れずに額まで下りる。"""
    mb, V, p = mirai["mb"], mirai["V"], mirai["p"]
    assert len(mb.part_indices("hair_front")) == 0
    g = mirai["grids"]["hair_back"][0]
    grid = V[g["v0"]:g["v0"] + g["rows"] * g["cols"]].reshape(g["rows"], g["cols"], 3)
    front = round(hair.FRONT / (2 * np.pi) * g["cols"]) % g["cols"]
    z = grid[:mirai["nv"] + 1, front, 2]
    assert np.all(np.diff(z[np.argmax(z):]) < 0.0)  # 頭頂から毛先まで上り返さずに下りる
    share = (z[-1] - p["z"]["chin"]) / (p["z"]["top"] - p["z"]["chin"])
    assert 0.55 < share < 0.67  # 毛先は顎から頭頂までの 6 割の高さ


@pytest.mark.parametrize("w", [0.0045, 0.016])  # 0.9 m と 3.2 m での輪郭線の太さ
def test_mirai_helmet_outline_does_not_fold_over_crown_and_bangs(mirai, w):
    """頭頂から前髪の毛先までの外殻を、頂点法線の向きへ w 押し出しても面が裏返らない。

    極のすぐ周り（リング 0〜2）は半径が 3.2 m の殻の幅 16 mm より小さく、押し出した
    殻が極を越えて反対側と重なるので、面ごとの向きの比較では測れない。そこは除く。
    """
    mb, V = mirai["mb"], mirai["V"]
    g = mirai["grids"]["hair_back"][0]
    faces = [np.array(f) for f in mb.faces[g["f0"]:g["f1"]]]
    VN = _vertex_normals(V, faces)
    folded, checked = [], 0
    for f in faces:
        rows = (f - g["v0"]) // g["cols"]
        if rows.min() < 3 or rows.max() > mirai["nv"]:
            continue
        checked += 1
        P = V[f]
        if _area_vec(P) @ _area_vec(P + VN[f] * w) < 0.0:
            folded.append((int(rows.min()), int((f[0] - g["v0"]) % g["cols"])))
    assert checked > 2000
    assert folded == []


def test_mirai_helmet_has_no_collapsed_faces(mirai):
    """兜の殻に面積がほぼ 0 の四角形を張らない。

    前髪の範囲は垂れが 0 なので、垂れ・内殻・縁のリングが生え際の 1 本に重なる。
    そこに張った面は法線が定まらず、輪郭線の殻がめくれて生え際に沿った線になる。
    """
    mb, V, p = mirai["mb"], mirai["V"], mirai["p"]
    keep = set(mb.part_indices("hair_back").tolist())
    area = [0.5 * np.linalg.norm(_area_vec(V[list(f)]))
            for f in mb.faces if f[0] in keep and len(f) == 4]
    assert min(area) >= (p["head_w"] * 1e-3) ** 2


def test_mirai_hairpins_face_into_the_head(mirai):
    """ヘアピンは 2 本とも開いた 1 枚板で、面を頭の中へ向ける。

    閉じた筒にすると、太さ 3 mm のピンの周りに 3.2 m で 16 mm の殻が塊になって出る。
    頭の中を向いた板なら殻は髪の下へ沈む。
    """
    mb, V, head = mirai["mb"], mirai["V"], mirai["head"]
    pins = mirai["grids"]["hair_acc"]
    assert len(pins) == 2
    for g in pins:
        inward = total = 0.0
        for f in mb.faces[g["f0"]:g["f1"]]:
            P = V[list(f)]
            n = _area_vec(P)
            total += np.linalg.norm(n)
            inward += np.linalg.norm(n) * (n @ (head.center - P.mean(axis=0)) > 0.0)
        assert inward / total > 0.95


def test_mirai_hairpin_shows_just_above_the_hair(mirai):
    mb, V, p, head = mirai["mb"], mirai["V"], mirai["p"], mirai["head"]
    names = [n for n in mb.parts if n.startswith("hair") and n != "hair_acc"]
    ex = _excess(head, V[mb.part_indices("hair_acc")], _tris(mb, V, names))
    assert not np.isnan(ex).any()
    assert 0.0005 < ex.max() < p["head_w"] * 0.03  # 上面だけが髪から出る
    assert ex.min() < 0.0  # 下は髪に埋まっていて、浮いた隙間が無い
