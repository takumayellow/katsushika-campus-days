"""樹木・ベンチ・照明柱などの小物。"""

import math
import random

from . import geom
from .mesh import MeshBuilder


# --------------------------------------------------------------------------- #
#  樹木（#51）
# --------------------------------------------------------------------------- #
# 以前は「低ポリ楕円体（add_blob）を核 1 個 + 枝先 5〜9 個ぶんだけ積む」作りだった。
# それを全部やめて、幹の根元から樹冠の天辺までを閉じた回転体 1 枚で引くことにした。
# やめた理由は 4 つとも実測で出ている:
#   - かたまり同士が交差したまま結合されておらず、独立シェルが keyaki 17 / round 7 /
#     pine 5 個あった。中に潜りこんだ面はカメラに裏を向けるので、Cull Front の
#     アウトライン殻（KCD_Toon、幅は最大 0.03 m）が面まるごと黒紫で塗りつぶす。
#     実測では keyaki が 30 m から最大 1.377%、pine は真下で 29.494% の裏面が出ていた。
#   - 幹が cap_top=False の開いた筒で z=3.4 止まり。樹冠の底まで 2.37 m 空いていた。
#   - seg=6 / rings=3 の楕円体は上下の半径が同じ（sin60 = sin120）樽で、遠景では
#     六角形の宝石に見えた。
#   - 4 段ランプの明るい 2 段がかたまりの「上面キャップ」に載っていて、目線 1.6 m の
#     プレイヤーには 1 段も見えていなかった（leaf_top 0.0% / leaf_light 0.0%）。
_LEAF_RAMP = ("leaf_dark", "leaf", "leaf_light", "leaf_top")


def _leaf_at(z, crown):
    """crown = (樹冠の下端 z, 上端 z, 切れ目 3 つ) から 4 段ランプを選ぶ。

    4 段は「樹冠の中の高さ」で横向きの面に割り当てる。上面キャップに置く以前の作りは
    目線 1.6 m から 1 段も見えなかった（見えていたのは keyaki で leaf_dark 90.5%、
    round で leaf 85.6% の 1 段だけ）。横を向いた面に上から順に並べれば、そのまま
    立っている人の目に入る。
    切れ目は下端 0.0 〜 上端 1.0 の割合。段の数は樹種ごとに違う（松は 10 段、丸木は
    6 段）ので樹種ごとに決める。真上を向いた面は目線 1.6 m からは見えないので、
    等分（0.25/0.50/0.75）ではなく明るい側を下へ寄せてある。
    """
    z0, z1, cuts = crown
    t = 0.0 if z1 <= z0 else (z - z0) / (z1 - z0)
    return _LEAF_RAMP[sum(1 for c in cuts if t >= c)]


