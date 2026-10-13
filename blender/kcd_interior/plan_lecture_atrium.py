"""講義棟の吹き抜けの 2F〜6F の縁と頂部（spec §1-5）。

吹き抜け（X -2.8〜6.6・Y 23.5〜31.5）の東西は 2 つのコアの箱の面で、各階の南北の廊下に白い手すり壁、
廊下の天井と上の階の床スラブの間に白い垂れ壁を立てる。6F の天井の上に頂部の白い天井（25.1）と
光の帯を張り、7F は吹き抜けの上を床で塞ぐ（plan_lecture_west）。1F の天井の穴の垂れ壁は
plan_lecture_core が作る。部品は plan_lecture_west の各階の区画（s2・n2・c2）へ積む。
"""

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_west as W

FLOORS = RD.FLOORS[:-1]               # 縁を作る階（2F〜6F）
PARAPET_T, PARAPET_H = 0.15, 1.2
HEADER_T = 0.10
WHITE = "lecture_rail_white"


def _edges(walls, floor):
    """floor の階の南北の手すり壁と、天井から上の階の床スラブ（6F は頂部の天井）までの垂れ壁。"""
    x0, y0, x1, y1 = D.ATRIUM
    z = RD.FLOOR_Z[floor]
    zc = z + RD.CEIL_H
    zt = D.VOID_TOP if floor == FLOORS[-1] else RD.next_z(floor) - D.SLAB
    k = W.segment((x0 + x1) * 0.5)
    s, n = walls.get("s", k), walls.get("n", k)
    kit.box(s, x0, y0 - PARAPET_T, z, x1, y0, z + PARAPET_H, WHITE)
    kit.box(n, x0, y1, z, x1, y1 + PARAPET_T, z + PARAPET_H, WHITE)
    kit.box(s, x0, y0 - HEADER_T, zc, x1, y0, zt, WHITE)
    kit.box(n, x0, y1, zc, x1, y1 + HEADER_T, zt, WHITE)


def _top(c, walls):
    """頂部の白い天井と光の帯（6F の区画 c2）、吹き抜けの明かり。"""
    x0, y0, x1, y1 = D.ATRIUM
    mb = walls.get("c", W.segment((x0 + x1) * 0.5))
    shell.ceiling(mb, x0, y0, x1, y1, D.VOID_TOP, "ceiling_white")
    for y in (25.5, 27.5, 29.5):
        shell.light_strip(mb, x0 + 0.3, y - 0.1, x1 - 0.3, y + 0.1, D.VOID_TOP)
    cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
    for z, e in ((7.0, 350.0), (15.0, 450.0), (23.5, 550.0)):
        c.light(cx, cy, z, energy=e, radius=2.0)


def build(c):
    for floor in FLOORS:
        _edges(W.bands(c, "wall", floor), floor)
    _top(c, W.bands(c, "wall", FLOORS[-1]))
