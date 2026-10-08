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
    """機械工学科 実験室: 島型の実験台 2 台と西の壁のドラフト。"""
    mb = c.furn("lab_a")
    for y in (23.5, 27.0):
        F.lab_bench(mb, -28.6, y, ang=0.0, rng=c.rng)
    xw = D.LAB_A[0] + WALL_FACE + 0.46
    for y in (22.0, 28.4):
        F.fume_hood(mb, xw, y, ang=-HALF_PI)
    F.whiteboard(mb, D.LAB_A[1] - WALL_FACE, 25.0, 1.5, ang=HALF_PI, w=2.4, h=1.2)


def _professor(c):
    """機械工学科 研究室（南の室列の居室）: 机 2 台と椅子、ホワイトボード、本棚。"""
    mb = c.furn("professor")
    for x in (-32.3, -27.6):
        F.desk(mb, x, 10.2, ang=0.0, top="research1_table_white", leg=STEEL, drawers=True)
        F.chair(mb, x, 10.9, ang=PI, mat="research1_chair_white", frame=STEEL)
    F.whiteboard(mb, D.PROF[0] + WALL_FACE, 13.0, 1.6, ang=-HALF_PI)
    x1 = D.PROF[1] - WALL_FACE
    kit.box(mb, x1 - 0.4, 11.4, D.Z1, x1, 15.4, 2.0, "research1_cabinet_white")


def _lab_b(c):
    """産学試作開発室: 島型の実験台 2 台と、東の壁の棚。"""
    mb = c.furn("lab_b")
    for y in (10.0, 14.6):
        F.lab_bench(mb, 51.7, y, ang=0.0, rng=c.rng)
    x1 = D.LAB_B[1] - WALL_FACE
    kit.box(mb, x1 - 0.6, 8.0, D.Z1, x1, 15.6, 2.1, "research1_locker_grey")


def _lounge(c):
    """西側ラウンジ: 背もたれ付きのベンチ 3 台、コピー機、分別ごみ箱。東の休憩スペースのベンチ 2 台。"""
    mb = c.furn("lounge")
    for x, y in D.LOUNGE_BENCHES:
        F.bench(mb, x, y, ang=0.0, mat="research1_bench_top", leg=STEEL, back=True)
    kit.box(mb, -25.1, 15.15, D.Z1, -24.5, 15.85, 1.1, "research1_cabinet_white")
    shell.trash_bins(mb, -25.4, 14.2, ang=-HALF_PI, n=3)
    for x in (10.4, 13.6):
        F.bench(mb, x, 12.3, ang=0.0, mat="research1_bench_top", leg=STEEL, back=True)


def build(c):
    _lab_a(c)
    _professor(c)
    _lab_b(c)
    _lounge(c)
