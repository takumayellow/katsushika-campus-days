"""背広の教授を走り・歩きの姿勢にしても、上着の下の服が外へ出ず、腰の継ぎ目が開かない (#172)。

Blender の線形ブレンドスキニングを numpy で組み直して測る。ウェイトはビルドと同じく
distance_weights → override_weights で付ける（Blender の頂点の位置は float32 なので、それに揃える）。
骨は anim のポーズを rig の階層の順に重ね、SkirtFront / SkirtBack は anim.skirt_rotations と同じ
角度で回す。こうして組んだ頂点の位置は、Blender で書き出した Run と Jump の全フレームと 0.1 mm
以内で一致する。

- 突き抜け: シャツ・ズボンの頂点から輪郭線の向きへ出した光線が上着（袖を含む）に当たる点を
  レストで決め、姿勢にしたとき、その点の面より外へ出た距離の最大。
- 継ぎ目: レストで同じ位置にある上着の裾の側（jacket）と胸の側（jacket_yoke）の頂点の、
  姿勢での距離の最大。
"""

import functools

import numpy as np
import pytest

import gait_fk
import mathutils_np
from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_under_cloth import _FakeObj

ANIM = mathutils_np.load_anim()
BONES = rig.HIERARCHY + rig.HAIR_HIERARCHY + rig.SKIRT_HIERARCHY
OUTER = ("jacket", "jacket_yoke", "sleeve_l", "sleeve_r")
INNER = {"shirt": ("shirt",), "pants": ("pants_l", "pants_r", "pants_seat")}
# 走り・歩きで許す量（m）。今は Run がシャツ 3.0 mm・ズボン 2.4 mm、Walk が 2.2 mm・2.8 mm、
# 継ぎ目はどちらも 0 mm。
LIMIT = {"seam": 0.0005, "shirt": 0.005, "pants": 0.005}


class _Verts(list):
    def __init__(self, items, pts):
        super().__init__(items)
        self._co = np.asarray(pts, dtype=np.float32).ravel()

    def foreach_get(self, attr, out):
        assert attr == "co"
        out[:] = self._co


