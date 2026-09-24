"""屋内 FBX に入れる、窓のすぐ外の近景（ext_<id> / ext_<id>_trees, #84）。

キャンパスを作り直さず、コミット済みの campus.fbx / trees.fbx を 1 回だけ読み、建物ごとに
外周（躯体外面の矩形 = spec の x0..x1 / y_face..y_back）を四方へ RADIUS m 広げた矩形の中を
切り出して建物ローカル座標へ写す。ゲームの屋外と同じ形・同じマテリアル名になるので、窓から見える
景色と外へ出たときの景色が食い違わない。

- 外周の内側に入る部分は切り落とす（屋内の壁と床の中に埋まるだけ）。
- 自分の建物の外装は外周から OWN_MARGIN m 以内を落とす。外装の窓割りは屋内の窓割りと
  別に作っているので、外壁やルーバーが屋内の窓の真ん前をふさぐ。
- 木は幹の位置で選び（幹から外周までが RADIUS + 樹冠の半径以内）、切らずに丸ごと入れる。
  樹冠が外周 + TREE_MARGIN にかかるものは入れない（切ると断面が見える）。
- 建物の中のどこから見ても裏を向いている面は入れない。Unity のキャンパス用マテリアルは
  すべて片面描画（URP Lit / KCD_Toon とも _Cull = Back, MaterialLibrary.EnsureCampus）なので、
  入れても 1 画素も描かれない（隣の棟の向こう側の外壁、樹冠の奥側など）。
- 高さは DZ だけ下げる。地面が屋内の床の上面（z = 0）より下、Unity で屋内の下に敷く面
  （OutsideGround y = -0.06、x 2000 m までのスロットでは CampusStage の OuterGround y = -0.05）
  より上に来る。

座標は build_interiors と同じ建物ローカル（原点 = entrance_<id> の真下、+Y = 入口から奥）。
"""

import math
import os

import bpy
from kcd_lib.mesh import MeshBuilder, _area3, _clip_half, _is_convex, _newell, _triangulate
from kcd_lib.site import _PLANT_RINGS, _plant_z

CAMPUS_FILES = ("campus.fbx", "trees.fbx")
IMPORT_OPTS = dict(axis_forward="-Z", axis_up="Y")

RADIUS = 30.0        # 外周を四方へ広げる幅。この矩形の中を入れる
DZ = -0.03           # 近景全体を下げる量
Z_MIN = -0.045       # これより下の頂点は持ち上げる（OuterGround -0.05 / OutsideGround -0.06 より上に置く）
Z_BURIED = -0.001    # キャンパスでこれより下にしか無い面（車道の帯の埋まった側面）は捨てる
OWN_MARGIN = 1.5     # 自分の建物の外装を落とす幅（外周から）
TREE_MARGIN = 0.3    # 樹冠と外周のあいだに空ける幅
PLANT_FULL = 12.0    # 花壇の株をそのままの形で入れる距離（外周から）。これより遠いと簡略形
TOP_RING = _PLANT_RINGS[0][0] / _PLANT_RINGS[-1][0]   # 株の天面の輪から下の輪への倍率
BLOOM_LIFT = (0.01, 0.06)   # 花の底が株の表面から浮く高さ、底から頂点までの高さ（site._plant）
PLANT_SEG = 7        # 株の角数（site._plant の seg）
PLANT_FACES = PLANT_SEG * (len(_PLANT_RINGS) - 1) + 1   # 株 1 つの面数（側面 + 天面）
EPS = 1e-6

EXT_PREFIX = "ext_"          # 近景のメッシュ名の頭。Unity 側で当たり判定を外す目印（#60）
TREES_SUFFIX = "_trees"      # 木だけのメッシュ ext_<id>_trees
# build_campus が付ける名前
TREE_PREFIX = "tree_mesh_"   # trees.fbx の樹種ごとの原型
TREE_GROUP_PREFIX = "trees_" # trees.fbx の樹種ごとの親 Empty（子が 1 本ずつの木）
BLD_PREFIX = "bld_"          # campus.fbx の棟の外装
BEDS = "site_props_beds"     # campus.fbx の花壇


