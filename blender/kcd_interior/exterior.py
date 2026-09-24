"""屋内 FBX に入れる、窓のすぐ外の近景（ext_<id> / ext_<id>_trees, #84）。

キャンパスを作り直さず、コミット済みの campus.fbx / trees.fbx を 1 回だけ読み、建物ごとに
外周（躯体外面の矩形 = spec の x0..x1 / y_face..y_back）を四方へ RADIUS m 広げた矩形の中を
切り出して建物ローカル座標へ写す。ゲームの屋外と同じ形・同じマテリアル名になるので、窓から見える
景色と外へ出たときの景色が食い違わない。

- 外周の内側に入る部分は切り落とす（屋内の壁と床の中に埋まるだけ）。
- 自分の建物の外装（bld_<id>）と、bld_entrances のうち自分の扉の部材（風除室・ガラス扉・
  枠・庇・マット）は入れない。屋内は外装と別に作っているので、外装の窓割りやルーバー、
  外へ張り出した柱・庇・塔が屋内の窓の真ん前をふさぐ。扉の前の石張り（天端 APRON_TOP 以下の
  面）は地面なので残す。
- 屋内の入口の前（開口の両脇へ LANE_SIDE m、外周から外へ LANE_DEPTH m）にかかる屋外の
  設備（花壇・ベンチ・照明柱・看板・自販機・ゴミ箱）は物ごと入れず、幹がそこから
  LANE_TREE m 以内の木も入れない。自分の棟の立て看板（キャンパスで入口の脇に立てたもの）と、
  地面・水面・ほかの棟は残す。
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
from kcd_lib import entrances
from kcd_lib.mesh import MeshBuilder, _area3, _clip_half, _is_convex, _newell, _triangulate
from kcd_lib.site import _PLANT_RINGS, _plant_z

from .spec import campus_frame

CAMPUS_FILES = ("campus.fbx", "trees.fbx")
IMPORT_OPTS = dict(axis_forward="-Z", axis_up="Y")

RADIUS = 30.0        # 外周を四方へ広げる幅。この矩形の中を入れる
DZ = -0.03           # 近景全体を下げる量
Z_MIN = -0.045       # これより下の頂点は持ち上げる（OuterGround -0.05 / OutsideGround -0.06 より上に置く）
Z_BURIED = -0.001    # キャンパスでこれより下にしか無い面（車道の帯の埋まった側面）は捨てる
TREE_MARGIN = 0.3    # 樹冠と外周のあいだに空ける幅
PLANT_FULL = 12.0    # 花壇の株をそのままの形で入れる距離（外周から）。これより遠いと簡略形
TOP_RING = _PLANT_RINGS[0][0] / _PLANT_RINGS[-1][0]   # 株の天面の輪から下の輪への倍率
BLOOM_LIFT = (0.01, 0.06)   # 花の底が株の表面から浮く高さ、底から頂点までの高さ（site._plant）
PLANT_SEG = 7        # 株の角数（site._plant の seg）
PLANT_FACES = PLANT_SEG * (len(_PLANT_RINGS) - 1) + 1   # 株 1 つの面数（側面 + 天面）
EPS = 1e-6
# 屋内の入口の前の通り道
LANE_SIDE = 1.0      # 開口の端から通り道の端まで
# 外周から外への長さ。キャンパスで扉の前に木・ベンチ・照明柱を置かない長さ（風除室の先から）。
# 屋内の入口には風除室が無いので外周から測る
LANE_DEPTH = entrances.KEEP_CLEAR
LANE_TREE = 0.5      # 幹と通り道のあいだに空ける幅（幹の太さぶん）
SIGN_TOL = 0.05      # 立て看板の位置と看板の外接矩形の照合の余裕
DOOR_TOL = 0.05      # 扉の部材の範囲の余裕
APRON_TOP = entrances.APRON_Z + 0.005   # 自分の扉の部材のうち、これより低い面（石張り）は残す
TOUCH = 0.02         # 外接箱がこれより近い部品は 1 つの物とみなす
GRID = 2.0           # TOUCH の判定に使う格子の幅

EXT_PREFIX = "ext_"          # 近景のメッシュ名の頭。Unity 側で当たり判定を外す目印（#60）
TREES_SUFFIX = "_trees"      # 木だけのメッシュ ext_<id>_trees
# build_campus が付ける名前
TREE_PREFIX = "tree_mesh_"   # trees.fbx の樹種ごとの原型
TREE_GROUP_PREFIX = "trees_" # trees.fbx の樹種ごとの親 Empty（子が 1 本ずつの木）
BLD_PREFIX = "bld_"          # campus.fbx の棟の外装
ENTRANCES = "bld_entrances"  # campus.fbx の全棟の扉まわり（entrances.build）
GROUND = ("site_ground", "site_water")   # 通り道にかかっても残す面
BEDS = "site_props_beds"     # campus.fbx の花壇


class Source:
    """campus.fbx / trees.fbx から読んだ面（ワールド座標）と木の配置。"""

    def __init__(self):
        # (出どころ, マテリアル, [(x, y, z)], (xmin, xmax, ymin, ymax), zmax, 株)
        # 株 = None か (株の番号, 簡略形か)。花壇の株の面と花、その簡略形（find_plants）
        self.polys = []
        # polys と同じ長さ。屋外の設備の面なら物の番号（prop_groups）、ほかは None
        self.groups = []
        self.plants = []   # 株の中心 (x, y)
        self.doors = {}    # 棟 -> [entrances.plan の扉]
        self.signs = {}    # 棟 -> [立て看板の位置 (x, y)]
        self.protos = {}   # 樹種 -> (頂点, [(頂点番号, マテリアル)], 樹冠の半径, 原型の姿勢)
        self.trees = []    # (樹種, 4x4 行列, (x, y), 倍率)


def _mat_name(mat):
    # 読み込み時に同名があると ".001" が付く
    return mat.name.split(".")[0] if mat is not None else "concrete_grey"


def load(campus_dir, data):
    """今のシーンに 2 つの FBX を読み込み、面と木を Python のデータに写して返す。

    data は campus.json（spec.load_campus）。扉の位置を build_campus と同じ計算で出す。
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
    src.groups = prop_groups(src.polys)
    ctx = {}
    for dr in entrances.plan(data, campus_frame(data), ctx):
        src.doors.setdefault(dr["id"], []).append(dr)
    for bid, xy, _z, _yaw in ctx["sign"]:
        src.signs.setdefault(bid, []).append(xy)

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


