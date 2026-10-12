"""第1実験棟（lab1）だけで使う部品。plan_lab1.py から呼ぶ。

座標はすべて建物のローカル（入口の真下の床が原点、+Y が奥、Z が上）。
機械は「自分の +Y が正面（操作する側）」でモデリングし、T が向きと位置を与える。
材質の名前は mats_lab1.py（lab1_ で始まる）か、imats.py の共有の材質。
"""

from . import furniture as F, kit
from .kit import T


def milling_machine(mb, x, y, ang=0.0):
    """青いフライス盤（柱 + 頭部 + 昇降台 + テーブル）。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.50, -0.45, 0.0, 0.50, 0.50, 0.20, "lab1_machine_grey")
    t.box(mb, -0.30, 0.20, 0.20, 0.30, 0.50, 1.95, "lab1_machine_blue")
    t.box(mb, -0.34, -0.15, 1.55, 0.34, 0.50, 2.05, "lab1_machine_blue")
    t.cyl(mb, 0.0, -0.02, 1.15, 1.55, 0.09, "lab1_machine_grey", seg=8)
    t.box(mb, -0.42, -0.35, 0.78, 0.42, 0.20, 0.90, "lab1_machine_blue")
    t.box(mb, -0.50, -0.30, 0.90, 0.50, 0.10, 0.98, "lab1_machine_grey")
    t.box(mb, 0.36, -0.40, 0.84, 0.46, -0.30, 1.12, "metal_dark")


def drill_press(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.28, -0.26, 0.0, 0.28, 0.26, 0.06, "lab1_machine_cream")
    t.cyl(mb, 0.0, 0.14, 0.06, 1.55, 0.05, "lab1_machine_grey", seg=6)
    t.box(mb, -0.17, -0.14, 1.28, 0.17, 0.24, 1.62, "lab1_machine_cream")
    t.box(mb, -0.15, -0.12, 0.75, 0.15, 0.12, 0.80, "lab1_machine_grey")
    t.cyl(mb, 0.0, 0.0, 0.88, 1.28, 0.02, "stainless", seg=5)
    t.box(mb, 0.17, 0.0, 1.38, 0.38, 0.04, 1.42, "metal_dark")


def surface_grinder(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.60, -0.45, 0.0, 0.60, 0.45, 0.90, "lab1_machine_cream")
    t.box(mb, -0.45, -0.30, 0.90, 0.45, 0.30, 0.98, "stainless")
    t.box(mb, -0.15, 0.30, 0.90, 0.15, 0.50, 1.60, "lab1_machine_cream")
    t.cyl(mb, 0.0, 0.0, 1.15, 1.50, 0.16, "lab1_machine_grey", seg=8)
    t.box(mb, 0.65, -0.20, 0.95, 0.85, 0.30, 1.35, "lab1_machine_grey")


def wire_edm(mb, x, y, ang=0.0):
    """ワイヤ放電加工機（大きな箱形の本体 + 加工槽の窓 + 操作盤）。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.95, -0.70, 0.0, 0.95, 0.75, 1.95, "lab1_machine_cream")
    t.box(mb, -0.70, 0.74, 0.70, 0.70, 0.78, 1.65, "glass_interior")
    t.box(mb, -0.72, 0.76, 0.66, 0.72, 0.80, 0.70, "lab1_machine_grey")
    t.box(mb, 1.00, -0.30, 0.0, 1.55, 0.45, 1.40, "lab1_machine_grey")
    t.box(mb, 1.05, -0.28, 1.40, 1.50, 0.12, 1.70, "plastic_black")
    t.box(mb, 1.08, 0.12, 1.45, 1.46, 0.15, 1.66, "screen_blue")


