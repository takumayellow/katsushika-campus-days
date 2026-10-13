"""講義棟の固有の家具（spec §3・§4）。"""

import math

from . import furniture as F
from . import kit

SHELL = "lecture_wood_shell"
NAVY = "lecture_seat_navy"
FRAME = "metal_dark"


def shell_seat(mb, t, seat=NAVY, shell=SHELL, frame=FRAME):
    """101 の木のシェルの椅子（42 三角形）。座面と背の前の張り地は紺、背の外側は木のシェル。

    外形は furniture.hall_seat と同じ（幅 ±0.238・奥行き -0.26〜0.24・高さ 0.10〜0.93）。
    t のローカルの +Y が正面。
    """
    t.box_nb(mb, -0.212, -0.22, 0.42, 0.212, 0.24, 0.47, seat)
    t.box_nb(mb, -0.212, -0.26, 0.47, 0.212, -0.20, 0.93, shell)
    t.box_nb(mb, -0.238, -0.26, 0.10, -0.190, 0.24, 0.47, frame)
    t.box_nb(mb, 0.190, -0.26, 0.10, 0.238, 0.24, 0.47, frame)
    y = -0.199
    mb.add_quad(t.p(-0.19, y, 0.50), t.p(-0.19, y, 0.90), t.p(0.19, y, 0.90),
                t.p(0.19, y, 0.50), seat)


def steel_shelf(mb, t, w=2.4, d=0.45, h=1.9, goods=("lecture_coop_band", "plastic_white",
                                                    "lecture_board_bluegreen")):
    """生協のスチール棚（約 120 三角形）。t のローカルの +Y が正面、原点は背の中央。"""
    hw = w * 0.5
    t.box_nb(mb, -hw, 0.0, 0.0, hw, 0.02, h, "metal_gray")
    t.box_nb(mb, -hw, 0.0, 0.0, -hw + 0.02, d, h, "metal_gray")
    t.box_nb(mb, hw - 0.02, 0.0, 0.0, hw, d, h, "metal_gray")
    for k, z in enumerate((0.15, 0.62, 1.09, 1.56)):
        t.box_nb(mb, -hw + 0.02, 0.02, z, hw - 0.02, d, z + 0.03, "metal_gray")
        if k < 3:
            m = goods[k % len(goods)]
            for i in range(3):
                x0 = -hw + 0.12 + i * (w - 0.24) / 3.0
                t.box_nb(mb, x0, 0.08, z + 0.03, x0 + (w - 0.24) / 3.0 - 0.08, d - 0.08,
                         z + 0.30, m)


def cafe_chair(mb, t, mat="lecture_seat_green", frame="metal_dark"):
    """ラウンジの椅子（4 本脚）。"""
    t.box_nb(mb, -0.21, -0.21, 0.42, 0.21, 0.21, 0.46, mat)
    t.box_nb(mb, -0.20, -0.22, 0.46, 0.20, -0.18, 0.84, mat)
    for lx in (-0.18, 0.14):
        for ly in (-0.18, 0.14):
            t.box_nb(mb, lx, ly, 0.0, lx + 0.04, ly + 0.04, 0.42, frame)


IVORY = "lecture_desk_ivory"
PANEL = "lecture_panel_dark"
WOOD = SHELL


def desk_bench(mb, t, w=1.8):
    """2F・3F の教室の 3 人掛けの固定机と、その後ろの背付きの長椅子（箱 4 つ・40 三角形）。

    t のローカルの +Y が正面（黒板の側）。原点は天板の後ろの縁の中央で、天板は y 0〜0.45、
    長椅子の背の後ろの面が y -0.52（列の間隔 1.0 m に収まる）。前板と長椅子の背が床まで届いて脚を兼ねる。
    """
    hw = w * 0.5
    t.box_nb(mb, -hw, 0.0, 0.70, hw, 0.45, 0.73, IVORY)
    t.box_nb(mb, -hw, 0.41, 0.0, hw, 0.45, 0.70, PANEL)
    t.box_nb(mb, -hw, -0.48, 0.42, hw, -0.18, 0.46, "chair_grey")
    t.box_nb(mb, -hw, -0.52, 0.0, hw, -0.48, 0.84, PANEL)


