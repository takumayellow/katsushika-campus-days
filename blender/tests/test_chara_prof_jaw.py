"""教授の下顔を大人の男の輪郭にすること (#172)。

卵形の頭のままだと顎先が細く尖り、頬のふくらみも残って、子どもか女の人の
丸顔に見えた。教授だけ head_jaw で顎先に幅を持たせ、エラの角を作る。
"""

import math

import numpy as np
import pytest

from kcd_chara import body, params


def _half_width(cid: str, t: float) -> float:
    """顎から t 頭高の高さでの頭の横の半幅（頭幅比）。"""
    p = params.resolve(cid)
    head = body.Head(p)
    az = np.linspace(0.0, 2 * math.pi, 360, endpoint=False)
    el = np.linspace(0.02, math.pi - 0.02, 1500)
    zt = p["z"]["chin"] + t * p["head_h"]
    best = 0.0
    for e in el:
        pts = head.surface(az, np.full_like(az, e))
        if abs(pts[0, 2] - zt) < p["head_h"] * 0.004:
            best = max(best, float(np.abs(pts[:, 0]).max()))
    return best / p["head_w"]


def _ring(z: float) -> np.ndarray:
    a = np.linspace(0.0, 2 * math.pi, 720, endpoint=False)
    r = math.sqrt(1.0 - z * z)
    return np.stack([np.cos(a) * r, np.sin(a) * r, np.full_like(a, z)], axis=1)


def test_only_prof_has_the_adult_jaw():
    """顎を張らせるのは教授だけ。ほかの 7 体の頭は前と同じ計算を通る。"""
    jaws = {cid: params.CHARACTERS[cid].get("head_jaw", 0.0) for cid in params.ALL_IDS}
    assert jaws.pop("prof") == pytest.approx(1.0)
    assert all(v == 0.0 for v in jaws.values()), jaws


def test_prof_chin_is_broad():
    """顎のすぐ上（0.05 頭高）の幅が、顔の中ほど（0.5 頭高）の半分を超える。

    卵形の学生は 0.38 倍まで細る。教授は 0.52 倍（実測）。
    """
    prof = _half_width("prof", 0.05) / _half_width("prof", 0.5)
    student = _half_width("student_m", 0.05) / _half_width("student_m", 0.5)
    assert prof > 0.50, prof
    assert student < 0.40, student


@pytest.mark.parametrize("z", [0.0, 0.3, 0.7])
def test_jaw_leaves_the_skull_alone(z):
    """頭蓋（顔の中ほどより上）の形は、顎を張らせても変わらない。髪と眼鏡はここに乗る。"""
    d = _ring(z)
    a = body._head_deform(d, 0.5, 0.0)
    b = body._head_deform(d, 0.5, 1.0)
    assert np.abs(a - b).max() < 1e-12


@pytest.mark.parametrize("z", [-0.8, -0.9, -0.95])
def test_jaw_widens_sideways_only(z):
    """顎先の近くは横へ広がり、後ろ側の奥行きは変えない（横顔の下顔が箱にならない）。

    頬の高さ（z=-0.6 前後）は頬のふくらみを削るぶん、逆にわずかに細くなる。
    """
    d = _ring(z)
    a = body._head_deform(d, 0.5, 0.0)
    b = body._head_deform(d, 0.5, 1.0)
    assert b[:, 0].max() > a[:, 0].max()
    back = d[:, 1] > 0.0
    assert np.abs(a[back, 1] - b[back, 1]).max() < 1e-12
