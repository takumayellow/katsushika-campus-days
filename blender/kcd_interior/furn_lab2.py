"""第2実験棟（lab2）だけで使う部品。plan_lab2.py から呼ぶ。

座標はすべて建物のローカル（入口の真下の床が原点、+Y が奥、Z が上）。
大型実験ホールの設備（アンカー穴の床・反力壁・反力フレーム・振動台・供試体・天井クレーン）。
材質の名前は mats_lab2.py（lab2_ で始まる）か、imats.py の共有の材質。
"""

from . import kit
from .kit import T


def anchor_floor(mb, x0, y0, x1, y1, pitch=1.0, z=0.016, s=0.07):
    """アンカー穴の格子（強固な床）。穴は小さな暗い四角で表す。"""
    nx = int((x1 - x0) / pitch)
    ny = int((y1 - y0) / pitch)
    for i in range(nx):
        for j in range(ny):
            cx = x0 + (i + 0.5) * pitch
            cy = y0 + (j + 0.5) * pitch
            kit.plate(mb, cx - s, cy - s, cx + s, cy + s, z, "lab2_anchor_hole")


def reaction_wall(mb, x0, x1, y, depth=1.2, h=6.0, hole_pitch=1.0):
    """反力壁（奥の壁の前に立つ厚いコンクリートの壁。前面に載荷用のボルト穴）。"""
    kit.box(mb, x0, y - depth, 0.0, x1, y, h, "lab2_reaction_concrete")
    n = int((x1 - x0) / hole_pitch)
    m = int((h - 1.0) / hole_pitch)
    for i in range(n):
        cx = x0 + (i + 0.5) * hole_pitch
        for k in range(m):
            cz = 0.8 + k * hole_pitch
            kit.box(mb, cx - 0.06, y - depth - 0.02, cz - 0.06,
                    cx + 0.06, y - depth, cz + 0.06, "lab2_anchor_hole")


def reaction_frame(mb, x, y, w=4.0, d=1.0, h=5.0, ang=0.0):
    """門形の反力フレーム。梁に油圧ジャッキ（アクチュエータ）を下げる。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    for sx in (-1, 1):
        t.box(mb, sx * hw - 0.20, -hd, 0.0, sx * hw + 0.20, hd, h, "lab2_frame_steel")
        t.box(mb, sx * hw - 0.40, -hd - 0.15, 0.0, sx * hw + 0.40, hd + 0.15, 0.12,
              "lab2_frame_steel")
    t.box(mb, -hw - 0.20, -hd, h - 0.60, hw + 0.20, hd, h, "lab2_frame_steel")
    t.box(mb, -0.20, -0.25, h - 1.60, 0.20, 0.25, h - 0.60, "lab2_actuator_grey")
    t.cyl(mb, 0.0, 0.0, h - 2.30, h - 1.60, 0.07, "stainless", seg=8)
    t.box(mb, -0.35, -0.30, h - 2.45, 0.35, 0.30, h - 2.30, "lab2_actuator_grey")


def specimen_beam(mb, x, y, ang=0.0, w=3.2, d=0.35, h=0.4, z=0.5):
    """載荷試験中の供試体（2 つの支点に載るコンクリート梁）。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    for sx in (-1, 1):
        t.box(mb, sx * (hw - 0.3) - 0.2, -0.3, 0.0, sx * (hw - 0.3) + 0.2, 0.3, z,
              "lab2_table_steel")
    t.box(mb, -hw, -d * 0.5, z, hw, d * 0.5, z + h, "lab2_specimen_concrete")
    t.box(mb, -0.25, -d * 0.5 - 0.04, z + h, 0.25, d * 0.5 + 0.04, z + h + 0.06,
          "lab2_table_steel")


