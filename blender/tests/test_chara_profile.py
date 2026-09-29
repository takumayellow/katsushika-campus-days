"""横顔で目と眉が顔の輪郭から前へ出ないこと (#47)。

顔パーツは頭の表面から -y へ浮かせて貼る。浮かせたぶんとドームのふくらみは、真横から見ると
そのまま輪郭の外へ出る。高さごとに頭の最前面と目・眉の最前面を比べ、出っ張りの最大値を測る。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M


@functools.cache
def _stick_out(name):
    """頭の奥行きに対する、目と眉が頭の最前面より前へ出る量の最大値。"""
    p = params.resolve(name)
    mb = M.MeshBuilder()
    _, head, _, _, _ = body.build_base(mb, p)
    verts = np.asarray(mb.verts)

    az, el = np.meshgrid(np.linspace(0.0, 2 * np.pi, 480, endpoint=False),
                         np.linspace(0.01, np.pi - 0.01, 480))
    surf = head.surface(az.ravel(), el.ravel())
    parts = [k for k in mb.parts if k.startswith(("eye_", "brow_"))]
    feat = np.concatenate([verts[mb.part_indices(k)] for k in parts])

    step = p["head_h"] * 0.01
    worst = -np.inf
    for z in np.arange(feat[:, 2].min(), feat[:, 2].max() + step, step):
        near = feat[np.abs(feat[:, 2] - z) < step]
        rim = surf[np.abs(surf[:, 2] - z) < step]
        if len(near) and len(rim):
            worst = max(worst, rim[:, 1].min() - near[:, 1].min())
    assert np.isfinite(worst)
    return worst / p["head_d"]


@pytest.mark.parametrize("name", ["mirai", "botchan", "madonna"])
def test_eyes_stay_inside_profile(name):
    assert params.resolve(name).get("outward_faces")
    # 直す前は mirai 0.083、botchan 0.027。今は 3 体とも 0.014 以下に収まる。
    assert _stick_out(name) < 0.015


@functools.cache
def _head(name, keel=True):
    """頭の頂点・陰に使う法線・目の頂点。keel=False は face_keel を 0 にして組む。"""
    p = params.resolve(name)
    if not keel:
        p = dict(p, face_keel=0.0)
    mb = M.MeshBuilder()
    body.build_base(mb, p)
    start, end = mb.parts["head"][0]
    verts = np.asarray(mb.verts)
    shade = mb.normal_array()
    unset = np.linalg.norm(shade, axis=1) == 0
    shade[unset] = M.vertex_normals(verts, mb.faces)[unset]
    eyes = np.concatenate([verts[mb.part_indices(k)] for k in mb.parts
                           if k.startswith("eye_")])
    return p, verts[start:end], shade[start:end], eyes


def _t(p, z):
    return (z - p["z"]["chin"]) / p["head_h"]


def test_nose_tip_leads_profile():
    p, verts, _, _ = _head("mirai")
    _, flat, _, _ = _head("mirai", keel=False)
    face = verts[_t(p, verts[:, 2]) < 0.55]
    tip = face[np.argmin(face[:, 1])]
    assert 0.24 < _t(p, tip[2]) < 0.30
    # 竜骨が無いと最前面は目の高さ（t 0.43）の丸みで、鼻先はそれより 0.04 hd 前へ出る
    assert (flat[:, 1].min() - tip[1]) / p["head_d"] > 0.03


def test_eyes_sit_behind_nose_bridge():
    p, verts, _, eyes = _head("mirai")
    step = p["head_h"] * 0.02
    margin = np.inf
    for z in np.arange(eyes[:, 2].min(), eyes[:, 2].max(), step):
        eye = eyes[np.abs(eyes[:, 2] - z) < step]
        skin = verts[np.abs(verts[:, 2] - z) < step]
        margin = min(margin, eye[:, 1].min() - skin[:, 1].min())
    # 竜骨が無いと目は頭の輪郭から 0.015 hd 前へ出る。今は鼻筋の後ろに 0.011 hd 引っ込む。
    assert margin / p["head_d"] > 0.005


def test_keel_keeps_front_outline():
    p, verts, _, _ = _head("mirai")
    _, flat, _, _ = _head("mirai", keel=False)
    step = p["head_h"] * 0.02
    for z in np.arange(flat[:, 2].min() + step, flat[:, 2].max() - step, step):
        a = verts[np.abs(verts[:, 2] - z) < step]
        b = flat[np.abs(flat[:, 2] - z) < step]
        assert abs(np.abs(a[:, 0]).max() - np.abs(b[:, 0]).max()) < 0.005 * p["head_w"]


def test_keel_shading_follows_round_head():
    _, verts, shade, _ = _head("mirai")
    _, flat, round_n, _ = _head("mirai", keel=False)
    front = np.where(flat[:, 1] < 0)[0]
    nose = np.where(verts[:, 1] < flat[:, 1].min())[0]
    assert len(nose) > 10
    for i in nose:
        near = front[np.argmin(np.linalg.norm(flat[front][:, [0, 2]] - verts[i, [0, 2]], axis=1))]
        # 面から求めた法線なら鼻の下は 40° 傾く。陰は丸い頭のまま 5° 以内に収める。
        assert np.dot(shade[i], round_n[near]) > np.cos(np.radians(5.0))
