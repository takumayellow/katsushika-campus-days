"""アーマチュア生成とスキニング。

ボーン名は DESIGN.md §2.1 の Unity Humanoid 準拠の名前をそのまま使う。
A ポーズ（腕を水平から 52 度下げる）で組み、ウェイトは
`bpy.ops.object.parent_set(type='ARMATURE_AUTO')` を第一候補に、失敗時 or
ゼロウェイト頂点が多いときは距離ベースの自前ウェイトへ切り替える。
その後で髪・スカート・袴・小物を親ボーンへ寄せる。
"""

from __future__ import annotations

import numpy as np

import bpy

from . import body as B
from . import mesh as M

#: DESIGN.md §2.1 の階層（親 → 子）
HIERARCHY: list[tuple[str, str | None]] = [
    ("Hips", None),
    ("Spine", "Hips"),
    ("Chest", "Spine"),
    ("UpperChest", "Chest"),
    ("Neck", "UpperChest"),
    ("Head", "Neck"),
    ("LeftShoulder", "UpperChest"),
    ("LeftUpperArm", "LeftShoulder"),
    ("LeftLowerArm", "LeftUpperArm"),
    ("LeftHand", "LeftLowerArm"),
    ("RightShoulder", "UpperChest"),
    ("RightUpperArm", "RightShoulder"),
    ("RightLowerArm", "RightUpperArm"),
    ("RightHand", "RightLowerArm"),
    ("LeftUpperLeg", "Hips"),
    ("LeftLowerLeg", "LeftUpperLeg"),
    ("LeftFoot", "LeftLowerLeg"),
    ("LeftToes", "LeftFoot"),
    ("RightUpperLeg", "Hips"),
    ("RightLowerLeg", "RightUpperLeg"),
    ("RightFoot", "RightLowerLeg"),
    ("RightToes", "RightFoot"),
]

BONE_NAMES = [n for n, _ in HIERARCHY]

#: 髪の揺れ用の追加ボーン（Humanoid 22 本の外側。Unity では generic 扱い）
HAIR_HIERARCHY: list[tuple[str, str]] = [
    ("HairFront", "Head"),
    ("HairBack", "Head"),
]
HAIR_BONE_NAMES = [n for n, _ in HAIR_HIERARCHY]


def bone_positions(p: dict, a: B.Anatomy) -> dict[str, tuple[np.ndarray, np.ndarray]]:
    h = p["height"]
    z = p["z"]
    z_hips = z["crotch"] + (z["hip"] - z["crotch"]) * 0.55
    z_spine = z["waist"] - h * 0.010
    z_chest = z["underbust"] + h * 0.006
    z_uchest = z["bust"] + (z["shoulder"] - z["bust"]) * 0.42
    z_neck = z["shoulder"] + h * 0.012
    z_head = z["chin"] - h * 0.006

    def v(x, y, zz):
        return np.array([float(x), float(y), float(zz)])

    out: dict[str, tuple[np.ndarray, np.ndarray]] = {
        "Hips": (v(0, 0, z_hips), v(0, 0, z_spine)),
        "Spine": (v(0, 0, z_spine), v(0, 0, z_chest)),
        "Chest": (v(0, 0, z_chest), v(0, 0, z_uchest)),
        "UpperChest": (v(0, 0, z_uchest), v(0, 0, z_neck)),
        "Neck": (v(0, 0, z_neck), v(0, 0, z_head)),
        "Head": (v(0, 0, z_head), v(0, 0, z_head + p["head_h"] * 0.88)),
    }
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        sh = a.shoulder * np.array([sgn, 1, 1])
        el = a.elbow * np.array([sgn, 1, 1])
        wr = a.wrist * np.array([sgn, 1, 1])
        tip = a.hand_tip * np.array([sgn, 1, 1])
        out[f"{side}Shoulder"] = (v(sgn * h * 0.016, 0.0, z_uchest + h * 0.016), sh)
        out[f"{side}UpperArm"] = (sh, el)
        out[f"{side}LowerArm"] = (el, wr)
        out[f"{side}Hand"] = (wr, tip)

        hp = a.hip_joint * np.array([sgn, 1, 1])
        kn = a.knee * np.array([sgn, 1, 1])
        an = a.ankle * np.array([sgn, 1, 1])
        to = a.toe * np.array([sgn, 1, 1])
        out[f"{side}UpperLeg"] = (hp, kn)
        out[f"{side}LowerLeg"] = (kn, an)
        out[f"{side}Foot"] = (an, to)
        out[f"{side}Toes"] = (to, to + np.array([0.0, -a.foot[1] * 0.26, 0.0]))

    hh = p["head_h"]
    hd = p["head_d"]
    z_top = z["chin"] + hh * 0.86
    out["HairFront"] = (v(0.0, -hd * 0.30, z_top),
                        v(0.0, -hd * 0.46, z_top - hh * 0.70))
    out["HairBack"] = (v(0.0, hd * 0.30, z_top),
                       v(0.0, hd * 0.42, z_top - hh * 1.10))
    return out


