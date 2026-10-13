"""体育館の外観（docs/ref/exterior/gym.md）。

6 階・21.8 m の 1 棟。OSM の輪郭は西の大きな塊（アリーナ）と東の細長い塊（部室の棟）に分かれる。
アリーナは窓の少ない大壁（西と南は 1 階に連続窓、2 階から上に白い縦フィン、最上部にハイサイドライト）、
部室の棟は東と北の妻面がレンガで東に外階段、南と西の面は 2〜6 階に水平ルーバーの連続窓、
屋根は緑化。
地面の高さの輪郭は OSM のまま変えない（張り出しは 2 階の床より上だけ）。
"""

import math

from . import buildings, facade, geom

ARENA_MAX_U = 7.1        # 辺の両端がこれより西（u）にあればアリーナの塊
HIGHSIDE_H = 5.4         # 最上部のハイサイドライトの帯の高さ（上端が建物の高さ）
HIGHSIDE_FLOORS = 2      # 帯の段数
FIN_FLOORS = 3           # 縦フィンを立てる階数（2 階から）
LOUVER_MAT = "louver_white"
ROOF_LAYER = 0.08                       # 屋根の上の重ね板の厚み
BAND = (-29.0, 74.8, 4.6, 80.6)         # アリーナの北の、設備が並ぶ暗い帯（uv の矩形）
BAND_BOXES = ((-27.0, 78.0, -19.0, 80.2, 2.0, "metal_grey"),
              (-14.0, 78.2, -5.0, 80.2, 1.8, "metal_white"))
GREEN_ROOF = (12.4, 38.4, 24.4, 66.0)   # 部室の棟の緑化屋根
WING_BOXES = ((12.8, 68.0, 17.2, 72.5, 2.2), (19.0, 68.0, 23.6, 72.8, 2.0))


def split_edges(loop, frame):
    """辺の番号を (アリーナ, 部室の棟) に分ける。"""
    arena, wing = [], []
    n = len(loop)
    for i in range(n):
        us = [frame.uv(loop[i])[0], frame.uv(loop[(i + 1) % n])[0]]
        (arena if max(us) <= ARENA_MAX_U else wing).append(i)
    return arena, wing


def _arena_wall(mb, loop, i, fh, h, frame, glazed, finned):
    """アリーナの面: 1 階（glazed なら連続窓）+ 大壁 + 最上部のハイサイドライト。"""
    a, b = loop[i], loop[(i + 1) % len(loop)]
    top = h - HIGHSIDE_H
    kw = dict(wall="concrete_light", glass="glass_dark", edges=[i])
    if glazed:
        facade.add_facade(mb, loop, 0.0, fh, 0, 1, seg=3.2, sill=0.9, header=0.45,
                          inset=0.35, mullion=0.26, **kw)
        facade.add_solid(mb, loop, fh, top, "concrete_light", edges=[i])
    else:
        facade.add_solid(mb, loop, 0.0, top, "concrete_light", edges=[i])
    facade.add_facade(mb, loop, top, HIGHSIDE_H / HIGHSIDE_FLOORS, 0, HIGHSIDE_FLOORS,
                      seg=3.2, sill=0.45, header=0.35, inset=0.35, mullion=0.24, **kw)
    if finned:
        buildings._lab1_fins(mb, a, b, fh, fh * (1 + FIN_FLOORS))


def _wing_wall(mb, loop, i, fh, lv, ribbon, foyer, dense):
    """部室の棟の面: 1 階は壁（foyer なら大きな窓）、2 階から上は水平ルーバーの連続窓。"""
    kw = dict(wall="concrete_light", glass="glass_dark", edges=[i], seg=3.0,
              header=0.45, inset=0.35, mullion=0.3)
    if foyer:
        facade.add_facade(mb, loop, 0.0, fh, 0, 1, sill=0.45, **kw)
    else:
        facade.add_solid(mb, loop, 0.0, fh, "concrete_light", edges=[i])
    if ribbon:
        facade.add_facade(mb, loop, 0.0, fh, 1, lv, sill=1.0, **kw)
        facade.add_louvers(mb, loop, 0.0, fh, 1, lv, mat=LOUVER_MAT, depth=0.5, thick=0.1,
                           edges=[i], per_floor=2 if dense else 1)
    else:
        facade.add_solid(mb, loop, fh, fh * lv, "concrete_light", edges=[i])