def add_lathe(mb, cx, cy, levels, crown=None, seg=12, arms=0, phase=0.0,
              noise=0.0, lean=0.0, lean_z=2.0, seed=0):
    """回転体（ロクロ）。levels は下から上へ並べた (半径, z, 腕, マテリアル)。

    幹から樹冠までを 1 枚の閉じた面で作る。独立したかたまりを積まないので
    シェル同士の交差が原理的に起きず、裏面も出ない。r = 0 の段は極（扇）。

    arm > 0 の段は半径を r + arm * ((1 + cos(arms*(theta - phase))) / 2) ** 1.5 にして、
    arms 本の腕を張り出させる。ケヤキの三又も、丸木と松の樹冠の崩しもこれで作る。
    かたまりを別に足すのに比べて三角形が 1 枚も増えないのが利点で、試作段階で
    置いていた「埋めこみこぶ 8 個」はこれに置き換えた。こぶは 85,248 tri
    （キャンパス全体の 43%）を食うのに、輪郭へ足していた画素は実測で
    keyaki 2.99% / round 1.28% / pine 0.00% しかなかった。

    lean は z = lean_z から上だけを +x 方向へ寄せる剪断（天辺で lean m）。
    完全な回転体だと「インスタンスを Z 回転させる」ことと「別の方位から見る」ことが
    同値になり、site.py が 1 本ごとに渡せる唯一の形の差（Z 回転）が効かなくなる。
    実測では 24 通りに回した 276 ペアのシルエット XOR が round 3.83% / pine 3.00% で、
    588 本のうち 384 本が「回しても同じ絵」の判子になっていた。lean=0.35 で
    round 11.18% / pine 13.29% と現行（13.90%）並みまで戻る。三角形は増えない。
    lean_z=2.0 は下げてはいけない。根元から傾けると身長 1.8 m での最大半径が
    0.292 → 0.357 m になり、Unity 側の幹カプセル（半径 0.3 m）を突き抜ける。

    mat が None の段は crown=(z0, z1, cuts) の高さから 4 段ランプで決める。
    noise は半径の ±倍率。乱数は random.Random(seed) 固定なので、同じコードなら
    同じ FBX になる。
    """
    rng = random.Random(seed)
    ztop = max(l[1] for l in levels)
    rings = []
    for r, z, arm, _m in levels:
        lx = lean * max(0.0, (z - lean_z) / (ztop - lean_z)) if ztop > lean_z else 0.0
        if r <= 1e-6 and arm <= 1e-6:
            rings.append((None, lx))    # 極
            continue
        pts = []
        for i in range(seg):
            th = 2.0 * math.pi * i / seg
            w = (0.5 * (1.0 + math.cos(arms * (th - phase)))) ** 1.5 if arm > 0 else 0.0
            rr = (r + arm * w) * (1.0 + rng.uniform(-noise, noise))
            pts.append((cx + lx + rr * math.cos(th), cy + rr * math.sin(th), z))
        rings.append((pts, lx))
    for k in range(len(levels) - 1):
        (lo, lo_x), (hi, hi_x) = rings[k], rings[k + 1]
        mat = levels[k][3]
        if mat is None:
            mat = _leaf_at(0.5 * (levels[k][1] + levels[k + 1][1]), crown)
        if lo is None and hi is None:
            continue
        if lo is None:                  # 下端の極
            apex = (cx + lo_x, cy, levels[k][1])
            for i in range(seg):
                mb.add_face([hi[(i + 1) % seg], hi[i], apex], mat)
        elif hi is None:                # 上端の極
            apex = (cx + hi_x, cy, levels[k + 1][1])
            for i in range(seg):
                mb.add_face([lo[i], lo[(i + 1) % seg], apex], mat)
        else:
            for i in range(seg):
                j = (i + 1) % seg
                mb.add_quad(lo[i], lo[j], hi[j], hi[i], mat)


# ケヤキ: 幹 → 三又 → 杯状の樹冠までを 1 枚の回転体で作る。
# (半径, z, 腕の張り出し, マテリアル) を下から。mat=None は高さから 4 段で決める。
# 幹のテーパー 0.280 → 0.215 は、身長 1.8 m 以下の最大半径が 0.292 m に収まるように
# 決めてある（Unity の幹カプセルは半径 0.3 m）。以前の round 0.837 / pine 2.758 は
# プレイヤーが葉の中を歩ける値だった。
# 腕は z=3.50 から樹冠の上まで残す。途中で 0 に落とすと 30 m でキノコに見える。
_KEYAKI_LEVELS = [
    (0.280, 0.00, 0.00, "trunk"),
    (0.270, 2.60, 0.00, "trunk"),
    (0.245, 3.50, 0.20, "trunk"),   # ここから三又が開きはじめる
    (0.215, 4.50, 0.50, "trunk"),
    (0.260, 5.35, 0.85, None),      # 樹冠の下端。腕はまだ 3 本に割れている
    (1.050, 6.30, 1.00, None),
    (1.900, 7.30, 0.85, None),
    (2.450, 8.20, 0.55, None),      # いちばん広いのは上寄り = 杯状
    (2.400, 9.05, 0.30, None),
    (1.350, 9.60, 0.12, None),
    (0.000, 9.95, 0.00, None),
]
_KEYAKI_CROWN = (5.35, 9.95, (0.22, 0.46, 0.68))

# 丸木: 幹から球へ。arms=5 / phase=0.6 は seg=10 の頂点と 1 つおきに噛み合うので、
# 平均半径をほとんど変えずに輪郭だけが五角星に崩れる（方位ごとのシルエット面積の
# ばらつきが 1.84% → 4.07%）。
_ROUND_LEVELS = [
    (0.230, 0.00, 0.00, "trunk"),
    (0.210, 1.95, 0.00, "trunk"),
    (0.340, 2.55, 0.05, None),      # 樹冠の下端。目線 1.6 m より上にある
    (1.100, 3.05, 0.16, None),
    (1.720, 3.62, 0.22, None),
    (1.950, 4.28, 0.24, None),
    (1.800, 4.92, 0.20, None),
    (1.240, 5.58, 0.12, None),
    (0.000, 6.20, 0.00, None),
]
_ROUND_CROWN = (2.55, 6.20, (0.15, 0.30, 0.66))

