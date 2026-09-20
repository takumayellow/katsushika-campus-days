"""表情の Shape Key。

DESIGN.md §2.1 で要求される 7 種:
blink_L / blink_R / smile / mouth_open / mouth_a / mouth_i / angry_brow。

顔パーツ（白目・虹彩・まつ毛・二重線・眉・口）はすべて独立したメッシュなので、
頂点を動かすと UV ごと動き、下地に元の絵が残らない。左右の目は別グループに
なっているので片目ずつの瞬きができる。
"""

from __future__ import annotations

import numpy as np

from . import mesh as M

KEY_NAMES = ("blink_L", "blink_R", "smile", "mouth_open", "mouth_a",
             "mouth_i", "angry_brow")

#: 片目を構成するパート名のサフィックス
_EYE_SUFFIX = ("_white", "_iris", "_lash_up", "_lash_lo", "_crease")


def _co_array(obj) -> np.ndarray:
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def build_shape_keys(obj, mb: M.MeshBuilder, p: dict) -> list[str]:
    obj.shape_key_add(name="Basis", from_mix=False)
    pts = _co_array(obj)

    def eye(side: str) -> np.ndarray:
        return mb.part_indices(*[f"eye_{side}{s}" for s in _EYE_SUFFIX])

    def white(side: str) -> np.ndarray:
        return mb.part_indices(f"eye_{side}_white")

    eye_l, eye_r = eye("l"), eye("r")
    mouth = mb.part_indices("mouth")
    head = mb.part_indices("head")
    brow_l = mb.part_indices("brow_l")
    brow_r = mb.part_indices("brow_r")

    hh = p["head_h"]
    made: list[str] = []

    def add_key(name: str, edits) -> None:
        key = obj.shape_key_add(name=name, from_mix=False)
        new = pts.copy()
        edits(new)
        for i in np.nonzero(np.abs(new - pts).sum(axis=1) > 1e-9)[0]:
            key.data[int(i)].co = tuple(new[int(i)])
        key.slider_min, key.slider_max = 0.0, 1.0
        made.append(name)

    # -- blink ---------------------------------------------------------------
    def _close(new, side: str) -> None:
        grp, w = eye(side), white(side)
        if len(grp) == 0 or len(w) == 0:
            return
        zw = pts[w, 2]
        lid = float(zw.min()) + (float(zw.max()) - float(zw.min())) * 0.32
        new[grp, 2] = lid + (pts[grp, 2] - lid) * 0.06
        new[grp, 1] += hh * 0.0015

    add_key("blink_L", lambda new: _close(new, "l"))
    add_key("blink_R", lambda new: _close(new, "r"))

    # -- mouth ---------------------------------------------------------------
    def _mouth_scale(new, sx: float, sz: float, *, anchor: str = "top",
                     lift: float = 0.0) -> None:
        if len(mouth) == 0:
            return
        mz = pts[mouth, 2]
        base = float(mz.max()) if anchor == "top" else float(mz.mean())
        new[mouth, 0] = pts[mouth, 0] * sx
        new[mouth, 2] = base + (mz - base) * sz + hh * lift
        new[mouth, 1] = pts[mouth, 1] + hh * 0.004

    def _jaw(new, drop: float) -> None:
        if len(head) == 0:
            return
        zc = p["z"]["chin"] + hh * 0.10
        sel = head[(pts[head, 2] < zc) & (pts[head, 1] < 0.0)]
        if len(sel) == 0:
            return
        t = np.clip((zc - pts[sel, 2]) / (hh * 0.24), 0.0, 1.0)
        new[sel, 2] -= t * drop

    def _mouth_open(new):
        _mouth_scale(new, 0.88, 2.60)
        _jaw(new, hh * 0.045)

    def _mouth_a(new):
        # 「あ」: 縦に大きく開いて少し丸い
        _mouth_scale(new, 1.04, 3.05)
        _jaw(new, hh * 0.058)

    def _mouth_i(new):
        # 「い」: 横に引いて薄く
        _mouth_scale(new, 1.42, 0.34, anchor="mid", lift=0.004)

    # -- smile ---------------------------------------------------------------
    def _smile(new):
        if len(mouth):
            mx = pts[mouth, 0]
            mz = pts[mouth, 2]
            cz = float(mz.mean())
            w = float(np.abs(mx).max()) + 1e-9
            new[mouth, 0] = mx * 1.16
            new[mouth, 2] = (cz + (mz - cz) * 1.06
                             + (np.abs(mx) / w) ** 2 * hh * 0.026)
        for side in ("l", "r"):
            grp, wt = eye(side), white(side)
            if len(grp) == 0 or len(wt) == 0:
                continue
            lo = float(pts[wt, 2].min())
            new[grp, 2] = lo + (pts[grp, 2] - lo) * 0.86
        for grp in (brow_l, brow_r):
            if len(grp):
                new[grp, 2] += hh * 0.014

    # -- angry_brow ----------------------------------------------------------
    def _angry(new):
        for grp in (brow_l, brow_r):
            if len(grp) == 0:
                continue
            ax = np.abs(pts[grp, 0])
            m = float(ax.mean())
            # 内側（鼻側）を下げ、外側を上げて怒り眉にする
            new[grp, 2] += (ax - m) * 0.62 - hh * 0.030
        for side in ("l", "r"):
            grp, wt = eye(side), white(side)
            if len(grp) == 0 or len(wt) == 0:
                continue
            zw = pts[wt, 2]
            top = float(zw.max())
            new[grp, 2] = top + (pts[grp, 2] - top) * 0.90 - hh * 0.004

    add_key("smile", _smile)
    add_key("mouth_open", _mouth_open)
    add_key("mouth_a", _mouth_a)
    add_key("mouth_i", _mouth_i)
    add_key("angry_brow", _angry)
    return made
