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