def lathe(mb, x, y, ang=0.0, length=2.2):
    t = T(x, y, 0.0, ang)
    hl = length * 0.5
    t.box(mb, -hl, -0.30, 0.0, -hl + 0.35, 0.30, 0.85, "lab1_machine_grey")
    t.box(mb, hl - 0.35, -0.30, 0.0, hl, 0.30, 0.85, "lab1_machine_grey")
    t.box(mb, -hl, -0.38, 0.85, hl, 0.34, 1.00, "lab1_machine_cream")
    t.box(mb, -hl, -0.38, 1.00, -hl + 0.55, 0.30, 1.40, "lab1_machine_blue")
    t.box(mb, hl - 0.45, -0.18, 1.00, hl - 0.15, 0.18, 1.18, "lab1_machine_grey")
    t.box(mb, -0.20, -0.30, 1.00, 0.20, 0.10, 1.12, "metal_dark")
    t.cyl(mb, -hl + 0.62, 0.0, 1.10, 1.12, 0.10, "stainless", seg=6)


def workbench_vise(mb, x, y, ang=0.0, w=2.4, d=0.8, vises=2):
    """オレンジの天板の作業台。万力と引き出し、背に工具板。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.0, -hw + 0.08, hd, 0.86, "metal_gray")
    t.box(mb, hw - 0.08, -hd, 0.0, hw, hd, 0.86, "metal_gray")
    t.box(mb, -hw, -hd, 0.12, hw, hd, 0.16, "metal_gray")
    t.box(mb, -hw - 0.02, -hd - 0.03, 0.86, hw + 0.02, hd + 0.03, 0.92,
          "lab1_bench_orange")
    t.box(mb, -hw + 0.1, -hd, 0.55, -hw + 0.8, hd * 0.8, 0.84, "lab1_tool_red")
    for k in range(vises):
        vx = -hw + 0.4 + (w - 0.8) * (k + 0.5) / max(1, vises)
        t.box(mb, vx - 0.09, -hd - 0.02, 0.92, vx + 0.09, -hd + 0.16, 1.04,
              "lab1_machine_blue")
        t.box(mb, vx - 0.02, -hd - 0.12, 0.96, vx + 0.02, -hd - 0.02, 1.00,
              "metal_dark")
    t.box(mb, -hw, hd - 0.04, 0.92, hw, hd, 1.80, "board_white")


def tool_chest(mb, x, y, ang=0.0, w=0.9):
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.30, 0.10, hw, 0.30, 1.05, "lab1_tool_red")
    t.box(mb, -hw - 0.02, -0.32, 1.05, hw + 0.02, 0.32, 1.10, "metal_dark")
    for k in range(5):
        z = 0.20 + k * 0.17
        t.box(mb, -hw + 0.05, 0.30, z, hw - 0.05, 0.315, z + 0.12, "metal_gray")
    for sx in (-1, 1):
        t.cyl(mb, sx * (hw - 0.1), 0.0, 0.0, 0.10, 0.05, "rubber_black", seg=6)


def stock_rack(mb, x, y, ang=0.0, w=2.0, d=0.6, h=1.9, rng=None):
    """棒材・板材の棚。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    for sx in (-1, 1):
        t.box(mb, sx * hw - 0.03, -hd, 0.0, sx * hw + 0.03, hd, h, "metal_gray")
    for k in range(4):
        z = 0.15 + k * (h - 0.25) / 3.0
        t.box(mb, -hw, -hd, z, hw, hd, z + 0.04, "metal_gray")
        n = 4 if rng is None else rng.randint(3, 6)
        for i in range(n):
            cx = -hw + 0.18 + i * (w - 0.36) / max(1, n - 1)
            t.cyl(mb, cx, 0.0, z + 0.04, z + 0.11, 0.045, "lab1_stock_steel", seg=5)


def chip_bin(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.35, -0.30, 0.10, 0.35, 0.30, 0.55, "lab1_machine_grey")
    t.box(mb, -0.32, -0.27, 0.55, 0.32, 0.27, 0.60, "metal_dark")
    for sx in (-1, 1):
        t.cyl(mb, sx * 0.26, -0.22, 0.0, 0.10, 0.05, "rubber_black", seg=6)
        t.cyl(mb, sx * 0.26, 0.22, 0.0, 0.10, 0.05, "rubber_black", seg=6)