# 松: 4 段の傘を 1 本の折れ線で表して、丸ごと 1 枚の回転体にする。段ごとに
# 「外へ出て少し下がる」→「すぼみながら上がる」を繰り返す。傘の裏と表が別の段に
# なるので、4 段ランプがそのまま横向きの面に乗る。
# いちばん下の裾は z=2.80。目線 1.6 m の頭上 1.2 m にあり、中に入らないし、
# 真下に立っても傘のふちの外に空が見える。以前は裾が z=1.35 / 半径 2.45 で、
# 目線のカメラが傘の内側に入って裏面画素率が 29.494% 出ていた。
# 裾の半径は「細くしすぎない」こと。アウトライン殻の幅は 0.03 m 固定なので、木を
# 細くすると木の画素が減る一方で線の画素は増える。1.20 倍する前の裾 2.300 では
# 30 m のアウトライン占有が現行 4.24% に対して 6.28% と、かえって悪化していた。
_PINE_LEVELS = [
    (0.270, 0.00, 0.00, "trunk"),
    # 裾の裏は真下に立つと視界いっぱいになる。高さで色を決めるとぜんぶ 1 段
    # （leaf_dark）で塗りつぶされるので、ここだけ内側と外側で 2 段に割る。
    # 実物でも傘のふちのほうが光が回りこんで明るい。割れ目は 1.050 m。裾を 1.20 倍に
    # 太らせたときに 1.500 のままにしたら、真下のカメラ（垂直画角 55 度・幹から
    # 0.45 m）の画面にはまだ内側しか入らず leaf_dark 98.4% だった。1.050 なら 64.3%。
    (0.250, 2.95, 0.00, "leaf_dark"),
    (1.050, 2.88, 0.00, "leaf"),
    (2.760, 2.80, 0.20, None),
    (0.600, 4.45, 0.00, None),
    (2.256, 4.30, 0.16, None),
    (0.500, 5.70, 0.00, None),
    (1.656, 5.55, 0.13, None),
    (0.400, 6.70, 0.00, None),
    (1.116, 6.58, 0.09, None),
    (0.255, 7.70, 0.00, None),      # 先端は数学的な 1 点ではなく、鈍い円錐にする
    (0.000, 8.10, 0.00, None),
]
# 松だけ切れ目がはっきり低い。円錐なので面積が下の段に集中していて、等分に近い
# 切れ目（0.22/0.46/0.72）だといちばん大きい第 1 段の上面が丸ごと leaf_dark になり、
# 明るい 2 段が可視 23.3% と基準 25% に届かなかった。0.14/0.28/0.55 にすると
# 第 1 段の上面が leaf、第 2 段が leaf_light、第 3 段から上が leaf_top に回り、
# 3〜30 m 等重みで leaf_light+leaf_top 38.8% / leaf_dark 3.1% になる。
# leaf_dark が遠景でほぼ消えるのは意図どおり。松の leaf_dark は「傘の裏」担当で、
# 木の下に立ったとき（真下 0 m）に 64.3% と支配的になる。
_PINE_CROWN = (2.80, 8.10, (0.14, 0.28, 0.55))

# 幹から上を +x へ寄せる量。回転体のままだと site.py の Z 回転が輪郭を変えられず、
# 588 本中 384 本（round 252 + pine 132）が「回しても同じ絵」の判子になる。
_LEAN = 0.35


def tree_keyaki(name="tree_mesh_keyaki"):
    """ケヤキ風。杯状に枝分かれし、上が広い。高さ 9.95 m。"""
    mb = MeshBuilder(name)
    add_lathe(mb, 0, 0, _KEYAKI_LEVELS, crown=_KEYAKI_CROWN, seg=12, arms=3,
              phase=0.4, noise=0.05, lean=_LEAN, seed=11)
    return mb


def tree_round(name="tree_mesh_round"):
    """丸い樹冠の中木。高さ 6.2 m。"""
    mb = MeshBuilder(name)
    add_lathe(mb, 0, 0, _ROUND_LEVELS, crown=_ROUND_CROWN, seg=10, arms=5,
              phase=0.6, noise=0.06, lean=_LEAN, seed=23)
    return mb