class _Sets:
    """union-find。"""

    def __init__(self, items):
        self.parent = {i: i for i in items}

    def root(self, i):
        par = self.parent
        while par[i] != i:
            par[i] = par[par[i]]
            i = par[i]
        return i

    def join(self, a, b):
        a, b = self.root(a), self.root(b)
        if a != b:
            self.parent[a] = b


def _join_shared(polys, idx):
    """idx の面のうち頂点を共有するものを 1 つにまとめた _Sets。"""
    sets = _Sets(idx)
    seen = {}
    for i in idx:
        for q in polys[i][2]:
            sets.join(i, seen.setdefault(_key(q), i))
    return sets


def _box3(pts, b=None):
    """pts を含む軸平行な箱 (xmin, xmax, ymin, ymax, zmin, zmax)。b があれば広げる。"""
    xs = [q[0] for q in pts]
    ys = [q[1] for q in pts]
    zs = [q[2] for q in pts]
    nb = (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))
    if b is None:
        return nb
    return (min(b[0], nb[0]), max(b[1], nb[1]), min(b[2], nb[2]), max(b[3], nb[3]),
            min(b[4], nb[4]), max(b[5], nb[5]))


def _touch(a, b, gap):
    return all(a[k] <= b[k + 1] + gap and b[k] <= a[k + 1] + gap for k in (0, 2, 4))


def prop_groups(polys):
    """屋外の設備（地面・水面・棟の面以外）の面を物ごとにまとめる。

    polys と同じ長さのリストを返す。設備の面には物の番号、ほかは None。
    頂点を共有する面をまとめたうえで、同じ出どころのまとまりどうしで外接箱が TOUCH 以内に
    接するものもまとめる（ベンチの座と脚、花壇の縁と株は頂点を共有しない別の箱）。
    出どころが違うもの（花壇の脇のベンチなど）はまとめない。"""
    idx = [i for i, p in enumerate(polys)
           if p[0] not in GROUND and not p[0].startswith(BLD_PREFIX)]
    sets = _join_shared(polys, idx)
    boxes = {}
    for i in idx:
        r = sets.root(i)
        boxes[r] = _box3(polys[i][2], boxes.get(r))
    grid = {}
    for r, b in boxes.items():
        name = polys[r][0]
        for gx, gy in _box_cells(b, TOUCH, GRID):
            cell = grid.setdefault((name, gx, gy), [])
            for o in cell:
                if _touch(b, boxes[o], TOUCH):
                    sets.join(r, o)
            cell.append(r)
    out = [None] * len(polys)
    for i in idx:
        out[i] = sets.root(i)
    return out


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
    sets = _join_shared(polys, leaf)
    groups = {}
    for i in leaf:
        groups.setdefault(sets.root(i), []).append(i)

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


