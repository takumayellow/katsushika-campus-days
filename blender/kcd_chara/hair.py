"""髪型の生成。

頭皮のシェル（頭より一回り大きい殻）＋ベジエ芯線から作る房（strand）の組み合わせ。
房は断面を扁平にしてあるので、少ない本数でもボリュームのある面に見える。
"""

from __future__ import annotations

import math

import numpy as np

from . import mesh as M

FRONT = 1.5 * math.pi  # 正面（-Y）の方位角


def _angdist(az: np.ndarray) -> np.ndarray:
    """正面からの角度差（0..pi）。"""
    return np.abs(((np.asarray(az) - FRONT + math.pi) % (2 * math.pi)) - math.pi)


def _el_max(az: np.ndarray, front: float, back: float, power: float = 0.75,
            flat_front: float = 0.0):
    """方位角ごとの地髪の縁（生え際）の仰角。

    d**power は d=0 で傾きが無限大なので、正面の生え際が尖った Λ になり、
    額の中央だけ髪が下りてくる。flat_front > 0 なら d がその値になるまで
    front のまま（仰角一定＝正面から見て水平な生え際）にして、そこから
    同じ形で back へ向かう。公式の生え際は額の上でほぼ水平なので、
    髪の帯の厚みが額の端まで一定になる。
    """
    d = _angdist(az) / math.pi
    if flat_front > 0.0:
        d = np.clip((d - flat_front) / (1.0 - flat_front), 0.0, 1.0)
    return front + (back - front) * d**power


def _outward(head, pts: np.ndarray, off) -> np.ndarray:
    d = pts - head.center
    d = d / (np.linalg.norm(d, axis=1, keepdims=True) + 1e-9)
    off = np.asarray(off, dtype=float).reshape(-1, 1)
    return pts + d * off


def _skull_radius(head, d: np.ndarray, iters: int = 8) -> np.ndarray:
    """頭の中心から向き d へ進んで頭の面に当たるまでの距離。

    頭の面は極角 (az, el) で張ってあり、変形で点の向きが (az, el) からずれる。
    面の点の向きが d にそろう (az, el) をニュートン法で探す。
    """
    u = d / np.linalg.norm(d, axis=1, keepdims=True)
    ref = np.where(np.abs(u[:, 2:3]) < 0.9, [[0.0, 0.0, 1.0]], [[1.0, 0.0, 0.0]])
    t1 = np.cross(u, ref)
    t1 /= np.linalg.norm(t1, axis=1, keepdims=True)
    t2 = np.cross(u, t1)

    def off_axis(a, e):
        s = head.surface(a, e) - head.center
        s /= np.linalg.norm(s, axis=1, keepdims=True)
        return np.stack([(s * t1).sum(axis=1), (s * t2).sum(axis=1)], axis=1)

    a = np.arctan2(u[:, 1], u[:, 0])
    e = np.clip(np.arccos(np.clip(u[:, 2], -1.0, 1.0)), 1e-3, math.pi - 1e-3)
    h = 1e-5
    for _ in range(iters):
        f = off_axis(a, e)
        ja, je = (off_axis(a + h, e) - f) / h, (off_axis(a, e + h) - f) / h
        det = ja[:, 0] * je[:, 1] - ja[:, 1] * je[:, 0]
        det = np.where(np.abs(det) < 1e-12, np.copysign(1e-12, det), det)
        da = (je[:, 0] * f[:, 1] - je[:, 1] * f[:, 0]) / det
        de = (ja[:, 1] * f[:, 0] - ja[:, 0] * f[:, 1]) / det
        # 1 回に 0.3 rad より大きくは動かさない（変形の強い所で別の枝へ飛ばない）。
        step = np.maximum(1.0, np.hypot(da, de) / 0.3)
        a, e = a + da / step, np.clip(e + de / step, 1e-4, math.pi - 1e-4)
    return np.linalg.norm(head.surface(a, e) - head.center, axis=1)


def _off_skull(head, pts: np.ndarray, gap) -> np.ndarray:
    """頭の面から gap より内へ入った点を、頭の中心から外へ押し出す。

    gap より外の点は動かさない。内側の点は面から gap の 0.64 倍の所へ
    なめらかに寄せる（押し出す所と押し出さない所の境で面が折れない）。
    深く入った点ほど 0.64 倍に近づき、それより内へは入らない。gap は点ごとの
    配列でもよい。
    """
    soft = 0.36 * np.asarray(gap, dtype=float)
    d = pts - head.center
    r = np.linalg.norm(d, axis=1)
    x = r - _skull_radius(head, d) - gap
    y = np.where(x >= 0.0, x, soft * (np.exp(np.minimum(x, 0.0) / soft) - 1.0))
    return pts + d * ((y - x) / r)[:, None]


def scalp_pt(head, az, el, off: float) -> np.ndarray:
    az = np.atleast_1d(np.asarray(az, dtype=float))
    el = np.atleast_1d(np.asarray(el, dtype=float))
    pts = head.surface(az, el)
    return _outward(head, pts, np.full(len(az), off))


def build_scalp(mb: M.MeshBuilder, p: dict, head, *, front_el: float,
                back_el: float, thickness: float, part: str = "hair_cap",
                nu: int = 32, flat_front: float = 0.0, jag: float = 0.0,
                teeth: float = 5.0, jag_span: float = 112.0, nape: float = 0.0):
    """頭皮に沿った地髪の殻。縁が生え際になる。

    jag > 0 なら額側の縁に切れ込みを入れる。真円の縁は水泳帽に見えるので、
    公式のように髪が額へ尖って食い込む形にするための刻み。
    nape > 0 なら耳より後ろの縁だけを、真後ろで nape だけ下げる（耳の上と
    額の縁は動かさない）。
    """
    nv = 10
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    emax = _el_max(az, front_el, back_el, flat_front=flat_front)
    if nape > 0.0:
        # 正面から 99°（耳のすぐ後ろ）で下げ始め、真後ろ (180°) で nape に届く。
        emax = emax + nape * _smooth((_angdist(az) / math.pi - 0.55) / 0.45)
    if jag > 0.0:
        # 正面からの符号付き角度。ギザギザは額側だけで、側頭部へ向けて消す。
        off = ((az - FRONT + math.pi) % (2 * math.pi)) - math.pi
        w = _smooth((math.radians(jag_span) - np.abs(off))
                    / math.radians(jag_span * 0.55))
        # |cos| の山は尖る。滑らかな正弦だと波打った縁にしかならず、
        # 髪が額へ「切れ込む」ようには見えない。
        emax = emax + jag * w * (1.0 - np.abs(np.cos(teeth * off * 0.5)))
    t_hair = p["head_w"] * thickness
    rings = []
    for j in range(nv + 1):
        t = j / nv
        el = np.maximum(t * emax, 1e-3)
        off = t_hair * (0.55 + 0.45 * math.sin(min(1.0, t) * math.pi) ** 0.5)
        pts = head.surface(az, el)
        rings.append(_outward(head, pts, np.full(nu, off)))
    with mb.part(part):
        mb.add_grid(rings, "hair", smooth=True, cap_start=True, cap_end=False,
                    flip=bool(p.get("outward_faces")))
    return rings[-1]


def _smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def _el_at_z(head, az: np.ndarray, z: np.ndarray) -> np.ndarray:
    """方位ごとに、頭の表面の高さが z になる極角。"""
    grid = np.linspace(0.0, 0.75 * math.pi, 271)
    return np.array([np.interp(-zz, -head.surface(np.full_like(grid, a), grid)[:, 2],
                               grid) for a, zz in zip(az, z)])


