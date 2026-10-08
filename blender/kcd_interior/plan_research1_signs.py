"""第1研究棟の看板（文字入り）と誘導灯。置く順が Empty の sign_research1_1〜25 の番号になる。

文言は docs/ref/interiors/research1/spec.md §5。
"""

import math

from . import shell, signs
from . import plan_research1_dims as D
from kcd_lib import signtext
from .kit import T
from .plan_research1_shell import HALF_PART

PI = math.pi
HALF_PI = math.pi * 0.5
NAVY = "research1_navy_panel"
CHAR = "research1_band_charcoal"
PAPER = "research1_whiteboard"
SC0F = D.Y_SC0 + HALF_PART
SC1F = D.Y_SC1 - HALF_PART
HALL_N = D.Y_NC0 - HALF_PART       # EV ホールの北の壁の南面
NC1F = D.Y_NC1 - HALF_PART

# フロアガイドの行: (階, 日本語, 英語)。階が空の行は前の行の続き。題は GUIDE_TITLE
GUIDE_TITLE = ("第1研究棟 フロアガイド", "Research Building 1 Floor Guide")
GUIDE_ROWS = (
    ("11F", "生命システム工学科", "Biological Science and Technology"),
    ("10F", "電子システム工学科", "Electronic Systems Engineering"),
    ("9F", "マテリアル創成工学科", "Materials Science and Technology"),
    ("8F", "物理工学科", "Applied Physics"),
    ("7F", "建築学科", "Architecture"),
    ("6F", "工業化学科", "Industrial Chemistry"),
    ("5F", "電気工学科", "Electrical Engineering"),
    ("4F", "機械工学科", "Mechanical Engineering"),
    ("3F", "学生実験室・高電圧実験室", "Student Labs, High Voltage Lab"),
    ("2F", "機能デザイン工学科・学生実験室", "Medical and Robotic Engineering Design, Student Labs"),
    ("1F", "入試センター・庶務課・学科事務センター",
     "Admissions Center, General Affairs, Department Office Center"),
    ("", "インキュベーションルーム・産学試作開発室", "Incubation Rooms, Prototyping Lab"),
)
GUIDE_JA = "\n".join([GUIDE_TITLE[0]]
                     + [(f + " " if f else "　 ") + ja for f, ja, _e in GUIDE_ROWS])
GUIDE_EN = "\n".join([GUIDE_TITLE[1]] + [(f + " " if f else "") + e for f, _j, e in GUIDE_ROWS])
FJR_JA = ("民間航空機用FJR710ジェットエンジン\n1998年3月までJAXAの短距離離着陸実験機「飛鳥」に\n"
          "実際に搭載されていたジェットエンジンです。")
FJR_EN = ("FJR710 Turbofan Engine for Civil Aircraft.\n"
          "Flown on JAXA's STOL research aircraft \"Asuka\" until March 1998.")
RENO_JA = "改装工事中\n生協は講義棟1階へ移転しました"
RENO_EN = "Under renovation.\nThe Co-op has moved to the Lecture Hall Building 1F."

# (x, y, z, ang, ja, en, w, h, plate)。壁の面に付ける看板
WALL_SIGNS = (
    (-5.1, D.Y_NC1, 2.9, PI, "入試センター", "Admissions Center", 4.6, 0.46, CHAR),
    (-0.1, D.Y_NC1, 2.9, PI, "庶務課", "General Affairs Division", 4.6, 0.46, CHAR),
    (5.0, D.Y_NC1, 2.9, PI, "学科事務センター", "Department Office Center", 4.6, 0.46, CHAR),
    (D.W_SHAFTS[0][2], 28.6, 2.4, -HALF_PI, "1W", "1W", 1.2, 0.9, NAVY),
    (14.4, HALL_N, 2.4, PI, "1E", "1E", 1.2, 0.9, NAVY),
)
INCUB_SIGNS = tuple((x + 1.0, SC0F, 2.4, 0.0, "インキュベーションルーム(%d)" % (k + 1),
                     "Incubation Room %d" % (k + 1), 1.3, 0.3, NAVY)
                    for k, x in enumerate((58.0, 61.4, 64.8)))
