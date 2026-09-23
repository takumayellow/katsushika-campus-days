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


def _spine_only(obj, mb: M.MeshBuilder, pts: np.ndarray, p: dict,
                a: B.Anatomy, parts, *, k: int = 2, power: float = 3.0) -> None:
    """胴の衣（着物の身頃・肩ヨーク）から腕ボーンのウェイトを外す。

    自動ウェイトだと脇の頂点に LeftUpperArm / RightUpperArm が混ざる。腕は
    Walk で 30 度以上振れるので、隣り合う頂点が胴と腕に引き裂かれ、脇の布が
    針のように伸びて素肌が縞になって覗いていた（botchan / Walk のプレビューで
    右脇に幅 60px・高さ 150px のささくれ）。着物の身頃は腕を振っても動かない
    ものなので、背骨の 4 本だけに預ける。袖（`sleeve_*`）は別部位なので
    これまでどおり腕について行く。
    """
    idx = mb.part_indices(*parts)
    if len(idx) == 0:
        return
    pos = bone_positions(p, a)
    names = ["Hips", "Spine", "Chest", "UpperChest"]
    sub = pts[idx]
    dist = np.stack([_seg_distance(sub, pos[n][0], pos[n][1]) for n in names],
                    axis=1)
    order = np.argsort(dist, axis=1)[:, :k]
    rows = np.arange(len(sub))[:, None]
    w = 1.0 / (dist[rows, order] + 0.008) ** power
    w /= w.sum(axis=1, keepdims=True)
    for vg in obj.vertex_groups:
        vg.remove([int(i) for i in idx])
    groups = {n: (obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n))
              for n in names}
    for n_, i in enumerate(idx.tolist()):
        for j in range(k):
            ww = float(w[n_, j])
            if ww > 1e-4:
                groups[names[int(order[n_, j])]].add([i], ww, "REPLACE")


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
#:   mix        前の中央で左右の脚を混ぜる割合（0.5 でちょうど等分。0 で無効）
#:   mix_t      その「左右等分で包む」帯の境目（t。小さいほど腰まで伸びる）
#:   mix_band   mix_t の境目をならす帯の半幅（t）。0 に近づけるほど硬い折り目になる
#:   mix_front  前後の境目をならす帯の半幅（胴の奥行き比）
#: 値は Walk / Run の腿がいちばん開くフレームで、素肌のはみ出し (cm) と横辺の
#: 伸び (cm) を実測して決めた。band を広げると布は滑らかになるが裾が脚に付いて
#: 行かなくなり、狭めると逆になる（mirai のスカートで band 0.30/0.45/0.80 のとき
#: Run のはみ出しは 0.73/1.88/3.62 cm、横辺の伸びは 6.15/3.36/1.72 cm）。
#: mix を入れたあとの mirai / Run は band 0.40/0.45/0.50/0.55 で
#: はみ出し 1.01/1.23/1.43/1.61 cm、全辺の伸び 4.52/3.83/3.35/2.93 cm。
CLOTH_LEG: dict[str, dict[str, float]] = {
    "skirt": dict(reach=1.00, hem_pow=0.85, band=0.45,
                  mix=0.50, mix_t=0.60, mix_band=0.30, mix_front=0.35),
    "apron": dict(reach=1.00, hem_pow=0.85, band=0.45,
                  mix=0.50, mix_t=0.60, mix_band=0.30, mix_front=0.35),
    "labcoat": dict(reach=0.72, hem_pow=1.40, band=0.85),
    "pants_seat": dict(reach=0.55, hem_pow=1.00, band=0.45),
    # 袴もスカートと同じ理由で前中央に mix が要る。入れる前は botchan の
    # Idle / Walk で前中央の裾が Hips に貼り付いたままになり、前へ出した膝が
    # 布を突き抜けて 40x50px の穴が空いていた。mix_t はスカート (0.60) より
    # 少し上から効かせる（袴のほうが丈が長く、膝が当たるのが上のため）。
    "hakama": dict(reach=1.00, hem_pow=0.40, band=0.40,
                   knee_band=0.10, knee_keep=0.80,
                   mix=0.50, mix_t=0.55, mix_band=0.30, mix_front=0.35),
}