class Source:
    """campus.fbx / trees.fbx から読んだ面（ワールド座標）と木の配置。"""

    def __init__(self):
        # (出どころ, マテリアル, [(x, y, z)], (xmin, xmax, ymin, ymax), zmax, 株)
        # 株 = None か (株の番号, 簡略形か)。花壇の株の面と花、その簡略形（find_plants）
        self.polys = []
        self.plants = []   # 株の中心 (x, y)
        self.protos = {}   # 樹種 -> (頂点, [(頂点番号, マテリアル)], 樹冠の半径, 原型の姿勢)
        self.trees = []    # (樹種, 4x4 行列, (x, y), 倍率)


def _mat_name(mat):
    # 読み込み時に同名があると ".001" が付く
    return mat.name.split(".")[0] if mat is not None else "concrete_grey"


def load(campus_dir):
    """今のシーンに 2 つの FBX を読み込み、面と木を Python のデータに写して返す。

    読み込む前からシーンにある物（起動時のシーンなど）は使わない。
    呼んだ側はこのあとシーンを初期化してよい（戻り値は bpy のデータを持たない）。"""
    before = {o.name for o in bpy.data.objects}
    for fn in CAMPUS_FILES:
        path = os.path.join(campus_dir, fn)
        if not os.path.isfile(path):
            raise FileNotFoundError("近景の元データが無い: %s" % path)
        bpy.ops.import_scene.fbx(filepath=path, **IMPORT_OPTS)
    loaded = [o for o in bpy.data.objects if o.name not in before]

    src = Source()
    for o in loaded:
        if o.type != "MESH" or o.name.startswith(TREE_PREFIX):
            continue
        mw = o.matrix_world
        vs = [tuple(mw @ v.co) for v in o.data.vertices]
        names = [_mat_name(m) for m in o.data.materials]
        for p in o.data.polygons:
            pts = [vs[i] for i in p.vertices]
            xs = [q[0] for q in pts]
            ys = [q[1] for q in pts]
            mat = names[p.material_index] if names else "concrete_grey"
            src.polys.append((o.name, mat, pts, (min(xs), max(xs), min(ys), max(ys)),
                              max(q[2] for q in pts), None))
    src.polys, src.plants = find_plants(src.polys)

    for o in loaded:
        if o.type != "MESH" or not o.name.startswith(TREE_PREFIX):
            continue
        mw = o.matrix_world
        verts = [v.co.copy() for v in o.data.vertices]
        names = [_mat_name(m) for m in o.data.materials]
        faces = [(tuple(p.vertices), names[p.material_index] if names else "leaf")
                 for p in o.data.polygons]
        crown = max(math.hypot(w.x - mw.translation.x, w.y - mw.translation.y)
                    for w in (mw @ v for v in verts))
        src.protos[o.name[len(TREE_PREFIX):]] = (verts, faces, crown, mw.copy())

    for o in loaded:
        par = o.parent
        if o.type != "EMPTY" or par is None or not par.name.startswith(TREE_GROUP_PREFIX):
            continue
        species = par.name[len(TREE_GROUP_PREFIX):]
        proto = src.protos.get(species)
        if proto is None:
            continue
        # Unity の CampusStage と同じ置き方: 木の Empty に「グループから見た原型の姿勢」を掛ける
        m = o.matrix_world @ par.matrix_world.inverted() @ proto[3]
        t = o.matrix_world.translation
        src.trees.append((species, m, (t.x, t.y), o.matrix_world.to_scale().x))
    return src


def _poly(name, mat, pts, plant):
    xs = [q[0] for q in pts]
    ys = [q[1] for q in pts]
    return (name, mat, pts, (min(xs), max(xs), min(ys), max(ys)),
            max(q[2] for q in pts), plant)


def _key(p):
    return (round(p[0], 4), round(p[1], 4), round(p[2], 4))