def tree_pine(name="tree_mesh_pine"):
    """常緑の円錐樹。高さ 8.1 m。"""
    mb = MeshBuilder(name)
    add_lathe(mb, 0, 0, _PINE_LEVELS, crown=_PINE_CROWN, seg=10, arms=5,
              phase=0.25, noise=0.05, lean=_LEAN, seed=41)
    return mb


TREE_SPECIES = [
    ("keyaki", tree_keyaki),
    ("round", tree_round),
    ("pine", tree_pine),
]


def add_bench(mb, x, y, ang, back=True):
    """ベンチ。ローカル +v 側に背もたれ（back=True）。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)

    # 座面
    mb.add_quad(P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(-0.9, -0.25, 0.38), P(-0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.38), P(0.9, -0.25, 0.38), "wood")
    mb.add_quad(P(-0.9, 0.25, 0.38), P(0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(0.9, -0.25, 0.38), P(-0.9, -0.25, 0.38),
                P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45), "wood")
    # 脚
    for du in (-0.7, 0.7):
        p = P(du, 0, 0)
        mb.add_box_c(p[0], p[1], 0.0, 0.10, 0.55, 0.40, "metal_grey")
    if back:
        add_box_rot(mb, x, y, ang, -0.9, 0.22, 0.50, 0.9, 0.28, 0.92, "wood")
        for du in (-0.7, 0.7):
            add_box_rot(mb, x, y, ang, du - 0.03, 0.22, 0.40, du + 0.03, 0.28, 0.50, "metal_grey")


def add_lamp(mb, x, y, h=4.0):
    mb.add_cylinder(x, y, 0.0, h, 0.09, "metal_white", seg=8, cap_top=False)
    mb.add_box_c(x, y, h, 0.55, 0.30, h + 0.22, "metal_white", top_mat="metal_white")
    mb.add_box_c(x, y, h - 0.08, 0.45, 0.22, h, "line_white")


def scatter_positions(polys, density, blocked, rng, jitter=1.0, limit=100000):
    """ポリゴン内に density [本/m^2] でランダム散布する（blocked(x,y)==True は除外）。"""
    out = []
    for poly in polys:
        x0, y0, x1, y1 = geom.bbox(poly)
        area = abs(geom.poly_area(poly))
        n = int(area * density)
        tries = 0
        placed = 0
        while placed < n and tries < n * 12 and len(out) < limit:
            tries += 1
            px = rng.uniform(x0, x1)
            py = rng.uniform(y0, y1)
            if not geom.point_in_poly((px, py), poly):
                continue
            if blocked(px, py):
                continue
            out.append((px, py))
            placed += 1
    return out


def line_positions(a, b, step, blocked, offset=0.0):
    """a→b に等間隔で並べる（offset は左手側へのずらし）。"""
    d = geom.sub(b, a)
    L = geom.length(d)
    if L < step:
        return []
    e = geom.mul(d, 1.0 / L)
    nrm = (-e[1], e[0])
    out = []
    n = int(L / step)
    for i in range(n + 1):
        p = geom.add(geom.add(a, geom.mul(e, i * step)), geom.mul(nrm, offset))
        if not blocked(p[0], p[1]):
            out.append(p)
    return out


# --------------------------------------------------------------------------- #
#  外構小物（看板・駐輪場・自販機・ゴミ箱）
# --------------------------------------------------------------------------- #
def _local(x, y, ang):
    """(x, y) を原点に ang 回転したローカル座標 (du, dv, z) → ワールド 3D 点。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)
    return P


def add_box_rot(mb, x, y, ang, du0, dv0, z0, du1, dv1, z1, mat, top=True):
    """ローカル座標 (du, dv) で指定した直方体を ang 回転して置く。"""
    P = _local(x, y, ang)
    poly = [P(du0, dv0, 0)[:2], P(du1, dv0, 0)[:2], P(du1, dv1, 0)[:2], P(du0, dv1, 0)[:2]]
    mb.add_prism(poly, z0, z1, mat, mat if top else None, None)


