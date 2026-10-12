"""anim.Gait を Blender なしで動かすための mathutils（numpy で書いた最小限）。

stubs.py の Vector / Matrix は import を通すだけの空の型なので、歩き・走りのポーズを
計算できない。ここでは anim が使う演算（3 次元のベクトルと 3x3 行列、軸回りの回転、
ZYX のオイラー角、軸と角への分解）だけを実装し、load_anim() で anim をもう 1 つ読み込む。
"""

from __future__ import annotations

import importlib.util
import math
import os
import sys
import types

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ANIM = os.path.join(os.path.dirname(HERE), "kcd_chara", "anim.py")


class Vector:
    __slots__ = ("v",)

    def __init__(self, xyz=(0.0, 0.0, 0.0)):
        self.v = np.array(xyz, dtype=float).reshape(3)

    x = property(lambda s: float(s.v[0]), lambda s, a: s.v.__setitem__(0, a))
    y = property(lambda s: float(s.v[1]), lambda s, a: s.v.__setitem__(1, a))
    z = property(lambda s: float(s.v[2]), lambda s, a: s.v.__setitem__(2, a))

    def __iter__(self):
        return iter(float(c) for c in self.v)

    def __getitem__(self, i):
        return float(self.v[i])

    def __len__(self):
        return 3

    def __add__(self, o):
        return Vector(self.v + o.v)

    def __sub__(self, o):
        return Vector(self.v - o.v)

    def __neg__(self):
        return Vector(-self.v)

    def __mul__(self, k):
        return Vector(self.v * float(k))

    __rmul__ = __mul__

    def __truediv__(self, k):
        return Vector(self.v / float(k))

    @property
    def length(self):
        return float(np.linalg.norm(self.v))

    def normalized(self):
        return Vector(self.v / max(self.length, 1e-12))

    def dot(self, o):
        return float(self.v @ o.v)

    def angle(self, o):
        c = self.dot(o) / max(self.length * o.length, 1e-12)
        return math.acos(max(-1.0, min(1.0, c)))

    def copy(self):
        return Vector(self.v)


class _Euler:
    def __init__(self, x, y, z):
        self.x, self.y, self.z = x, y, z


class _Quaternion:
    def __init__(self, axis, angle):
        self._axis, self._angle = axis, angle

    def to_axis_angle(self):
        return self._axis, self._angle


class Matrix:
    __slots__ = ("m",)

    def __init__(self, rows):
        self.m = np.array(rows, dtype=float).reshape(3, 3)

    @classmethod
    def Identity(cls, n):
        assert n == 3
        return cls(np.eye(3))

    @classmethod
    def Rotation(cls, angle, n, axis):
        assert n == 3
        a = np.array(list(axis), dtype=float)
        a = a / np.linalg.norm(a)
        K = np.array([[0.0, -a[2], a[1]], [a[2], 0.0, -a[0]], [-a[1], a[0], 0.0]])
        return cls(np.eye(3) + math.sin(angle) * K + (1.0 - math.cos(angle)) * (K @ K))

    def __matmul__(self, o):
        if isinstance(o, Matrix):
            return Matrix(self.m @ o.m)
        return Vector(self.m @ o.v)

    def inverted(self):
        return Matrix(np.linalg.inv(self.m))

    def to_3x3(self):
        return Matrix(self.m)

    def to_euler(self, order="XYZ", compat=None):
        # 前のキーに角度を寄せる compat は make_action（Blender 上だけ）でしか使わない
        if compat is not None:
            raise NotImplementedError("compat")
        m = self.m
        if order == "ZYX":                     # R = Rx @ Ry @ Rz
            y = math.asin(max(-1.0, min(1.0, m[0, 2])))
            return _Euler(math.atan2(-m[1, 2], m[2, 2]), y, math.atan2(-m[0, 1], m[0, 0]))
        if order == "XYZ":                     # R = Rz @ Ry @ Rx
            y = math.asin(max(-1.0, min(1.0, -m[2, 0])))
            return _Euler(math.atan2(m[2, 1], m[2, 2]), y, math.atan2(m[1, 0], m[0, 0]))
        raise NotImplementedError(order)

    def to_quaternion(self):
        m = self.m
        c = max(-1.0, min(1.0, (np.trace(m) - 1.0) * 0.5))
        ang = math.acos(c)
        if ang < 1e-9:
            return _Quaternion(Vector((1.0, 0.0, 0.0)), 0.0)
        ax = np.array([m[2, 1] - m[1, 2], m[0, 2] - m[2, 0], m[1, 0] - m[0, 1]])
        n = np.linalg.norm(ax)
        if n < 1e-9:                           # 180 度: 対称部分から軸を取る
            w, V = np.linalg.eigh((m + m.T) * 0.5)
            ax = V[:, int(np.argmax(w))]
            n = np.linalg.norm(ax)
        return _Quaternion(Vector(ax / n), ang)


def load_anim():
    """mathutils を差し替えて kcd_chara/anim.py を別名で読み込む（元の anim には触らない）。"""
    mod = types.ModuleType("mathutils")
    mod.Vector, mod.Matrix = Vector, Matrix
    saved = sys.modules.get("mathutils")
    sys.modules["mathutils"] = mod
    try:
        spec = importlib.util.spec_from_file_location("kcd_chara._anim_np", ANIM)
        anim = importlib.util.module_from_spec(spec)
        anim.__package__ = "kcd_chara"
        spec.loader.exec_module(anim)
    finally:
        if saved is None:
            del sys.modules["mathutils"]
        else:
            sys.modules["mathutils"] = saved
    return anim


class _Bone:
    def __init__(self, head, tail):
        self.head_local = Vector(head)
        self.tail_local = Vector(tail)


class FakeArmature:
    """rig.bone_positions のレストポーズだけを持つアーマチュア（_Legs が読む分）。"""

    def __init__(self, pos: dict):
        self.data = types.SimpleNamespace(
            bones={k: _Bone(h, t) for k, (h, t) in pos.items()})
