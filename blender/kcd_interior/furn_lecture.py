"""講義棟の固有の家具（spec §3・§4）。"""

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
