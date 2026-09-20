"""campus.json の建物 footprint から、インテリア用のローカル座標系を導く。

ローカル座標（Blender）:
  原点 = entrance_<id> の真下の床面（= 外装側が置いた Empty の位置、z=0）
  +Y   = 入口から建物内部へ向かう向き
  +X   = 内部を向いたときの右手
  +Z   = 上

FBX を axis_forward='-Z' / axis_up='Y' で書き出すので、Unity では
  Unity(+X) = Blender(+X)、Unity(+Y) = Blender(+Z)、Unity(+Z) = Blender(+Y)
となり、契約どおり **Unity の +Z が「入口から内部へ」** になる。

外壁面（ファサード）はローカル Y = spec.y_face にある。spec.y_face は
外装側が entrance Empty を壁からどれだけ手前に置いたかの距離で、
buildings.py の各ビルダーと 1 対 1 で対応させている（ここがズレると
Unity で内外の位置が合わないので、変更時は両方直すこと）。
"""

import json
import math
import os

from kcd_lib import geom

# 建物 id -> (entrance を決める関数, 内向き方向)
#   内向き方向 "+v" / "-v" / "-u" は campus frame (u, v) 上の向き。
#   entrance_uv は buildings.py の ctx["entrance"] と完全に一致させる。


def _entrance_uv(bid, uvbb):
    u0, v0, u1, v1 = uvbb
    du = u1 - u0
    if bid == "research1":
        return ((u0 + u1) * 0.5, v0 - 6.5), "+v"
    if bid == "lecture":
        return (u0 + du * 0.40, v0 - 4.0), "+v"
    if bid == "research2":
        return ((u0 + u1) * 0.5, v0 - 4.0), "+v"
    if bid == "kyoso":
        # build_kyoso は footprint を +v 側へ 10.5 m 広げている
        return (u0 + du * 0.30 - 8.0, v0 + 10.5 + 4.0), "-v"
    if bid == "library":
        return (u1 + 4.0, -24.0), "-u"
    if bid == "gym":
        return (u0 + du * 0.5, v0 - 3.5), "+v"
    if bid in ("lab1", "lab2"):
        return ((u0 + u1) * 0.5, v0 - 3.0), "+v"
    if bid == "greenhouse":
        return ((u0 + u1) * 0.5, v0 - 2.5), "+v"
    return ((u0 + u1) * 0.5, v0 - 3.0), "+v"


def _uvbb_for(bid, uvbb):
    """外装側で footprint を拡張している建物は、その拡張後の bbox を返す。"""
    u0, v0, u1, v1 = uvbb
    if bid == "kyoso":
        return (u0, v0, u1, v0 + 10.5)
    return uvbb


class InteriorSpec:
    """1 棟ぶんのローカル座標系と室内エンベロープ。

    x0..x1 : 躯体外面のローカル X 範囲
    y_face : 入口側ファサード外面のローカル Y（= 原点から壁までの距離）
    y_back : 奥側ファサード外面のローカル Y
    """

    WALL = 0.30  # 外周壁厚（DESIGN.md §3.4）

    def __init__(self, bid, display, levels, height, uvbb, ent_uv, inward,
                 frame):
        self.id = bid
        self.display = display
        self.levels = max(1, int(levels or 1))
        self.height = float(height or 4.0)
        self.frame = frame
        self.uvbb = uvbb
        self.ent_uv = ent_uv
        self.inward = inward
        u0, v0, u1, v1 = uvbb
        corners = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        pts = [self.to_local_uv(c) for c in corners]
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        self.x0, self.x1 = min(xs), max(xs)
        self.y_face, self.y_back = min(ys), max(ys)

    # ---- 座標変換 ----
    def to_local_uv(self, uv):
        du = uv[0] - self.ent_uv[0]
        dv = uv[1] - self.ent_uv[1]
        if self.inward == "+v":
            return (du, dv)
        if self.inward == "-v":
            return (-du, -dv)
        if self.inward == "-u":
            return (dv, -du)
        return (-dv, du)   # "+u"

    def to_world_uv(self, local):
        lx, ly = local
        if self.inward == "+v":
            du, dv = lx, ly
        elif self.inward == "-v":
            du, dv = -lx, -ly
        elif self.inward == "-u":
            du, dv = -ly, lx
        else:
            du, dv = ly, -lx
        return (self.ent_uv[0] + du, self.ent_uv[1] + dv)

    def to_world_xy(self, local):
        u, v = self.to_world_uv(local)
        return self.frame.xy(u, v)

    # ---- 便利プロパティ ----
    @property
    def width(self):
        return self.x1 - self.x0

    @property
    def depth(self):
        return self.y_back - self.y_face

    @property
    def cx(self):
        return (self.x0 + self.x1) * 0.5

    def inner(self, margin=None):
        """外周壁の内側（=室内側）の矩形 (x0, y0, x1, y1)。"""
        m = self.WALL if margin is None else margin
        return (self.x0 + m, self.y_face + m, self.x1 - m, self.y_back - m)

    def floor_h(self, default=4.2):
        """1 層ぶんの階高。"""
        if self.levels > 0 and self.height > 0:
            return self.height / self.levels
        return default

    def __repr__(self):
        return ("<InteriorSpec %s %.1f x %.1f  face_y=%.1f levels=%d>"
                % (self.id, self.width, self.depth, self.y_face, self.levels))