def _carrier(p: dict, head, pts: np.ndarray, *, base: float, gain: float,
             lift: float, ease: float = 0.3) -> np.ndarray:
    """髪を載せる形へ、頭の形に沿って作った髪の点 pts を空間ごと移す。

    目の少し下（顎→頭頂の base の高さ）から上で、幅と後頭部の奥行きを頭頂へ
    向けて 1 → gain 倍へ広げ、目の少し下から頭頂までの高さを lift 倍にする。
    頭頂より上へ出た殻の頂は同じ勾配のまま広げ続ける（mirai の殻の頂で幅は
    約 1.3 倍）。
    顔の前面の奥行きは変えないので、前髪は額から浮かない。頭の形に沿わせた
    殻は頭蓋に貼り付いた兜に見えるので、殻と頭の間を目より上で空けて髪の
    量を出す。

    広がり始めは ease の幅で 2 次から直線へつなぎ、base の高さの横髪に
    折れ目を作らない。広げる倍率は z だけで決まるので、base より下の横髪
    の毛先は動かない（載せる形ごと広げた根元から毛先が外へ開かない）。
    """
    hd = p["head_d"]
    z0 = p["z"]["chin"] + (p["z"]["top"] - p["z"]["chin"]) * base
    u = (pts[:, 2] - z0) / (p["z"]["top"] - z0)
    r = np.where(u <= 0.0, 0.0,
                 np.where(u < ease, u * u / (2.0 * ease), u - 0.5 * ease)) / (1.0 - 0.5 * ease)
    k = 1.0 + (gain - 1.0) * r
    d = pts - head.center
    back = _smooth((d[:, 1] / hd + 0.15) / 0.30)   # 顔の前面 0 .. 横から後ろ 1
    return head.center + np.stack([
        d[:, 0] * k,
        d[:, 1] * (1.0 + (k - 1.0) * back),
        d[:, 2] + (lift - 1.0) * (p["z"]["top"] - z0) * r * r], axis=1)


def _bang_tips(p: dict, head, az: np.ndarray, bangs: dict):
    """殻から続けて下ろす前髪の、方位ごとの毛先の極角と前髪の重み。

    毛先は teeth 枚の歯。歯の先は尖らせ、歯と歯の切れ込みは丸める（切れ込み
    が尖っていると、そこで輪郭線の殻が折れ返って毛先から上へ短い線が出る）。
    返すのは (歯の先を結んだ包絡の極角, 毛先の極角, 重み, 切れ込みの度合い)。
    重みは前髪の範囲の両端で 0 へ落とし、横髪の生え際へ滑らかに戻す。
    切れ込みの度合いは歯の先で 0、歯と歯の切れ込みで 1。
    """
    hh = p["head_h"]
    off = ((az - FRONT + math.pi) % (2 * math.pi)) - math.pi
    us = off / bangs["reach"]
    au = np.minimum(np.abs(us), 1.0)
    phase = ((us + 1.0) * bangs["teeth"] / 2.0) % 1.0
    notch = np.abs(np.cos(math.pi * phase))        # 0 = 歯の先、1 = 切れ込み
    z0 = p["z"]["chin"] - hh * 0.015
    z_end = z0 + (p["z"]["top"] - z0) * bangs["end_v"]
    z_env = z_end - hh * bangs["droop"] * au ** 1.6
    el_env = _el_at_z(head, az, z_env)
    el_tip = _el_at_z(head, az, z_env + hh * bangs["notch"] * notch)
    w = _smooth((1.0 - np.abs(us)) / bangs["fade"])
    return el_env, el_tip, w, notch


def _helmet_rows(p: dict, head, *, front_el: float,
                 back_el: float, thickness: float, z_end_side: float,
                 z_end_back: float, puff: float = 1.0, ridges: int = 14,
                 ridge_amp: float = 0.30, jag: float = 0.030,
                 hang_lo: float = 0.26, hang_hi: float = 0.50,
                 inward: float = 0.74, depth: float = 0.10,
                 nu: int = 72, nv: int = 10,
                 nh: int = 12, bangs: dict | None = None,
                 carry: dict | None = None):
    """地髪と横髪・後ろ髪を 1 枚の連続した殻にする頂点の列 (rings, outer, inner)。

    房を並べる方式だと、地髪の縁から房が垂れる境目が「帽子の下のプリーツ
    カーテン」に見える。殻を生え際から毛先まで途切れなく続け、縦の畝
    （ridges）と毛先のギザギザ（jag）で房の感じを出す。

    bangs を渡すと、前髪も別の部品にせず、正面側の殻をそのまま毛先の歯
    まで下ろす。前髪を別の板や房で重ねると、Unity の輪郭線（面を法線の
    向きへ押し出した殻の裏面）が重なりの段や板の端ごとに線を引き、頭頂を
    横切る亀裂や、こめかみの縦の弧、毛先の X 字になる。1 枚の面なら
    段も端も無い。

    - reach: 前髪の範囲（正面からの方位角、ラジアン）。
    - teeth / notch / droop / end_v: 歯の数、切れ込みの深さ、こめかみ側の
      毛先の下がり（ともに頭高比）、中央の毛先の高さ（顎→頭頂の割合）。
    - tip: 毛先での頭皮からの高さ（頭幅比）。額へ寄せ切る。
    - fall: 毛先へ向けて高さを落とす曲線の指数。大きいほど毛先の手前まで
      厚みを保ち、毛先で丸く落ちる。
    - valley: 0 なら歯ごとに毛先で高さを落とし切り、1 なら歯の先を結んだ
      包絡で落とす（切れ込みに谷を作らない）。
    - fade: 前髪の範囲の両端で横髪へ戻す幅（reach 比）。
    - onset: 垂れる割合 hang がこれより小さい垂れ始めの横髪は、外へ膨らま
      せず地髪の面の向きのまま下ろす。
    - groove: 歯と歯の間の溝の深さ（殻の厚みに対する割合）。0 なら前髪は
      毛先の歯のほかは 1 枚のなめらかな面になり、帽子に見える。
    - g_from: 溝を始める極角（最も厚い所の極角に対する倍率）。

    carry を渡すと、作り終えた殻を頭より大きい「髪を載せる形」へ移す
    （`_carrier` の base / gain / lift / ease）。
    """
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    emax = _el_max(az, front_el, back_el)
    d = _angdist(az) / math.pi                     # 0=正面 .. 1=真後ろ
    hang = _smooth((d - hang_lo) / (hang_hi - hang_lo))  # 垂れる割合
    wb = _smooth((d - 0.50) / 0.50)                # 後ろ寄りの重み
    t_hair = hw * thickness
    crest = 0.5 + 0.5 * np.cos(az * ridges)        # 畝の山
    # 頭頂から最も厚い所（生え際までの半分）までは、どの方位も同じ高さの
    # 曲線。そこから縁（前髪なら毛先）へ向けて高さを落とす。
    el_pk = 0.5 * emax
    if bangs:
        el_env, el_tip, fw, notch = _bang_tips(p, head, az, bangs)
        # 房の溝。歯と歯の切れ込みの列を、毛先へ向けて深く凹ませる。溝の
        # 深さは切れ込みの度合いに比例させる（2 乗にして溝を細く深くすると、
        # 3.2 m の輪郭線の殻が溝の底で折れ返る）。
        groove = bangs.get("groove", 0.0) * fw * notch
        E = emax + (np.maximum(el_tip, emax) - emax) * fw
        e_ref = el_tip + (el_env - el_tip) * bangs["valley"]
        e_ref = emax + (np.maximum(e_ref, emax) - emax) * fw
        r_end = t_hair * 0.55 + (hw * bangs["tip"] - t_hair * 0.55) * fw
        fall = bangs["fall"]
    else:
        E = emax
    rings = []
    for j in range(nv + 1):
        t = j / nv
        if bangs:
            el = t * E
            rise = t_hair * (0.55 + 0.45 * np.sin(np.minimum(0.5, el / emax) * math.pi) ** 0.5)
            s = np.clip((el - el_pk) / (e_ref - el_pk), 0.0, 1.0)
            g = (1.0 - fw) * np.cos(s * 0.5 * math.pi) ** 0.5 + fw * (1.0 - s ** fall)
            off = np.where(el <= el_pk, rise, r_end + (t_hair - r_end) * g)
            # 溝は頭頂を避けて始め、毛先へ向けて深くする。頭頂から溝を
            # 入れると、溝が極へ集まってかぼちゃの筋に見える。
            e0 = bangs.get("g_from", 1.0) * el_pk
            off = off * (1.0 - groove * _smooth((el - e0) / np.maximum(e_ref - e0, 1e-6)))
        else:
            off = t_hair * (0.55 + 0.45 * math.sin(min(1.0, t) * math.pi) ** 0.5)
        el = np.maximum(t * E, 1e-3)
        off = off * (1.0 + ridge_amp * crest * t ** 2 * hang)
        pts = head.surface(az, el)
        rings.append(_outward(head, pts, off))
    rim = rings[-1]
    # 垂れは縁の厚み（地髪の厚み、前髪なら毛先の高さ）より頭へ寄せない。
    # 毛先を顎の高さのまま内へ引くと、後頭部の張り出しの所で垂れが頭蓋の
    # 内側をくぐり、後ろから見ると髪の縁の下に後頭部の肌が顔のような楕円で出る。
    gap = np.minimum(t_hair * 0.55, np.broadcast_to(off, az.shape))
    nrm = rim * np.array([1.0, 1.0, 0.0])
    nrm = nrm / (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9)

    # 毛先の位置。畝の山ほど低く（尖る）、正面は垂らさない。
    z_end = z_end_side + (z_end_back - z_end_side) * wb
    z_end = z_end + hh * jag * (np.cos(az * ridges) - 0.3)
    z_end = np.minimum(z_end, rim[:, 2] - hh * 0.02)
    z_end = rim[:, 2] + (z_end - rim[:, 2]) * hang
    ex = rim[:, 0] * (0.90 - (0.90 - inward) * wb)
    ey = rim[:, 1] * (1.0 - wb) * 0.70 + hd * depth * wb + hd * 0.02
    e = np.stack([ex, ey, z_end], axis=1)
    e = rim + (e - rim) * hang[:, None]
    drop = (rim[:, 2] - e[:, 2])[:, None]
    c1 = rim + nrm * (hw * 0.055 * puff * hang[:, None]) - drop * 0.30 * np.array([0, 0, 1.0])
    c2 = e + nrm * (hw * 0.030 * puff * hang[:, None]) + drop * 0.34 * np.array([0, 0, 1.0])
    if bangs:
        # 垂れ始めの短い横髪は、地髪の面の向きのまま下ろす。外へ膨らませると
        # 数 mm の垂れの付け根で面が谷折りになり、輪郭線の殻がこめかみで
        # 折れ返って線を引く。
        tan = rim - rings[-2]
        tan = tan / (np.linalg.norm(tan, axis=1, keepdims=True) + 1e-12)
        straight = (1.0 - _smooth(hang / bangs["onset"]))[:, None]
        c1, c2, e = (q + (rim + tan * np.linalg.norm(q - rim, axis=1, keepdims=True) - q)
                     * straight for q in (c1, c2, e))
    outer, inner = [], []
    for k in range(1, nh + 1):
        v = k / nh
        b0, b1, b2, b3 = (1 - v) ** 3, 3 * v * (1 - v) ** 2, 3 * v * v * (1 - v), v ** 3
        pos = b0 * rim + b1 * c1 + b2 * c2 + b3 * e
        # 垂れない列 (hang = 0) は縁の 1 点に潰れたまま残す。
        pos = np.where(hang[:, None] > 0.0, _off_skull(head, pos, gap), pos)
        ridge = nrm * (t_hair * ridge_amp * crest * hang * (1.0 - 0.55 * v))[:, None]
        if bangs:
            # 畝の高さは縁で地髪の畝（縁の厚み r_end に比例）から始め、段を作らない。
            ridge = ridge * (r_end / t_hair + (1.0 - r_end / t_hair) * _smooth(v / 0.35))[:, None]
        outer.append(pos + ridge)
        inner.append(pos + ridge - nrm * (hw * 0.022 * hang)[:, None])
    if carry:
        rings, outer, inner = ([_carrier(p, head, q, **carry) for q in rows]
                               for rows in (rings, outer, inner))
    return rings, outer, inner