def find_plants(polys):
    """花壇の株（site._plant）を面の集まりから拾い、遠くで使う簡略形を足す。

    株は flower_leaf の 7 角の天面と 2 段の側面（頂点を共有する 1 つのまとまり）、花は株ごとに
    1 色の小さな 3 角錐 7 個。簡略形は下の輪から天面へ 1 段の側面で結び、天面をその株の花の
    色で塗る（花 21 面と中段の 14 面が無くなる）。

    戻り値は (面, 株の中心)。株の面・花・簡略形には (株の番号, 簡略形か) を付ける。
    花の付いていない株は元の形のまま（番号なし）。"""
    plants = _plant_groups(polys)
    blooms = _match_blooms(polys, plants) if plants else {}
    out = list(polys)
    centers = []
    for n, (c, top, zb, faces, _r, _h) in enumerate(plants):
        mine = blooms.get(n)
        if not mine:
            continue
        k = len(centers)
        centers.append(c)
        for i in faces + mine:
            out[i] = polys[i][:5] + ((k, False),)
        col = polys[mine[0]][1]
        ring = [(c[0] + (q[0] - c[0]) * TOP_RING, c[1] + (q[1] - c[1]) * TOP_RING, zb)
                for q in top]
        for j in range(PLANT_SEG):
            j1 = (j + 1) % PLANT_SEG
            out.append(_poly(BEDS, "flower_leaf", [ring[j], ring[j1], top[j1], top[j]], (k, True)))
        out.append(_poly(BEDS, col, list(top), (k, True)))
    if not centers and any(p[0] == BEDS and p[1] == "flower_leaf" for p in polys):
        print("[exterior] 警告: 花壇の株を見分けられない（site._plant の形が変わった？）。"
              "遠くの株も元の形のまま入れる")
    return out, centers


def _plant_groups(polys):
    """花壇の flower_leaf の面を頂点の共有でまとめ（union-find）、株の形のものを返す。

    (中心, 天面の頂点, 下の輪の高さ, 面の番号, 半径, 高さ) のリスト。"""
    leaf = [i for i, p in enumerate(polys) if p[0] == BEDS and p[1] == "flower_leaf"]
    parent = {i: i for i in leaf}

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    seen = {}
    for i in leaf:
        for q in polys[i][2]:
            j = seen.setdefault(_key(q), i)
            if j != i:
                a, b = root(i), root(j)
                if a != b:
                    parent[a] = b
    groups = {}
    for i in leaf:
        groups.setdefault(root(i), []).append(i)

    plants = []
    for faces in groups.values():
        tops = [i for i in faces if len(polys[i][2]) == PLANT_SEG]
        if len(tops) != 1 or len(faces) != PLANT_FACES:
            continue    # 株の形をしていない
        top = polys[tops[0]][2]
        cx = sum(q[0] for q in top) / float(PLANT_SEG)
        cy = sum(q[1] for q in top) / float(PLANT_SEG)
        zb = min(q[2] for i in faces for q in polys[i][2])
        rad = (sum(math.hypot(q[0] - cx, q[1] - cy) for q in top)
               / float(PLANT_SEG) / _PLANT_RINGS[-1][0])
        plants.append(((cx, cy), top, zb, faces, rad, top[0][2] - zb))
    return plants


def _cell(x, y, size):
    return int(math.floor(x / size)), int(math.floor(y / size))


def _match_blooms(polys, plants, cell=1.0):
    """花の 3 角錐（頂点を共有する 3 面）を株に振り分ける。{株の番号: [面の番号]}

    株どうしは重なり、花は隣の株の中心のほうが近いことがある。花は株の表面に置かれている
    （底 = 表面 + BLOOM_LIFT[0]）ので、3 角錐の頂点の高さが表面の高さと合う株に付ける。
    隣り合う株は高さが違う（site._flowers の h）ので取り違えない。"""
    grid = {}
    for n, p in enumerate(plants):
        grid.setdefault(_cell(p[0][0], p[0][1], cell), []).append(n)
    cones = {}
    for i, p in enumerate(polys):
        if p[0] != BEDS or not p[1].startswith("flower_") or p[1] == "flower_leaf":
            continue
        apex = max(p[2], key=lambda q: q[2])
        cones.setdefault(_key(apex), (apex, []))[1].append(i)
    blooms = {}
    for apex, idx in cones.values():
        gx, gy = _cell(apex[0], apex[1], cell)
        near = [n for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                for n in grid.get((gx + dx, gy + dy), ())]
        best = _best_plant(apex, plants, near)
        if best is not None:
            blooms.setdefault(best, []).extend(idx)
    return blooms