def green_board(mb, t, w=7.2, z0=0.9, z1=2.1, board="lecture_board_green"):
    """壁掛けの緑の黒板と木の枠・受け。t は壁の面の上で、ローカルの +Y が室の側。"""
    hw = w * 0.5
    f = 0.06
    t.box_nb(mb, -hw - f, 0.0, z0 - f, hw + f, 0.03, z1 + f, WOOD)
    t.vplate(mb, hw, 0.031, -hw, 0.031, z0, z1, board)    # 法線はローカルの +Y
    t.box_nb(mb, -hw, 0.03, z0 - f, hw, 0.12, z0 - f + 0.04, WOOD)


def pc_desk(mb, t, w=1.4):
    """306 の 2 人掛けの PC の机: 天板と前板、黒いモニター 2 台・キーボード 2 枚・灰色の椅子 2 脚。

    t のローカルの +Y が正面（白板の側）。原点は天板の後ろの縁の中央、天板は y 0〜0.6。
    """
    hw = w * 0.5
    t.box_nb(mb, -hw, 0.0, 0.70, hw, 0.60, 0.73, "desk_white")
    t.box_nb(mb, -hw, 0.56, 0.0, hw, 0.60, 0.70, PANEL)
    for sx in (-0.35, 0.35):
        t.box_nb(mb, sx - 0.26, 0.42, 0.85, sx + 0.26, 0.45, 1.16, "plastic_black")
        t.box_nb(mb, sx - 0.04, 0.45, 0.73, sx + 0.04, 0.50, 0.85, "plastic_black")
        t.box_nb(mb, sx - 0.19, 0.12, 0.73, sx + 0.19, 0.26, 0.75, "plastic_black")
        chair = kit.T(*t.p(sx, -0.30, 0.0), ang=math.atan2(t.s, t.c))
        F.chair_min(mb, chair, mat="chair_grey", frame="metal_gray")


def warmer_case(mb, t, w=1.0, d=0.45, h=0.42):
    """311 の弁当の保温ケース（長机の上に載せる。約 150 三角形）。ステンレスの枠と 2 段の棚、
    正面のガラス越しに弁当が並ぶ。t のローカルの +Y が正面（客の側）、原点は底の背の中央。
    """
    hw = w * 0.5
    t.box_nb(mb, -hw, 0.0, 0.0, hw, d, 0.04, "stainless")
    t.box_nb(mb, -hw, 0.0, h - 0.04, hw, d, h, "stainless")
    t.box_nb(mb, -hw, 0.0, 0.04, hw, 0.02, h - 0.04, "stainless")
    for sx in (-1.0, 1.0):
        t.box_nb(mb, sx * hw - 0.02, 0.0, 0.04, sx * hw + 0.02, d, h - 0.04, "stainless")
    zm = h * 0.5
    t.box_nb(mb, -hw + 0.02, 0.02, zm - 0.01, hw - 0.02, d - 0.02, zm + 0.01, "stainless")
    t.vplate(mb, hw, d, -hw, d, 0.04, h - 0.04, "glass_interior")    # 法線はローカルの +Y
    for z in (0.04, zm + 0.01):
        for k in range(3):
            x0 = -hw + 0.06 + k * (w - 0.12) / 3.0
            x1 = x0 + (w - 0.12) / 3.0 - 0.04
            bento(mb, t, x0, x1, 0.08, d - 0.08, z)


def bento(mb, t, x0, x1, y0, y1, z):
    """弁当 1 つ（黒い容器と白い蓋）。t のローカルの (x0..x1, y0..y1)、底が z。"""
    t.box_nb(mb, x0, y0, z, x1, y1, z + 0.05, "plastic_black")
    t.box_nb(mb, x0, y0, z + 0.05, x1, y1, z + 0.065, "plastic_white")