def _lay_helmet(mb: M.MeshBuilder, p: dict, rings, outer, inner) -> None:
    """_helmet_rows の列に、外殻・内殻・毛先の縁の帯の順で面を張る。"""
    # 外殻・内殻・縁の 3 枚は向きが揃っていて、揃って裏返っている
    # (外殻の面が頭の中を向く)。直すときは 3 枚とも返す。
    out = bool(p.get("outward_faces"))
    # 正面 (hang = 0) では垂れも内殻も縁も生え際の 1 本に潰れて面積 0 の面になる。
    # 向きを直した髪ではそれを張らない（潰れた面の頂点は法線が定まらず、
    # 輪郭線の殻が生え際に沿ってめくれて頭頂を横切る Λ の線になる）。
    flat = (p["head_w"] * 1e-3) ** 2 if out else 0.0
    mb.add_grid(rings + outer, "hair", smooth=True, cap_start=True,
                cap_end=False, flip=out, min_area=flat)
    mb.add_grid(inner, "hair", smooth=True, flip=not out, min_area=flat)
    mb.add_grid([outer[-1], inner[-1]], "hair", smooth=False, flip=out,
                min_area=flat)


def build_helmet(mb: M.MeshBuilder, p: dict, head, *, part: str = "hair_back",
                 **shape):
    """地髪と横髪・後ろ髪の殻を part に組み、生え際のリングを返す。

    形の引数 shape は `_helmet_rows` のもの。陰の法線は、同じ殻を畝
    (ridge_amp) と毛先のギザギザ (jag) 無しで組んだときの頂点法線にする。
    畝のまま陰を付けると、陰の境目の近くで畝ごとに明暗が入れ替わり、後ろ髪と
    横髪にぼやけた濃い斑が並ぶ（後ろから映すゲームのカメラで一番目立つ）。
    ギザギザは畝と同じ周期で垂れの長さを変えるので、毛先の近くで同じ斑になる。
    畝もギザギザも形には残るので、シルエットは変わらない。
    """
    # 陰の代理形状。_lay_helmet で張るのは頂点の並びを本物とそろえるためで、
    # 使うのは頂点の位置だけ（法線を求める面は本物の面）。
    proxy = M.MeshBuilder()
    _lay_helmet(proxy, p, *_helmet_rows(p, head, **{**shape, "ridge_amp": 0.0,
                                                    "jag": 0.0}))
    rings, outer, inner = _helmet_rows(p, head, **shape)
    with mb.part(part):
        v0, f0 = len(mb.verts), len(mb.faces)
        _lay_helmet(mb, p, rings, outer, inner)
        # 縁の帯は外殻・内殻と別の頂点で張るので、毛先で頂点が重なる。法線を
        # そろえないと輪郭線の殻が毛先で裂け、横髪の前の縁に沿った点線になる。
        # shade_as は重なった頂点の法線もそろえる。
        mb.shade_as(v0, f0, proxy.verts)
    return rings[-1]


def strand(mb: M.MeshBuilder, part: str, ctrl, r0: float, r1: float, *,
           n: int = 8, flat: float = 0.44, power: float = 2.6, seg: int = 11,
           taper: float = 1.5, mat: str = "hair", root: float = 1.0,
           root_len: float = 0.32):
    """芯線 ctrl に沿う房。root < 1 なら根元を r0*root から太らせる。

    根元を頭皮の内側に置いて細く始めると、房の切り口が地髪の外に段差
    （棚）として見えなくなり、地髪から房が生えているように繋がる。
    """
    path = M.bezier3(ctrl[0], ctrl[1], ctrl[2], ctrl[3], seg)
    t = np.linspace(0.0, 1.0, seg)
    rr = r0 + (r1 - r0) * t**taper
    if root < 1.0:
        g = np.clip(t / root_len, 0.0, 1.0)
        rr = rr * (root + (1.0 - root) * (g * g * (3.0 - 2.0 * g)))
    radii = [(float(r * flat), float(r)) for r in rr]
    with mb.part(part):
        mb.add_tube(path, radii, mat, n=n, power=power, cap_start=False,
                    cap_end=True)


def _part_tris(mb, names) -> np.ndarray:
    """部位 names の面を三角形に割った (T, 3, 3) の頂点座標。"""
    keep = set(mb.part_indices(*names).tolist())
    V = np.asarray(mb.verts, dtype=float)
    tris = [(f[0], f[k], f[k + 1]) for f in mb.faces if f[0] in keep
            for k in range(1, len(f) - 1)]
    return V[np.array(tris)]