def add_pylon_sign(mb, x, y, ang, zc, w=2.4, h=1.2):
    """建物名の立て看板。ローカル +u が板の正面（法線）。文字は Unity 側で貼る。"""
    hw = w * 0.5
    band = 0.28
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc - h * 0.5, 0.05, hw, zc + h * 0.5 - band,
                "sign_plate")
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc + h * 0.5 - band, 0.05, hw, zc + h * 0.5,
                "tus_green")
    P = _local(x, y, ang)
    for dv in (-hw + 0.25, hw - 0.25):
        p = P(0.0, dv, 0.0)
        mb.add_cylinder(p[0], p[1], 0.0, zc - h * 0.5, 0.05, "metal_grey", seg=8,
                        cap_top=False)


def _beam_uz(mb, P, du0, z0, du1, z1, w, hv, mat, dvc=0.0):
    """ローカルの u-z 平面で (du0, z0)-(du1, z1) を結ぶ、断面 w × 2*hv の角棒。

    add_box_rot は軸に平行な直方体しか作れないので、斜めの棒（自転車のダウンチューブ
    やフォーク）はここで面を直接積む。dvc は v 方向の中心で、車輪の板（v = 0）から
    逃がしたいチェーンステーとフォークに使う。
    巻き方: 断面は u-z で時計回りに並べる。こうすると dvc+hv 側の面の法線が +v、
    dvc-hv 側が -v、側面が断面の外を向く（Cull Back で消えない）。"""
    ddu, dz = du1 - du0, z1 - z0
    ln = math.hypot(ddu, dz)
    if ln < 1e-6:
        return
    nu, nz = -dz / ln * w * 0.5, ddu / ln * w * 0.5
    sec = [(du0 + nu, z0 + nz), (du1 + nu, z1 + nz),
           (du1 - nu, z1 - nz), (du0 - nu, z0 - nz)]
    back = [P(du, dvc + hv, z) for du, z in sec]
    front = [P(du, dvc - hv, z) for du, z in sec]
    mb.add_face(back, mat)
    mb.add_face(list(reversed(front)), mat)
    for i in range(4):
        j = (i + 1) % 4
        mb.add_quad(front[i], front[j], back[j], back[i], mat)


def _disc_uz(mb, P, du, z, r, hv, mat, seg=8, dvc=0.0):
    """ローカルの u-z 平面に立つ、厚み 2*hv の n 角形の円盤。自転車の車輪。

    以前は車輪が 0.66 × 0.66 の箱で、真横から見るとただの四角だった（#46）。"""
    ring = [(du + r * math.cos(-2.0 * math.pi * i / seg),
             z + r * math.sin(-2.0 * math.pi * i / seg)) for i in range(seg)]
    back = [P(u, dvc + hv, zz) for u, zz in ring]
    front = [P(u, dvc - hv, zz) for u, zz in ring]
    mb.add_face(back, mat)
    mb.add_face(list(reversed(front)), mat)
    for i in range(seg):
        j = (i + 1) % seg
        mb.add_quad(front[i], front[j], back[j], back[i], mat)


def _wedge_uv(mb, P, du0, du1, dv0, dv1, z_at_dv0, z_at_dv1, t, mat):
    """ローカルの u-v 矩形を、v 方向に傾けた厚み t の板にする。片流れの屋根と梁。

    上面の高さは dv0 で z_at_dv0、dv1 で z_at_dv1（その間は線形）。下面は t だけ下。
    add_prism は天地が水平な柱しか作れないので、ここも面を直接積む。"""
    corners = [(du0, dv0), (du1, dv0), (du1, dv1), (du0, dv1)]   # 上から見て反時計回り

    def zt(dv):
        s = 0.0 if abs(dv1 - dv0) < 1e-9 else (dv - dv0) / (dv1 - dv0)
        return z_at_dv0 + (z_at_dv1 - z_at_dv0) * s
    top = [P(du, dv, zt(dv)) for du, dv in corners]
    bot = [P(du, dv, zt(dv) - t) for du, dv in corners]
    mb.add_face(top, mat)
    mb.add_face(list(reversed(bot)), mat)
    for i in range(4):
        j = (i + 1) % 4
        mb.add_quad(bot[i], bot[j], top[j], top[i], mat)