def shaking_table(mb, x, y, ang=0.0, w=3.0, d=3.0):
    """振動台（ピット上の鋼製の台と、横のアクチュエータ）。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw - 0.3, -hd - 0.3, 0.0, hw + 0.3, hd + 0.3, 0.30, "lab2_table_steel")
    t.box(mb, -hw, -hd, 0.30, hw, hd, 0.55, "lab2_table_steel")
    t.box(mb, -hw + 0.1, -hd + 0.1, 0.55, hw - 0.1, hd - 0.1, 0.58, "metal_dark")
    t.cyl(mb, hw + 0.35, 0.0, 0.18, 0.60, 0.10, "lab2_actuator_grey", seg=8)
    t.box(mb, hw + 0.30, -0.35, 0.0, hw + 0.60, 0.35, 0.18, "lab2_frame_steel")


def specimen_stack(mb, x, y, n=6, ang=0.0):
    """積んだ供試体（コンクリートの円柱）と鉄筋の束。"""
    t = T(x, y, 0.0, ang)
    for i in range(n):
        t.cyl(mb, (i % 3) * 0.24 - 0.24, (i // 3) * 0.24, 0.0, 0.30, 0.10,
              "lab2_specimen_concrete", seg=8)
    t.box(mb, 0.50, -0.40, 0.0, 0.62, 0.40, 0.10, "metal_dark")
    for k in range(4):
        t.cyl(mb, 0.56, -0.28 + k * 0.18, 0.10, 0.20, 0.04, "lab2_rebar", seg=5)


def crane(mb, x0, x1, y0, y1, z, bridge_y, hook_x):
    """天井クレーン。壁ぎわの走行桁 2 本（y 方向）、その間の橋形桁（x 方向）、ホイストと吊り具。"""
    for x in (x0, x1):
        kit.box(mb, x - 0.18, y0, z - 0.45, x + 0.18, y1, z, "lab2_crane_yellow")
    kit.box(mb, x0, bridge_y - 0.30, z - 0.95, x1, bridge_y + 0.30, z - 0.50,
            "lab2_crane_yellow")
    kit.box(mb, hook_x - 0.55, bridge_y - 0.45, z - 1.45, hook_x + 0.55, bridge_y + 0.45,
            z - 0.95, "lab2_frame_steel")
    kit.tube(mb, (hook_x, bridge_y, z - 1.45), (hook_x, bridge_y, z - 3.6), 0.02,
             "metal_dark", seg=4)
    kit.box(mb, hook_x - 0.14, bridge_y - 0.10, z - 3.9, hook_x + 0.14, bridge_y + 0.10,
            z - 3.6, "lab2_crane_yellow")
    # 手元のペンダント操作盤
    kit.tube(mb, (hook_x + 0.8, bridge_y, z - 0.95), (hook_x + 0.8, bridge_y, z - 4.6),
             0.012, "metal_dark", seg=4)
    kit.box(mb, hook_x + 0.70, bridge_y - 0.06, z - 5.0, hook_x + 0.90, bridge_y + 0.06,
            z - 4.6, "lab2_rail_red")


def safety_rail(mb, x0, y0, x1, y1, h=1.0, post=2.0):
    """赤い安全柵（Y 方向か X 方向のどちらかに長い 1 本）。"""
    L = max(abs(x1 - x0), abs(y1 - y0))
    n = max(2, int(L / post) + 1)
    for i in range(n):
        f = i / (n - 1)
        px, py = x0 + (x1 - x0) * f, y0 + (y1 - y0) * f
        kit.box(mb, px - 0.03, py - 0.03, 0.0, px + 0.03, py + 0.03, h, "lab2_rail_red")
    for z in (h, h * 0.5):
        kit.tube(mb, (x0, y0, z), (x1, y1, z), 0.025, "lab2_rail_red", seg=4)


def hardhat_rack(mb, x, y, ang=0.0, n=8):
    """ヘルメット掛け（玄関ホールの壁）。"""
    t = T(x, y, 0.0, ang)
    w = 0.30 * n
    t.box(mb, -w * 0.5, 0.0, 1.45, w * 0.5, 0.05, 1.55, "metal_gray")
    for i in range(n):
        cx = -w * 0.5 + 0.15 + i * 0.30
        t.cyl(mb, cx, 0.14, 1.50, 1.64, 0.11, "chair_orange", seg=6)
