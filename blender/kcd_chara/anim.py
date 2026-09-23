"""アニメーション（Action）生成。

DESIGN.md §2.1 の 6 本: Idle / Walk / Run / Jump / Wave / Talk。
ポーズはワールド軸まわりの回転で指定し、ボーンのレスト行列で局所回転に
変換する（ボーンのロール依存を考えなくて済む）。
歩き・走りだけは足首のワールド軌道を先に決めて 2 リンクの逆運動学で脚を解く。
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


def _rot(axis: str, ang: float) -> Matrix:
    return Matrix.Rotation(ang, 3, _AXIS[axis])


def _world_rot(pbone, ops) -> Matrix:
    """ops（[(軸, 角), ...] か 3x3 行列）をボーンの局所回転に直す。"""
    if isinstance(ops, Matrix):
        R = ops.to_3x3()
    else:
        R = Matrix.Identity(3)
        for axis, ang in ops:
            R = _rot(axis, ang) @ R
    Mb = pbone.bone.matrix_local.to_3x3()
    return Mb.inverted() @ R @ Mb


def _reset(arm) -> None:
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def _apply(arm, spec: dict, prev: dict | None = None) -> list:
    """spec: {bone: [(axis, rad), ...] か 3x3 行列} と特別キー 'root'（ワールド移動）。

    prev を渡すと、前のキーのオイラー角に近い表現を選ぶ（±180 度の飛びを防ぐ）。
    """
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
        loc = _world_rot(pb, ops)
        old = None if prev is None else prev.get(name)
        eul = loc.to_euler("XYZ", old) if old is not None else loc.to_euler("XYZ")
        pb.rotation_euler = eul
        if prev is not None:
            prev[name] = eul.copy()
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
    prev: dict = {}
    for f in frames:
        t = (f - 1) / nframes if loop else (f - 1) / max(1, nframes - 1)
        # frame_set は既に打ったキーで姿勢を上書きするので、姿勢を作る前に動かす。
        # 後に置くと 2 キー目以降が全部 1 キー目の姿勢になる。
        bpy.context.scene.frame_set(f)
        _reset(arm)
        touched = _apply(arm, poser(t % 1.0 if loop else t), prev)
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
        ops = spec.get(bone, [])
        if isinstance(ops, Matrix):
            continue
        out[bone] = [("Y", D(sgn * drop))] + list(ops)
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


# --------------------------------------------------------------------------
# 歩き・走り・ジャンプ（脚は逆運動学で解く）
# --------------------------------------------------------------------------

#: Blender の既定 fps。クリップの長さと前進速度の対応に使う。
FPS = 24.0

#: ゲーム側のブレンドツリーの閾値（PlayerController.DefaultWalkSpeed / AnimatorFactory.RunSpeed）。
#: クリップ自体の前進速度をここへ合わせておくと、その速さで動くあいだ足が滑らない。
#: duty = 接地している割合、reach = 接地中に足が進む距離 / 脚長、nf = フレーム数の下限と上限。
GAIT_TARGET = {
    "Walk": dict(v=2.6, duty=0.45, reach=0.95, nf=(10, 22)),
    "Run": dict(v=5.4, duty=0.32, reach=1.02, nf=(8, 18)),
}


def _ease(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return u * u * (3.0 - 2.0 * u)


def _hermite(p0, p1, m0, m1, u):
    """端の傾きを指定した 3 次補間。"""
    u2 = u * u
    u3 = u2 * u
    return ((2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0
            + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1)


def _key(pts, t: float):
    """(位置, 値) の並びを ease で補間する。"""
    if t <= pts[0][0]:
        return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            w = 0.0 if t1 <= t0 else _ease((t - t0) / (t1 - t0))
            return v0 + (v1 - v0) * w
    return pts[-1][1]


class _Legs:
    """アーマチュアのレストポーズから脚の寸法を拾い、逆運動学で脚を解く。"""

    def __init__(self, arm):
        b = arm.data.bones

        def head(name):
            return Vector(b[name].head_local)

        hips = head("Hips")
        hip = head("LeftUpperLeg")
        knee = head("LeftLowerLeg")
        ankle = head("LeftFoot")
        ball = head("LeftToes")
        self.hips_z = float(hips.z)
        self.hip_x = float(hip.x)
        self.hip_z = float(hip.z)
        self.thigh = float(hip.z - knee.z)
        self.shank = float(knee.z - ankle.z)
        self.leg = self.thigh + self.shank
        self.ank_z = float(ankle.z)
        self.ank_x = float(ankle.x)
        # 接地点: 母指球（Toes ボーンの根元）と、その 0.44 倍うしろのかかと
        self.ball = float(abs(ball.y - ankle.y))
        self.heel = self.ball * 0.44
        self.tip = self.ball + float(abs(b["LeftToes"].tail_local.y - ball.y))

    # -- 逆運動学 ---------------------------------------------------------
    def _ik(self, d: Vector):
        """骨盤ローカルの（股関節 → 足首）ベクトルから (外転, 前後, 膝) を返す。"""
        t, s = self.thigh, self.shank
        r = d.length
        hi = 0.995 * self.leg
        lo = max(abs(t - s) + 0.02 * self.leg, 0.40 * self.leg)
        if r > hi or r < lo:
            r2 = min(hi, max(lo, r))
            d = d * (r2 / max(r, 1e-9))
            r = r2
        ck = (r * r - t * t - s * s) / (2.0 * t * s)
        knee = math.acos(max(-1.0, min(1.0, ck)))   # 0 = 伸び切り
        vy = s * math.sin(knee)
        vz = -(t + s * math.cos(knee))
        ab = math.asin(max(-1.0, min(1.0, d.x / vz)))
        fb = math.atan2(d.y, -d.z) - math.atan2(vy, -vz * math.cos(ab))
        return ab, fb, knee

    def solve_leg(self, spec: dict, side: str, sgn: float, Rp: Matrix,
                  hip_w: Vector, ank_w: Vector, pitch: float) -> None:
        """足首のワールド位置と足のピッチから、脚 3 本の回転を spec に入れる。"""
        d = Rp.inverted() @ (ank_w - hip_w)
        d.x *= sgn                                   # 右脚は左右反転して解く
        ab, fb, knee = self._ik(d)
        spec[side + "UpperLeg"] = [("Y", sgn * ab), ("X", fb)]
        spec[side + "LowerLeg"] = [("X", knee)]
        shank = (Rp @ _rot("X", fb) @ _rot("Y", sgn * ab) @ _rot("X", knee))
        spec[side + "Foot"] = shank.inverted() @ _rot("X", pitch)

    def hip_world(self, off: Vector, Rp: Matrix, sgn: float) -> Vector:
        base = Vector((0.0, 0.0, self.hips_z))
        rest = Vector((sgn * self.hip_x, 0.0, self.hip_z))
        return base + off + Rp @ (rest - base)

    def torso(self, spec: dict, Rp: Matrix, yaw: float, roll: float,
              lean: float, thx: float, pitch_t: float, keep: float = 0.18):
        """胸郭を骨盤と逆へ回し、首と頭で頭部のヨー・ロールを打ち消す。"""
        yaw_s = -(1.0 + thx) * yaw          # 背骨 3 本で分担する合計
        roll_s = -0.85 * roll
        W = Rp
        for name, f in (("Spine", 0.45), ("Chest", 0.30), ("UpperChest", 0.25)):
            spec[name] = [("Y", roll_s * f), ("Z", yaw_s * f), ("X", lean * f)]
            W = W @ (_rot("X", lean * f) @ _rot("Z", yaw_s * f)
                     @ _rot("Y", roll_s * f))
        e = W.to_euler("ZYX")
        tgt = (_rot("Z", e.z * keep) @ _rot("Y", e.y * keep)
               @ _rot("X", pitch_t))
        rest = W.inverted() @ tgt
        axis, ang = rest.to_quaternion().to_axis_angle()
        neck = Matrix.Rotation(ang * 0.5, 3, axis)
        spec["Neck"] = neck
        spec["Head"] = neck.inverted() @ rest


class Gait(_Legs):
    """1 周期ぶんの歩き／走り。位相 0 = 左足の接地。

    接地中の足はワールドに貼り付いたまま後ろへ流れ、かかと → 足裏 → 母指球と
    接地点が移る（ロッカー）。前進速度がゲーム側のブレンドツリーの閾値と一致する
    ようにフレーム数を決めるので、等速で移動しているあいだ足は滑らない。
    """

    def __init__(self, arm, kind: str = "Walk"):
        super().__init__(arm)
        g = GAIT_TARGET[kind]
        run = kind == "Run"
        self.kind = kind

        # --- 周期・歩幅・接地率 -------------------------------------------
        self.v = g["v"]
        nf = round(FPS * g["reach"] * self.leg / (g["duty"] * self.v))
        self.nframes = int(min(g["nf"][1], max(g["nf"][0], nf)))
        self.cycle = self.nframes / FPS
        self.stride = self.v * self.cycle
        self.duty = min(g["duty"], g["reach"] * self.leg / self.stride)
        self.exc = self.duty * self.stride          # 接地中に足が進む距離
        # 上下動が最大になる位相: 両脚支持なら立脚中期、滞空があれば滞空中期
        self.hi = (self.duty * 0.5 if self.duty >= 0.5
                   else (self.duty + 0.5) * 0.5)

        # --- 振れ幅（片振幅） ---------------------------------------------
        # 上下動は振幅を決め打ちせず、接地中の脚が届く高さから逆算する（_fit）。
        # 立脚中期に膝をどれだけ曲げるかだけを与える: 歩きは伸ばし気味、走りは沈める。
        self.k_end = 0.985
        self.k_mid = 0.870 if run else 0.950
        # 膝がいちばん曲がる接地位相と、伸び切る位相（歩きは早めに曲げて蹴り出しで伸ばす）
        u_peak, self.k_span = (0.45, 0.92) if run else (0.28, 0.78)
        self.k_skew = math.log(0.5) / math.log(u_peak / self.k_span)
        self.lat = self.leg * (0.014 if run else 0.024)
        self.yaw = D(6.5 if run else 4.5)           # 骨盤の水平回旋
        self.obl = D(5.0 if run else 4.0)           # 骨盤の傾斜
        self.thx = 0.95 if run else 0.70            # 胸郭の逆回旋の比
        self.lean = D(11.0 if run else 4.0)         # 前傾
        # 肩の前後振り（片振幅）。走りの肩関節 ROM は実測で全振幅 50〜65 度なので、
        # ±33 度（全振幅 66 度）に収める。以前は ±52 度（全振幅 103 度）で、
        # 短距離走でもやらない大振りになっていた（#43「腕を振りすぎ」）。
        self.arm = D(33.0 if run else 26.0)
        # 肘の屈曲 = e0 + e1 * (0.5 - 0.5f)。f=+1 が腕を後ろ、f=-1 が前に振った
        # ところなので、前で深く曲がり後ろで伸びる（ランニングのセオリーどおり）。
        # 走りは指定値 72〜88 度（以前は 66〜100 度で肘まで大きく煽っていた）。
        # ここは上腕からの相対角の「指定値」で、A ポーズの肘の開きと外転のぶん
        # 浅くなる。書き出した FBX で実測すると曲がりは 60〜72 度（内角 108〜120
        # 度）で、走りの定説（約 90 度）よりは伸ばし気味。
        self.elbow = (D(72.0), D(16.0)) if run else (D(14.0), D(24.0))
        self.clear = self.leg * (0.20 if run else 0.12)
        self.hair = 9.0 if run else 5.0
        self.p_heel = D(-6.0 if run else -12.0)     # 接地の瞬間（つま先上げ）
        self.p_toe = D(38.0 if run else 30.0)       # 蹴り出し（かかと上げ）
        self.roll_in = 0.10 if run else 0.18        # 足裏が着くまで
        self.roll_out = 0.50 if run else 0.62       # かかとが離れるまで
        self.foot_x = self.ank_x * (0.35 if run else 0.55)
        self.y0 = self.heel - 0.42 * self.exc       # 接地時のかかとの前後位置

        self.zz = [0.0] * 48                        # 骨盤の高さ（_fit が埋める）
        self.sink = 0.0
        self._fit()

    # -- 骨盤 -------------------------------------------------------------
    def _bob(self, ph: float) -> float:
        n = len(self.zz)
        x = (ph % 1.0) * n
        i = int(math.floor(x))
        f = x - i
        return self.zz[i % n] * (1.0 - f) + self.zz[(i + 1) % n] * f

    def pelvis(self, ph: float):
        """骨盤のワールド移動と回転（ヨー、ロール）。"""
        w = 2 * math.pi
        x = self.lat * math.sin(w * (ph - 0.06))            # 支持脚の側へ寄る
        yaw = -self.yaw * math.cos(w * (ph - 0.03))         # 遊脚側が前
        roll = -self.obl * math.sin(w * (ph - 0.05))        # 遊脚側が下がる
        return Vector((x, 0.0, self._bob(ph))), yaw, roll

    # -- 足首の軌道 -------------------------------------------------------
    def _pitch_stance(self, u: float) -> float:
        if u < self.roll_in:
            return self.p_heel * (1.0 - _ease(u / self.roll_in))
        if u < self.roll_out:
            return 0.0
        return self.p_toe * _ease((u - self.roll_out) / (1.0 - self.roll_out))

    def _stance(self, u: float):
        """接地中。接地点をワールドに固定したまま足を回す（＝滑らない）。"""
        pitch = self._pitch_stance(u)
        if u < self.roll_out:
            piv = Vector((0.0, self.heel, -self.ank_z))
            y0 = self.y0
        else:
            piv = Vector((0.0, -self.ball, -self.ank_z))
            y0 = self.y0 - self.heel - self.ball
        ground = Vector((self.foot_x, y0 + self.exc * u, 0.0))
        return ground - _rot("X", pitch) @ piv, pitch

    def _swing(self, u: float):
        """遊脚。両端の速度を接地中とそろえて滑らかにつなぐ。"""
        a0, _ = self._stance(1.0)
        a1, _ = self._stance(0.0)
        m = self.exc * (1.0 - self.duty) / self.duty
        y = _hermite(a0.y, a1.y, m, m, u)
        z = (_hermite(a0.z, a1.z, 0.0, 0.0, u)
             + self.clear * math.sin(math.pi * u) ** 1.2)
        p0, p1 = self.p_toe, self.p_heel
        mid = D(-9.0)
        if u < 0.35:
            pitch = p0 + (mid - p0) * _ease(u / 0.35)
        else:
            pitch = mid + (p1 - mid) * _ease((u - 0.35) / 0.65)
        return Vector((self.foot_x, y, z)), pitch

    def ankle(self, ph: float):
        """足首のワールド位置（左脚）と足のピッチ。"""
        s = ph % 1.0
        if s < self.duty:
            return self._stance(s / self.duty)
        return self._swing((s - self.duty) / (1.0 - self.duty))

    # -- 骨盤の高さ -------------------------------------------------------
    def _limit(self, u: float) -> float:
        """接地位相 u での「股関節 - 足首」の長さ（脚をどれだけ伸ばすか）。

        接地直後に膝を曲げて衝撃を吸収し、蹴り出しまでに伸ばし切る。左右非対称
        なのは実際の歩行と同じで、これを対称にすると立脚後半に骨盤が落ちて
        上下動が 1 歩に 2 山（＝1 ストライドに 4 山）出てしまう。
        """
        if u >= self.k_span:
            return self.k_end * self.leg
        g = (u / self.k_span) ** self.k_skew
        f = 0.5 * (1.0 - math.cos(2.0 * math.pi * g))
        return (self.k_end - (self.k_end - self.k_mid) * f) * self.leg

    def _fit(self) -> None:
        """骨盤の高さを接地中の脚が届く位置に合わせる。

        上下動はここで決まる。接地の瞬間は脚が前後に開くぶん低く、立脚中期は
        高くなる（歩き）。走りは立脚中期に膝を曲げて沈み、滞空で放物線を描いて
        持ち上がる。どちらも 1 ストライドに 2 周期で、文献の重心軌道と同じ形。
        """
        n = len(self.zz)
        for _ in range(16):
            delta: list[float | None] = [None] * n
            for i in range(n):
                ph = i / n
                off, yaw, roll = self.pelvis(ph)
                Rp = _rot("Z", yaw) @ _rot("Y", roll)
                for sgn, pho in ((1.0, 0.0), (-1.0, 0.5)):
                    s = (ph + pho) % 1.0
                    if s >= self.duty:
                        continue
                    ank, _ = self.ankle(ph + pho)
                    ank = Vector((sgn * ank.x, ank.y, ank.z))
                    r = (ank - self.hip_world(off, Rp, sgn)).length
                    e = self._limit(s / self.duty) - r
                    delta[i] = e if delta[i] is None else min(delta[i], e)
            self._raise(delta, 0.8)
        self.sink = -sum(self.zz) / n

    def _raise(self, delta, damp: float) -> None:
        n = len(self.zz)
        z = [v + (d * damp if d is not None else 0.0)
             for v, d in zip(self.zz, delta)]
        for i, d in enumerate(delta):
            if d is not None:
                continue
            a, b = i, i
            while delta[a % n] is None:
                a -= 1
            while delta[b % n] is None:
                b += 1
            w = (i - a) / (b - a)
            tf = (b - a) / n * self.cycle
            # 滞空中は放物線（重力）で持ち上がる
            z[i] = (z[a % n] * (1.0 - w) + z[b % n] * w
                    + 9.8 * tf * tf / 8.0 * 4.0 * w * (1.0 - w))
        self.zz = [(z[(i - 1) % n] + 2.0 * z[i] + z[(i + 1) % n]) * 0.25
                   for i in range(n)]

    # -- ポーズ -----------------------------------------------------------
    def pose(self, t: float) -> dict:
        w = 2 * math.pi
        ph = t % 1.0
        off, yaw, roll = self.pelvis(ph)
        Rp = _rot("Z", yaw) @ _rot("Y", roll)
        spec = {
            **_hair(1.05, self.hair, t, freq=2.0),
            "root": tuple(off),
            "Hips": [("Y", roll), ("Z", yaw)],
        }
        for side, sgn, pho in (("Left", 1.0, 0.0), ("Right", -1.0, 0.5)):
            ank, pitch = self.ankle(ph + pho)
            self.solve_leg(spec, side, sgn, Rp,
                           self.hip_world(off, Rp, sgn),
                           Vector((sgn * ank.x, ank.y, ank.z)), pitch)
        pitch_t = self.lean * 0.25 + D(1.2) * math.cos(2 * w * (ph - self.hi))
        self.torso(spec, Rp, yaw, roll, self.lean, self.thx, pitch_t)
        # 腕は脚と逆位相（左脚が前に出る接地で左腕は後ろ）
        e0, e1 = self.elbow
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            f = math.cos(w * (ph - 0.02)) * sgn
            spec[side + "UpperArm"] = [("Y", D(-4.0) * sgn), ("X", self.arm * f)]
            spec[side + "LowerArm"] = [("X", -(e0 + e1 * (0.5 - 0.5 * f)))]
        return spec


class Jump(_Legs):
    """踏み切り → 空中 → 着地。腕は胸の高さまでしか上げない（Y 字にしない）。"""

    nframes = 18

    #: (t, 値) の折れ線。長さは脚長に対する比、角度は度。
    DZ = ((0.0, -0.03), (0.06, -0.09), (0.17, -0.01), (0.28, 0.02),
          (0.45, 0.02), (0.62, 0.01), (0.76, -0.02), (0.88, -0.14), (1.0, -0.04))
    DY = ((0.0, 0.02), (0.06, 0.05), (0.17, 0.01), (0.28, 0.0),
          (0.62, 0.0), (0.76, 0.01), (0.88, 0.05), (1.0, 0.02))
    LEAN = ((0.0, 6.0), (0.06, 15.0), (0.17, 8.0), (0.28, 5.0), (0.45, 7.0),
            (0.62, 9.0), (0.76, 11.0), (0.88, 17.0), (1.0, 7.0))
    LY = ((0.0, 0.0), (0.17, -0.02), (0.28, -0.22), (0.45, -0.32),
          (0.62, -0.30), (0.76, -0.16), (0.88, -0.04), (1.0, 0.0))
    LZ = ((0.0, 0.0), (0.17, 0.06), (0.28, 0.30), (0.45, 0.44),
          (0.62, 0.40), (0.76, 0.14), (0.88, 0.0), (1.0, 0.0))
    RY = ((0.0, 0.0), (0.17, 0.02), (0.28, 0.22), (0.45, 0.32),
          (0.62, 0.30), (0.76, 0.14), (0.88, 0.04), (1.0, 0.0))
    RZ = ((0.0, 0.0), (0.17, 0.06), (0.28, 0.16), (0.45, 0.26),
          (0.62, 0.22), (0.76, 0.06), (0.88, 0.0), (1.0, 0.0))
    PL = ((0.0, 0.0), (0.06, -4.0), (0.17, 26.0), (0.28, 16.0), (0.45, -8.0),
          (0.62, -12.0), (0.76, -14.0), (0.88, -4.0), (1.0, 0.0))
    PR = ((0.0, 0.0), (0.06, -4.0), (0.17, 28.0), (0.28, 22.0), (0.45, 6.0),
          (0.62, -2.0), (0.76, -10.0), (0.88, -4.0), (1.0, 0.0))
    ARM = ((0.0, 14.0), (0.06, 40.0), (0.17, 14.0), (0.28, -44.0),
           (0.45, -42.0), (0.62, -34.0), (0.76, -16.0), (0.88, 20.0), (1.0, 10.0))
    ELB = ((0.0, -20.0), (0.06, -28.0), (0.17, -36.0), (0.28, -62.0),
           (0.45, -60.0), (0.62, -54.0), (0.76, -46.0), (0.88, -56.0),
           (1.0, -30.0))
    ABD = ((0.0, 2.0), (0.06, 5.0), (0.17, 7.0), (0.28, 11.0), (0.45, 14.0),
           (0.62, 15.0), (0.76, 13.0), (0.88, 9.0), (1.0, 3.0))

    def pose(self, t: float) -> dict:
        L = self.leg
        dz = _key(self.DZ, t) * L
        dy = _key(self.DY, t) * L
        lean = D(_key(self.LEAN, t))
        off = Vector((0.0, dy, dz))
        Rp = _rot("X", lean * 0.25)
        spec = {
            "root": tuple(off),
            "Hips": [("X", lean * 0.25)],
        }
        legs = (("Left", 1.0, self.LY, self.LZ, self.PL),
                ("Right", -1.0, self.RY, self.RZ, self.PR))
        for side, sgn, ky, kz, kp in legs:
            ank = Vector((sgn * self.ank_x,
                          _key(ky, t) * L,
                          self.ank_z + _key(kz, t) * L))
            self.solve_leg(spec, side, sgn, Rp,
                           self.hip_world(off, Rp, sgn), ank,
                           D(_key(kp, t)))
        self.torso(spec, Rp, 0.0, 0.0, lean * 0.75, 0.0, lean * 0.2, keep=0.0)
        bend = -dz / L
        spec["HairFront"] = [("X", D(-18.0) * bend)]
        spec["HairBack"] = [("X", D(24.0) * bend)]
        # 腕は前後に振るだけ。外転は最大 15 度で、頭より上へは絶対に上げない。
        sw = D(_key(self.ARM, t))
        elb = D(_key(self.ELB, t))
        abd = D(_key(self.ABD, t))
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            spec[side + "UpperArm"] = [("Y", -abd * sgn), ("X", sw)]
            spec[side + "LowerArm"] = [("X", elb)]
        return spec


def _wave(t, drop=ARM_DROP):
    # 振る腕の Y は -(104 + drop)。_posed が先頭に足す +drop と打ち消し合い、
    # 腕をどれだけ下ろすキャラでも振る手の高さは同じ -104° になる。
    swing = math.sin(2 * math.pi * 3.0 * t)
    ramp = min(1.0, t / 0.18) * min(1.0, (1.0 - t) / 0.18 + 0.0 if t > 0.82 else 1.0)
    ramp = max(0.0, min(1.0, ramp))
    return {
        **_hair(0.7, 3.6 * ramp, t, freq=3.0),
        "Hips": [("Y", D(1.5) * swing * ramp)],
        "Spine": [("Z", D(3.0) * ramp)],
        "Head": [("Z", D(5.0) * ramp), ("Y", D(-4.0) * ramp),
                 ("X", D(-3.0) * ramp)],
        "LeftUpperArm": [("Y", D(-(104.0 + drop)) * ramp), ("X", D(-8.0) * ramp)],
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


def _posed(fn, drop=ARM_DROP):
    """poser に腕下ろしを合成する。"""
    return lambda t: _arms_down(fn(t), drop)


def action_specs(arm, arm_drop=ARM_DROP):
    """(名前, フレーム数, poser, ループ, キー数) の一覧。

    歩き・走りの長さはキャラの脚長から決まる（速度を合わせるため人によって違う）。
    arm_drop は A ポーズから上腕を下ろす角度（params の "arm_drop"。既定 ARM_DROP）。
    """
    d = arm_drop
    walk = Gait(arm, "Walk")
    run = Gait(arm, "Run")
    jump = Jump(arm)
    return (
        ("Idle", 60, _posed(_idle, d), True, 9),
        ("Walk", walk.nframes, _posed(walk.pose, d), True, walk.nframes + 1),
        ("Run", run.nframes, _posed(run.pose, d), True, run.nframes + 1),
        ("Jump", jump.nframes, _posed(jump.pose, d), False, jump.nframes),
        ("Wave", 40, _posed(lambda t: _wave(t, d), d), False, 17),
        ("Talk", 60, _posed(_talk, d), True, 13),
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


def build_actions(arm, arm_drop=ARM_DROP) -> list[str]:
    names = []
    for name, nf, fn, loop, keys in action_specs(arm, arm_drop):
        act = make_action(arm, name, nf, fn, loop=loop, keys=keys)
        names.append(act.name)
    return names
