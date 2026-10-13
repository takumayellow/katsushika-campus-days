"""テクスチャを貼る面に、メートル単位の UV を書く (#59)。

MeshBuilder は頂点をワールド座標のまま原点のオブジェクトに入れるので、座標をそのまま
メートルの UV にできる。1 枚の画像が何 m 四方かは data/textures/surfaces.json の tile_cm で、
Unity 側は `_BaseMap_ST` のタイリングに 100 / tile_cm を入れる（MaterialLibrary）。

- 水平に近い面（|法線の z| >= 0.7）は、上から見た座標を `axis` とその直交方向へ投影する。
  キャンパスは第 1 研究棟の長辺（build_campus.make_frame）を渡して、敷石の目地を建物の並びに揃える。
- それ以外（壁）は、壁に沿った水平方向を u、高さ z を v にする。外から見て右へ u が増えるので
  画像は裏返らず、z = 0 の地面から煉瓦の段が始まる。

テクスチャを使うマテリアルの面だけに UV を書き、それ以外の面は (0, 0) に揃える。
FBX の書き出しは同じ UV をまとめるので、使わない面のぶんはほとんど増えない。
テクスチャを使う面が 1 つも無いオブジェクトには UV レイヤー自体を作らない。
"""

import json
import os

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SURFACES_JSON = os.path.join(ROOT, "data", "textures", "surfaces.json")

LAYER = "UVMap"
HORIZONTAL_NZ = 0.7


def load_surfaces(path=SURFACES_JSON):
    """マテリアル名 → tile_cm。"""
    with open(path, "r", encoding="utf-8") as fp:
        spec = json.load(fp)
    return {name: float(s["tile_cm"]) for s in spec["surfaces"] for name in s["materials"]}


def textured_materials(path=SURFACES_JSON):
    return set(load_surfaces(path))


def _unit(axis):
    a = np.asarray(axis, dtype=np.float64)[:2]
    length = float(np.hypot(a[0], a[1]))
    if length < 1e-9:
        raise ValueError("axis の長さが 0: %r" % (axis,))
    return a / length


def _read(collection, attr, count, width, dtype):
    buf = np.empty(count * width, dtype=dtype)
    collection.foreach_get(attr, buf)
    return buf.reshape(count, width) if width > 1 else buf


def metric_uvs(points, normals, axis):
    """ループごとの位置 (N, 3) と法線 (N, 3) から、メートル単位の UV (N, 2) を返す。"""
    d = _unit(axis)
    perp = np.array((-d[1], d[0]))
    horizontal = np.abs(normals[:, 2]) >= HORIZONTAL_NZ

    tangent = np.stack((-normals[:, 1], normals[:, 0]), axis=1)
    length = np.hypot(tangent[:, 0], tangent[:, 1])
    flat = length < 1e-9
    tangent[flat] = (1.0, 0.0)
    tangent[~flat] /= length[~flat, None]

    xy = points[:, :2]
    u = np.where(horizontal, xy @ d, np.einsum("ij,ij->i", xy, tangent))
    v = np.where(horizontal, xy @ perp, points[:, 2])
    return np.stack((u, v), axis=1)


def write_object(obj, axis=(1.0, 0.0), textured=None):
    """1 オブジェクトに UV を書く。書いたループ数を返す（書かなければ 0）。"""
    if obj.type != "MESH":
        return 0
    me = obj.data
    names = textured_materials() if textured is None else textured
    slots = np.array([m is not None and m.name in names for m in me.materials], dtype=bool)
    if len(slots) == 0 or not slots.any():
        return 0

    n_poly, n_loop, n_vert = len(me.polygons), len(me.loops), len(me.vertices)
    mat_index = _read(me.polygons, "material_index", n_poly, 1, np.int32)
    use = slots[np.clip(mat_index, 0, len(slots) - 1)]
    if not use.any():
        return 0

    loop_start = _read(me.polygons, "loop_start", n_poly, 1, np.int32)
    loop_total = _read(me.polygons, "loop_total", n_poly, 1, np.int32)
    if n_poly and (loop_start[0] != 0 or np.any(np.diff(loop_start) != loop_total[:-1])):
        raise RuntimeError("%s: 面のループが連続していない" % obj.name)
    poly_of_loop = np.repeat(np.arange(n_poly), loop_total)

    world = np.array(obj.matrix_world, dtype=np.float64)
    rot = world[:3, :3]
    co = _read(me.vertices, "co", n_vert, 3, np.float32).astype(np.float64) @ rot.T + world[:3, 3]
    normal = _read(me.polygons, "normal", n_poly, 3, np.float32).astype(np.float64)
    normal = normal @ np.linalg.inv(rot)
    normal /= np.maximum(np.linalg.norm(normal, axis=1), 1e-12)[:, None]

    vert_of_loop = _read(me.loops, "vertex_index", n_loop, 1, np.int32)
    uv = metric_uvs(co[vert_of_loop], normal[poly_of_loop], axis)
    uv[~use[poly_of_loop]] = 0.0

    layer = me.uv_layers.get(LAYER) or me.uv_layers.new(name=LAYER)
    layer.data.foreach_set("uv", uv.astype(np.float32).ravel())
    me.update()
    return int(np.count_nonzero(use[poly_of_loop]))


def write_scene(scene, axis=(1.0, 0.0), textured=None):
    """シーンの全メッシュに UV を書き、{オブジェクト名: 書いたループ数} を返す。"""
    names = textured_materials() if textured is None else textured
    written = {}
    for obj in scene.objects:
        n = write_object(obj, axis, names)
        if n:
            written[obj.name] = n
    return written
