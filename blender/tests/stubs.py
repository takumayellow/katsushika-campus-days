"""Blender を入れずに kcd_lib / kcd_route を import するためのスタブ (#68)。

bpy と bmesh は空のモジュールで、属性に触った時点で AttributeError を出す。テストが Blender に
依存する経路（MeshBuilder.to_object など）に入ったら、黙って通らずにそこで落ちる。

mathutils.geometry.tessellate_polygon だけは中身が要る（MeshBuilder.add_ngon_flat と
split_by_grid が凹多角形を三角形に割るのに使う）。ここでは耳切りで実装し、三角形は入力と同じ
巻き方向で返す。Blender の実装（BLI_scanfill）も多角形自身の法線（Newell 法）で投影して割るので、
向きは入力に従う。反時計回り（上から見て）の敷地の外形を渡した site_ground が
build_campus.check_site_ground の「下向きの水平面なし」を通っていることと合う。
"""

import sys
import types


class _Stubbed(types.ModuleType):
    """属性を引いたら AttributeError を出すモジュール。"""

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
        raise AttributeError("%s.%s is stubbed: Blender is not available under pytest"
                             % (self.__name__, name))


class Vector(tuple):
    """mathutils.Vector の代わり（render.py が import するだけで、テストでは使わない）。"""


class Matrix(tuple):
    """mathutils.Matrix の代わり。"""


def _newell(pts):
    nx = ny = nz = 0.0
    n = len(pts)
    for i in range(n):
        x0, y0, z0 = pts[i]
        x1, y1, z1 = pts[(i + 1) % n]
        nx += (y0 - y1) * (z0 + z1)
        ny += (z0 - z1) * (x0 + x1)
        nz += (x0 - x1) * (y0 + y1)
    return nx, ny, nz


def _cross2(o, a, b):
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def _strictly_inside(p, a, b, c, sign, eps=1e-12):
    return (sign * _cross2(a, b, p) > eps and sign * _cross2(b, c, p) > eps
            and sign * _cross2(c, a, p) > eps)


def tessellate_polygon(veclist_list):
    """1 本の閉じた多角形を三角形に割り、頂点番号の 3 つ組のリストを返す。

    穴（2 本目以降の輪）は扱わない。三角形の巻き方向は入力の多角形と同じ。"""
    if len(veclist_list) != 1:
        raise NotImplementedError("the tessellate_polygon stub takes exactly one loop")
    pts = []
    for p in veclist_list[0]:
        p = tuple(float(c) for c in p)
        pts.append(p if len(p) == 3 else (p[0], p[1], 0.0))
    n = len(pts)
    if n < 3:
        return []
    nrm = _newell(pts)
    drop = max(range(3), key=lambda k: abs(nrm[k]))
    keep = [k for k in range(3) if k != drop]
    p2 = [(p[keep[0]], p[keep[1]]) for p in pts]
    area2 = sum(p2[i][0] * p2[(i + 1) % n][1] - p2[(i + 1) % n][0] * p2[i][1] for i in range(n))
    sign = 1.0 if area2 >= 0.0 else -1.0

    idx = list(range(n))
    tris = []
    while len(idx) > 3:
        ear = _find_ear(p2, idx, sign, strict=True)
        if ear is None:
            # 残りが一直線に並んだ点を含む: 面積 0 の耳を落として進める
            ear = _find_ear(p2, idx, sign, strict=False)
        if ear is None:
            raise ValueError("ear clipping failed (self-intersecting polygon?)")
        m = len(idx)
        tris.append((idx[ear - 1], idx[ear], idx[(ear + 1) % m]))
        idx.pop(ear)
    tris.append(tuple(idx))
    return tris


def _find_ear(p2, idx, sign, strict):
    m = len(idx)
    for k in range(m):
        i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % m]
        a, b, c = p2[i0], p2[i1], p2[i2]
        turn = sign * _cross2(a, b, c)
        if strict and turn <= 1e-12:
            continue
        if not strict and turn < -1e-12:
            continue
        if strict and any(_strictly_inside(p2[j], a, b, c, sign)
                          for j in idx if j not in (i0, i1, i2)):
            continue
        return k
    return None


def install():
    """sys.modules に bpy / bmesh / mathutils / mathutils.geometry のスタブを入れる（何度呼んでもよい）。"""
    if getattr(sys.modules.get("mathutils"), "_kcd_stub", False):
        return
    for name in ("bpy", "bmesh"):
        sys.modules[name] = _Stubbed(name)
    mathutils = types.ModuleType("mathutils")
    mathutils._kcd_stub = True
    mathutils.Vector = Vector
    mathutils.Matrix = Matrix
    geometry = types.ModuleType("mathutils.geometry")
    geometry.tessellate_polygon = tessellate_polygon
    mathutils.geometry = geometry
    sys.modules["mathutils"] = mathutils
    sys.modules["mathutils.geometry"] = geometry