# 駐輪場の寸法。日本の大学の屋根付き駐輪場（片流れ + 門形フレーム + 前輪差し込み式
# ラック + 三方開放）に合わせた（#46 の実測と修正計画 D）。
RACK_PITCH = 0.45        # ラックのピッチ。実物は 0.40〜0.50
RACK_TOP = 0.30          # ラックの天端。プレイヤーの stepOffset 0.40 より低くして跨げるようにする
POST_SPAN_MAX = 2.8      # 柱の最大スパン。実物は 2.0〜3.0 m ごとの門形フレーム
BIKE_PITCH = 0.90        # 自転車はラック 2 本に 1 台
SHED_EAVE_FRONT = 2.15   # 開口側（-v）の軒高
SHED_EAVE_REAR = 2.60    # 背面側（+v）の軒高。勾配 = (後 - 前) / depth
SHED_ROOF_T = 0.06       # 屋根の厚み
SHED_STOP_H = 0.15       # 背面の車止めの高さ。以前はここに高さ 1.0 m の腰壁があった


def add_bike(mb, x, y, ang):
    """自転車（前後輪 + ダイヤモンドフレーム + サドル + ハンドル）。ローカル +u が前。

    車輪は 8 角形の円盤（半径 0.335 = 26 インチ相当、ホイールベース 1.05 m）。
    フレームの棒は車輪の円盤と交わらない線形に引いてある。以前はトップチューブ・
    シートチューブ・フォークの 3 本が前後輪の箱の中に埋まっていた（#46）。
    前ホークとチェーンステーだけは車輪をまたぐので v 方向に 0.055 逃がす
    （タイヤの厚みは ±0.025 なので触れない）。"""
    P = _local(x, y, ang)
    r, hub = 0.335, 0.525
    _disc_uz(mb, P, -hub, r, r, 0.025, "bike_tire", seg=8)
    _disc_uz(mb, P, hub, r, r, 0.025, "bike_tire", seg=8)
    bb = (-0.06, 0.27)            # ボトムブラケット
    head_lo, head_hi = (0.47, 0.76), (0.40, 1.00)   # ヘッドチューブ（フォークの上）
    seat_top = (-0.16, 0.86)
    _beam_uz(mb, P, bb[0], bb[1], 0.455, 0.80, 0.055, 0.022, "bike_frame")      # ダウンチューブ
    _beam_uz(mb, P, seat_top[0], 0.845, 0.445, 0.82, 0.045, 0.020, "bike_frame")  # トップチューブ
    _beam_uz(mb, P, bb[0], bb[1], seat_top[0], seat_top[1], 0.05, 0.022, "bike_frame")
    _beam_uz(mb, P, head_lo[0], head_lo[1], head_hi[0], head_hi[1], 0.05, 0.022, "bike_frame")
    for dv in (-0.055, 0.055):
        _beam_uz(mb, P, hub, r, head_lo[0], head_lo[1], 0.035, 0.014, "bike_frame", dvc=dv)
        _beam_uz(mb, P, -hub, r, bb[0], bb[1], 0.035, 0.014, "bike_frame", dvc=dv)
    add_box_rot(mb, x, y, ang, -0.25, -0.055, 0.88, -0.07, 0.055, 0.94, "bike_tire")
    add_box_rot(mb, x, y, ang, 0.36, -0.25, 0.97, 0.42, 0.25, 1.02, "bike_frame")


