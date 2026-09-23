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


def spine_z(p: dict) -> dict[str, float]:
    """背骨系ボーンの頭の高さ。ウェイト側からも同じ値を使う。"""
    h = p["height"]
    z = p["z"]
    return {
        "Hips": z["crotch"] + (z["hip"] - z["crotch"]) * 0.55,
        "Spine": z["waist"] - h * 0.010,
        "Chest": z["underbust"] + h * 0.006,
        "UpperChest": z["bust"] + (z["shoulder"] - z["bust"]) * 0.42,
    }


def bone_positions(p: dict, a: B.Anatomy) -> dict[str, tuple[np.ndarray, np.ndarray]]:
    h = p["height"]
    z = p["z"]
    sz = spine_z(p)
    z_hips, z_spine = sz["Hips"], sz["Spine"]
    z_chest, z_uchest = sz["Chest"], sz["UpperChest"]
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


def _keep_leg_side(obj, mb: M.MeshBuilder) -> None:
    """左右のある部位（leg_l, socks_r など）から反対側の脚ボーンのウェイトを外す。

    距離ウェイトは脚を閉じた立ち姿だと内側の面が両脚からほぼ等距離になり、
    靴下やズボンの内側が逆の脚へ 5 割近く乗る。歩くと衣装だけ脚の間に残って
    素肌が突き抜けるので、外した分は同じ側の同じ部位のボーンへ移す。
    """
    legs = ("UpperLeg", "LowerLeg", "Foot", "Toes")
    for part in mb.parts:
        if part.endswith("_l"):
            own, other = "Left", "Right"
        elif part.endswith("_r"):
            own, other = "Right", "Left"
        else:
            continue
        for seg in legs:
            src = obj.vertex_groups.get(other + seg)
            if src is None:
                continue
            dst = (obj.vertex_groups.get(own + seg)
                   or obj.vertex_groups.new(name=own + seg))
            # src.weight() は入っていない頂点でコンソールにエラーを出すので、
            # 頂点側の所属グループから重みを拾う
            for i in mb.part_indices(part).tolist():
                w = next((g.weight for g in obj.data.vertices[i].groups
                          if g.group == src.index), None)
                if w is None:
                    continue
                src.remove([i])
                dst.add([i], w, "ADD")


#: 衣装を Hips ＋ 脚ボーン 1 本だけで包むときの調整値。
#:   reach      裾で脚に付いて回る割合（1.0 で脚と同じだけ動く）
#:   hem_pow    腰から裾への立ち上がり（大きいほど裾の近くだけ動く）
#:   band       前後の中央で脚寄せを 0 に落とす幅（横向き比 0..1、1.0 で真横）
#:   knee_band  膝で UpperLeg と LowerLeg を切り替える帯の半幅（脚長比）
#:   knee_keep  その切り替え面に残す脚寄せの割合（0 で完全に Hips へ戻す）
#: 値は Walk / Run の腿がいちばん開くフレームで、素肌のはみ出し (cm) と横辺の
#: 伸び (cm) を実測して決めた。band を広げると布は滑らかになるが裾が脚に付いて
#: 行かなくなり、狭めると逆になる（mirai のスカートで band 0.30/0.45/0.80 のとき
#: Run のはみ出しは 0.73/1.88/3.62 cm、横辺の伸びは 6.15/3.36/1.72 cm）。
CLOTH_LEG: dict[str, dict[str, float]] = {
    "skirt": dict(reach=1.00, hem_pow=0.85, band=0.40),
    "apron": dict(reach=1.00, hem_pow=0.85, band=0.40),
    "labcoat": dict(reach=0.72, hem_pow=1.40, band=0.85),
    "pants_seat": dict(reach=0.55, hem_pow=1.00, band=0.45),
    "hakama": dict(reach=1.00, hem_pow=0.40, band=0.40,
                   knee_band=0.10, knee_keep=0.80),
}