def _best_plant(apex, plants, near):
    """near の株のうち、apex の真下の表面の高さが一番合うもの（真下に無ければ None）。"""
    ax, ay, az = apex
    best, be = None, 1e9
    for n in near:
        c, _t, zb, _f, rad, h = plants[n]
        t = math.hypot(ax - c[0], ay - c[1]) / rad
        if t > 1.0:
            continue
        err = abs(az - (zb + h * _plant_z(t) + sum(BLOOM_LIFT)))
        if err < be:
            best, be = n, err
    return best


# --------------------------------------------------------------------------- #
#  切り出し
# --------------------------------------------------------------------------- #
def _rect_dist(x, y, rect):
    x0, x1, y0, y1 = rect
    dx = max(x0 - x, 0.0, x - x1)
    dy = max(y0 - y, 0.0, y - y1)
    return math.hypot(dx, dy)


def _clip(pieces, axis, c, side):
    out = []
    for pc in pieces:
        s = [side * (p[axis] - c) for p in pc]
        if min(s) >= -EPS:
            out.append(pc)
        elif max(s) > EPS:
            q = _clip_half(pc, axis, c, side, EPS)
            if len(q) >= 3 and _area3(q) > 1e-8:
                out.append(q)
    return out


def _on_line(pc, axis, c):
    return all(abs(p[axis] - c) <= 1e-5 for p in pc)


def outside_pieces(poly, box, hole):
    """poly（建物ローカルの 3D 多角形）を box の内側・hole の外側に切った凸な断片。

    box / hole は (x0, x1, y0, y1)。hole の外は 4 つの凸な領域（左・右・手前・奥）に
    分けて、それぞれで Sutherland-Hodgman をかける。"""
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    bx0, bx1, by0, by1 = box
    if max(xs) <= bx0 or min(xs) >= bx1 or max(ys) <= by0 or min(ys) >= by1:
        return []
    hx0, hx1, hy0, hy1 = hole
    pieces = [poly] if _is_convex(poly) else _triangulate(poly)
    pieces = _clip(pieces, 0, bx0, 1.0)
    pieces = _clip(pieces, 0, bx1, -1.0)
    pieces = _clip(pieces, 1, by0, 1.0)
    pieces = _clip(pieces, 1, by1, -1.0)
    if not pieces:
        return []
    if max(xs) <= hx0 or min(xs) >= hx1 or max(ys) <= hy0 or min(ys) >= hy1:
        return pieces
    if min(xs) >= hx0 and max(xs) <= hx1 and min(ys) >= hy0 and max(ys) <= hy1:
        return []
    out = []
    out += _clip(pieces, 0, hx0, -1.0)                       # 左（x <= hx0）
    out += _clip(pieces, 0, hx1, 1.0)                        # 右（x >= hx1）
    mid = _clip(_clip(pieces, 0, hx0, 1.0), 0, hx1, -1.0)
    for pc in _clip(mid, 1, hy0, -1.0) + _clip(mid, 1, hy1, 1.0):   # 手前・奥
        # x = hx0 / hx1 の面上にある断片は左右の側で拾い済み
        if _on_line(pc, 0, hx0) or _on_line(pc, 0, hx1):
            continue
        out.append(pc)
    # 外周の面そのものに貼り付いた断片（屋内の外壁と重なる）は捨てる
    keep = []
    for pc in out:
        cx = sum(p[0] for p in pc) / len(pc)
        cy = sum(p[1] for p in pc) / len(pc)
        if hx0 - 1e-4 <= cx <= hx1 + 1e-4 and hy0 - 1e-4 <= cy <= hy1 + 1e-4:
            continue
        keep.append(pc)
    return keep


def faces_viewer(pts, corners):
    """建物の中（corners を頂点とする箱）のどこかから、この面の表が見えるか。

    表が見える点の集合は半空間 n·(w - p) > 0 なので、箱のどれかの頂点が入っていれば
    箱と交わる。"""
    nx, ny, nz = _newell(pts)
    ln = math.sqrt(nx * nx + ny * ny + nz * nz)
    if ln < 1e-9:
        return False
    px, py, pz = pts[0]
    return any(nx * (cx - px) + ny * (cy - py) + nz * (cz - pz) > 1e-6 * ln
               for cx, cy, cz in corners)


def _corners(sp, top):
    return [(x, y, z) for x in (sp.x0, sp.x1) for y in (sp.y_face, sp.y_back)
            for z in (0.0, top)]


