"""アニメーション（Action）生成。

DESIGN.md §2.1 の 6 本: Idle / Walk / Run / Jump / Wave / Talk。
ポーズはワールド軸まわりの回転で指定し、ボーンのレスト行列で局所回転に
変換する（ボーンのロール依存を考えなくて済む）。
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector

import bpy

ACTION_NAMES = ("Idle", "Walk", "Run", "Jump", "Wave", "Talk")

#: Action 名 -> {Shape Key 名: [(フレーム比 0..1, 値), ...]}
SHAPE_TRACKS: dict[str, dict[str, list[tuple[float, float]]]] = {
    "Wave": {"smile": [(0.0, 0.0), (0.18, 1.0), (0.82, 1.0), (1.0, 0.25)]},
    "Talk": {"mouth_a": [(0.0, 0.05), (0.18, 0.85), (0.36, 0.10),
                         (0.54, 0.75), (0.72, 0.10), (1.0, 0.05)],
             "mouth_i": [(0.0, 0.0), (0.27, 0.70), (0.45, 0.05),
                         (0.63, 0.60), (0.90, 0.05), (1.0, 0.0)]},
    "Idle": {"blink_L": [(0.0, 0.0), (0.60, 0.0), (0.64, 1.0), (0.68, 0.0),
                         (1.0, 0.0)],
             "blink_R": [(0.0, 0.0), (0.60, 0.0), (0.64, 1.0), (0.68, 0.0),
                         (1.0, 0.0)]},
}


_AXIS = {"X": Vector((1.0, 0.0, 0.0)),
         "Y": Vector((0.0, 1.0, 0.0)),
         "Z": Vector((0.0, 0.0, 1.0))}


def _world_rot(pbone, ops) -> Matrix:
    R = Matrix.Identity(3)
    for axis, ang in ops:
        R = Matrix.Rotation(ang, 3, _AXIS[axis]) @ R
    Mb = pbone.bone.matrix_local.to_3x3()
    return Mb.inverted() @ R @ Mb


def _reset(arm) -> None:
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def _apply(arm, spec: dict) -> list:
    """spec: {bone: [(axis, rad), ...]} と特別キー 'root' (ワールド移動)。"""
    touched = []
    for name, ops in spec.items():
        if name == "root":
            pb = arm.pose.bones.get("Hips")
            if pb is None:
                continue
            Mb = pb.bone.matrix_local.to_3x3()
            pb.location = Mb.inverted() @ Vector(ops)
            touched.append((pb, "location"))
            continue
        pb = arm.pose.bones.get(name)
        if pb is None:
            continue
        pb.rotation_euler = _world_rot(pb, ops).to_euler("XYZ")
        touched.append((pb, "rotation_euler"))
    return touched


def make_action(arm, name: str, nframes: int, poser, *, loop: bool = True,
                keys: int = 9):
    """poser(t) -> spec。t は 0..1。loop=True なら最終フレームで先頭に戻る。"""
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = None
    _reset(arm)

    total = nframes + 1 if loop else nframes
    frames = [1 + round(i * (total - 1) / (keys - 1)) for i in range(keys)]
    # 表情トラックの折れ点は必ずサンプルする（瞬きのような短い山を潰さない）
    tracks = SHAPE_TRACKS.get(name, {})
    span = total - 1
    # 折れ点はフレームに丸めた時点の値を正とする（丸め誤差で山が削れないように）
    exact = {k: {1 + round(tt * span): v for tt, v in pts}
             for k, pts in tracks.items()}
    for pts in tracks.values():
        frames += [1 + round(tt * span) for tt, _ in pts]
    frames = sorted({min(max(1, f), total) for f in frames})
    face_bone = arm.pose.bones.get(SHAPE_DRIVER_BONE)
    props = [f"sk_{k}" for k in shape_prop_names()
             if face_bone is not None and f"sk_{k}" in face_bone]
    act = None
    for f in frames:
        t = (f - 1) / nframes if loop else (f - 1) / max(1, nframes - 1)
        _reset(arm)
        touched = _apply(arm, poser(t % 1.0 if loop else t))
        bpy.context.scene.frame_set(f)
        for pb, path in touched:
            pb.keyframe_insert(data_path=path, frame=f)
        for prop in props:
            key = prop[3:]
            pts = tracks.get(key)
            if not pts:
                face_bone[prop] = 0.0
            elif f in exact[key]:
                face_bone[prop] = float(exact[key][f])
            else:
                face_bone[prop] = _track_value(pts, (f - 1) / span)
            face_bone.keyframe_insert(data_path=f'["{prop}"]', frame=f)
        if act is None:
            act = arm.animation_data.action
    if act is None:
        act = bpy.data.actions.new(name)
        arm.animation_data.action = act
    act.name = name
    act.use_fake_user = True
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
    arm.animation_data.action = None
    _reset(arm)
    return act


def _fcurves(act):
    try:
        return list(act.fcurves)
    except Exception:  # noqa: BLE001
        out = []
        for layer in getattr(act, "layers", []):
            for strip in layer.strips:
                for cb in getattr(strip, "channelbags", []):
                    out.extend(cb.fcurves)
        return out


# --------------------------------------------------------------------------
# 各モーション
# --------------------------------------------------------------------------

D = math.radians


#: レストポーズは A ポーズ（上腕が鉛直から 38° 開く）。動作中は腕を体側に下ろした姿勢を
#: 基準にしたいので、全 Action の上腕にこの分の内転（ワールド Y 軸回り）を先に入れる。
#: 残り約 12° が「気をつけ」で自然に見える開き。
ARM_DROP = 26.0


def _arms_down(spec: dict, drop: float = ARM_DROP) -> dict:
    """上腕の回転リストの先頭に内転を足す（左は +Y、右は -Y が「下ろす」向き）。"""
    out = dict(spec)
    for bone, sgn in (("LeftUpperArm", 1.0), ("RightUpperArm", -1.0)):
        out[bone] = [("Y", D(sgn * drop))] + list(spec.get(bone, []))
    return out


def _hair(lag: float, amp: float, t: float, *, freq: float = 1.0):
    """髪の揺れ。頭の動きから位相を遅らせて追従させる。"""
    ph = 2 * math.pi * (freq * t) - lag
    return {
        "HairFront": [("X", D(amp * 0.55) * math.sin(ph)),
                      ("Y", D(amp * 0.40) * math.cos(ph * 0.5))],
        "HairBack": [("X", D(-amp) * math.sin(ph)),
                     ("Y", D(amp * 0.60) * math.cos(ph * 0.5))],
    }


def _idle(t):
    s = math.sin(2 * math.pi * t)
    c = math.cos(2 * math.pi * t)
    br = math.sin(4 * math.pi * t)
    return {
        **_hair(0.85, 3.2, t),
        "root": (0.0, 0.0, 0.004 * br),
        "Hips": [("Y", D(1.2) * s)],
        "Spine": [("X", D(-1.4) * br), ("Z", D(1.0) * s)],
        "Chest": [("X", D(-1.0) * br)],
        "UpperChest": [("X", D(1.8) * br)],
        "Neck": [("X", D(-1.2) * br)],
        "Head": [("X", D(1.6) * br), ("Z", D(2.2) * s), ("Y", D(-1.5) * c)],
        "LeftUpperArm": [("X", D(-2.0) * s), ("Y", D(-2.5) - D(1.5) * br)],
        "RightUpperArm": [("X", D(2.0) * s), ("Y", D(2.5) + D(1.5) * br)],
        "LeftLowerArm": [("X", D(-6.0) - D(2.0) * br)],
        "RightLowerArm": [("X", D(-6.0) - D(2.0) * br)],
    }


def _gait(t, *, leg=26.0, knee=36.0, arm=26.0, bob=0.013, lean=0.0,
          elbow=16.0, foot=18.0, twist=9.0, hair=5.0):
    ph = 2 * math.pi * t
    s, c = math.sin(ph), math.cos(ph)
    knee_l = knee * max(0.0, math.sin(ph - 0.9))
    knee_r = knee * max(0.0, math.sin(ph + math.pi - 0.9))
    return {
        **_hair(1.05, hair, t, freq=2.0),
        "root": (0.0, 0.0, bob * abs(math.sin(2 * ph)) - bob * 0.35),
        # 腰のひねり（Z）＋左右の傾き（Y）
        "Hips": [("Z", D(twist) * s), ("Y", D(4.0) * c)],
        "Spine": [("X", D(lean)), ("Z", D(-twist * 0.75) * s)],
        "Chest": [("X", D(lean * 0.4))],
        "UpperChest": [("Z", D(-twist * 0.55) * s)],
        "Neck": [("X", D(-lean * 0.6))],
        "Head": [("X", D(-lean * 0.5) + D(2.0) * abs(c))],
        "LeftUpperLeg": [("X", D(-leg) * s)],
        "RightUpperLeg": [("X", D(leg) * s)],
        "LeftLowerLeg": [("X", D(knee_l))],
        "RightLowerLeg": [("X", D(knee_r))],
        "LeftFoot": [("X", D(foot) * math.sin(ph + 1.2))],
        "RightFoot": [("X", D(foot) * math.sin(ph + math.pi + 1.2))],
        "LeftUpperArm": [("X", D(arm) * s), ("Y", D(-4.0))],
        "RightUpperArm": [("X", D(-arm) * s), ("Y", D(4.0))],
        "LeftLowerArm": [("X", D(-elbow) - D(elbow * 0.6) * s)],
        "RightLowerArm": [("X", D(-elbow) + D(elbow * 0.6) * s)],
    }


def _walk(t):
    return _gait(t)


def _run(t):
    return _gait(t, leg=44.0, knee=78.0, arm=48.0, bob=0.030, lean=-14.0,
                 elbow=62.0, foot=26.0, twist=13.0, hair=9.0)


def _jump(t):
    # 0.0 沈み込み → 0.35 踏み切り → 0.62 空中 → 1.0 着地
    if t < 0.28:
        k = t / 0.28
        crouch, up, arm = 0.9 * k, 0.0, -20.0 * k
    elif t < 0.45:
        k = (t - 0.28) / 0.17
        crouch, up, arm = 0.9 * (1 - k), 0.20 * k, -20.0 + 150.0 * k
    elif t < 0.72:
        k = (t - 0.45) / 0.27
        crouch, up, arm = 0.0, 0.20 + 0.10 * math.sin(math.pi * k), 130.0
    else:
        k = (t - 0.72) / 0.28
        crouch, up, arm = 0.75 * math.sin(math.pi * k), 0.20 * (1 - k), 130.0 * (1 - k) + 10.0
    h = 0.10
    return {
        "HairFront": [("X", D(-16.0) * (up * 4.0 - crouch))],
        "HairBack": [("X", D(22.0) * (up * 4.0 - crouch))],
        "root": (0.0, 0.0, up * 1.0 - crouch * h),
        "Hips": [("X", D(6.0) * crouch)],
        "Spine": [("X", D(-16.0) * crouch)],
        "Chest": [("X", D(-6.0) * crouch)],
        "Head": [("X", D(8.0) * crouch)],
        "LeftUpperLeg": [("X", D(52.0) * crouch)],
        "RightUpperLeg": [("X", D(52.0) * crouch)],
        "LeftLowerLeg": [("X", D(72.0) * crouch)],
        "RightLowerLeg": [("X", D(72.0) * crouch)],
        "LeftFoot": [("X", D(-26.0) * crouch)],
        "RightFoot": [("X", D(-26.0) * crouch)],
        "LeftUpperArm": [("Y", D(-abs(arm) * 0.8)), ("X", D(-arm * 0.3))],
        "RightUpperArm": [("Y", D(abs(arm) * 0.8)), ("X", D(-arm * 0.3))],
        "LeftLowerArm": [("X", D(-18.0))],
        "RightLowerArm": [("X", D(-18.0))],
    }


def _wave(t):
    swing = math.sin(2 * math.pi * 3.0 * t)
    ramp = min(1.0, t / 0.18) * min(1.0, (1.0 - t) / 0.18 + 0.0 if t > 0.82 else 1.0)
    ramp = max(0.0, min(1.0, ramp))
    return {
        **_hair(0.7, 3.6 * ramp, t, freq=3.0),
        "Hips": [("Y", D(1.5) * swing * ramp)],
        "Spine": [("Z", D(3.0) * ramp)],
        "Head": [("Z", D(5.0) * ramp), ("Y", D(-4.0) * ramp),
                 ("X", D(-3.0) * ramp)],
        "LeftUpperArm": [("Y", D(-(104.0 + ARM_DROP)) * ramp), ("X", D(-8.0) * ramp)],
        "LeftLowerArm": [("Y", D(-26.0) * ramp), ("X", D(-24.0) * ramp * (0.5 + 0.5 * swing))],
        "LeftHand": [("Y", D(-18.0) * swing * ramp)],
        "RightUpperArm": [("Y", D(3.0)), ("X", D(4.0) * ramp)],
        "RightLowerArm": [("X", D(-14.0))],
    }


def _talk(t):
    ph = 2 * math.pi * t
    nod = math.sin(2.0 * ph)
    sway = math.sin(ph)
    g = 0.5 + 0.5 * math.sin(3.0 * ph)
    return {
        **_hair(0.9, 2.6, t, freq=2.0),
        "root": (0.0, 0.0, 0.003 * nod),
        "Hips": [("Y", D(1.0) * sway)],
        "Spine": [("Z", D(2.0) * sway)],
        "Chest": [("X", D(-1.5) * nod)],
        "Neck": [("X", D(3.0) * nod)],
        "Head": [("X", D(6.0) * nod), ("Z", D(4.0) * sway), ("Y", D(-3.0) * sway)],
        "LeftUpperArm": [("Y", D(-12.0) - D(8.0) * g), ("X", D(-10.0) * g)],
        "RightUpperArm": [("Y", D(10.0) + D(6.0) * (1.0 - g)), ("X", D(-8.0) * (1.0 - g))],
        "LeftLowerArm": [("X", D(-46.0) - D(18.0) * g)],
        "RightLowerArm": [("X", D(-40.0) - D(16.0) * (1.0 - g))],
        "LeftHand": [("X", D(-10.0) * g)],
        "RightHand": [("X", D(-10.0) * (1.0 - g))],
    }


def _posed(fn):
    """poser に腕下ろしを合成する。"""
    return lambda t: _arms_down(fn(t))


SPECS = (
    ("Idle", 60, _posed(_idle), True, 9),
    ("Walk", 30, _posed(_walk), True, 13),
    ("Run", 20, _posed(_run), True, 13),
    ("Jump", 30, _posed(_jump), False, 13),
    ("Wave", 40, _posed(_wave), False, 17),
    ("Talk", 60, _posed(_talk), True, 13),
)


#: Shape Key を駆動するカスタムプロパティを置くボーン
SHAPE_DRIVER_BONE = "Head"


def shape_prop_names() -> list[str]:
    out: list[str] = []
    for tracks in SHAPE_TRACKS.values():
        for k in tracks:
            if k not in out:
                out.append(k)
    return out


def _track_value(pts, t: float) -> float:
    """(比, 値) の折れ線を線形補間する。"""
    if t <= pts[0][0]:
        return float(pts[0][1])
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            w = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
            return float(v0 + (v1 - v0) * w)
    return float(pts[-1][1])


def setup_shape_drivers(obj, arm) -> list[str]:
    """Shape Key を Head ボーンのカスタムプロパティから駆動させる。

    FBX エクスポータは Action ごとにシーンを評価してから焼くので、ドライバに
    しておくとアーマチュアの Action（Wave 等）と同じ AnimStack に表情の
    カーブが入る。Shape Key は別データブロックなので、素の Action のままでは
    同じテイクに同居できない。
    """
    sk = getattr(obj.data, "shape_keys", None)
    if sk is None:
        return []
    pb = arm.pose.bones.get(SHAPE_DRIVER_BONE)
    if pb is None:
        return []
    made: list[str] = []
    for key in shape_prop_names():
        kb = sk.key_blocks.get(key)
        if kb is None:
            continue
        prop = f"sk_{key}"
        pb[prop] = 0.0
        try:
            fc = kb.driver_add("value")
        except Exception:  # noqa: BLE001
            continue
        drv = fc.driver
        drv.type = "AVERAGE"
        for v in list(drv.variables):
            drv.variables.remove(v)
        var = drv.variables.new()
        var.name = "v"
        var.type = "SINGLE_PROP"
        var.targets[0].id = arm
        var.targets[0].data_path = (
            f'pose.bones["{SHAPE_DRIVER_BONE}"]["{prop}"]')
        made.append(key)
    return made


def build_actions(arm) -> list[str]:
    names = []
    for name, nf, fn, loop, keys in SPECS:
        act = make_action(arm, name, nf, fn, loop=loop, keys=keys)
        names.append(act.name)
    return names