def _cloth_leg_weights(obj, mb: M.MeshBuilder, pts: np.ndarray, a: B.Anatomy,
                       part: str, *, parts=None, z_hi=None, z_lo=None,
                       knee: bool = False, **over) -> None:
    """スカート・袴・白衣・ズボンの尻を、裾ほど脚へ寄せて包む。

    ウェイトは必ず **Hips ＋ 脚ボーン 1 本** だけにする。WebGL 版が使う品質
    レベル (Mobile) は 1 頂点 2 ボーンでしかスキンしない（QualitySettings の
    skinWeights）ので、3 本以上のウェイトはそこで上位 2 本へ切り詰められて
    別の形に化ける。はじめから 2 本で成立する形にしておけば化けない。

    左右は x=0 のハード分割にしない。中央では `band` の幅で脚寄せを 0 まで
    落とし、どちらの脚を選んでも同じ位置（Hips 100%）になるようにする。
    分割したままだと、前後の中央で隣り合う頂点が逆向きの脚に引かれ、走ると
    布がギザギザに裂ける。袴は膝の上下で UpperLeg / LowerLeg を切り替え、
    その面でも同じ理由で脚寄せを `knee_keep` まで落とす。
    """
    cfg = dict(CLOTH_LEG[part])
    cfg.update(over)
    idx = mb.part_indices(*(parts or (part,)))
    if len(idx) == 0:
        return
    pp = pts[idx]
    z = pp[:, 2]
    if z_hi is None:
        z_hi = float(z.max())
    if z_lo is None:
        z_lo = float(z.min())
    t = np.clip((z_hi - z) / max(1e-6, z_hi - z_lo), 0.0, 1.0) ** cfg["hem_pow"]
    # 左右寄せは x の生値ではなく「胴の中心から見た向き」で測る。ヒダは山と谷
    # で x が行きつ戻りつするので、x で測ると隣り合う頂点のウェイトが階段状に
    # 暴れ、走るとヒダ 1 枚ごとに裂ける。向きはヒダの凹凸では変わらない。
    rx = max(1e-6, float(np.abs(pp[:, 0]).max()))
    ry = max(1e-6, float(np.abs(pp[:, 1]).max()))
    ux = np.abs(pp[:, 0]) / rx
    nx = ux / np.maximum(np.hypot(ux, pp[:, 1] / ry), 1e-6)
    w = cfg["reach"] * t * M.smoothstep(0.0, cfg["band"], nx)

    side = np.where(pp[:, 0] >= 0.0, "Left", "Right")
    seg = np.full(len(idx), "UpperLeg", dtype=object)
    if knee:
        span = abs(float(a.hip_joint[2]) - float(a.ankle[2]))
        kb = max(1e-4, span * cfg["knee_band"])
        su = M.smoothstep(float(a.knee[2]) - kb, float(a.knee[2]) + kb, z)
        keep = cfg["knee_keep"]
        w = w * (keep + (1.0 - keep) * np.abs(2.0 * su - 1.0))
        seg = np.where(su >= 0.5, seg, "LowerLeg")
    names = [str(s) + str(g) for s, g in zip(side, seg)]

    vg_hips = obj.vertex_groups.get("Hips") or obj.vertex_groups.new(name="Hips")
    for vg in obj.vertex_groups:
        vg.remove([int(i) for i in idx])
    cache: dict[str, object] = {}
    for n, i in enumerate(idx.tolist()):
        ww = float(np.clip(w[n], 0.0, 1.0))
        vg_hips.add([i], 1.0 - ww, "REPLACE")
        if ww <= 1e-4:
            continue
        vg = cache.get(names[n])
        if vg is None:
            vg = (obj.vertex_groups.get(names[n])
                  or obj.vertex_groups.new(name=names[n]))
            cache[names[n]] = vg
        vg.add([i], ww, "REPLACE")


def _prune_influences(obj, mb: M.MeshBuilder, parts, k: int = 2) -> None:
    """指定した部位のウェイトを上位 k ボーンだけにして正規化する。

    靴下・ズボン・ブーツの筒は距離ウェイトのまま 3 ボーン乗ることがある
    （例: madonna のブーツは LowerLeg / Foot / Toes）。WebGL 版は 1 頂点
    2 ボーンなので、3 本目はそこで勝手に落ちて PC 版と形が変わる
    （madonna のブーツで Run 時 1.54 cm）。先に落としておけば両方同じになる。
    """
    idx = mb.part_indices(*parts)
    if len(idx) == 0:
        return
    gname = {g.index: g.name for g in obj.vertex_groups}
    for i in idx.tolist():
        ws = sorted(((g.weight, gname[g.group]) for g in obj.data.vertices[i].groups
                     if g.weight > 0.0), reverse=True)
        if len(ws) <= k:
            continue
        keep = ws[:k]
        tot = sum(w for w, _ in keep) or 1.0
        for vg in obj.vertex_groups:
            vg.remove([i])
        for w, n in keep:
            obj.vertex_groups[n].add([i], float(w / tot), "REPLACE")


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

    # スカート・白衣・袴・ズボンの尻は Hips ＋ 脚 1 本で包む（_cloth_leg_weights）
    skirt_z = pts[mb.part_indices("skirt"), 2]
    _cloth_leg_weights(obj, mb, pts, a, "skirt")
    _cloth_leg_weights(obj, mb, pts, a, "labcoat")
    if len(skirt_z):
        # エプロンは真下のスカートと同じ高さ・同じ割合で脚へ寄せる。
        # 別の割合にすると、歩くたびにヒダの尾根がエプロンを突き抜ける。
        _cloth_leg_weights(obj, mb, pts, a, "apron",
                           z_hi=float(skirt_z.max()), z_lo=float(skirt_z.min()))
    else:
        _cloth_leg_weights(obj, mb, pts, a, "apron")

    _keep_leg_side(obj, mb)
    _cloth_leg_weights(obj, mb, pts, a, "hakama", knee=True)
    _cloth_leg_weights(obj, mb, pts, a, "pants_seat")

    # 靴下・ズボン・ブーツの筒も 2 ボーンに揃える（WebGL と PC で同じ形に）
    _prune_influences(obj, mb, ("socks_l", "socks_r", "pants_l",
                                "pants_r", "bootleg_l", "bootleg_r"))
    for side in ("l", "r"):
        bone = "LeftFoot" if side == "l" else "RightFoot"
        _set_exclusive(obj, mb.part_indices(f"shoes_{side}", f"foot_{side}"),
                       {bone: 1.0})
    if p.get("bag_mount") == "shoulder":
        _set_exclusive(obj, mb.part_indices("bag"), {"UpperChest": 1.0})
    else:
        _set_exclusive(obj, mb.part_indices("bag"), {"RightHand": 1.0})
