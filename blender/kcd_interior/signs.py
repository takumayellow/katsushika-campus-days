"""屋内の看板（案内サイン・吊り看板・室名札・店の看板）を文字つきで置く。

建物のプランは文言を渡すだけでよい:

    from . import signs

    signs.wall(c, mb, x, y, z, ang=math.pi, ja="貸出・返却", en="Circulation Desk",
               w=3.2, h=0.62)
    signs.hanging(c, mb, x, y, z_ceil, ja="インフォメーション", en="Information",
                  w=3.6, h=0.46, drop=0.35)

- 板と枠は shell.wall_sign / shell.hanging_sign が作り、文字は kcd_lib.signtext がメッシュにして
  板の面に貼る（板と同じ MeshBuilder に入る）。屋内の看板の文字に Unity の TMP は使わない。
- ja と en は両方とも必須。どちらかが空なら ValueError で止まる（白紙の看板を作らない）。
  日本語の行を上、英語の行を下に小さく並べる。改行は "\\n"。
- 文字の材質は板の見た目の色（発光する板は発光ぶんを足した色）に対して、plastic_white と
  plastic_black のうち輝度の対比が大きいほう。text= で別の材質を渡せる。
- 字の大きさは板に収まる最大。字の em が signtext.MIN_SIZE（0.03 m）より小さくなる板と文言の組は
  ValueError。板を大きくするか文言を短くする。
- 吊り看板は既定で両面に文字を貼る（both=False で表だけ）。
- 照明の目印の Empty sign_<id>_<n> を 1 枚に 1 つ置く（壁の看板は壁の面の上の板の中心、吊り看板は板の中心）。
  同じ札を近くに並べるとき（エレベーターの号機札・書架の分類札など）は anchor=False で省く
  （Unity は sign_ Empty を天井灯の目印にする）。
- 置いた看板は c.signs に記録する。build_interiors.py / build_dorm.py が <id>.json の "signs" に書き出し、
  文字の三角形の数を "text_triangles" と書き出しのログ・_summary.json に出す。文字の三角形は棟の
  三角数（上限 INT_BUDGET_ONE）に含まれる。
- 看板の Empty は必ずこのモジュールで置く。c.sign() を直に呼ぶと、文字の無い Empty として
  check() がビルドを止める。
"""

import math

from kcd_lib import mats as lib_mats
from kcd_lib import signtext

from . import imats, shell
from .kit import T

TEXT_LIGHT = "plastic_white"
TEXT_DARK = "plastic_black"


# ---- 文字の色 ----
def shade(name):
    """材質の見た目の色（線形 RGB）。発光する材質は発光の色 x 強さを足し、1 で切る。

    Unity の InteriorPalette.cs は imats を写しているので imats を先に引く。
    """
    for table in (imats.PALETTE, imats.TRANSPARENT, lib_mats.PALETTE):
        if name in table:
            rgb = table[name][0]
            break
    else:
        raise ValueError("看板の板の材質 %r がパレットに無い（文字の材質を text= で渡す）" % (name,))
    emit = imats.EMISSIVE.get(name)
    if emit is not None:
        col, strength = emit
        rgb = tuple(min(1.0, a + b * strength) for a, b in zip(rgb, col))
    return tuple(rgb)


def luminance(rgb):
    """線形 RGB の相対輝度。"""
    return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]


def contrast(a, b):
    """材質 a と b の輝度の対比（WCAG の比。1〜21）。"""
    la, lb = luminance(shade(a)), luminance(shade(b))
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def text_mat(plate):
    """板の材質 plate に載せる文字の材質（TEXT_LIGHT か TEXT_DARK）。"""
    if contrast(TEXT_LIGHT, plate) >= contrast(TEXT_DARK, plate):
        return TEXT_LIGHT
    return TEXT_DARK


# ---- 記録 ----
def _record(c, empty, kind, ja, en, tris, pos, ang, w, h, faces, plate, text, align, drop=None):
    x, y, z = pos
    rec = {
        "empty": empty,
        "kind": kind,
        "ja": ja,
        "en": en,
        "tris": tris,
        # Empty の位置（壁の看板は壁の面の上の板の中心、吊り看板は板の中心）。"empties" と同じ並び（y が上）
        "pos": {"x": round(x, 3), "y": round(z, 3), "z": round(y, 3)},
        # 表の文字が向く方向（同じ並び）
        "facing": {"x": round(-math.sin(ang), 4), "y": 0.0, "z": round(math.cos(ang), 4)},
        "w": round(w, 3),
        "h": round(h, 3),
        "faces": faces,
        "plate": plate,
        "text": text,
        "align": align,
    }
    if drop is not None:
        rec["drop"] = round(drop, 3)   # 吊り看板: 板の上端から吊り元（天井の面）まで
    c.signs.append(rec)
    return rec


# ---- 看板 ----
def wall(c, mb, x, y, z, ang=0.0, *, ja, en, w=1.6, h=0.5, plate="sign_plate_blue",
         frame="metal_white", out=0.06, text=None, align="center", anchor=True):
    """壁付けの看板。(x, y, z) は壁の面の上の板の中心、ang は文字の向き（kit.T と同じ。板は正面へ out 出る）。

    戻り値は置いた Empty の名前（anchor=False なら None）。
    """
    text = text or text_mat(plate)
    shell.wall_sign(mb, x, y, z, ang=ang, w=w, h=h, mat=plate, frame=frame, out=out)
    tris = signtext.place(mb, T(x, y, z, ang), w, h, ja, en, face=out, side=1, mat=text,
                          align=align)
    empty = c.sign(x, y, z) if anchor else None
    _record(c, empty, "wall", ja, en, tris, (x, y, z), ang, w, h, 1, plate, text, align)
    return empty


def hanging(c, mb, x, y, z, ang=0.0, *, ja, en, w=1.8, h=0.42, drop=0.5,
            plate="sign_plate_blue", text=None, both=True, align="center", anchor=True):
    """天井から吊る看板。(x, y, z) は吊り元（天井の面）、板は drop 下がった所から高さ h。

    表（ang の正面）と、both=True なら裏にも同じ文言を貼る。戻り値は Empty の名前（anchor=False なら None）。
    """
    text = text or text_mat(plate)
    shell.hanging_sign(mb, x, y, z, ang=ang, w=w, h=h, mat=plate, drop=drop)
    frame = T(x, y, z, ang)
    dz = -drop - h * 0.5
    sides = (1, -1) if both else (1,)
    tris = 0
    for side in sides:
        tris += signtext.place(mb, frame, w, h, ja, en, face=shell.HANG_HALF, side=side, dz=dz,
                               mat=text, align=align)
    center = frame.p(0.0, 0.0, dz)
    empty = c.sign(*center) if anchor else None
    _record(c, empty, "hanging", ja, en, tris, center, ang, w, h, len(sides), plate, text, align,
            drop=drop)
    return empty


# ---- 検査 ----
def unlabeled(c):
    """文字を貼った看板の記録が無い sign_<id>_<n> の Empty。"""
    labeled = {r["empty"] for r in c.signs if r["empty"]}
    prefix = "sign_%s_" % c.spec.id
    return [n for n, _ in c.empties if n.startswith(prefix) and n not in labeled]


def check(c):
    """文字の無い看板の Empty があれば RuntimeError。plan.build() のあとに呼ぶ。"""
    bad = unlabeled(c)
    if bad:
        raise RuntimeError("文字の無い看板の Empty: %s（signs.wall / signs.hanging で置く）"
                           % ", ".join(bad))
