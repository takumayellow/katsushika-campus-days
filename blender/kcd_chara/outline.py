"""トゥーン輪郭線（インバート・ハル方式）。

本体メッシュを複製し、頂点法線方向へわずかに膨らませてから面の向きを反転する。
Unity 側で背面カリング（`Cull Front` ではなく通常の `Cull Back`）を効かせると、
膨らませたシェルのうちシルエットの外側だけが残って黒い縁になる。

複製は `obj.copy()` で作るので、頂点グループとアーマチュアモディファイアを
そのまま受け継ぐ。つまり本体と同じスキニングで一緒に動く。
Shape Key は輪郭には不要なので落とす（FBX の肥大を避けるため）。
"""

from __future__ import annotations

import bmesh
import bpy
import numpy as np

#: 膨らませる量（身長に対する比）。太すぎると顔のパーツが潰れる。
THICKNESS = 0.0022

#: 輪郭を作らないパート。
#:
#: 顔のパーツは頭の表面から数ミリしか浮いていないので、頭の膨張シェルが
#: そのまま前へ回り込んで目を黒く塗り潰す。パーツを膨らませない（growth=0）
#: だけでは反転した複製が元の面と同じ位置に残って Z ファイティングで黒く
#: なるため、複製メッシュからは頂点ごと削除する。
SKIP_PARTS = ("eye_l_white", "eye_r_white", "eye_l_iris", "eye_r_iris",
              "eye_l_crease", "eye_r_crease",
              "eye_l_lash_up", "eye_r_lash_up",
              "eye_l_lash_lo", "eye_r_lash_lo",
              "brow_l", "brow_r", "nose", "mouth")


def build_outline(obj, mb, p: dict, materials: dict, *,
                  mat_name: str = "outline"):
    """`<id>_outline` を作ってシーンに追加し、そのオブジェクトを返す。"""
    mat = materials.get(mat_name)
    if mat is None:
        return None
    cid = p["id"]
    name = f"{cid}_outline"

    me = obj.data.copy()
    me.name = name
    dup = obj.copy()
    dup.data = me
    dup.name = name
    bpy.context.scene.collection.objects.link(dup)

    # Shape Key は輪郭に不要
    if me.shape_keys is not None:
        dup.shape_key_clear()

    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    nrm = np.empty(nv * 3, dtype=np.float64)
    me.vertex_normals.foreach_get("vector", nrm)
    co = co.reshape(-1, 3)
    nrm = nrm.reshape(-1, 3)

    grow = np.full(nv, p["height"] * THICKNESS)
    skip = mb.part_indices(*SKIP_PARTS)
    if len(skip):
        grow[skip] = 0.0
    me.vertices.foreach_set("co", (co + nrm * grow[:, None]).ravel())

    # 顔パーツを取り除いてから面を裏返す（背面カリングで外周だけが残る）
    bm = bmesh.new()
    bm.from_mesh(me)
    if len(skip):
        bm.verts.ensure_lookup_table()
        dead = set(int(i) for i in skip)
        gone = [v for v in bm.verts if v.index in dead]
        if gone:
            bmesh.ops.delete(bm, geom=gone, context="VERTS")
    for f in bm.faces:
        f.normal_flip()
    bm.to_mesh(me)
    bm.free()

    me.materials.clear()
    me.materials.append(mat)
    idx = np.zeros(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_set("material_index", idx)
    me.update()

    # Eevee で本体を覆い隠さないよう、マテリアル側で背面カリングを効かせる
    mat.use_backface_culling = True
    return dup


def outline_tris(dup) -> int:
    if dup is None:
        return 0
    me = dup.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)