def _far_hit(tris: np.ndarray, origin, dirs: np.ndarray,
             chunk: int = 128, eps: float = 1e-6) -> np.ndarray:
    """origin から dirs の向きへ飛ばした光線が tris に当たる一番遠い距離。

    当たらなければ nan。光線が三角形の辺や頂点をちょうど通っても外さない
    よう、重心座標に eps の余裕を持たせる。
    """
    a = tris[:, 0]
    e1, e2 = tris[:, 1] - a, tris[:, 2] - a
    s = np.asarray(origin, dtype=float) - a
    q = np.cross(s, e1)
    tq = (e2 * q).sum(axis=1)
    out = np.full(len(dirs), np.nan)
    for i0 in range(0, len(dirs), chunk):
        D = dirs[i0:i0 + chunk]
        h = np.cross(D[:, None, :], e2[None, :, :])
        det = (h * e1[None]).sum(axis=2)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        u = (h * s[None]).sum(axis=2) * inv
        v = (D @ q.T) * inv
        t = tq[None, :] * inv
        hit = ok & (u >= -eps) & (v >= -eps) & (u + v <= 1.0 + eps) & (t > 0.0)
        far = np.where(hit, t, -np.inf).max(axis=1)
        out[i0:i0 + chunk] = np.where(np.isfinite(far), far, np.nan)
    return out


# --------------------------------------------------------------------------
# 房の配置
# --------------------------------------------------------------------------


def _thicken(P: np.ndarray, t: float) -> np.ndarray:
    """(nu, k, 3) のシート点列を面法線方向へ t だけ内側へずらした点列。"""
    du = np.gradient(P, axis=0)
    dv = np.gradient(P, axis=1)
    n = np.cross(du, dv)
    n = n / (np.linalg.norm(n, axis=2, keepdims=True) + 1e-9)
    return P - n * t


def _sheet(mb, part: str, ctrl_fn, us, *, seg: int = 11, frac: float = 0.82,
           thick: float = 0.0, mat: str = "hair") -> None:
    """房の裏に張る薄いシェル。房と房の隙間から地肌が見えるのを防ぐ。"""
    paths = [M.bezier3(*ctrl_fn(float(u)), seg) for u in us]
    P = np.stack(paths, axis=0)
    k = max(2, int(round(seg * frac)))
    P = P[:, :k, :]
    Q = _thicken(P, thick)
    with mb.part(part):
        v0, f0 = len(mb.verts), len(mb.faces)
        mb.add_grid([P[:, j, :] for j in range(k)], mat, smooth=True,
                    close_u=False)
        mb.add_grid([Q[:, j, :] for j in range(k)], mat, smooth=True,
                    close_u=False, flip=True)
        mb.add_grid([P[:, k - 1, :], Q[:, k - 1, :]], mat, smooth=False,
                    close_u=False)
        # 毛先の縁の帯と表裏の面の継ぎ目で、輪郭線の殻を裂けないようにする。
        mb.share_normals(v0, f0)


def _bang_ctrl(p, head, u: float, *, span: float, el: float, z_end: float,
               parted: float, sweep: float, jag: float, phase: float,
               lift: float, tuck: float = 0.0, droop: float = 1.0,
               fwd: float = 0.14):
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = FRONT + u * span
    s = scalp_pt(head, az, el, hw * lift)[0]
    side = math.sin(u * span)
    sign_u = 1.0 if u >= 0.0 else -1.0
    x_end = s[0] * 1.14 + parted * hw * 0.10 * sign_u + sweep * hw * 0.30
    y_end = -hd * (0.44 + 0.05 * (1.0 - abs(side)))
    # 房ごとに長さを散らす。全部同じ丈だと毛先が四角い歯のように並ぶ。
    wob = (math.sin(phase * 2.399) * 0.60
           + math.sin(phase * 5.131 + 1.7) * 0.40)
    jz = hh * 0.040 * jag * wob - hh * 0.010 * abs(u)
    # 中央を短く、こめかみへ向かって長くする（額の中央から眉が出る）
    e = np.array([x_end, y_end + hd * 0.040 * tuck,
                  z_end - hh * 0.085 * droop * abs(u) ** 1.6 + jz])
    c1 = s + np.array([0.0, -hd * fwd, hh * 0.02])
    # 毛先の手前で一度前へ出してから内へ戻すと、房が額に沿って丸く
    # 内巻きになる（板を貼り付けたようにならない）
    c2 = e + np.array([0.0, -hd * (0.05 + 0.07 * tuck), hh * 0.13])
    # 根元は地髪の中。浮かせたまま始めると、房の切り口が頭頂側から
    # ギザギザの冠のように見える。
    s0 = scalp_pt(head, az, el - 0.20, hw * 0.010)[0]
    return s0, c1, c2, e


def _bangs(mb, p, head, *, span: float, count: int, el: float,
           end_v: float, width: float, parted: float = 0.0, sweep: float = 0.0,
           jag: float = 1.0, blunt: float = 0.0,
           lift: tuple[float, float] = (0.070, 0.030), fwd: float = 0.14,
           root: float = 0.30, root_len: float = 0.32, tuck: float = 0.9,
           half: bool = True, sheet_frac: float = 0.90):
    """前髪。5〜7 房の独立した房で構成する。

    - 房は毛先へ向かって細り、先端が尖る（taper）。
    - 房は頭皮から浮かせて（lift）空気層を作る。
    - 房の裏に薄いシェルを張り、房の隙間から地肌が見えないようにする。

    `blunt`（0..1）は「ぱっつん」の度合い。1 に近づけるほど房の長さの散らばり
    (jag) と中央→こめかみの下がり (droop) を殺し、毛先の半径を残して房どうしを
    重ねるので、下端が 1 本の水平線になる。公式 tus_chara02.jpg の
    マドンナちゃんの前髪は真横一文字なので、0 のままだと鋸歯が 9 枚並ぶ。

    房の形は次の引数で変えられる。mirai 以外の髪型は既定値で作る。

    - `lift`: 房を頭皮から浮かせる量（頭幅比）。(基準, 房ごとの揺らぎ)。
    - `fwd`: 根元の次の制御点を額の前へ出す量（頭の奥行き比）。
    - `root` / `root_len`: 根元の太さ（r0 比）と、そこから太り切るまでの区間。
    - `tuck`: 毛先を額へ寄せる量。
    - `half`: 房の間に半房を差し込むか。
    - `sheet_frac`: 裏当てを房の芯線のどこまで張るか。
    """
    hw, hh = p["head_w"], p["head_h"]
    z0 = p["z"]["chin"] - hh * 0.015
    z1 = p["z"]["top"]
    z_end = z0 + (z1 - z0) * end_v
    # ぱっつんは「細い房をたくさん並べて下端を揃える」。房を太らせて数を
    # 据え置くと 1 本が直径 0.59 頭幅の腸詰めになり、6 本が額の上で融合して
    # 「積み重ねた饅頭」の冠になる（実際そう見えた）。数で埋める。
    n = int(min(16, max(5, round(count * (0.42 + 0.58 * blunt)))))
    jag = jag * (1.0 - 0.92 * blunt)
    droop = 1.0 - 0.72 * blunt

    # 裏当て。房より少し長くしておかないと房の隙間から地肌が覗く。
    _sheet(mb, "hair_front",
           lambda u: _bang_ctrl(p, head, u, span=span * 1.02, el=el + 0.03,
                                z_end=z_end - hh * 0.030, parted=parted,
                                sweep=sweep, jag=0.0, phase=0.0, lift=0.026,
                                droop=droop),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=sheet_frac,
           thick=hw * (0.018 + 0.022 * blunt))

    # ぱっつんでは裏当てのシェルが前髪の本体で、房はその上に乗る細い畝。
    # 房を太いままにすると側面から額の前へ 0.19 頭高も突き出した庇になる。
    r0 = hw * (span / n) * (0.98 - 0.30 * blunt)
    tip = 0.05 + 0.45 * blunt
    flat_b = 0.74 - 0.45 * blunt
    # ぱっつんでは房の毛先を裏当てシェルの下端より上で止める。房の丸い
    # 毛先がシェルより下に出ると、輪郭シェーダが房 1 本ずつを縁取るので
    # 前髪の下端が「櫛の歯」に見える（レンダ拡大で 9 枚の歯を確認）。
    z_tip = z_end + hh * 0.060 * blunt
    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        # 房ごとに頭皮からの浮きを変える。全部同じだと 1 枚の板に見える。
        lift_i = (lift[0] - 0.038 * blunt
                  + lift[1] * math.sin(i * 2.399 + 0.6) * (1.0 - blunt))
        ctrl = _bang_ctrl(p, head, u, span=span, el=el, z_end=z_tip,
                          parted=parted, sweep=sweep, jag=jag, phase=i,
                          lift=lift_i, tuck=tuck, droop=droop, fwd=fwd)
        strand(mb, "hair_front", ctrl, r0, r0 * tip, n=10, flat=flat_b,
               power=2.0, seg=14, taper=2.0 - 1.3 * blunt, root=root,
               root_len=root_len)
        # ぱっつんでは本数で埋めるので、房の間に差し込む半房は要らない
        # （入れると房が 2 枚重なって額が団子で埋まる）。
        if half and i < n - 1 and blunt < 0.5:
            u2 = u + 1.0 / n
            ctrl2 = _bang_ctrl(p, head, u2, span=span,
                               el=el - 0.055 * (1.0 - 0.6 * blunt),
                               z_end=z_end - hh * 0.048 * (1.0 - 0.85 * blunt),
                               parted=parted, sweep=sweep, jag=jag * 1.5,
                               phase=i + 0.5, lift=0.046, tuck=0.6,
                               droop=droop, fwd=fwd)
            strand(mb, "hair_front", ctrl2, r0 * (0.52 + 0.34 * blunt),
                   r0 * (0.06 + 0.40 * blunt), n=8, flat=0.66, power=2.0,
                   seg=12, taper=1.9 - 1.2 * blunt, root=root,
                   root_len=root_len)


