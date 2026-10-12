"""寮の階段（東奥の折り返し階段 1F-2F と、2F から塔屋へ上がる直階段）。

段は kcd_route.dorm_rooms_props.stair_run の実ジオメトリ（c.floor に積む。Unity は "floor" で始まる
メッシュだけを Ground にするので、踏面は必ず c.floor）。高さは dorm_levels の RISE。
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior import shell as sh                  # noqa: E402
from kcd_route import dorm_rooms_props as R           # noqa: E402
from kcd_route.dorm_levels import (  # noqa: E402
    CORE, CORE_XM, CORE_YL, RISE, Z2, ZC2, ZR)

STEPS = 10                        # 1 本 10 段（0.165 m * 10 = 1.65 m）
ROOF_X = (-3.90, -2.70)           # 塔屋へ上がる階段の幅（有効 1.20 m）
ROOF_Y0 = 34.60                   # 上端（屋上の床に出る所）
ROOF_STEPS = 20
ROOF_Y1 = CORE[3] - 0.45          # 塔屋の奥の壁（外周から離して、塔屋の屋根に外周から上がれないようにする）
TOWER_H = 2.40


def roof_hole():
    """2F の天井と屋上の床に開ける穴（塔屋の階段）。"""
    return (ROOF_X[0], ROOF_Y0, ROOF_X[1], ROOF_Y1)


def main_stair(c):
    """1F から 2F への折り返し階段。上り（A、西）→ 踊り場 → 下り側から見て上り（B、東）。"""
    x0, y0, x1, y1 = CORE
    xl = CORE_XM - sh.PART * 0.5
    xr = CORE_XM + sh.PART * 0.5
    half = Z2 * 0.5
    R.stair_run(c.floor, x0, xl, y0, CORE_YL, 0.0, half, STEPS)
    kit.box(c.floor, x0, CORE_YL, 0.0, x1, y1, half, "wall_white", top="floor_tile_grey")
    R.stair_run(c.floor, xr, x1, CORE_YL, y0, half, Z2, STEPS)
    # 上りと下りを分ける壁、西の壁（1F は入口の手前 0.9 m を開ける）、2F 側の南の壁
    sh.partition(c.wall, (CORE_XM, y0), (CORE_XM, CORE_YL), 0.0, ZC2)
    sh.partition(c.wall, (x0, y0 + 0.90), (x0, y1), 0.0, ZC2)
    sh.partition(c.wall, (x0, y0), (xl + sh.PART * 0.5, y0), Z2 - 0.05, ZC2)
    sh.partition(c.wall, (x0, y0), (x0, y0 + 0.90), Z2 - 0.05, ZC2)
    # 手すり（壁側の面に 1 本ずつ）
    z = lambda k: RISE * k                                  # noqa: E731
    kit.tube(c.wall, (xl - 0.08, y0, z(1) + 0.90), (xl - 0.08, CORE_YL, z(STEPS) + 0.90),
             0.03, "metal_white", seg=5)
    kit.tube(c.wall, (xr + 0.08, CORE_YL, z(STEPS) + 0.90), (xr + 0.08, y0, Z2 + 0.90),
             0.03, "metal_white", seg=5)
    # 踊り場の窓際の明かり取りは外周壁の窓帯に任せる。天井の穴は dorm_interior 側


def roof_stair(c):
    """2F から塔屋（屋上）への直階段。下端が北、上端が南（屋上の床に出る）。"""
    x0, x1 = ROOF_X
    y1 = ROOF_Y1
    R.stair_run(c.floor, x0, x1, y1 - 0.30, ROOF_Y0, Z2, ZR, ROOF_STEPS, z_base=Z2)
    # 塔屋: 階段の両脇の壁、屋根、入口の鴨居
    t = sh.PART * 0.5
    sh.partition(c.wall, (x0 - t, ROOF_Y0), (x0 - t, y1), ZR, ZR + TOWER_H)
    sh.partition(c.wall, (x1 + t, ROOF_Y0), (x1 + t, y1), ZR, ZR + TOWER_H)
    sh.partition(c.wall, (x0 - sh.PART, y1), (x1 + sh.PART, y1), ZR, ZR + TOWER_H)
    kit.box(c.wall, x0 - sh.PART, ROOF_Y0, ZR + TOWER_H, x1 + sh.PART, y1,
            ZR + TOWER_H + 0.16, "concrete_light", bottom="wall_white")
    sh.partition(c.wall, (x0, ROOF_Y0), (x1, ROOF_Y0), ZR + 2.20, ZR + TOWER_H)
    base = len(c.wall.verts)
    sh.door(c.wall, (x0 + x1) * 0.5, ROOF_Y0 + 0.05, ang=math.pi, w=x1 - x0, h=2.20,
            leaf="metal_white", open_=0.86)
    kit.lift(c.wall, base, ZR)