def campus_frame(data):
    """build_campus.make_frame と同一のローカル軸（第1研究棟の長辺）。"""
    for b in data["buildings"]:
        if b["id"] == "research1":
            loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
            i = geom.longest_edge(loop)
            d = geom.normalize(geom.sub(loop[(i + 1) % len(loop)], loop[i]))
            if d[0] < 0:
                d = (-d[0], -d[1])
            return geom.Frame(d)
    return geom.Frame((1.0, 0.0))


def load_campus(path):
    with open(path, "r", encoding="utf-8") as fp:
        data = json.load(fp)
    for b in data["buildings"]:
        b["footprint"] = [tuple(p) for p in b["footprint"]]
    return data


def build_specs(data, ids=None):
    """対象建物の InteriorSpec を作る。ids=None なら内部を持つ全棟。"""
    frame = campus_frame(data)
    out = {}
    for b in data["buildings"]:
        bid = b["id"]
        if ids is not None and bid not in ids:
            continue
        if not b.get("on_campus"):
            continue
        style = b.get("style")
        if style in (None, "background", "dormitory"):
            continue
        loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
        if len(loop) < 3:
            continue
        uvbb = _uvbb_for(bid, frame.uv_bbox(loop))
        ent, inward = _entrance_uv(bid, frame.uv_bbox(loop))
        out[bid] = InteriorSpec(bid, b.get("display", bid), b.get("levels"),
                                b.get("height"), uvbb, ent, inward, frame)
    return out


def dump_sidecar(spec, extra, path):
    """Unity 側が読む配置情報（entrance からのオフセットなど）。"""
    ent_xy = spec.frame.xy(spec.ent_uv[0], spec.ent_uv[1])
    ang = math.atan2(spec.frame.u[1], spec.frame.u[0])
    # インテリアのローカル +Y をワールド XY に写したときの向き（Unity の Y 回転）
    fwd = spec.to_world_xy((0.0, 1.0))
    yaw = math.degrees(math.atan2(fwd[0] - ent_xy[0], fwd[1] - ent_xy[1]))
    meta = {
        "id": spec.id,
        "display": spec.display,
        "entrance_world": {"x": round(ent_xy[0], 4), "z": round(ent_xy[1], 4)},
        "yaw_deg": round(yaw, 4),
        "campus_frame_deg": round(math.degrees(ang), 4),
        "envelope": {"x0": round(spec.x0, 3), "x1": round(spec.x1, 3),
                     "y_face": round(spec.y_face, 3),
                     "y_back": round(spec.y_back, 3)},
        "wall_thickness": spec.WALL,
    }
    meta.update(extra)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fp:
        json.dump(meta, fp, ensure_ascii=False, indent=1)
    return path