def _side(mb, p, head, *, count: int, az_lo: float, az_hi: float, el: float,
          z_end: float, width: float, curl: float = 0.0):
    """横髪。房数を絞って 1 房を太くし、毛先を尖らせる。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    n = max(3, int(round(count * 0.95)))
    w = width * math.sqrt(count / n) * 0.80
    for sgn in (-1, 1):
        for i in range(n):
            t = (i + 0.5) / n
            az = FRONT + sgn * (az_lo + (az_hi - az_lo) * t)
            row = i % 2
            lift = 0.050 + 0.032 * row
            # 根元は地髪の中（el を上へ、浮きをほぼ 0 に）。房の切り口が
            # 地髪の外へ出ると、横から見てそこが棚になる。
            s0 = scalp_pt(head, az, el - 0.30, hw * 0.012)[0]
            s = scalp_pt(head, az, el, hw * lift)[0]
            # 顔寄りの房を短く、後ろへ行くほど長くし、さらに房ごとに散らす。
            # 全部同じ丈だと横から見て段ボールの板になる。
            drop = hh * (0.30 * (1.0 - t) ** 1.6
                         - 0.075 * math.sin(i * 2.399)
                         - 0.045 * math.sin(i * 5.131 + 1.1))
            # 毛先の前後位置も房ごとに変える（同じ深さで揃うと 1 枚の板）
            depth = 0.52 + 0.09 * row - 0.05 * math.sin(i * 2.399 + 0.4)
            e = np.array([s[0] * (0.82 + 0.04 * t) + sgn * curl * hw * 0.12,
                          s[1] * depth + hd * 0.02,
                          z_end + drop])
            c1 = s + np.array([sgn * hw * 0.06, 0.0, -hh * 0.12])
            c2 = np.array([e[0] + sgn * hw * 0.05, e[1],
                           e[2] + (s[2] - e[2]) * 0.34])
            strand(mb, "hair_side", (s0, c1, c2, e), hw * w, hw * w * 0.05,
                   n=9, flat=0.86, power=2.0, seg=14, taper=2.1, root=0.28)


def _back_ctrl(p, head, u: float, *, el: float, z_end: float, puff: float,
               lift: float, jag: float = 0.0, phase: float = 0.0,
               depth: float = 0.0, sink: float = 0.0):
    """sink > 0 なら根元を el - sink の位置（地髪の中）に置く。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = FRONT + math.pi + u * math.radians(86.0)
    s = scalp_pt(head, az, el, hw * lift)[0]
    s0 = scalp_pt(head, az, el - sink, hw * 0.012)[0] if sink > 0 else s
    jz = hh * (0.055 * jag * math.sin(phase * 2.399)
               + 0.030 * jag * math.sin(phase * 5.131 + 0.8))
    # 毛先へ向けて幅を絞る。頭の幅より外へ出ると、横から見たときに
    # 後ろ髪が扇のように張り出して見える。
    e = np.array([s[0] * 0.74, hd * (0.10 + 0.05 * (1.0 - abs(u)) + depth),
                  z_end + jz])
    c1 = s + np.array([0.0, hd * 0.05 * puff, -hh * 0.22])
    c2 = np.array([e[0] * 1.06, e[1] + hd * 0.03 * puff,
                   e[2] + (s[2] - e[2]) * 0.34])
    # 毛先を首の方へ寄せると後頭部の張り出しの内側に入る。房は根元と同じ
    # 高さだけ頭の面から浮かせたまま下ろす。
    c1, c2, e = _off_skull(head, np.array([c1, c2, e]), hw * lift)
    return s0, c1, c2, e


def _back(mb, p, head, *, count: int, el: float, z_end: float, width: float,
          puff: float = 1.0):
    """後ろ髪。裏当てシェル＋太めの房。毛先は尖らせる。"""
    hw, hh = p["head_w"], p["head_h"]
    n = max(6, int(round(count * 0.66)))
    w = width * math.sqrt(count / n) * 1.02

    _sheet(mb, "hair_back",
           lambda u: _back_ctrl(p, head, u, el=el - 0.05,
                                z_end=z_end + hh * 0.05, puff=puff,
                                lift=0.026),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=0.86, thick=hw * 0.015)

    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        row = i % 2
        lift = 0.052 + 0.028 * row
        ctrl = _back_ctrl(p, head, u, el=el, z_end=z_end, puff=puff,
                          lift=lift, jag=1.0, phase=i, depth=0.06 * row,
                          sink=0.32)
        strand(mb, "hair_back", ctrl, hw * w, hw * w * 0.05, n=9, flat=0.88,
               power=2.0, seg=14, taper=2.1, root=0.28)


def _spikes(mb, p, head, *, count: int, span: float, el: float,
            length: float, part: str = "hair_front", lift: float = 0.03,
            forward: float = 0.25, updown: float = 1.0, base: float = 0.60,
            taper: float = 1.6, phase: float = 0.0, flat: float = 0.85):
    """生え際から上へ跳ねるトゲ房（坊っちゃんの逆立てた髪）。

    updown < 0 なら下向き（生え際のギザギザ）。base は根元の太さ。
    """
    hw, hh = p["head_w"], p["head_h"]
    up = np.array([0.0, 0.0, updown])
    r0 = hw * (span / count) * base
    for i in range(count):
        u = (i + 0.5 + phase) / count * 2.0 - 1.0
        az = FRONT + u * span * 0.5
        s = scalp_pt(head, az, el, hw * lift)[0]
        d = M.normalize(s - head.center)
        wob = 0.85 + 0.15 * math.sin(i * 2.399)
        length_i = hh * length * wob * (1.0 - 0.22 * abs(u))
        direction = M.normalize(up * 1.0 + d * 0.55
                                + np.array([0.0, -forward, 0.0]))
        e = s + direction * length_i
        c1 = s + d * length_i * 0.30 + up * length_i * 0.12
        c2 = s + direction * length_i * 0.70
        strand(mb, part, (s, c1, c2, e), r0, r0 * 0.04, n=8, flat=flat,
               power=2.0, seg=9, taper=taper)