@functools.cache
def _skin(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    pts = np.asarray(mb.verts, dtype=float)
    obj = _FakeObj([{} for _ in range(len(pts))], [])
    obj.data.vertices = _Verts(obj.data.vertices, pts)
    pos = rig.bone_positions(p, a)
    rig.distance_weights(obj, None, pos)
    rig.override_weights(obj, mb, p, a)
    col = {n: k for k, (n, _) in enumerate(BONES)}
    W = np.zeros((len(pts), len(col)))
    for i, w in enumerate(obj.w):
        for n, v in w.items():
            W[i, col[n]] = v
    assert (W.sum(axis=1) > 0).all()
    W /= W.sum(axis=1, keepdims=True)
    rest = {n: np.asarray(pos[n][0], dtype=float) for n in col}
    return p, mb, pts, W, col, rest, mathutils_np.FakeArmature(pos)


def _rotation(axis, angle):
    x, y, z = axis
    c, s = np.cos(angle), np.sin(angle)
    C = 1.0 - c
    return np.array([[c + x * x * C, x * y * C - z * s, x * z * C + y * s],
                     [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
                     [z * x * C - y * s, z * y * C + x * s, c + z * z * C]])


def _signed_angle(a, b, axis):
    pa, pb = a - axis * (a @ axis), b - axis * (b @ axis)
    return np.arctan2(axis @ np.cross(pa, pb), pa @ pb)


def _bones(spec, rest):
    """各ボーンのワールドの回転と根元の位置。anim の回転は親の回転に左から重なる。"""
    R, H = {}, {}
    for name, parent in BONES:
        local = gait_fk._ops_matrix(spec.get(name, []))[:3, :3]
        if parent is None:
            R[name] = local
            H[name] = rest[name] + np.array(spec.get("root", (0.0, 0.0, 0.0)))
        else:
            R[name] = R[parent] @ local
            H[name] = H[parent] + R[parent] @ (rest[name] - rest[parent])
    return R, H


def _follow_thighs(R, H, rest):
    """anim.skirt_rotations と同じく、前へ（後ろへ）いちばん出ている腿の分だけ回す。"""
    undo = R["Hips"].T
    angles, axes = [], np.zeros(3)
    for side in ("Left", "Right"):
        up, lo = side + "UpperLeg", side + "LowerLeg"
        thigh = rest[lo] - rest[up]
        axis = np.cross(thigh, (0.0, -1.0, 0.0))
        axis /= np.linalg.norm(axis)
        angles.append(_signed_angle(thigh, undo @ (H[lo] - H[up]), axis))
        axes += axis
    axes /= np.linalg.norm(axes)
    R["SkirtFront"] = R["Hips"] @ _rotation(axes, max(0.0, *angles) * ANIM.SKIRT_FOLLOW)
    R["SkirtBack"] = R["Hips"] @ _rotation(axes, min(0.0, *angles) * ANIM.SKIRT_FOLLOW)


def _deform(name, spec):
    _, _, pts, W, col, rest, _ = _skin(name)
    R, H = _bones(spec, rest)
    _follow_thighs(R, H, rest)
    out = np.zeros_like(pts)
    for n, k in col.items():
        m = W[:, k] > 0
        if m.any():
            out[m] += W[m, k, None] * ((pts[m] - rest[n]) @ R[n].T + H[n])
    return out


@functools.cache
def _probes(name):
    """内側の服の頂点と、それを覆う上着の面（レストで当てた三角形と重心座標）。"""
    p, mb, pts, *_ = _skin(name)
    h = p["height"]
    N = mb.outline_normals()
    outer = M.triangles(mb.faces, mb.part_indices(*OUTER))
    out = {}
    for group, parts in INNER.items():
        idx = mb.part_indices(*parts)
        hits = M.first_hits(pts[idx] + N[idx] * h * 0.0002, N[idx], pts, outer, h * 0.03)
        m = hits[:, 0] >= 0
        out[group] = (idx[m], outer[hits[m, 0].astype(int)], hits[m, 1], hits[m, 2], N[idx[m]])
    jacket, yoke = mb.part_indices("jacket"), mb.part_indices("jacket_yoke")
    at = {tuple(np.round(pts[i], 6)): i for i in yoke.tolist()}
    pairs = [(i, at[k]) for i in jacket.tolist() if (k := tuple(np.round(pts[i], 6))) in at]
    return out, np.array(pairs)


def _worst(name, kind):
    p, *_, arm = _skin(name)
    over = (p.get("gait") or {}).get(kind, {})
    poser = ANIM.Gait(arm, kind, **over)
    probes, pairs = _probes(name)
    res = dict.fromkeys(LIMIT, 0.0)
    for k in range(poser.nframes):
        spec = ANIM._arms_down(poser.pose(k / poser.nframes), body.arm_drop(p), body.elbow_open(p))
        P = _deform(name, spec)
        gap = np.linalg.norm(P[pairs[:, 0]] - P[pairs[:, 1]], axis=1).max()
        res["seam"] = max(res["seam"], float(gap))
        for group, (idx, T, u, v, N) in probes.items():
            hit = (P[T[:, 0]] * (1 - u - v)[:, None] + P[T[:, 1]] * u[:, None]
                   + P[T[:, 2]] * v[:, None])
            n = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
            n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
            n *= np.sign(np.einsum("ij,ij->i", n, N))[:, None]
            res[group] = max(res[group], float(np.einsum("ij,ij->i", P[idx] - hit, n).max()))
    return res


def test_rest_pose_keeps_the_mesh():
    _, _, pts, *_ = _skin("prof")
    assert np.abs(_deform("prof", {}) - pts).max() < 1e-9


def test_bones_follow_the_gait():
    """走りの途中で、骨の根元が gait_fk の関節の位置と一致する。"""
    *_, rest, _ = _skin("prof")
    _, H = _bones(gait_fk.spec_at("prof", "Run", 0.3), rest)
    joints = gait_fk.joints("prof", "Run", 0.3)
    for name, _ in rig.HIERARCHY:
        assert np.abs(H[name] - joints[name]).max() < 1e-9, name


def test_probes_cover_the_suit():
    probes, pairs = _probes("prof")
    assert len(probes["shirt"][0]) > 200
    assert len(probes["pants"][0]) > 100
    assert len(pairs) > 20


@pytest.mark.parametrize("kind", ["Run", "Walk"])
def test_suit_holds_while_moving(kind):
    res = _worst("prof", kind)
    over = {k: round(v * 1000, 1) for k, v in res.items() if v > LIMIT[k]}
    assert not over, f"{kind} で上限を超えた（mm）: {over}"
