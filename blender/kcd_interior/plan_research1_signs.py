"""第1研究棟の看板（文字入り）と誘導灯。置く順が Empty の sign_research1_1〜23 の番号になる（anchor=False の 2 枚は Empty を持たない）。

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

GUIDE_JA = ("第1研究棟 フロアガイド\n11F 生命システム工学科\n10F 電子システム工学科\n"
            "9F マテリアル創成工学科\n8F 物理工学科\n7F 建築学科\n6F 工業化学科\n5F 電気工学科\n"
            "4F 機械工学科\n3F 学生実験室・高電圧実験室\n2F 機能デザイン工学科・学生実験室\n"
            "1F 入試センター・庶務課・学科事務センター\n　 インキュベーションルーム・産学試作開発室")
GUIDE_EN = ("Research Building 1 Floor Guide\n"
            "11F Biological Science and Technology  10F Electronic Systems Engineering\n"
            "9F Materials Science and Technology  8F Applied Physics  7F Architecture\n"
            "6F Industrial Chemistry  5F Electrical Engineering  4F Mechanical Engineering\n"
            "3F Student Labs, High Voltage Lab  2F Medical and Robotic Engineering Design, "
            "Student Labs\n1F Admissions Center, General Affairs, Department Office Center, "
            "Incubation Rooms, Prototyping Lab")
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
    (D.PROF[1] + HALF_PART, 15.0, 1.5, -HALF_PI, "エコステーション", "Eco Station", 1.4,
     0.32, NAVY),
    (-27.55, SC1F, 1.9, PI, "機械工学科 実験室", "Mechanical Engineering Laboratory", 1.3, 0.3,
     NAVY),
    (-31.35, SC0F, 1.9, 0.0, "機械工学科 研究室", "Mechanical Engineering Lab Office", 1.3, 0.3,
     NAVY),
    (-51.2, SC0F, 1.4, 0.0, RENO_JA, RENO_EN, 1.0, 0.7, PAPER),
    (D.RECEPTION[0] - 0.35, D.RECEPTION[1], 0.75, HALF_PI, "受付", "Reception", 0.5, 0.2, NAVY),
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


def _guide(c):
    """フロアガイド（案内板の壁の南面。紺の板に白い文字を左寄せ）。signs.wall と同じ置き方で、許容誤差だけ固定する。"""
    x, y, z, ang, w, h = 0.0, D.GUIDE_WALL[1], 1.6, PI, 4.6, 2.0
    text = signs.text_mat(NAVY)
    shell.wall_sign(c.wall, x, y, z, ang=ang, w=w, h=h, mat=NAVY, frame="metal_white", out=0.06)
    tris = signtext.place(c.wall, T(x, y, z, ang), w, h, GUIDE_JA, GUIDE_EN, face=0.06, side=1,
                          mat=text, align="left", tol=GUIDE_TOL)
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
    signs.wall(c, c.wall, 27.4, SC1F, 2.45, PI, ja=WC[0], en=WC[1], w=1.4, h=0.3, plate=NAVY,
               anchor=False)
    _place(c, UPPER_SIGNS)
    signs.wall(c, c.wall, -28.7, NC1F, 1.9, PI, ja=LAB[0], en=LAB[1], w=1.0, h=0.28, plate=NAVY)
    signs.wall(c, c.wall, 36.3, NC1F, 1.9, PI, ja=LAB[0], en=LAB[1], w=1.0, h=0.28, plate=NAVY,
               anchor=False)
    for x, y, z, ang in EXITS:
        shell.exit_sign(c.wall, x, y, z, ang=ang)