def _crest_tufts(mb, p, head, rows, rim, *, az_span: float, length: float,
                 lean: float = -0.34, base: float = 0.050,
                 flat: float = 0.62, out_mix: float = 0.30,
                 part: str = "hair_front", root_off: float = 0.11):
    """額の上に立てる跳ねたトゲ房の束（坊っちゃんの逆立てた前髪）。

    頭頂の稜線（正面→後頭部）に並べると、横から見たとき前後に連なる
    フィンになって鶏冠＝ちょんまげに見える。公式イラストのトゲは額の
    上だけに 3 つ、しかも前へ倒れながら跳ねているので、稜線ではなく
    前頭部の「方位角 × 仰角」の面へ散らす。1 本を細くして本数を稼ぐと、
    大きな三角板ではなく毛束に見える。

    rows は (生え際比, 本数, 長さ倍率, 太さ倍率) の並び。仰角を直接では
    なく生え際（rim(az)）に対する比で持つのは、生え際の高さを変えても
    房の根元が地髪の外へ飛び出さないようにするため。1.0 が生え際、
    0.0 が頭頂。
    """
    hw, hh = p["head_w"], p["head_h"]
    up = np.array([0.0, 0.0, 1.0])
    for ri, (frac, cnt, lsc, wsc) in enumerate(rows):
        r0 = hw * base * wsc
        for i in range(cnt):
            u = (i + 0.5) / cnt * 2.0 - 1.0
            # 行ごとに半ピッチずらす。真上から見て格子に揃うと櫛の歯になる。
            u += (0.5 / cnt) * (1.0 if ri % 2 else -1.0)
            # 1 本ずつ間隔・長さ・傾きをずらす。等間隔で同じ高さのトゲが
            # 並ぶと、正面から見て王冠（ノコギリ刃）にしか見えない。公式の
            # トゲは高さも角度もバラバラ。乱数だとビルドのたびに形が変わって
            # 差分が出るので、周期の違う sin を足して固定のゆらぎにする。
            w1 = math.sin(i * 2.399 + ri * 1.31)
            w2 = math.sin(i * 5.051 + ri * 2.713)
            u += (0.62 / cnt) * w2
            az = FRONT + u * az_span * 0.5
            el = max(0.04, frac * float(rim(az)))
            # 根元は地髪の殻の中（殻の厚み 0.14hw より内側）。殻の外に
            # 置くと房の切り口が輪になって浮く。
            s = scalp_pt(head, az, el, hw * root_off)[0]
            d = M.normalize(s - head.center)
            wob = 1.0 + 0.30 * (0.62 * w1 + 0.38 * w2)
            # 中央を長く、こめかみ側を短く。公式も中央の 1 本が一番高い。
            li = hh * length * lsc * wob * (1.0 - 0.26 * abs(u) ** 1.5)
            # lean < 0 は前（-Y）。外向き d の比率 out_mix を小さくすると
            # 房どうしが平行に近くなって重なり、1 つの束に見える。大きいと
            # 放射状に開いて、鉄兜に棘を植えたように見える。
            direction = M.normalize(up + d * out_mix
                                    + np.array([0.0, lean * (1.0 + 0.30 * w2),
                                                0.0]))
            e = s + direction * li
            c1 = s + d * li * 0.34 + up * li * 0.10
            c2 = s + direction * li * 0.70
            # frames() の法線は矢状面（前後）に来るので flat は前後方向を
            # 潰す。断面を前後に薄く・左右に広い刃にすると、正面から見て
            # 隣の房と繋がった一群の山になり、円錐の棘が生えた鉄兜に
            # 見えなくなる。power を上げて角を立てるのも同じ狙い。
            strand(mb, part, (s, c1, c2, e), r0, r0 * 0.04, n=8, flat=flat,
                   power=2.8, seg=8, taper=1.25)


def _tail(mb, p, head, origin, direction, length: float, *, width: float,
          seg_part: str, droop: float = 0.55):
    hw = p["head_w"]
    d = M.normalize(direction)
    down = np.array([0.0, 0.0, -1.0])
    s = np.asarray(origin, dtype=float)
    c1 = s + d * length * 0.34
    c2 = s + d * length * 0.62 + down * length * droop * 0.6
    e = s + d * length * 0.72 + down * length * droop
    strand(mb, seg_part, (s, c1, c2, e), hw * width, hw * width * 0.12,
           n=10, flat=0.62, seg=15, taper=2.1)


# --------------------------------------------------------------------------
# アクセサリ
# --------------------------------------------------------------------------


def _hair_top(mb, head, dirs):
    """頭の中心から dirs の各向きに見た、作り終えた髪の一番外側の面までの距離。"""
    names = [n for n in mb.parts if n.startswith("hair") and n != "hair_acc"]
    return _far_hit(_part_tris(mb, names), head.center,
                    np.asarray(dirs, dtype=float))


def _hairpin(mb, p, head):
    """左の前髪に留めた青いヘアピン 2 本。

    ピンは髪の一番外側の面に寝かせた浅いアーチの 1 枚板で、中央の筋だけを
    髪から出し、縁と両端は髪に埋める。高さは作り終えた髪の頂点から測るので、
    髪を作った後に呼ぶ。

    面は頭の中を向ける（裏から見せる）。Unity の輪郭線は法線の向きへ押し出した
    殻の裏面なので、閉じた筒のピンだと太さ 3 mm のピンの周りに 16 mm (3.2 m)
    の殻が塊になって出る。頭の中を向いた板の殻は髪の下へ沈んで見えず、
    本体は両面描画なので色はそのまま出る。
    """
    hw = p["head_w"]
    K, A = 10, 7
    t = np.linspace(0.0, 1.0, K)
    s = np.linspace(-1.0, 1.0, A)
    w, top, bury = hw * 0.0166, hw * 0.0057, hw * 0.0060
    for k in range(2):
        az = FRONT + np.radians(28.0 + 6.0 * t + 7.0 * k)
        el = 0.86 + 0.20 * t
        d = head.surface(az, el) - head.center
        d = d / np.linalg.norm(d, axis=1, keepdims=True)
        C = head.center + d * _hair_top(mb, head, d)[:, None]
        T = np.gradient(C, axis=0)
        T = T / np.linalg.norm(T, axis=1, keepdims=True)
        L = np.cross(d, T)
        L = L / np.linalg.norm(L, axis=1, keepdims=True)
        N = np.cross(T, L)
        # 両端は細めて髪へ沈め、ピンの先が髪から丸く出てくるようにする
        sc = np.maximum(0.35, np.sin(np.pi * np.clip(t, 0.02, 0.98)) ** 0.3)
        g = np.sin(np.pi * t) ** 0.5
        rings = [C[j] + np.outer(s * w * sc[j], L[j])
                 + np.outer((top + bury) * (1.0 - s * s) * g[j] - bury, N[j])
                 for j in range(K)]
        # 列 i→i+1 が L 向き、行 j→j+1 が T 向きなので、そのままで法線は
        # L x T = -N（頭の中）を向く
        with mb.part("hair_acc"):
            mb.add_grid(rings, "metal", smooth=True, close_u=False)


def _bow(mb, p, head, mat: str = "ribbon_red", scale: float | None = None):
    """頭頂のリボン。`bow_scale` で原作の大きさに合わせる。

    公式 tus_chara02.jpg の実測は 65x32px = 頭の輪郭幅の 0.66 x 見えている
    頭の高さの 0.39（縦横比 2.03）。羽の端は結び目から左右へ 0.33*head_w*bs、
    羽そのものの半径が 0.185*head_w*bs なので、全幅はほぼ head_w*bs になる。
    頭の輪郭は head_w の 1.21 倍まで髪で広がるので bs = 0.66*1.21 = 0.80 前後。
    結び目の el は 0.46 だと額寄りに落ちて「頭に刺した羽根」に見えたので、
    公式どおり頭頂へ上げる。
    """
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    bs = float(p.get("bow_scale", 1.0) if scale is None else scale)
    az = FRONT + math.radians(10.0)
    knot = scalp_pt(head, az, 0.26, hw * 0.11)[0]
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(knot), (hw * 0.100 * bs, hw * 0.088 * bs,
                                    hh * 0.082 * bs), mat, nu=12, nv=8)
    up = np.array([0.0, 0.0, 1.0])
    # 結び目が頭頂に近いと out がほぼ真上になり、cross が退化して羽が
    # ウサギの耳のように立ってしまう。左右は素直にワールド X 軸で取る。
    side = np.array([1.0, 0.0, 0.0])
    fwd = np.array([0.0, -1.0, 0.0])
    # 羽は扁平な筒で作る。球だと正面から赤い団子 2 つにしか見えない。
    for sgn in (-1, 1):
        d = M.normalize(side * sgn + fwd * 0.16)
        c0 = knot + d * hw * 0.04 * bs
        c1 = knot + d * hw * 0.16 * bs + up * hh * 0.036 * bs
        c2 = knot + d * hw * 0.28 * bs + up * hh * 0.020 * bs
        c3 = knot + d * hw * 0.33 * bs - up * hh * 0.026 * bs
        # flat=0.78 だと羽が薄すぎて縦横比 3.5 の「潰れた帯」になる。
        # 公式の 2.03 に合わせて厚みを残す。
        strand(mb, "hair_acc", (c0, c1, c2, c3),
               hw * 0.055 * bs, hw * 0.185 * bs, flat=1.00, power=2.2,
               taper=1.0, mat=mat)
    # 垂れ
    for sgn in (-1, 1):
        st = knot + side * sgn * hw * 0.05 * bs
        # 垂れは公式では短く、リボンの下にわずかに覗くだけ
        e = st + np.array([side[0] * sgn * hw * 0.10 * bs, hd * 0.04,
                           -hh * 0.26 * bs])
        strand(mb, "hair_acc", (st, st + np.array([0.0, 0.0, -hh * 0.16 * bs]),
                                e + np.array([0.0, 0.0, hh * 0.14 * bs]), e),
               hw * 0.055 * bs, hw * 0.045 * bs, flat=0.30, power=3.0, mat=mat)


