"""campus.json の建物 footprint から、インテリア用のローカル座標系を導く。

ローカル座標（Blender）:
  原点 = entrance_<id> の真下の床面（= 外装側が置いた Empty の位置、z=0）
  +Y   = 入口から建物内部へ向かう向き
  +X   = 内部を向いたときの右手
  +Z   = 上

FBX を axis_forward='-Z' / axis_up='Y' で書き出すので、Unity では
  Unity(+X) = Blender(+X)、Unity(+Y) = Blender(+Z)、Unity(+Z) = Blender(+Y)
となり、契約どおり **Unity の +Z が「入口から内部へ」** になる。

入口の向きと、入口の面に沿った位置は、キャンパスの扉（kcd_lib/entrances.py の DOORS）
から導く。屋内の外周は footprint の外接矩形なので、入口の開口はその矩形の面の上に置き、
原点は面から _FACE_OFFSET だけ外に取る。外壁面（ファサード）はローカル Y = spec.y_face にある。
"""

import json
import math
import os

from kcd_lib import entrances, geom

# 入口の開口（外接矩形の面）から原点までの距離 [m]。屋内の間取りはこの距離を前提に
# 入口まわりを置いているので、棟ごとの値を変えない。DOORS に無い棟は _FACE_OFFSET_DEFAULT。
_FACE_OFFSET = {
    "research1": 6.5,
    "lecture": 4.0,
    "research2": 4.0,
    "kyoso": 4.0,
    "library": 4.0,
    "gym": 3.5,
    "lab1": 3.0,
    "lab2": 3.0,
    "greenhouse": 2.5,
}
_FACE_OFFSET_DEFAULT = 3.0

# DOORS の「建物へ向かう向き」(du, dv) -> 内向き方向
_INWARD = {(0, 1): "+v", (0, -1): "-v", (-1, 0): "-u", (1, 0): "+u"}


def _door_row(bid):
    for row in entrances.DOORS:
        if row[0] == bid:
            return row
    return None


def _entrance_uv(bid, uvbb):
    """入口の原点 (u, v) と内向き方向。

    uvbb は屋内の外周（_uvbb_for の外接矩形）。内向きは DOORS の「建物へ向かう向き」、
    面に沿った位置は DOORS の探索の起点と同じにする（扉は起点から軸に沿って壁を探すので、
    扉の中心も同じ位置にある）。DOORS に無い棟は南面（-v 側）の中央。
    """
    u0, v0, u1, v1 = uvbb
    off = _FACE_OFFSET.get(bid, _FACE_OFFSET_DEFAULT)
    row = _door_row(bid)
    if row is None:
        return ((u0 + u1) * 0.5, v0 - off), "+v"
    _bid, probe, into = row[:3]
    inward = _INWARD.get(tuple(into))
    if inward is None:
        raise ValueError("%s: DOORS の向き %s が軸に沿っていない" % (bid, into))
    if inward == "+v":
        return (probe[0], v0 - off), inward
    if inward == "-v":
        return (probe[0], v1 + off), inward
    if inward == "-u":
        return (u1 + off, probe[1]), inward
    return (u0 - off, probe[1]), inward


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
        ent, inward = _entrance_uv(bid, uvbb)
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