ROOM_SIGNS = (
    (50.45, SC0F, 1.9, 0.0, "産学試作開発室", "Industry-Academia Prototyping Lab", 1.3, 0.3, NAVY),
)
LATER_SIGNS = (
    (D.PROF[1] + HALF_PART, D.ECO_Y, 1.5, -HALF_PI, "エコステーション", "Eco Station", 1.4,
     0.32, NAVY),
    (-27.55, SC1F, 1.9, PI, "機械工学科 実験室", "Mechanical Engineering Laboratory", 1.3, 0.3,
     NAVY),
    (-31.35, SC0F, 1.9, 0.0, "機械工学科 研究室", "Mechanical Engineering Lab Office", 1.3, 0.3,
     NAVY),
    (-51.2, SC0F, 1.4, 0.0, RENO_JA, RENO_EN, 1.0, 0.7, PAPER),
    (D.RECEPTION[0] - 0.30, D.RECEPTION[1], 0.45, HALF_PI, "受付", "Reception", 0.5, 0.2, NAVY),
    (-4.9, 16.84, 2.5, 0.0, "掲示板", "Notice Board", 1.2, 0.26, NAVY),
)
WC = ("トイレ ／ 多目的トイレ", "Restrooms / Accessible Restroom")
UPPER_SIGNS = (
    (D.W_SHAFTS[0][2], 28.6, 6.9, -HALF_PI, "2W", "2W", 1.2, 0.9, NAVY),
    (14.4, HALL_N, 6.9, PI, "2E", "2E", 1.2, 0.9, NAVY),
    (-13.9, D.W_HALL[1] + HALF_PART, 7.0, 0.0, "学生実験室", "Student Laboratory", 1.2, 0.3, NAVY),
)
LAB = ("実験室", "Laboratory")
# 誘導灯 (x, y, z, ang)。z は緑の箱の上端
EXITS = (
    (0.0, D.VEST_Y1 + 0.07, 3.0, 0.0),
    (D.X_W_IN + 0.5, 18.3, D.C_COR - 0.26, HALF_PI),
    (D.X_E_IN - 0.5, 18.3, D.C_COR - 0.26, HALF_PI),
    (D.X_NW_IN + 0.5, 31.9, D.C_COR - 0.26, HALF_PI),
    (D.X_NE_IN - 0.5, 31.9, D.C_COR - 0.26, HALF_PI),
    (-11.0, 19.6, D.Z2 - 0.49, 0.0),
    (9.05, 24.0, D.Z2 - 0.49, 0.0),
    (-11.0, 29.9, D.C2 - 0.26, 0.0),
)


def _place(c, rows):
    for x, y, z, ang, ja, en, w, h, plate in rows:
        signs.wall(c, c.wall, x, y, z, ang, ja=ja, en=en, w=w, h=h, plate=plate)


# フロアガイドの文字の輪郭の許容誤差（em）。「物」の輪郭には 0.0002 em の辺があり、既定（〜0.04 em）では
# 辺が溶接の下限より短くなって貼れない。0.05 em なら最短の辺が 0.018 em になる
GUIDE_TOL = 0.05


GUIDE_TITLE_GAP = 0.5        # 題の行の下に足す空き（字の大きさに対する比）
GUIDE_COL_GAP = (0.6, 1.2)   # 階の列と日本語の列、日本語の列と英語の列の間（同上）


def _em_width(fnt, text):
    return sum(fnt.advance(fnt.glyph_id(ch)) for ch in text) / fnt.units_per_em