def _roof(mb, frame, loop, h, brick):
    """屋根（空中写真）: アリーナは白い大きな平屋根、北の帯は設備が並ぶ暗い屋根、
    部室の棟は緑化屋根と北寄りの空調機。"""
    mb.add_ngon_flat(loop, h, "metal_white")
    facade.add_parapet(mb, loop, h, 0.9, 0.4, "concrete_light",
                       edges=[i for i in range(len(loop)) if i not in brick])
    facade.add_parapet(mb, loop, h, 0.9, 0.4, "brick_red", edges=brick)
    # 北の帯
    mb.add_slab(frame.rect(*BAND), h, h + ROOF_LAYER, "roof_grey")
    for u0, v0, u1, v1, bh, mat in BAND_BOXES:
        mb.add_prism(frame.rect(u0, v0, u1, v1), h, h + bh, mat, "metal_white")
    for r in range(2):
        for c in range(5):
            uc, vc = -24.0 + c * 1.9, 76.2 + r * 2.3
            mb.add_prism(frame.rect(uc - 0.7, vc - 0.5, uc + 0.7, vc + 0.5), h, h + 1.5,
                         "metal_grey", "metal_white")
    # 部室の棟
    mb.add_slab(frame.rect(*GREEN_ROOF), h, h + ROOF_LAYER, "grass")
    for u0, v0, u1, v1, bh in WING_BOXES:
        mb.add_prism(frame.rect(u0, v0, u1, v1), h, h + bh, "metal_white", "metal_white")
    for k in range(3):
        px, py = frame.xy(21.5, 69.5 + k * 1.6)
        mb.add_cylinder(px, py, h, h + 3.0, 0.5, "metal_grey", seg=8)


def build(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = max(1, b["levels"] or 6)
    fh = h / lv
    arena, wing = split_edges(loop, frame)
    n = len(loop)
    facing = [buildings._lab1_facing(frame, loop[i], loop[(i + 1) % n]) for i in range(n)]

    def longest(group, side):
        cand = [i for i in group if facing[i] == side]
        return max(cand, key=lambda i: math.dist(loop[i], loop[(i + 1) % n])) if cand else None

    south_arena = longest(arena, "s")
    west_arena = longest(arena, "w")
    east_wing = longest(wing, "e")
    north_wing = longest(wing, "n")
    south_wing = longest(wing, "s")
    west_wing = longest(wing, "w")

    brick = []
    for i in range(n):
        a, c = loop[i], loop[(i + 1) % n]
        if i in arena:
            _arena_wall(mb, loop, i, fh, h, frame, glazed=i in (south_arena, west_arena),
                        finned=i in (south_arena, west_arena))
        elif i == east_wing:
            buildings._lab1_stair_slot(mb, a, c, fh, lv)
            brick.append(i)
        elif i == north_wing:
            buildings._lab1_wall(mb, a, c, 0.0, h, "brick_red")
            brick.append(i)
        else:
            _wing_wall(mb, loop, i, fh, lv, ribbon=i in (south_wing, west_wing),
                       foyer=i == south_wing, dense=i == west_wing)
    if south_arena is not None:
        a, c = loop[south_arena], loop[(south_arena + 1) % n]
        buildings._lab1_canopy(mb, a, c, fh, buildings._lab1_door_t(a, c, frame, "gym"))
    _roof(mb, frame, loop, h, brick)
    # 入口と看板は kcd_lib.entrances が置く（南面の中央）