def build_armature(p: dict, a: B.Anatomy, name: str):
    pos = bone_positions(p, a)
    arm_data = bpy.data.armatures.new(f"{name}_arm")
    arm = bpy.data.objects.new("Armature", arm_data)
    bpy.context.scene.collection.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    created = {}
    for bname, parent in HIERARCHY + HAIR_HIERARCHY:
        b = eb.new(bname)
        head, tail = pos[bname]
        b.head = tuple(head)
        b.tail = tuple(tail)
        b.roll = 0.0
        if parent is not None:
            b.parent = created[parent]
            b.use_connect = bool(np.allclose(pos[parent][1], head, atol=1e-6))
        created[bname] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm, pos


# --------------------------------------------------------------------------
# ウェイト
# --------------------------------------------------------------------------


def _seg_distance(pts: np.ndarray, p0: np.ndarray, p1: np.ndarray) -> np.ndarray:
    d = p1 - p0
    ln = float(np.dot(d, d)) + 1e-12
    t = np.clip(((pts - p0) @ d) / ln, 0.0, 1.0)
    proj = p0[None, :] + t[:, None] * d[None, :]
    return np.linalg.norm(pts - proj, axis=1)


def distance_weights(obj, arm, pos: dict, *, k: int = 3, power: float = 3.0):
    """距離ベースの自前スキニング（ARMATURE_AUTO が使えないときの保険）。"""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    pts = co.reshape(-1, 3)

    names = BONE_NAMES
    dist = np.stack([_seg_distance(pts, pos[n][0], pos[n][1]) for n in names],
                    axis=1)
    order = np.argsort(dist, axis=1)[:, :k]
    rows = np.arange(len(pts))[:, None]
    d = dist[rows, order]
    w = 1.0 / (d + 0.008) ** power
    w /= w.sum(axis=1, keepdims=True)

    groups = {n: (obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n))
              for n in names}
    for j in range(k):
        for bi in range(len(names)):
            sel = np.nonzero(order[:, j] == bi)[0]
            if len(sel) == 0:
                continue
            g = groups[names[bi]]
            ww = w[sel, j]
            for vi, val in zip(sel.tolist(), ww.tolist()):
                g.add([vi], float(val), "ADD")


def zero_weight_ratio(obj) -> float:
    me = obj.data
    n = len(me.vertices)
    if n == 0:
        return 1.0
    bad = 0
    for v in me.vertices:
        if sum(g.weight for g in v.groups) < 1e-5:
            bad += 1
    return bad / n


def bind(obj, arm, pos: dict) -> str:
    """スキニングして、使った手法名を返す。"""
    method = "ARMATURE_AUTO"
    try:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except Exception as exc:  # noqa: BLE001 - headless では context 由来で落ちうる
        print(f"[rig] ARMATURE_AUTO 失敗: {exc}")
        method = "DISTANCE"
    else:
        ratio = zero_weight_ratio(obj)
        if ratio > 0.02:
            print(f"[rig] ARMATURE_AUTO のゼロウェイト率 {ratio:.1%} → 距離ベースに切替")
            method = "DISTANCE"

    if method == "DISTANCE":
        for vg in list(obj.vertex_groups):
            obj.vertex_groups.remove(vg)
        obj.parent = arm
        obj.matrix_parent_inverse = arm.matrix_world.inverted()
        mod = obj.modifiers.get("Armature")
        if mod is None:
            mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        distance_weights(obj, arm, pos)
    return method