def _cloth_leg_weights(obj, mb: M.MeshBuilder, pts: np.ndarray, a: B.Anatomy,
                       part: str, *, parts=None, z_hi=None, z_lo=None,
                       knee: bool = False, **over) -> None:
    """スカート・袴・白衣・ズボンの尻を、裾ほど脚へ寄せて包む。

    ウェイトは **Hips ＋ 左右の UpperLeg** の 3 本までに収める。

    以前は「2 本まで」だった。WebGL が使う品質レベル (Mobile) の
    skinWeights が 2 で、3 本以上は上位 2 本へ切り詰められて形が化けたため。
    だが前中央を「Hips 100%」と「左右 50:50」の 2 択にすると、その境目が
    1 本の線になり、布のたわみが全部そこへ集まって棚ができる（#49 で実測。
    41.9 mm の辺が 4.0 mm まで潰れ、折れ角が 25.2° → 165.3° になっていた）。
    境目をなめらかに繋ぐと、途中に必ず 3 本乗る帯ができる（幾何的に避けられない。
    実測で 3 本目は最大 0.327）。そこで Mobile 側の skinWeights を PC と同じ 4 に
    上げた（`Assets/Tests/EditMode/SkinWeightsTests.cs` が見張っている）。

    左右は x=0 のハード分割にしない。裂けない条件は「x=0 で左脚と右脚の
    ウェイトが等しいこと」。上位 2 本に切られると、x=0 をまたぐ隣り合った
    頂点が「Hips ＋ 右脚」と「Hips ＋ 左脚」に分かれる。走ると左右の脚は
    逆位相なので、そこで布が裂ける。

    * 後ろと腰       … `band` の幅で脚寄せを 0 まで落として Hips 100% にする。
    * 前の裾 (`mix`) … 左右の脚を 50:50 で混ぜる。Hips を使わない代わりに、
      中央でも脚について行く。走りで腿が前へ出たとき、前の中央が Hips に
      貼り付いたままだと腿がそこを突き抜ける（mirai の Run で実測 11.1 cm）。

    50:50 は「左右の脚の平均」なので、股関節の軸へ cos(振り角) だけ縮む。
    それが効きすぎると腰がスカートから外へ出るので、混ぜるのは `mix_t` より
    下（裾側）の、`mix_front` より前だけ。腰は Hips に任せる。

    `knee=True`（裾が脚ごとに割れる馬乗り袴）のときは膝の上下で UpperLeg /
    LowerLeg を切り替え、その面でも同じ理由で脚寄せを `knee_keep` まで落とす。
    筒の行灯袴は切り替えない（膝の境目で段になって裂けるため。#49）。
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
    band = max(cfg["band"], 1e-6)
    s = M.smoothstep(0.0, 1.0, nx / band)
    w = cfg["reach"] * t * s
    mix = cfg.get("mix", 0.0)
    if mix > 0.0:
        # 前の中央（s→0）だけは、Hips へ戻す代わりに **左右の脚を等分** で
        # 混ぜる。x=0 の両側で Left と Right が入れ替わっても値が同じなので、
        # 中央でも脚について行きながら布は左右に裂けない。
        k = mix * (1.0 - s)
        # 「前の裾かどうか」は真偽値で切ってはいけない。以前は
        # `(t >= mix_t) & (y < 0)` という段差で、その 1 本の線に布のたわみが
        # 全部集まり、41.9 mm の辺が 4.0 mm まで潰れて水平の棚ができていた
        # （mirai / Run。折れ角は t=mix_t のリングで 25.2° → 165.3°、
        # その下のリングは逆に 11.5° → 0.5° の硬いコーンになっていた）。
        # 上下 (mix_band) と前後 (mix_front) の両方をなめらかに繋ぐ。
        gt = M.smoothstep(cfg["mix_t"] - cfg["mix_band"],
                          cfg["mix_t"] + cfg["mix_band"], t)
        fw = max(1e-6, cfg["mix_front"])
        gy = 1.0 - M.smoothstep(-fw, fw, pp[:, 1] / ry)
        g = gt * gy
        # g=0 では素の w（= reach * t * s）に完全に戻る。ここを s*u で置き換えて
        # いたせいで、「前の裾だけ」のはずが後ろ上部まで 78.5% の頂点で値が動いていた。
        own = cfg["reach"] * (t * s * (1.0 - g) + (1.0 - k) * g)
        oth = cfg["reach"] * k * g
    else:
        own, oth = w, np.zeros_like(w)

    side = np.where(pp[:, 0] >= 0.0, "Left", "Right")
    flip = np.where(pp[:, 0] >= 0.0, "Right", "Left")
    seg = np.full(len(idx), "UpperLeg", dtype=object)
    if knee:
        span = abs(float(a.hip_joint[2]) - float(a.ankle[2]))
        kb = max(1e-4, span * cfg["knee_band"])
        su = M.smoothstep(float(a.knee[2]) - kb, float(a.knee[2]) + kb, z)
        keep = cfg["knee_keep"]
        damp = keep + (1.0 - keep) * np.abs(2.0 * su - 1.0)
        own = own * damp
        oth = oth * damp
        seg = np.where(su >= 0.5, seg, "LowerLeg")
    names = [str(a_) + str(g) for a_, g in zip(side, seg)]
    others = [str(a_) + str(g) for a_, g in zip(flip, seg)]

    own = np.clip(own, 0.0, 1.0)
    oth = np.clip(oth, 0.0, 1.0)
    hips = np.clip(1.0 - own - oth, 0.0, 1.0)

    vg_hips = obj.vertex_groups.get("Hips") or obj.vertex_groups.new(name="Hips")
    for vg in obj.vertex_groups:
        vg.remove([int(i) for i in idx])
    cache: dict[str, object] = {}

    def _vg(name):
        vg = cache.get(name)
        if vg is None:
            vg = (obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name))
            cache[name] = vg
        return vg

    for n, i in enumerate(idx.tolist()):
        # Hips も own / oth と同じしきい値で切る。無条件に書いていたせいで、
        # 前中央の「Hips は 0 のはず」の頂点に 1.0 - own - oth の丸め残り
        # 5.55e-17 が乗り、意味のないウェイトが 1 本増えていた。
        if hips[n] > 1e-4:
            vg_hips.add([i], float(hips[n]), "REPLACE")
        if own[n] > 1e-4:
            _vg(names[n]).add([i], float(own[n]), "REPLACE")
        if oth[n] > 1e-4:
            _vg(others[n]).add([i], float(oth[n]), "REPLACE")


def _prune_influences(obj, mb: M.MeshBuilder, parts, k: int = 2) -> None:
    """指定した部位のウェイトを上位 k ボーンだけにして正規化する。

    靴下・ズボン・ブーツの筒は距離ウェイトのまま 3 ボーン乗ることがある
    （例: madonna のブーツは LowerLeg / Foot / Toes）。3 本目は距離の裾野が
    たまたま届いただけで、形に効くというより PC と WebGL で食い違う種だった
    （madonna のブーツで Run 時 1.54 cm）。ここで落として両方同じにする。

    以前は「WebGL は 1 頂点 2 ボーンだから」が理由だったが、その前提は #49 で
    なくなった。スカート前中央は Hips ＋ 左右 UpperLeg の 3 本が要る場所で、
    2 本に切ると左右どちらの脚を残すかが x=0 をまたいで入れ替わり、布が裂ける。
    そのため QualitySettings の Mobile（＝WebGL）も PC と同じ 4 本にした。
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
    _spine_only(obj, mb, pts, p, a, ("kimono", "kimono_yoke"))
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
    # 膝で UpperLeg / LowerLeg を切り替えるのは、裾が脛まで割れて脚ごとに
    # 分かれる馬乗り袴（高下駄の坊っちゃん）だけ。筒の行灯袴（ブーツのマドンナ
    # ちゃん）に掛けると、走りで膝を曲げたとき膝より上は腿へ、下は脛へ
    # 付いていき、その境目で袴が段になって裂け、素肌が見えていた (#49)。
    _cloth_leg_weights(obj, mb, pts, a, "hakama",
                       knee="boots" not in p.get("accessories", ()))
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
