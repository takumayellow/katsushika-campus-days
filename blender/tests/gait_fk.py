"""歩き・走りのポーズを Blender なしで組み立て、関節の位置と角度を測る。

anim の回転はワールド軸で書かれ、親の回転に左から重なる（_world_rot）。ボーンのレスト位置
（rig.bone_positions）に親から順に回転を当てれば、Blender で書き出したときと同じ関節の位置になる。
前は -y、上は +z。
"""

from __future__ import annotations

import math
from functools import lru_cache

import numpy as np

import mathutils_np
from kcd_chara import body, params, rig

ANIM = mathutils_np.load_anim()


def _ops_matrix(ops) -> np.ndarray:
    if isinstance(ops, mathutils_np.Matrix):
        return ops.m
    R = np.eye(3)
    for axis, ang in ops:
        R = ANIM._rot(axis, ang).m @ R
    return R


@lru_cache(maxsize=None)
def rest(cid: str) -> dict:
    p = params.resolve(cid)
    return rig.bone_positions(p, body.Anatomy(p))


@lru_cache(maxsize=None)
def gait(cid: str, kind: str):
    p = params.resolve(cid)
    over = (p.get("gait") or {}).get(kind, {})
    return ANIM.Gait(mathutils_np.FakeArmature(rest(cid)), kind, **over)


def spec_at(cid: str, kind: str, ph: float) -> dict:
    """位相 ph のポーズ。ゲームに書き出すときと同じく腕下ろしを合成する。"""
    p = params.resolve(cid)
    return ANIM._arms_down(gait(cid, kind).pose(ph), body.arm_drop(p), body.elbow_open(p))


def joints(cid: str, kind: str, ph: float) -> dict[str, np.ndarray]:
    """各ボーンの根元のワールド位置と、手首・つま先などの先端（名前 + "_tail"）。"""
    pos = rest(cid)
    spec = spec_at(cid, kind, ph)
    rot: dict[str, np.ndarray] = {}
    head: dict[str, np.ndarray] = {}
    out: dict[str, np.ndarray] = {}
    for name, parent in rig.HIERARCHY:
        R = _ops_matrix(spec.get(name, []))
        h0, t0 = pos[name]
        if parent is None:
            rot[name] = R
            head[name] = h0 + np.array(spec.get("root", (0.0, 0.0, 0.0)))
        else:
            rot[name] = rot[parent] @ R
            head[name] = head[parent] + rot[parent] @ (h0 - pos[parent][0])
        out[name] = head[name]
        out[name + "_tail"] = head[name] + rot[name] @ (t0 - h0)
    return out


def _angle(a: np.ndarray, b: np.ndarray) -> float:
    c = float(a @ b) / float(np.linalg.norm(a) * np.linalg.norm(b))
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


def _front(v: np.ndarray) -> float:
    """横から見て鉛直下向きから前（-y）へ何度出るか。"""
    return math.degrees(math.atan2(-v[1], -v[2]))


def leg(j: dict, side: str) -> dict:
    hip, knee, ank = j[side + "UpperLeg"], j[side + "LowerLeg"], j[side + "Foot"]
    thigh, shin = knee - hip, ank - knee
    return {
        "knee": _angle(thigh, shin),          # 膝の屈曲（0 = 伸び切り）
        "hip": _front(thigh),                 # 股関節の屈曲（前が正）
        "shin": _front(shin),                 # すねの傾き（膝が足首より前なら負）
        "foot_ahead": float(hip[1] - ank[1]),  # 足首が股関節より前に出ている距離
        "knee_ahead": float(ank[1] - knee[1]),  # 膝が足首より前に出ている距離
    }


def arm(j: dict, side: str) -> dict:
    sh, el, wr = j[side + "UpperArm"], j[side + "LowerArm"], j[side + "Hand"]
    upper, fore = el - sh, wr - el
    down = np.array([0.0, 0.0, -1.0])
    return {
        "elbow": _angle(upper, fore),          # 肘の屈曲（0 = 伸び切り）
        "lift": _angle(upper, down),           # 上腕が鉛直下向きから離れる角度（脇の開き）
        "swing": _front(upper),                # 上腕の前後の振り（前が正）
        "elbow_back": float(el[1] - sh[1]),     # 肘が肩関節より後ろにある距離
        "elbow_up": float(el[2] - sh[2]),       # 肘の高さ（肩関節から、下が負）
        "wrist_z": float(wr[2]),
        "fore_rise": math.degrees(math.atan2(fore[2], math.hypot(fore[0], fore[1]))),
    }


def pelvis_z(cid: str, kind: str, ph: float) -> float:
    return float(joints(cid, kind, ph)["Hips"][2])


def trunk_lean(j: dict) -> float:
    """骨盤から首の付け根までの線の前傾（度）。"""
    v = j["Neck"] - j["Hips"]
    return math.degrees(math.atan2(-v[1], v[2]))