def _box_cells(b, pad, size):
    """外接矩形 b を四方へ pad 広げた範囲にかかる格子のます。"""
    gx0, gy0 = _cell(b[0] - pad, b[2] - pad, size)
    gx1, gy1 = _cell(b[1] + pad, b[3] + pad, size)
    return [(gx, gy) for gx in range(gx0, gx1 + 1) for gy in range(gy0, gy1 + 1)]


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


def entrance_lane(sp, door_gap):
    """屋内の入口の前の通り道（建物ローカルの矩形 (x0, x1, y0, y1)）。

    door_gap は入口の開口の (x0, x1)（common.envelope）。None なら通り道も None。"""
    if door_gap is None:
        return None
    c = 0.5 * (door_gap[0] + door_gap[1])
    hw = 0.5 * abs(door_gap[1] - door_gap[0]) + LANE_SIDE
    return (c - hw, c + hw, sp.y_face - LANE_DEPTH, sp.y_face)


def _overlaps(b, rect):
    return b[0] < rect[1] and b[1] > rect[0] and b[2] < rect[3] and b[3] > rect[2]


def _holds(b, pts):
    return any(b[0] - SIGN_TOL <= x <= b[1] + SIGN_TOL and b[2] - SIGN_TOL <= y <= b[3] + SIGN_TOL
               for x, y in pts)


def in_door(dr, pts):
    """面（ワールド座標）の重心が扉 dr の部材の範囲に入るか。

    扉の部材（entrances.build_one）は、扉の座標（s = 壁沿い、d = 壁から外へ）で
    |s| <= 石張り・庇・風除室の半幅、-BURY <= d <= 石張り・庇・風除室の出 に収まる。"""
    cx = sum(q[0] for q in pts) / len(pts) - dr["origin"][0]
    cy = sum(q[1] for q in pts) / len(pts) - dr["origin"][1]
    s = cx * dr["t"][0] + cy * dr["t"][1]
    d = cx * dr["n"][0] + cy * dr["n"][1]
    s_max = max(dr["APRON_S"], dr["CAN_S"], dr["WO"]) + DOOR_TOL
    d_max = max(dr["APRON_D"], dr["CAN_D"], dr["D"]) + DOOR_TOL
    return abs(s) <= s_max and -entrances.BURY - DOOR_TOL <= d <= d_max


def build(sp, src, top, door_gap=None, radius=RADIUS):
    """1 棟ぶんの近景。([MeshBuilder], 集計) を返す。

    top は屋内の一番高い点の z、door_gap は屋内の入口の開口の (x0, x1)（Ctx.door_gap）。

    集計:
      tris        入れた三角数（出どころ別。棟の外装はまとめて bld、木は trees）
      culled_tris 裏を向くので入れなかった三角数（切り出したあとの数。site / trees）
      plants      入れた花壇の株の数（full = 元の形、simple = 簡略形）
      trees       入れた木の本数（placed）と、樹冠が外周にかかるので入れなかった本数（skipped）
      own         入れなかった自分の棟の面の数（外装 bld、石張りを除く扉の部材 door）
      entrance    屋内の入口の前の通り道にかかるので入れなかった設備の数（props）とその面の数
                  （prop_faces）、木の本数（trees）。自分の棟の立て看板は通り道にかかっても入れる"""
    env = (sp.x0, sp.x1, sp.y_face, sp.y_back)
    box = (sp.x0 - radius, sp.x1 + radius, sp.y_face - radius, sp.y_back + radius)
    lane = entrance_lane(sp, door_gap)
    corners = _corners(sp, top)

    cand, gbox, own_faces = _collect(sp, src, env, box)
    signs = [_to_local(sp, xy + (0.0,))[:2] for xy in src.signs.get(sp.id, [])]
    blocked = set() if lane is None else {
        g for g, b in gbox.items() if _overlaps(b, lane) and not _holds(b, signs)}
    mb, site = _site_mesh(sp, cand, blocked, box, env, corners)
    tb, forest = _tree_mesh(sp, src, env, lane, corners, radius)

    tris = dict(site["tris"])
    if forest["tris"]:
        tris["trees"] = forest["tris"]
    used = site["used"]
    stats = {
        "tris": tris,
        "culled_tris": {"site": site["culled"], "trees": forest["culled"]},
        "plants": {"full": sum(1 for v in used.values() if not v),
                   "simple": sum(1 for v in used.values() if v)},
        "trees": {"placed": forest["placed"], "skipped": forest["skipped"]},
        "own": own_faces,
        "entrance": {"props": len(blocked), "prop_faces": site["lane_faces"],
                     "trees": forest["lane"]},
    }
    return [b for b in (mb, tb) if b.faces], stats