def _tail_bow(mb, p, base, sgn: int, mat: str = "ribbon_red") -> None:
    """ツインテールの結び目リボン。"""
    hw, hh = p["head_w"], p["head_h"]
    base = np.asarray(base, dtype=float)
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(base), (hw * 0.070, hw * 0.062, hh * 0.058), mat,
                      nu=12, nv=8)
    side = np.array([0.0, 1.0, 0.0])
    up = np.array([0.0, 0.0, 1.0])
    out = np.array([float(sgn), 0.0, 0.0])
    for s2 in (-1, 1):
        d = M.normalize(side * s2 + out * 0.35 + up * 0.25)
        c0 = base + d * hw * 0.04
        c1 = base + d * hw * 0.18 + up * hh * 0.050
        c2 = base + d * hw * 0.32 + up * hh * 0.030
        c3 = base + d * hw * 0.36 - up * hh * 0.038
        strand(mb, "hair_acc", (c0, c1, c2, c3), hw * 0.030, hw * 0.098,
               flat=0.30, power=2.8, taper=1.1, mat=mat)


def _bandana(mb, p, head, mat: str = "cloth_bandana") -> None:
    """頭に巻くバンダナ。帯＋横結び。"""
    hw, hh = p["head_w"], p["head_h"]
    nu = 28
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    els = (0.40, 0.72)
    t = hw * 0.030
    outer = [_outward(head, head.surface(az, np.full(nu, e)),
                      np.full(nu, hw * 0.115)) for e in els]
    inner = [_outward(head, head.surface(az, np.full(nu, e)),
                      np.full(nu, hw * 0.115 - t)) for e in els]
    rings = [outer[0], outer[1], inner[1], inner[0], outer[0]]
    with mb.part("hair_acc"):
        mb.add_grid(rings, mat, smooth=False, close_u=True)
    # 横の結び目と垂れ
    knot = scalp_pt(head, FRONT + math.radians(104.0), 0.60, hw * 0.16)[0]
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(knot), (hw * 0.062, hw * 0.052, hh * 0.048), mat,
                      nu=10, nv=6)
    for k, dz in ((0, -0.06), (1, 0.02)):
        d = M.normalize(np.array([0.30, -0.25 + 0.5 * k, -0.90 + dz]))
        strand(mb, "hair_acc",
               (knot, knot + d * hh * 0.10, knot + d * hh * 0.22,
                knot + d * hh * 0.30), hw * 0.042, hw * 0.012,
               flat=0.34, power=3.0, taper=1.8, mat=mat)


def _bow_shape(pts: np.ndarray, d: np.ndarray) -> np.ndarray:
    """楕円体を d 方向へ寄せてリボンの羽らしく絞る。"""
    t = np.clip(0.5 + 0.5 * (pts @ M.normalize(d)) / (np.abs(pts).max() + 1e-9), 0, 1)
    k = (0.35 + 0.65 * t)[:, None]
    return pts * np.array([1.0, 1.0, 1.0]) * k + M.normalize(d) * 0.0


def _glasses(mb, p, head, fs, uv_box, mat: str = "glasses"):
    fl = p["face_layout"]
    x0, x1, z0, z1 = uv_box
    hw = p["head_w"]
    ex = (x1 - x0) * fl["eye_dx"]
    ez = z0 + (z1 - z0) * fl["eye_y"]
    rx = (x1 - x0) * fl["eye_rx"] * 1.42
    rz = (z1 - z0) * fl["eye_ry"] * 1.28
    tube = hw * 0.016
    for sgn in (-1, 1):
        cx = sgn * ex
        yy = fs.y_at(cx, ez) - hw * 0.055
        n = 22
        a = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
        e = 2.0 / 3.0
        px = cx + np.sign(np.cos(a)) * np.abs(np.cos(a)) ** e * rx
        pz = ez + np.sign(np.sin(a)) * np.abs(np.sin(a)) ** e * rz
        loop = np.stack([px, np.full(n, yy), pz], axis=1)
        path = np.vstack([loop, loop[:1]])
        with mb.part("glasses"):
            mb.add_tube(path, [tube] * len(path), mat, n=6, cap_start=False,
                        cap_end=False)
        # つる
        ear = np.array([sgn * hw * 0.50, p["head_d"] * 0.10, ez + rz * 0.30])
        temple = np.array([cx + sgn * rx, yy + hw * 0.02, ez + rz * 0.30])
        with mb.part("glasses"):
            mb.add_tube(np.array([temple, (temple + ear) * 0.5, ear]),
                        [tube * 0.9] * 3, mat, n=6)
    # ブリッジ
    ybr = fs.y_at(0.0, ez) - hw * 0.050
    with mb.part("glasses"):
        mb.add_tube(np.array([[-ex + rx * 0.9, ybr, ez + rz * 0.12],
                              [0.0, ybr - hw * 0.010, ez + rz * 0.16],
                              [ex - rx * 0.9, ybr, ez + rz * 0.12]]),
                    [tube * 0.85] * 3, mat, n=6)


# --------------------------------------------------------------------------


