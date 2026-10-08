"""第1研究棟の入れる室と廊下沿いの家具: 機械工学科の実験室と研究室、産学試作開発室、西側ラウンジ、
東の休憩スペース。
"""

import math

from . import furniture as F, kit, shell
from . import plan_research1_dims as D

PI = math.pi
HALF_PI = math.pi * 0.5
STEEL = "research1_steel_leg"
WALL_FACE = shell.PART * 0.5


def _lab_a(c):
    """機械工学科 実験室: 島型の実験台 3 台（POI と南の扉の東）、西の壁のドラフト、東の壁のホワイトボード。"""
    mb = c.furn("lab_a")
    for y in (22.4, 25.6, 28.8):
        F.lab_bench(mb, -25.9, y, ang=0.0, rng=c.rng)
    F.fume_hood(mb, D.LAB_A[0] + WALL_FACE + 0.46, 25.0, ang=-HALF_PI)
    F.whiteboard(mb, D.LAB_A[1] - WALL_FACE, 25.0, 1.5, ang=HALF_PI, w=2.4, h=1.2)


def _professor(c):
    """機械工学科 研究室（南の室列の居室）: 背中合わせの机 4 台と椅子、西の壁のホワイトボード、東の壁の本棚 2 連。"""
    mb = c.furn("professor")
    for x in (-30.75, -29.25):
        for y, sgn in ((10.04, -1.0), (10.76, 1.0)):   # sgn: 座る側（南の机は -Y、北の机は +Y）
            F.desk(mb, x, y, ang=0.0 if sgn > 0 else PI, top="research1_table_white", leg=STEEL,
                   drawers=True)
            F.chair(mb, x, y + sgn * 0.7, ang=PI if sgn > 0 else 0.0, mat="research1_chair_white",
                    frame=STEEL)
    F.whiteboard(mb, D.PROF[0] + WALL_FACE, 13.0, 1.6, ang=-HALF_PI)
    xs = D.PROF[1] - WALL_FACE - 0.15
    for y in (12.4, 13.3):
        F.bookshelf(mb, xs, y, ang=HALF_PI, body="research1_cabinet_white", rng=c.rng)


def _lab_b(c):
    """産学試作開発室: 作業台 3 台（POI の西）と、東の壁のスチール棚 2 連。"""
    mb = c.furn("lab_b")
    for y in (9.2, 12.0, 14.8):
        F.lab_bench(mb, 49.0, y, ang=0.0, w=3.0, rng=c.rng)
    xs = D.LAB_B[1] - WALL_FACE - 0.3
    for y in (9.6, 14.0):
        F.bookshelf(mb, xs, y, ang=HALF_PI, w=3.0, h=2.1, body="research1_locker_grey", depth=0.6,
                    books=False)


def _lounge(c):
    """西側ラウンジ: 背もたれ付きのベンチ 3 台、コピー機、エコステーション（分別箱 4）。
    東の休憩スペース: 南の壁の自販機 2 台（中心 (13.5, 12.05)）とベンチ 2 台。
    """
    mb = c.furn("lounge")
    for x, y in D.LOUNGE_BENCHES:
        F.bench(mb, x, y, ang=0.0, w=D.BENCH_W, mat="research1_bench_top", leg=STEEL, back=True)
    kit.box(mb, -25.1, 15.15, D.Z1, -24.5, 15.85, 1.1, "research1_cabinet_white")
    shell.trash_bins(mb, -25.4, D.ECO_Y, ang=-HALF_PI, n=4)
    for x, mat in ((12.95, "research1_red_outlet"), (14.05, "fm_blue")):
        shell.vending(mb, x, 12.05, ang=0.0, mat=mat)
    for x in (9.2, 11.2):
        F.bench(mb, x, 12.3, ang=0.0, w=D.BENCH_W, mat="research1_bench_top", leg=STEEL, back=True)


def build(c):
    _lab_a(c)
    _professor(c)
    _lab_b(c)
    _lounge(c)