def _collect(sp, src, env, box):
    """範囲 box に入る面を建物ローカルへ写し、設備は物ごとの外接矩形を取る。

    自分の棟の外装と扉の部材、使わないほうの形の株（外周から PLANT_FULL より遠い株は簡略形、
    近い株は元の形を使う）は入れない。
    (候補の面 [(name, mat, local, plant, 物の番号)], {物の番号: 外接矩形}, 集計 own) を返す。"""
    wx0, wx1, wy0, wy1 = _world_bbox(sp, box)
    own = BLD_PREFIX + sp.id
    doors = src.doors.get(sp.id, [])
    cand = []
    gbox = {}   # 物の番号 -> 建物ローカルの外接矩形
    own_faces = {"bld": 0, "door": 0}
    far = {}    # 株の番号 -> 外周から PLANT_FULL より遠いか
    for i, (name, mat, pts, bb, zmax, plant) in enumerate(src.polys):
        if bb[1] < wx0 or bb[0] > wx1 or bb[3] < wy0 or bb[2] > wy1:
            continue
        if zmax < Z_BURIED:
            continue
        if name == own:
            own_faces["bld"] += 1
            continue
        if name == ENTRANCES and zmax > APRON_TOP and any(in_door(dr, pts) for dr in doors):
            own_faces["door"] += 1
            continue
        if plant is not None:
            n, simple = plant
            if n not in far:
                cx, cy, _ = _to_local(sp, src.plants[n] + (0.0,))
                far[n] = _rect_dist(cx, cy, env) > PLANT_FULL
            if far[n] != simple:
                continue
        local = [_to_local(sp, p) for p in pts]
        g = src.groups[i]
        if g is not None:
            gbox[g] = _box3(local, gbox.get(g))
        cand.append((name, mat, local, plant, g))
    if doors and not own_faces["door"]:
        print("[exterior] 警告: %s の扉の部材が bld_entrances に見つからない"
              "（entrances の寸法が変わった？）" % sp.id)
    return cand, gbox, own_faces


def _site_mesh(sp, cand, blocked, box, env, corners):
    """候補の面を外周の外へ切り出して ext_<id> に入れる。blocked の物の面は入れない。

    (MeshBuilder, 集計) を返す。集計は tris（出どころ別の三角数）、culled（裏向きで
    入れなかった三角数）、used（{入れた株の番号: 簡略形か}）、lane_faces（blocked で除いた面の数）。"""
    mb = MeshBuilder(EXT_PREFIX + sp.id)
    tris = {}
    culled = 0
    used = {}
    lane_faces = 0
    for name, mat, local, plant, g in cand:
        if g in blocked:
            lane_faces += 1
            continue
        pieces = outside_pieces(local, box, env)
        if not pieces:
            continue
        n_tris = sum(len(pc) - 2 for pc in pieces)
        if not faces_viewer(local, corners):
            culled += n_tris
            continue
        for pc in pieces:
            mb.add_face(_lift(pc), mat)
        key = "bld" if name.startswith(BLD_PREFIX) else name
        tris[key] = tris.get(key, 0) + n_tris
        if plant is not None:
            used[plant[0]] = plant[1]
    return mb, {"tris": tris, "culled": culled, "used": used, "lane_faces": lane_faces}


def _tree_mesh(sp, src, env, lane, corners, radius):
    """幹が外周から radius + 樹冠の半径 以内の木を丸ごと ext_<id>_trees に入れる。

    樹冠が外周 + TREE_MARGIN にかかる木と、幹が入口の前の通り道 lane から LANE_TREE 以内の木は
    入れない。(MeshBuilder, 集計) を返す。集計は tris / culled（三角数）と、木の本数 placed /
    skipped（樹冠が外周にかかる）/ lane（通り道）。"""
    tb = MeshBuilder(EXT_PREFIX + sp.id + TREES_SUFFIX)
    tris = culled = placed = skipped = lane_trees = 0
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
        if lane is not None and _rect_dist(lx, ly, lane) <= LANE_TREE:
            lane_trees += 1
            continue
        world = [m @ v for v in verts]
        local = [_to_local(sp, (w.x, w.y, w.z)) for w in world]
        n_tris = 0
        for idx, fmat in faces:
            pc = [local[i] for i in idx]
            if not faces_viewer(pc, corners):
                culled += len(idx) - 2
                continue
            tb.add_face(_lift(pc), fmat)
            n_tris += len(idx) - 2
        if n_tris:
            tris += n_tris
            placed += 1
    return tb, {"tris": tris, "culled": culled, "placed": placed, "skipped": skipped,
                "lane": lane_trees}