def build_hair(mb: M.MeshBuilder, p: dict, head, a, fs, uv_box) -> None:
    style = p["hair"]
    z = p["z"]
    hh = p["head_h"]
    hw = p["head_w"]
    chin = z["chin"]

    if style == "bob":
        build_helmet(mb, p, head, front_el=0.63, back_el=1.72, thickness=0.165,
                     z_end_side=chin - hh * 0.30, z_end_back=chin - hh * 0.22,
                     puff=1.10, ridges=14, ridge_amp=0.30, jag=0.030,
                     hang_lo=0.44, inward=0.76, nu=144, nv=18,
                     bangs=dict(reach=math.radians(78.75), teeth=7,
                                notch=0.080, droop=0.085, end_v=0.614,
                                tip=0.020, fall=3.0, valley=0.5, fade=0.25,
                                onset=0.5, groove=0.40, g_from=1.3),
                     carry=dict(base=0.36, gain=1.25, lift=1.12))

    elif style == "long_blunt":
        # 公式 tus_chara02.jpg の頭頂はつるりとした 1 枚のドームで、畝も瘤も
        # 無い。ridge_amp=0.34 / ridges=16 だと正面から団子が 6 個並んで
        # 「積み重ねた饅頭」に見えたので、畝を細かく・浅くして艶だけ残す。
        # 毛先の jag も、公式のストレートロングに合わせてほぼ平らにする。
        build_helmet(mb, p, head, front_el=0.70, back_el=1.70, thickness=0.104,
                     z_end_side=z["bust"], z_end_back=z["waist"] - 0.02,
                     puff=1.25, ridges=34, ridge_amp=0.026, jag=0.011,
                     inward=0.74, depth=0.12)
        _bangs(mb, p, head, span=math.radians(80.0), count=15, el=0.62,
               end_v=0.655, width=0.116, blunt=0.88)

    elif style == "twintail":
        build_helmet(mb, p, head, front_el=0.64, back_el=1.62, thickness=0.082,
                     z_end_side=chin + hh * 0.10, z_end_back=chin + hh * 0.22,
                     puff=0.9, ridges=14, ridge_amp=0.34, jag=0.026,
                     hang_lo=0.30, inward=0.80)
        _bangs(mb, p, head, span=math.radians(76.0), count=11, el=0.58,
               end_v=0.650, width=0.110)
        for sgn in (-1, 1):
            base = scalp_pt(head, FRONT + sgn * math.radians(100.0), 0.86,
                            hw * 0.12)[0]
            if p.get("tail_ribbon"):
                _tail_bow(mb, p, base, sgn)
            else:
                with mb.part("hair_acc"):
                    mb.add_sphere(tuple(base),
                                  (hw * 0.075, hw * 0.075, hh * 0.060),
                                  "ribbon_red", nu=12, nv=8)
            for k, spread in enumerate((-0.30, 0.0, 0.30)):
                d = np.array([sgn * 0.34, spread * 0.40, -0.58])
                _tail(mb, p, head, base, d, hh * 1.52,
                      width=0.168 - 0.028 * abs(k - 1), seg_part="hair_tail",
                      droop=1.05)

    elif style == "ponytail":
        build_helmet(mb, p, head, front_el=0.70, back_el=1.60, thickness=0.080,
                     z_end_side=chin + hh * 0.16, z_end_back=chin + hh * 0.30,
                     puff=0.8, ridges=14, ridge_amp=0.32, jag=0.024,
                     hang_lo=0.32, inward=0.82)
        _bangs(mb, p, head, span=math.radians(72.0), count=10, el=0.62,
               end_v=0.668, width=0.105, sweep=0.20)
        base = scalp_pt(head, FRONT + math.pi, 0.52, hw * 0.10)[0]
        with mb.part("hair_acc"):
            mb.add_sphere(tuple(base), (hw * 0.070, hw * 0.070, hh * 0.058),
                          "cloth_ribbon_green", nu=12, nv=8)
        # 細い房を離して並べると箒になる。太めに重ねて 1 本の尾に見せる。
        for spread in (-0.24, -0.08, 0.08, 0.24):
            _tail(mb, p, head, base, np.array([spread, 1.0, -0.08]), hh * 1.95,
                  width=0.172, seg_part="hair_tail", droop=1.15)

    elif style == "short":
        build_helmet(mb, p, head, front_el=0.68, back_el=1.62, thickness=0.086,
                     z_end_side=chin + hh * 0.06, z_end_back=chin + hh * 0.20,
                     puff=0.9, ridges=14, ridge_amp=0.34, jag=0.028,
                     hang_lo=0.28, inward=0.80)
        _bangs(mb, p, head, span=math.radians(78.0), count=11, el=0.62,
               end_v=0.662, width=0.112)

    elif style == "crew":
        # 坊っちゃん: 逆立てた短い黒髪。額の上にトゲ房を散らし、こめかみから
        # 耳の前へもみあげを 1 本ずつ降ろす。後ろ髪・横髪は作らず耳を出す。
        # 生え際を上げる。公式は 顎→生え際 が頭シルエットの 0.80 で額が広い
        # （front_el=1.02 だと 0.70 しかなく、水泳帽を目深に被って見える）。
        # flat_front で額の上を水平な帯にし、jag で髪が額へ尖って食い込む。
        # 殻の厚みは 0.140hw だと頭の輪郭から 0.028 はみ出して兜の縁に見えた。
        # 0.112hw ではみ出しを 0.020 に減らすと頭に貼り付き、jag の刻みも
        # 縁が薄くなった分はっきり出る（地肌の透けは正面・側面・背面で無し）。
        # back_el=1.92（頭頂から 110°）だと殻が後頭部の赤道までで終わり、
        # 背面プレビューで襟足からうなじにかけて素肌が V 字に露出していた
        # （botchan_back.png で幅 450px・高さ 140px の禿げ）。公式は後ろ髪が
        # 顎の高さまで下りているので 2.40（138°）まで回す。
        # front_el も 0.66 では生え際が頭高の 0.92（公式 0.875）で高すぎた。
        front_el, back_el, flat_front = 0.78, 2.40, 0.25
        build_scalp(mb, p, head, front_el=front_el, back_el=back_el,
                    thickness=0.112, nu=64, flat_front=flat_front,
                    jag=0.075, teeth=9.0)

        def rim(az):
            return _el_max(np.array([az]), front_el, back_el,
                           flat_front=flat_front)[0]
        # トゲ房は額の上だけ。公式では頭シルエットの上 5〜8% しか出ておらず、
        # 位置も頭頂ではなく前寄り（頭幅の 0.17〜0.41 の帯）に固まっている。
        # 行は生え際からの比（1.0=生え際, 0.0=頭頂）で、0.86/0.58/0.30 は
        # 前頭部の下・中・上。細い房を 14 本重ねて 1 つの毛束に見せる。
        _crest_tufts(mb, p, head,
                     ((0.86, 6, 0.82, 1.00),   # 生え際寄り。前へ強く寝る
                      (0.58, 5, 1.00, 0.92),   # 中段。ここが一番高い
                      (0.30, 3, 0.74, 0.82)),  # 頭頂寄り。短く立てて奥行きを出す
                     rim, az_span=math.radians(72.0), length=0.150,
                     lean=-0.46, base=0.056, flat=0.54, out_mix=0.26,
                     # 殻を 0.112hw に薄くしたので根元も内側へ。既定の
                     # 0.11hw だと殻の外に出て切り口が輪になって見える。
                     root_off=0.086)
        # もみあげ。正面から 98° だと耳より後ろで、房の先が耳の上端
        # （body.py の耳球 = z chin+0.355..0.565*head_h）へ刺さっていた。
        # 耳の前面は同じ耳球から y=-0.030*head_d なので、それより前（-Y）
        # を通る方位角まで回し、耳の高さの中ほどまで降ろす。
        for sgn in (-1, 1):
            # 根元は地髪の殻の中。76° の生え際は仰角 1.08 なので、それより
            # 上（仰角が小さい）から始めれば房の切り口が外に出ない。
            s = scalp_pt(head, FRONT + sgn * math.radians(76.0), 1.00,
                         hw * 0.03)[0]
            # 先端を el=1.72（頭高の 0.435 = 目より下）まで落とすと、頬に
            # 黒いナイフの刃が貼り付いて見えた。公式のもみあげは眉の高さ
            # （頭高 0.62 前後）で終わる楔なので el=1.38 で止める。
            e = scalp_pt(head, FRONT + sgn * math.radians(70.0), 1.38,
                         hw * 0.022)[0]
            # 制御点も頭の面の上に取る。直線で結ぶと弧を描く頬にめり込む。
            c1 = scalp_pt(head, FRONT + sgn * math.radians(74.0), 1.13,
                          hw * 0.03)[0]
            c2 = scalp_pt(head, FRONT + sgn * math.radians(72.0), 1.26,
                          hw * 0.026)[0]
            # flat を小さくして前後に薄い板にする。こめかみに貼り付いた
            # 帯に見せたいので、丸い房のままだと角から生えた棒に見える。
            # 細く尖らせると刃になるので、先端も 0.022hw の幅を残す。
            strand(mb, "hair_side", (s, c1, c2, e), hw * 0.062, hw * 0.026,
                   n=8, flat=0.62, power=2.4, seg=8, taper=1.1)

    elif style in ("slickback", "short_old"):
        # 頭頂を跨ぐ長い房を放射状に並べると房の間が開いてトゲトゲの
        # カツラになる。厚めの地髪＋短い前髪・横髪・襟足で構成する。
        front_el = 0.58 if style == "slickback" else 0.52
        # 耳の上の縁は 1.80 のまま、後頭部の縁を襟足（顎の 0.13hh 上）まで
        # 下ろす。1.80 で止めると縁の下に後頭部の肌が出て、後ろから見ると顔に見える。
        build_scalp(mb, p, head, front_el=front_el, back_el=1.80,
                    thickness=0.108, nape=0.55)
        _bangs(mb, p, head, span=math.radians(88.0), count=11,
               el=front_el - 0.05,
               end_v=0.668 if style == "slickback" else 0.678,
               width=0.098, sweep=0.22 if style == "slickback" else 0.0,
               jag=2.8 if style == "slickback" else 1.8)
        _side(mb, p, head, count=2, az_lo=math.radians(70.0),
              az_hi=math.radians(96.0), el=1.20,
              z_end=chin + hh * 0.70, width=0.058, curl=0.0)
        _back(mb, p, head, count=9, el=1.72, z_end=chin + hh * 0.10,
              width=0.102, puff=0.85)

    acc = p.get("hair_accessory")
    if acc == "pin_blue":
        _hairpin(mb, p, head)
    elif acc == "bow_red":
        _bow(mb, p, head)
    elif acc == "bandana":
        _bandana(mb, p, head)
    elif acc in ("glasses_red", "glasses_gray"):
        _glasses(mb, p, head, fs, uv_box)

    _ = a