def _world_bbox(sp, rect):
    """建物ローカルの矩形をワールドに写したときの軸平行な外接矩形。"""
    x0, x1, y0, y1 = rect
    pts = [sp.to_world_xy(p) for p in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    return (min(p[0] for p in pts), max(p[0] for p in pts),
            min(p[1] for p in pts), max(p[1] for p in pts))


def _to_local(sp, p):
    lx, ly = sp.to_local_uv(sp.frame.uv((p[0], p[1])))
    return (lx, ly, p[2])


def _lift(pc):
    return [(p[0], p[1], max(p[2] + DZ, Z_MIN)) for p in pc]


def build(sp, src, top, radius=RADIUS):
    """1 棟ぶんの近景。([MeshBuilder], 集計) を返す。top は屋内の一番高い点の z。

    集計:
      tris        入れた三角数（出どころ別。棟の外装はまとめて bld、木は trees）
      culled_tris 裏を向くので入れなかった三角数（切り出したあとの数。site / trees）
      plants      入れた花壇の株の数（full = 元の形、simple = 簡略形）
      trees       入れた木の本数（placed）と、樹冠が外周にかかるので入れなかった本数（skipped）"""
    env = (sp.x0, sp.x1, sp.y_face, sp.y_back)
    box = (sp.x0 - radius, sp.x1 + radius, sp.y_face - radius, sp.y_back + radius)
    own_hole = (sp.x0 - OWN_MARGIN, sp.x1 + OWN_MARGIN,
                sp.y_face - OWN_MARGIN, sp.y_back + OWN_MARGIN)
    wx0, wx1, wy0, wy1 = _world_bbox(sp, box)
    own = BLD_PREFIX + sp.id

    corners = _corners(sp, top)

    mb = MeshBuilder(EXT_PREFIX + sp.id)
    tris = {}
    culled = {"site": 0, "trees": 0}
    far = {}    # 株の番号 -> 外周から PLANT_FULL より遠いか
    used = {}   # 入れた株の番号 -> 簡略形か
    for name, mat, pts, bb, zmax, plant in src.polys:
        if bb[1] < wx0 or bb[0] > wx1 or bb[3] < wy0 or bb[2] > wy1:
            continue
        if zmax < Z_BURIED:
            continue
        if plant is not None:
            n, simple = plant
            if n not in far:
                cx, cy, _ = _to_local(sp, src.plants[n] + (0.0,))
                far[n] = _rect_dist(cx, cy, env) > PLANT_FULL
            if far[n] != simple:
                continue
        local = [_to_local(sp, p) for p in pts]
        pieces = outside_pieces(local, box, own_hole if name == own else env)
        if not pieces:
            continue
        n_tris = sum(len(pc) - 2 for pc in pieces)
        if not faces_viewer(local, corners):
            culled["site"] += n_tris
            continue
        for pc in pieces:
            mb.add_face(_lift(pc), mat)
        key = "bld" if name.startswith(BLD_PREFIX) else name
        tris[key] = tris.get(key, 0) + n_tris
        if plant is not None:
            used[plant[0]] = plant[1]

    tb = MeshBuilder(EXT_PREFIX + sp.id + TREES_SUFFIX)
    placed = skipped = 0
    for species, m, (x, y), scale in src.trees:
        lx, ly, _ = _to_local(sp, (x, y, 0.0))
        verts, faces, crown, _pm = src.protos[species]
        r = crown * scale
        d = _rect_dist(lx, ly, env)
        if d > radius + r:
            continue
        if d < r + TREE_MARGIN:
            skipped += 1
            continue
        world = [m @ v for v in verts]
        local = [_to_local(sp, (w.x, w.y, w.z)) for w in world]
        n_tris = 0
        for idx, fmat in faces:
            pc = [local[i] for i in idx]
            if not faces_viewer(pc, corners):
                culled["trees"] += len(idx) - 2
                continue
            tb.add_face(_lift(pc), fmat)
            n_tris += len(idx) - 2
        if n_tris:
            tris["trees"] = tris.get("trees", 0) + n_tris
            placed += 1
    stats = {
        "tris": tris,
        "culled_tris": culled,
        "plants": {"full": sum(1 for v in used.values() if not v),
                   "simple": sum(1 for v in used.values() if v)},
        "trees": {"placed": placed, "skipped": skipped},
    }
    return [b for b in (mb, tb) if b.faces], stats