# --------------------------------------------------------------------------
# 部位ごとの上書き
# --------------------------------------------------------------------------


def _set_exclusive(obj, indices, weights: dict[str, float]) -> None:
    if len(indices) == 0:
        return
    idx = [int(i) for i in indices]
    for vg in obj.vertex_groups:
        vg.remove(idx)
    total = sum(weights.values()) or 1.0
    for bone, w in weights.items():
        if w <= 0.0:
            continue
        g = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
        g.add(idx, float(w / total), "REPLACE")


def override_weights(obj, mb: M.MeshBuilder, p: dict, a: B.Anatomy) -> None:
    """髪・小物・スカート類を親ボーンへ寄せる。"""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    pts = co.reshape(-1, 3)

    face_parts = tuple(f"eye_{sd}{sf}" for sd in ("l", "r")
                       for sf in ("_white", "_iris", "_lash_up", "_lash_lo",
                                  "_crease"))
    head_parts = ("hair_cap", "hair_front", "hair_side", "hair_back",
                  "hair_tail", "hair_acc", "glasses",
                  "brow_l", "brow_r", "nose", "mouth", "head") + face_parts
    _set_exclusive(obj, mb.part_indices(*head_parts), {"Head": 1.0})

    # 髪は毛先ほど揺れボーンへ寄せる（根元は Head のまま＝頭皮から浮かない）
    z_top = p["z"]["chin"] + p["head_h"] * 0.86
    for parts, bone, reach in ((("hair_front", "hair_side"), "HairFront", 0.62),
                               (("hair_back", "hair_tail"), "HairBack", 0.72)):
        idx = mb.part_indices(*parts)
        if len(idx) == 0:
            continue
        zz = pts[idx, 2]
        span = max(1e-6, z_top - float(zz.min()))
        t = np.clip((z_top - zz) / span, 0.0, 1.0) ** 1.6 * reach
        vg_head = obj.vertex_groups.get("Head") or obj.vertex_groups.new(name="Head")
        vg_h = (obj.vertex_groups.get(bone)
                or obj.vertex_groups.new(name=bone))
        for vg in obj.vertex_groups:
            vg.remove([int(i) for i in idx])
        for n, i in enumerate(idx.tolist()):
            w = float(t[n])
            vg_head.add([i], 1.0 - w, "REPLACE")
            if w > 0.0:
                vg_h.add([i], w, "REPLACE")

    _set_exclusive(obj, mb.part_indices("collar"),
                   {"Neck": 0.35, "UpperChest": 0.65})
    _set_exclusive(obj, mb.part_indices("ribbon"), {"UpperChest": 1.0})
    _set_exclusive(obj, mb.part_indices("obi", "himo", "waistband"),
                   {"Hips": 0.7, "Spine": 0.3})

    # スカート・袴・白衣の裾は Hips 主体で、下端だけ脚へ寄せる
    for part, spread in (("skirt", 0.45), ("hakama", 0.40),
                         ("labcoat", 0.25), ("apron", 0.30)):
        idx = mb.part_indices(part)
        if len(idx) == 0:
            continue
        z = pts[idx, 2]
        z_hi, z_lo = float(z.max()), float(z.min())
        t = np.clip((z_hi - z) / max(1e-6, z_hi - z_lo), 0.0, 1.0)
        leg_w = spread * t
        left = pts[idx, 0] >= 0.0
        vg_hips = obj.vertex_groups.get("Hips") or obj.vertex_groups.new(name="Hips")
        vg_l = obj.vertex_groups.get("LeftUpperLeg")
        vg_r = obj.vertex_groups.get("RightUpperLeg")
        for vg in obj.vertex_groups:
            vg.remove([int(i) for i in idx])
        for n, i in enumerate(idx.tolist()):
            w = float(leg_w[n])
            vg_hips.add([i], 1.0 - w, "REPLACE")
            if w > 0.0:
                (vg_l if left[n] else vg_r).add([i], w, "REPLACE")

    for side in ("l", "r"):
        bone = "LeftFoot" if side == "l" else "RightFoot"
        _set_exclusive(obj, mb.part_indices(f"shoes_{side}", f"foot_{side}"),
                       {bone: 1.0})
    _set_exclusive(obj, mb.part_indices("bag"), {"RightHand": 1.0})