def lane(mb, x0, y0, x1, y1, w=0.10, z=0.016):
    """黄色の通路線（矩形の枠）。"""
    m = "lab1_lane_yellow"
    kit.plate(mb, x0, y0, x1, y0 + w, z, m)
    kit.plate(mb, x0, y1 - w, x1, y1, z, m)
    kit.plate(mb, x0, y0 + w, x0 + w, y1 - w, z, m)
    kit.plate(mb, x1 - w, y0 + w, x1, y1 - w, z, m)


def ceiling_ducts(mb, x0, x1, y_list, z, r=0.22):
    """x 方向に通るダクトと、その脇のケーブルラック。"""
    for y in y_list:
        kit.tube(mb, (x0, y, z - 0.30), (x1, y, z - 0.30), r, "lab1_duct", seg=8)
        k = 0
        while x0 + k * 3.0 <= x1:
            hx = x0 + k * 3.0
            kit.box(mb, hx - 0.03, y - 0.03, z - 0.30, hx + 0.03, y + 0.03, z,
                    "lab1_cable_rack")
            k += 1
        kit.box(mb, x0, y + 0.55, z - 0.34, x1, y + 1.00, z - 0.30,
                "lab1_cable_rack")


def cassette_ac(mb, x, y, z):
    """天井カセット形のエアコン（下から見える吹き出しの枠）。"""
    kit.box(mb, x - 0.45, y - 0.45, z - 0.16, x + 0.45, y + 0.45, z, "plastic_white")
    kit.box(mb, x - 0.30, y - 0.30, z - 0.17, x + 0.30, y + 0.30, z - 0.16,
            "metal_dark")


def roll_up_shutter_box(mb, x0, x1, y, z):
    """巻き上げシャッターの収納箱（開口の上）と両脇のレール。"""
    kit.box(mb, x0, y - 0.20, z, x1, y + 0.20, z + 0.42, "lab1_shutter")
    for sx in (x0, x1 - 0.06):
        kit.box(mb, sx, y - 0.14, 0.0, sx + 0.06, y + 0.14, z, "lab1_shutter")


def nmr_magnet(mb, x, y):
    """NMR の超電導磁石（円筒の寒剤槽と上のやぐら）。"""
    kit.cyl(mb, x, y, 0.0, 0.12, 0.62, "metal_dark", seg=12)
    kit.cyl(mb, x, y, 0.12, 2.10, 0.50, "lab1_nmr_blue", seg=14)
    kit.cyl(mb, x, y, 1.10, 1.22, 0.53, "stainless", seg=14)
    kit.cyl(mb, x, y, 2.10, 2.55, 0.16, "stainless", seg=8)
    kit.cyl(mb, x, y, 2.55, 2.70, 0.26, "lab1_nmr_blue", seg=10)
    kit.box(mb, x + 0.55, y - 0.10, 0.0, x + 0.80, y + 0.10, 1.20, "metal_gray")


def test_rig(mb, x, y, ang=0.0):
    """研究室の実験装置の架台（アルミフレーム + モータ + 回転軸）。"""
    t = T(x, y, 0.0, ang)
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box(mb, sx * 0.55 - 0.03, sy * 0.35 - 0.03, 0.0, sx * 0.55 + 0.03,
                  sy * 0.35 + 0.03, 1.00, "metal_gray")
    t.box(mb, -0.58, -0.38, 0.95, 0.58, 0.38, 1.00, "metal_gray")
    t.box(mb, -0.58, -0.38, 0.40, 0.58, 0.38, 0.43, "metal_gray")
    t.box(mb, -0.50, -0.15, 1.00, -0.15, 0.15, 1.28, "lab1_rig_green")
    t.cyl(mb, 0.10, 0.0, 1.14, 1.16, 0.06, "stainless", seg=6)
    t.box(mb, 0.20, -0.20, 1.00, 0.50, 0.20, 1.20, "metal_dark")


def tech_stool(mb, x, y, ang=0.0):
    """座れる丸椅子（工場・研究室用）。"""
    vi = len(mb.verts)
    F.stool(mb, x, y, ang, mat="chair_grey", h=0.66)
    F._seat(mb, T(x, y, 0.0, ang), vi, 0.2, -0.2, 0.2, 0.3, 0.1)
