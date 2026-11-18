"""前髪を兜の外殻で下ろす髪型（mirai の bob、inari の twintail）と、mirai のヘアピンの形 (#47)。

Unity の輪郭線は、面を法線の向きへ押し出した殻の裏面。前髪を兜と別の部品で重ねると、
重なりの段や板の端ごとに殻が線を引き、頭頂を横切る亀裂やこめかみの縦の弧になる。
前髪は兜の外殻をそのまま正面で下ろしたものなので、その殻が 0.9 m と 3.2 m の
輪郭線の太さで折れ返らないことと、潰れた面を張っていないことを確かめる。

頭の形に沿わせた殻は頭蓋に貼り付いた兜に見え、前髪が 1 枚のなめらかな面だと帽子の
つばに見える。殻を目より上で頭から離して髪の量を出すことと、前髪を歯ごとの房に
分ける溝があることも確かめる。

inari は前髪の毛先を眉の上で止めるので、眉の端から端まで、前髪の毛先もこめかみの
横髪も眉より上にあることを確かめる。前髪の歯の先と切れ込みが殻の頂点の列に乗ることも
確かめる。

ヘアピンは髪の下に埋もれると見えず、髪から浮くと殻の線がピンの下に回り込む。頭の
中心から頂点へ光線を飛ばし、その先で一番遠くに当たる髪の面との差で、どれだけ外へ
出ているかを測る。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M

# 前髪を兜の殻で下ろすキャラ。share は正面の前髪の毛先の高さ（顎から頭頂までの比）の範囲、
# crown・side・back は目より上で殻が頭から離れる量の下限、dip は房の間の溝の深さの下限
# （どちらも顔の幅比）。
HELMET_BANGS = {
    "mirai": dict(share=(0.55, 0.67), crown=0.25, side=0.32, back=0.31, dip=0.03),
    # 毛先を眉（0.54〜0.63）の上で止め、髪の量は mirai より控えめにする
    "inari": dict(share=(0.64, 0.74), crown=0.15, side=0.19, back=0.20, dip=0.02),
}


def _build(cid, patch=None):
    """cid の髪を組み、add_grid で張った面を部位ごとに呼び出し順で返す。

    patch を渡すと、build_helmet へ渡す引数をそれで書き換えてから組む。
    """
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
        if patch:
            kw = patch(kw)
        helmets.append(kw)
        return orig_helmet(*args, **kw)

    M.MeshBuilder.add_grid = add_grid
    hair.build_helmet = build_helmet
    try:
        p = params.resolve(cid)
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
    finally:
        M.MeshBuilder.add_grid = orig_grid
        hair.build_helmet = orig_helmet
    assert len(helmets) == 1
    return dict(cid=cid, p=p, mb=mb, head=head, V=np.asarray(mb.verts, dtype=float),
                grids=grids, nv=helmets[0]["nv"], kw=helmets[0])


@functools.lru_cache(maxsize=None)
def _built(cid):
    return _build(cid)


@pytest.fixture(scope="module", params=sorted(HELMET_BANGS))
def hm(request):
    """前髪を兜の殻で下ろすキャラの髪。"""
    return _built(request.param)


@pytest.fixture(scope="module")
def mirai():
    return _built("mirai")


@pytest.fixture(scope="module")
def inari():
    return _built("inari")


def _shell(m):
    """兜の外殻のグリッド（行は頭頂から縁へのリング、列は方位）。"""
    g = m["grids"]["hair_back"][0]
    return m["V"][g["v0"]:g["v0"] + g["rows"] * g["cols"]].reshape(g["rows"], g["cols"], 3)


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
            den = np.linalg.norm(e0) * np.linalg.norm(e1)
            if den < 1e-30:  # 長さ 0 の辺の角は向きが無い
                continue
            VN[f[k]] += n / L * np.arccos(np.clip(e0 @ e1 / den, -1.0, 1.0))
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


def test_bangs_are_the_helmet_shell(hm):
    """前髪は別の部品にせず、兜の外殻が頭頂から途切れずに額まで下りる。"""
    mb, p = hm["mb"], hm["p"]
    assert len(mb.part_indices("hair_front")) == 0
    grid = _shell(hm)
    front = round(hair.FRONT / (2 * np.pi) * grid.shape[1]) % grid.shape[1]
    z = grid[:hm["nv"] + 1, front, 2]
    assert np.all(np.diff(z[np.argmax(z):]) < 0.0)  # 頭頂から毛先まで上り返さずに下りる
    share = (z[-1] - p["z"]["chin"]) / (p["z"]["top"] - p["z"]["chin"])
    lo, hi = HELMET_BANGS[hm["cid"]]["share"]
    assert lo < share < hi


@pytest.mark.parametrize("w", [0.0045, 0.016])  # 0.9 m と 3.2 m での輪郭線の太さ
def test_helmet_outline_does_not_fold_over_crown_and_bangs(hm, w):
    """頭頂から前髪の毛先までの外殻を、書き出す頂点法線の向きへ w 押し出しても面が裏返らない。

    輪郭線の殻は FBX に書き出した法線の向きへ押し出すので、陰の法線を決めた頂点はその
    法線で、決めていない頂点は面から求めた法線で測る。
    極のすぐ周り（リング 0〜2）は半径が 3.2 m の殻の幅 16 mm より小さく、押し出した
    殻が極を越えて反対側と重なるので、面ごとの向きの比較では測れない。そこは除く。
    """
    mb, V = hm["mb"], hm["V"]
    g = hm["grids"]["hair_back"][0]
    faces = [np.array(f) for f in mb.faces[g["f0"]:g["f1"]]]
    custom = mb.normal_array()
    VN = M.merge_normals(custom, np.arange(len(V)), _vertex_normals(V, faces))
    VN /= np.maximum(np.linalg.norm(VN, axis=1, keepdims=True), 1e-30)
    folded, checked = [], 0
    for f in faces:
        rows = (f - g["v0"]) // g["cols"]
        if rows.min() < 3 or rows.max() > hm["nv"]:
            continue
        checked += 1
        assert np.all(np.linalg.norm(custom[f], axis=1) > 0.0)  # 陰の法線を決めた頂点で測る
        P = V[f]
        if _area_vec(P) @ _area_vec(P + VN[f] * w) < 0.0:
            folded.append((int(rows.min()), int((f[0] - g["v0"]) % g["cols"])))
    assert checked >= (hm["nv"] - 3) * (g["cols"] - 1)  # リング 3 から毛先までの全部の面
    assert folded == []


def test_helmet_has_no_collapsed_faces(hm):
    """兜の殻に面積がほぼ 0 の四角形を張らない。

    前髪の範囲は垂れが 0 なので、垂れ・内殻・縁のリングが生え際の 1 本に重なる。
    そこに張った面は法線が定まらず、輪郭線の殻がめくれて生え際に沿った線になる。
    """
    mb, V, p = hm["mb"], hm["V"], hm["p"]
    keep = set(mb.part_indices("hair_back").tolist())
    area = [0.5 * np.linalg.norm(_area_vec(V[list(f)]))
            for f in mb.faces if f[0] in keep and len(f) == 4]
    assert min(area) >= (p["head_w"] * 1e-3) ** 2


def test_carrier_widens_above_the_eyes_and_keeps_the_face_front(hm):
    """髪を載せる形は base より下を動かさず、頭頂の高さで幅と後頭部を gain 倍、
    高さを (lift - 1) 倍ぶん上げ、顔の前面の奥行きは変えない。"""
    p, head = hm["p"], hm["head"]
    carry = hm["kw"]["carry"]
    c, hd, top = head.center, p["head_d"], p["z"]["top"]
    z0 = p["z"]["chin"] + (top - p["z"]["chin"]) * carry["base"]
    pts = np.array([
        [c[0] + 0.05, c[1] + 0.05, z0 - 0.01],   # base より下
        [c[0] + 0.05, c[1] - 0.5 * hd, top],     # 頭頂の高さの顔の前面
        [c[0] + 0.05, c[1] + 0.5 * hd, top],     # 頭頂の高さの後頭部
    ])
    q = hair._carrier(p, head, pts, **carry)
    assert np.array_equal(q[0], pts[0])
    for k in (1, 2):
        assert q[k, 0] - c[0] == pytest.approx(0.05 * carry["gain"])
        assert q[k, 2] == pytest.approx(top + (carry["lift"] - 1.0) * (top - z0))
    assert q[1, 1] == pytest.approx(pts[1, 1])
    assert q[2, 1] - c[1] == pytest.approx(0.5 * hd * carry["gain"])


def test_hair_stands_off_the_skull_above_the_eyes(hm):
    """殻は目より上で頭から離れ、頭頂・横・後頭部に髪の量が出る。目の高さの
    横髪は頭に沿ったまま（ここで離すと、目の下の横髪が外へ跳ねる）。"""
    mb, V, p = hm["mb"], hm["V"], hm["p"]
    lim = HELMET_BANGS[hm["cid"]]
    hw = p["head_w"]
    P = _shell(hm).reshape(-1, 3)
    H = V[mb.part_indices("head")]
    eye = V[mb.part_indices("eye_l_white"), 2].mean()

    def gap(z):
        s = P[np.abs(P[:, 2] - z) < hw * 0.03]
        h = H[np.abs(H[:, 2] - z) < hw * 0.03]
        return ((np.abs(s[:, 0]).max() - np.abs(h[:, 0]).max()) / hw,
                (s[:, 1].max() - h[:, 1].max()) / hw)

    assert (P[:, 2].max() - H[:, 2].max()) / hw > lim["crown"]  # 頭頂の髪の厚み
    side, back = gap(eye + 0.6 * hw)
    assert side > lim["side"] and back > lim["back"]  # 額より上の横と後ろの髪の量
    assert gap(eye)[0] < 0.13  # 目の高さの横髪は頭から顔の幅の 1 割ほど


def test_bangs_part_into_clumps(hm):
    """前髪の歯と歯の切れ込みの列を毛先へ向けて凹ませ、歯ごとの房に分ける。

    溝の無い前髪と比べ、切れ込みの列は頭の中心へ近づき、歯の先の列と頭頂の
    リングは動かない（頭頂に溝を入れると、極へ集まってかぼちゃの筋になる）。
    """
    p, head, kw = hm["p"], hm["head"], hm["kw"]
    flat = _build(hm["cid"], lambda k: {**k, "bangs": {**k["bangs"], "groove": 0.0}})
    az = np.linspace(0.0, 2 * np.pi, kw["nu"], endpoint=False)
    _, _, fw, notch = hair._bang_tips(p, head, az, kw["bangs"])

    def radius(m):
        return np.linalg.norm(_shell(m)[:kw["nv"] + 1] - head.center, axis=2)

    dip = (radius(flat) - radius(hm)) / p["head_w"]
    deep = (notch > 0.9) & (fw > 0.9)  # 前髪の中ほどの、歯と歯の切れ込みの列
    tip = (notch < 0.1) & (fw > 0.9)  # 歯の先の列
    assert deep.sum() >= kw["bangs"]["teeth"] - 1 and tip.any()
    assert dip[:, deep].max(axis=0).min() > HELMET_BANGS[hm["cid"]]["dip"]  # どの切れ込みも凹む
    assert np.abs(dip[:, tip]).max() < 1e-9
    assert np.abs(dip[:kw["nv"] // 3]).max() < 1e-9  # 頭頂側の 1/3 のリング


def test_inari_bangs_stop_above_the_brows(inari):
    """inari の髪は、眉の端から端まで眉の上端より上で止まる。

    正面から見た髪の下の縁と眉の上端の間を、0.9 m から見たときの輪郭線の太さより広く取る
    （髪の輪郭線を眉に重ねない）。下の縁は、殻の行ごとに顔の側の頂点を x の順に結んだ線の
    眉の頂点の x での高さの、一番低いもの。毛先のリングのほか、こめかみから下りる横髪の行も見る。
    """
    mb, V, head = inari["mb"], inari["V"], inari["head"]
    shell = _shell(inari)
    face = shell[inari["nv"], :, 1] < head.center[1]  # 顔の側の列
    brow = V[mb.part_indices("brow_l", "brow_r")]
    rows = []
    for row in shell[:, face]:
        o = np.argsort(row[:, 0])
        rows.append(np.interp(brow[:, 0], row[o, 0], row[o, 2], left=np.inf, right=np.inf))
    edge = np.min(rows, axis=0)
    assert (edge - brow[:, 2]).min() > 0.0045


def test_inari_bang_teeth_sit_on_vertex_columns(inari):
    """inari の前髪は正面から左右に 78.75° ずつで、96 列の殻の列の間隔（3.75°）の 21 個分。
    7 枚の歯の先と、歯と歯の切れ込みがどれも頂点の列に乗る。"""
    p, head, kw = inari["p"], inari["head"], inari["kw"]
    az = np.linspace(0.0, 2 * np.pi, kw["nu"], endpoint=False)
    _, _, _, notch = hair._bang_tips(p, head, az, kw["bangs"])
    off = (az - hair.FRONT + np.pi) % (2 * np.pi) - np.pi
    inside = np.abs(off) <= kw["bangs"]["reach"] + 1e-9  # 前髪の範囲の列
    teeth = kw["bangs"]["teeth"]
    assert np.sum(inside & (notch < 1e-6)) == teeth  # 歯の先
    assert np.sum(inside & (notch > 1.0 - 1e-6)) == teeth + 1  # 切れ込み（範囲の両端を含む）


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