def add_bike_shed(mb, x, y, ang, length=14.0, depth=2.4, bikes=True):
    """屋根付き駐輪場。ローカル u = 長手、v = 奥行（-v 側が開口、+v 側が背面）。

    片流れの屋根を門形フレームで受け、前輪差し込み式のラックを並べた三方開放の形。
    背面は高さ SHED_STOP_H の車止めだけにしてある。以前はここに高さ 1.0 m の腰壁が
    あり、屋根（下端 2.30）との間で跳べる高さが 2.30 - 1.62 = 0.68 m しかなくて
    中から出られず、しかもラックが厚み 0.04 / ピッチ 0.60（隙間 0.56 m < カプセル
    直径 0.616 m）かつ天端 0.45 m > stepOffset 0.40 m で通ることも跨ぐこともできず、
    入ると詰んでいた（#46）。"""
    P = _local(x, y, ang)
    hl, hd = length * 0.5, depth * 0.5
    slope = (SHED_EAVE_REAR - SHED_EAVE_FRONT) / max(depth, 1e-6)

    def eave(dv):
        """その v での軒（屋根の上面）の高さ。dv = -hd で SHED_EAVE_FRONT。"""
        return SHED_EAVE_FRONT + (dv + hd) * slope

    # 屋根。妻の出 0.20、前後の出 0.25。
    ru0, ru1 = -hl - 0.20, hl + 0.20
    rv0, rv1 = -hd - 0.25, hd + 0.25
    _wedge_uv(mb, P, ru0, ru1, rv0, rv1, eave(rv0), eave(rv1), SHED_ROOF_T, "metal_grey")
    # 鼻隠し。前の軒先に見付け 0.10 を垂らす。
    add_box_rot(mb, x, y, ang, ru0, rv0, eave(rv0) - SHED_ROOF_T - 0.10,
                ru1, rv0 + 0.05, eave(rv0) - SHED_ROOF_T, "metal_grey")
    # 門形フレーム。スパンが POST_SPAN_MAX を超えないように割る（前は 4 隅の 4 本だけ）。
    pu, pv = hl - 0.25, hd - 0.15
    nbay = max(1, int(math.ceil(2.0 * pu / POST_SPAN_MAX)))
    for i in range(nbay + 1):
        du = -pu + 2.0 * pu * i / nbay
        for dv in (-pv, pv):
            p = P(du, dv, 0.0)
            mb.add_cylinder(p[0], p[1], 0.0, eave(dv) - SHED_ROOF_T, 0.045,
                            "metal_grey", seg=6, cap_top=False)
        _wedge_uv(mb, P, du - 0.05, du + 0.05, -pv, pv,
                  eave(-pv) - SHED_ROOF_T, eave(pv) - SHED_ROOF_T, 0.09, "metal_grey")
    # 母屋 2 本。屋根の裏に沿わせる。
    for dv in (-hd * 0.45, hd * 0.45):
        add_box_rot(mb, x, y, ang, -hl, dv - 0.04, eave(dv) - SHED_ROOF_T - 0.06,
                    hl, dv + 0.04, eave(dv) - SHED_ROOF_T, "metal_grey")
    # 背面の車止め（腰壁の代わり）。
    add_box_rot(mb, x, y, ang, -hl, hd - 0.12, 0.0, hl, hd, SHED_STOP_H, "concrete_grey")
    # ラックの基礎レールと、前輪差し込み式のラック（千鳥に 0.05 ずらす）。
    add_box_rot(mb, x, y, ang, -hl + 0.3, -0.15, 0.0, hl - 0.3, 0.60, 0.06, "concrete_grey")
    n = max(1, int((length - 0.6) / RACK_PITCH))
    du0 = -(n - 1) * RACK_PITCH * 0.5
    for i in range(n):
        du = du0 + i * RACK_PITCH
        dv = -0.10 + (0.05 if i % 2 else 0.0)
        add_box_rot(mb, x, y, ang, du - 0.02, dv, 0.06, du + 0.02, dv + 0.65,
                    RACK_TOP, "metal_grey")
        # 自転車はラックの刃ではなく溝に入れる（半ピッチずらす）。ノーズは奥（+v）。
        # 何台か抜いて、満車に見えないようにする。
        if bikes and i % 2 == 0 and (i // 2) % 5 != 3:
            p = P(du + RACK_PITCH * 0.5, -0.10, 0.0)
            add_bike(mb, p[0], p[1], ang + math.pi * 0.5)


def add_vending(mb, x, y, ang, color="vending_red"):
    """自販機 1.0 (幅) × 0.75 (奥行) × 1.85 m。ローカル +u が正面。"""
    add_box_rot(mb, x, y, ang, -0.375, -0.5, 0.0, 0.375, 0.5, 1.85, color)
    P = _local(x, y, ang)
    mb.add_quad(P(0.39, -0.42, 0.95), P(0.39, 0.42, 0.95),
                P(0.39, 0.42, 1.75), P(0.39, -0.42, 1.75), "glass_dark")
    mb.add_quad(P(0.39, -0.42, 0.25), P(0.39, 0.42, 0.25),
                P(0.39, 0.42, 0.60), P(0.39, -0.42, 0.60), "metal_grey")


def add_trash_can(mb, x, y):
    mb.add_cylinder(x, y, 0.0, 0.85, 0.28, "bin_green", seg=10, cap_top=True)
    mb.add_cylinder(x, y, 0.85, 0.92, 0.30, "metal_grey", seg=10, cap_top=True)