def _guide_glyphs(w, h):
    """フロアガイドの字の位置 [(字, u, 基線, 大きさ)]。u は読む人から見て右、基線は板の中心から上（m）。

    階・日本語・英語の 3 列に分け、行ごとに基線をそろえる（題の日本語は階の列から書く）。字の大きさは
    板の内側（signtext.margin の余白の内）に全部の行と列が入る最大にし、英語は日本語の EN_SCALE 倍。
    3 列のまとまりは板の左右の中央に置く。
    """
    fnt = signtext.font()
    m = signtext.margin(w, h)
    iw, ih = w - 2.0 * m, h - 2.0 * m
    rows = (("",) + GUIDE_TITLE,) + GUIDE_ROWS
    en = signtext.EN_SCALE
    fw = max(_em_width(fnt, f) for f, _j, _e in rows) + GUIDE_COL_GAP[0]
    jw = max(_em_width(fnt, j) for _f, j, _e in rows[1:]) + GUIDE_COL_GAP[1]
    ew = max(_em_width(fnt, e) for _f, _j, e in rows) * en
    tall = len(rows) + (len(rows) - 1) * signtext.LEADING + GUIDE_TITLE_GAP
    size = min(ih / tall, iw / (fw + jw + ew))
    left = -(fw + jw + ew) * size * 0.5
    desc = -fnt.descender / fnt.units_per_em
    top = tall * size * 0.5
    out = []
    for k, (f, ja, e) in enumerate(rows):
        base = top - size + desc * size
        cols = ((left, f, size), (left if k == 0 else left + fw * size, ja, size),
                (left + (fw + jw) * size, e, size * en))
        for u, line, sz in cols:
            for ch in line:
                out.append((ch, u, base, sz))
                u += fnt.advance(fnt.glyph_id(ch)) / fnt.units_per_em * sz
        top -= (1.0 + signtext.LEADING + (GUIDE_TITLE_GAP if k == 0 else 0.0)) * size
    return out


def _guide(c):
    """フロアガイド（案内板の壁の南面。紺の板に白い文字で、階・日本語・英語の 3 列）。

    板は signs.wall と同じ置き方。文字は 3 列に並べるので signtext.place を使わずに字を置き、
    許容誤差を GUIDE_TOL に固定する。
    """
    x, y, z, ang, w, h = 0.0, D.GUIDE_WALL[1], 1.6, PI, 4.6, 2.0
    out = 0.06
    text = signs.text_mat(NAVY)
    shell.wall_sign(c.wall, x, y, z, ang=ang, w=w, h=h, mat=NAVY, frame="metal_white", out=out)
    frame = T(x, y, z, ang)
    lift = out + signtext.LIFT
    tris = 0
    for ch, u, base, size in _guide_glyphs(w, h):
        for tri in signtext.fitted_glyph(ch, size, GUIDE_TOL)[0]:
            c.wall.add_face([frame.p(-(u + gx * size), lift, base + gy * size) for gx, gy in tri],
                            text)
            tris += 1
    empty = c.sign(x, y, z)
    signs._record(c, empty, "wall", GUIDE_JA, GUIDE_EN, tris, (x, y, z), ang, w, h, 1, NAVY, text,
                  "left")


def _fjr(c):
    """FJR710 の説明板（黒い自立板の正面）。"""
    x, y = D.FJR_PLATE
    signs.wall(c, c.wall, x, y, 0.85, PI * 0.75, ja=FJR_JA, en=FJR_EN, w=1.6, h=0.9,
               plate=NAVY)


def build(c):
    _guide(c)
    _place(c, WALL_SIGNS)
    _fjr(c)
    _place(c, INCUB_SIGNS + ROOM_SIGNS)
    signs.hanging(c, c.wall, -20.0, 13.0, D.C_COR, 0.0, ja="西側ラウンジ", en="West Lounge",
                  w=1.8, h=0.42, drop=0.3, plate=NAVY)
    _place(c, LATER_SIGNS)
    signs.wall(c, c.wall, -52.6, SC1F, 2.45, PI, ja=WC[0], en=WC[1], w=1.4, h=0.3, plate=NAVY)
    _place(c, UPPER_SIGNS)
    signs.wall(c, c.wall, -28.7, NC1F, 1.9, PI, ja=LAB[0], en=LAB[1], w=1.0, h=0.28, plate=NAVY)
    for x, y, z, ang in EXITS:
        shell.exit_sign(c.wall, x, y, z, ang=ang)
    # 東のトイレと東の実験室（24・25。Empty の照明で南の廊下と北の廊下の東の暗がりを埋める）
    signs.wall(c, c.wall, 27.4, SC1F, 2.45, PI, ja=WC[0], en=WC[1], w=1.4, h=0.3, plate=NAVY)
    signs.wall(c, c.wall, 36.3, NC1F, 1.9, PI, ja=LAB[0], en=LAB[1], w=1.0, h=0.28, plate=NAVY)
