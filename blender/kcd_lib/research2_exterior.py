"""第2研究棟（と 1〜2F の食堂）の外観。仕様は docs/ref/interiors/research2/spec.md の §0・§1・§6 と #293。

2 層の食堂が台座で、その上の 3〜6F の研究棟は台座の外形のうち矩形の範囲だけに建つ。
研究棟の面は明るい暖灰色の深い格子（床の線の帯が縦の桟より前に出て、窓は奥に引っ込む）。
西の端にレンガの塊（南西の面にガラスの階段の帯）、屋上に濃い灰色の目隠し、台座の屋根は屋上緑化。
全面ガラスは食堂の面（南南西から西北西の曲面とロビーの面）だけ。曲面の 2F は屋外のテラスで、
ガラスの壁を奥に引き、縁に低い立ち上がりと金属の柵を回す。曲面に沿って 2F のテラスへ上がる外階段がある。"""
import math

from . import facade, geom

FLOORS_PODIUM = 2
# 局所（原点 = 研究棟の入口の真下）からワールドへ。spec.md §0。
_YAW = math.radians(24.5514)
_ORIGIN = (6.0634, -21.6235)

# 3〜6F の床の範囲（局所 X, Y）。spec.md §0。
TOWER_RECT = (-39.64, 15.4, 31.86, 41.3)
# 北北東のレンガの階段室（局所 X, Y）。spec.md §1-5。3F から上で、研究棟の北の壁より 0.35 m 出す。
CORE_RECT = (23.3, 37.3, 31.1, 41.65)
# 西の端のレンガの塊（局所 X, Y）。研究棟の西の端に重ね、外形の西北西の突き出しの上に建つ（#293。幅は推定）。
WEST_CORE_RECT = (-44.0, 27.5, -37.0, 41.75)
# 屋上の目隠し（局所 X, Y）。研究棟の縁から引き、2 つの階段室に掛からない範囲（#293）。
ROOF_SCREEN_RECT = (-36.0, 18.5, 22.0, 38.3)
ROOF_SCREEN_H = 2.4
# 塊の頂（研究棟の屋根の上）。目隠しと同じ高さまで出す。
CORE_TOP = ROOF_SCREEN_H

# 研究棟の格子。1 マス約 3.95 m（1 段に 18 マス）、床の線の帯は 0.3 m 前に出る。
GRID_SEG = 3.95
GRID_INSET = 0.8
LEDGE_OUT = 0.3

# 食堂の面（外形の辺 index。campus.json の 15 点の順）。3〜10 = 西から南の曲面、12 = 研究棟側のロビーの面。
# 西北西の突き出し（0〜2）は裏方の壁。
GLASS_EDGES = (3, 4, 5, 6, 7, 8, 9, 10, 12)
# 2F が屋外のテラスになる辺（西北西の曲面。屋内の spec.md §1-6 と同じ (−20.20, 4.00)〜(−46.38, 32.65)）。
TERRACE_EDGES = (3, 4, 5, 6, 7)
# テラスの奥行き（2F のガラスの壁を外形の線から引く長さ）
TERRACE_SETBACK = 4.0
# 2F のテラスの天井（屋根の縁の厚い白い帯の下端）
TERRACE_CEILING = 8.7
# 外階段：辺 7 の外に沿い、西（頂点 7 の側）へ上る。踊り場は頂点 7 から辺に沿って STAIR_LANDING まで。
STAIR_EDGE = 7
STAIR_LANDING = 1.8
STAIR_STEPS = 16
STAIR_GOING = 0.3
STAIR_OFFSET = (0.15, 1.75)
# 研究棟側の東の面（濃い灰色の石張り）
EAST_EDGE = 13
FOOTPRINT_POINTS = 15
# 外形の頂点（局所 X, Y。spec.md §0）。GLASS_EDGES の辺 index がこの順の辺を指すことを build で照合する
FOOTPRINT_LOCAL = ((-39.68, 41.26), (-40.71, 44.12), (-45.57, 44.33), (-46.38, 32.65), (-49.45, 32.52),
                   (-46.15, 25.23), (-40.75, 16.90), (-31.5, 8.49), (-20.2, 4.0), (-18.69, 7.69),
                   (-8.45, 5.54), (8.36, 6.22), (9.13, 19.11), (31.88, 19.18), (31.78, 41.35))
FOOTPRINT_TOL = 0.1


def _lw(x, y):
    """局所 (X, Y) をワールド (x, y) にする。x = 6.0634 + 0.9096X + 0.4156Y、y = -21.6235 - 0.4156X + 0.9096Y。"""
    c, s = math.cos(_YAW), math.sin(_YAW)
    return (_ORIGIN[0] + c * x + s * y, _ORIGIN[1] - s * x + c * y)


def _rect(x0, y0, x1, y1):
    return [_lw(x0, y0), _lw(x1, y0), _lw(x1, y1), _lw(x0, y1)]


def _wall(mb, a, b, z0, z1, mat):
    """a→b の両面の壁（薄い板）。"""
    mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1), mat)
    mb.add_quad((b[0], b[1], z0), (a[0], a[1], z0), (a[0], a[1], z1), (b[0], b[1], z1), mat)


def _check_footprint(loop):
    assert len(loop) == FOOTPRINT_POINTS, "GLASS_EDGES は campus.json の 15 点の順に対応する"
    for i, q in enumerate(FOOTPRINT_LOCAL):
        w = _lw(*q)
        assert math.hypot(loop[i][0] - w[0], loop[i][1] - w[1]) < FOOTPRINT_TOL, \
            "外形の頂点 %d が spec.md §0 の位置と合わない。GLASS_EDGES の辺 index を見直す" % i


def stair_corner(loop, t, d):
    """外階段の辺の上の点。t は頂点 STAIR_EDGE から辺に沿う距離、d は外向きの距離。"""
    p, q = loop[STAIR_EDGE], loop[(STAIR_EDGE + 1) % len(loop)]
    e = geom.normalize(geom.sub(q, p))
    n = geom.outward_normal(p, q)
    return geom.add(geom.add(p, geom.mul(e, t)), geom.mul(n, d))


def _podium(mb, loop, podium_fh, podium_h):
    n = len(loop)
    glass = [i for i in GLASS_EDGES if i < n]
    solid = [i for i in range(n) if i not in glass and i != EAST_EDGE]
    facade.add_solid(mb, loop, 0.0, podium_h, "concrete_grey", edges=solid)
    facade.add_solid(mb, loop, 0.0, podium_h, "concrete_dark", edges=[EAST_EDGE])
    # 1F：床に近い所から天井までのガラス、白い縦の桟 1.4 m おき。上の 1.0 m は 2F との間の明るい帯
    facade.add_facade(mb, loop, 0.0, podium_fh, 0, 1, wall="concrete_light", glass="glass_clear",
                      seg=1.4, sill=0.3, header=1.0, inset=0.25, mullion=0.14, edges=glass)
    # 2F（テラスでない面）：外形の線のガラス。上の 0.9 m は屋根の縁の帯
    front = [i for i in glass if i not in TERRACE_EDGES]
    facade.add_facade(mb, loop, 0.0, podium_fh, 1, 2, wall="concrete_light", glass="glass_clear",
                      seg=1.5, sill=0.8, header=0.9, inset=0.3, mullion=0.14, edges=front)
    _terrace(mb, loop, podium_fh, podium_h)
    # 屋根は屋上緑化。縁は白い立ち上がり
    mb.add_ngon_flat(loop, podium_h, "grass")
    facade.add_parapet(mb, loop, podium_h, 0.6, 0.35, "concrete_light")


def _terrace(mb, loop, z2f, podium_h):
    """西北西の曲面の 2F の屋外テラス。ガラスの壁を TERRACE_SETBACK 引き、縁に低い立ち上がりと金属の柵。"""
    n = len(loop)
    inner = geom.offset_polygon(loop, -TERRACE_SETBACK)
    rail = z2f + 0.5
    for i in TERRACE_EDGES:
        j = (i + 1) % n
        mb.add_ngon_flat([loop[i], loop[j], inner[j], inner[i]], z2f, "concrete_light")
        mb.add_ngon_flat([loop[i], loop[j], inner[j], inner[i]], TERRACE_CEILING, "concrete_light", flip=True)
        # 縁の立ち上がりと金属の柵の笠木（柵の網は描かない）。外階段の踊り場が着く所はあける
        a = stair_corner(loop, STAIR_LANDING, 0.0) if i == STAIR_EDGE else loop[i]
        _wall(mb, a, loop[j], z2f, rail, "concrete_light")
        _wall(mb, a, loop[j], z2f + 1.0, z2f + 1.08, "metal_grey")
    # 屋根の縁の厚い帯（テラスの天井から屋根まで）
    facade.add_solid(mb, loop, TERRACE_CEILING, podium_h, "concrete_light", edges=list(TERRACE_EDGES))
    # 奥に引いたガラスの壁（黒い縦の桟 1.5 m おき）と、テラスの両端の壁
    facade.add_facade(mb, inner, z2f, TERRACE_CEILING - z2f, 0, 1, wall="metal_charcoal", glass="glass_clear",
                      seg=1.5, sill=0.1, header=0.2, inset=0.12, mullion=0.12,
                      edges=list(TERRACE_EDGES), side_reveals=False)
    for k in (TERRACE_EDGES[0], TERRACE_EDGES[-1] + 1):
        _wall(mb, loop[k % n], inner[k % n], z2f, TERRACE_CEILING, "concrete_light")
    _outdoor_stair(mb, loop, z2f)


def _outdoor_stair(mb, loop, z2f):
    """辺 STAIR_EDGE の外に沿って地面から 2F のテラスへ上がる外階段。頂点の側に踊り場。"""
    d0, d1 = STAIR_OFFSET
    rise = z2f / STAIR_STEPS

    def box(t0, t1, z):
        pts = [stair_corner(loop, t0, d0), stair_corner(loop, t1, d0),
               stair_corner(loop, t1, d1), stair_corner(loop, t0, d1)]
        mb.add_prism(pts, 0.0, z, "concrete_light", "concrete_light")

    box(0.0, STAIR_LANDING, z2f)
    for k in range(STAIR_STEPS - 1):
        t0 = STAIR_LANDING + k * STAIR_GOING
        box(t0, t0 + STAIR_GOING, z2f - (k + 1) * rise)
    # 外側の手すり（金属）
    top = STAIR_LANDING + (STAIR_STEPS - 1) * STAIR_GOING
    a, b = stair_corner(loop, 0.0, d1), stair_corner(loop, top, d1)
    mb.add_quad((a[0], a[1], z2f + 0.9), (b[0], b[1], rise + 0.9), (b[0], b[1], rise + 1.0),
                (a[0], a[1], z2f + 1.0), "metal_grey")
    mb.add_quad((b[0], b[1], rise + 0.9), (a[0], a[1], z2f + 0.9), (a[0], a[1], z2f + 1.0),
                (b[0], b[1], rise + 1.0), "metal_grey")


def _tower(mb, podium_h, tower_fh, floors, h):
    """3〜6F の研究棟。深い格子の面、床の線の帯、張り出しの裏、屋上の目隠し。"""
    tower = _rect(*TOWER_RECT)
    facade.add_facade(mb, tower, podium_h, tower_fh, 0, floors,
                      wall="concrete_light", glass="glass_dark",
                      seg=GRID_SEG, sill=0.55, header=0.75, inset=GRID_INSET, mullion=0.6)
    ledge = geom.offset_polygon(tower, LEDGE_OUT)
    for k in range(floors + 1):
        z = podium_h + k * tower_fh
        z0, z1 = (z - 0.3, z + 0.3) if k == 0 else ((z - 0.3, z + 0.5) if k == floors else (z - 0.15, z + 0.15))
        mb.add_prism(ledge, z0, z1, "concrete_light", "concrete_light", "concrete_light")
    mb.add_ngon_flat(tower, podium_h, "metal_charcoal", flip=True)   # 南へ張り出す所の裏
    mb.add_ngon_flat(tower, h, "roof_grey")
    screen = _rect(*ROOF_SCREEN_RECT)
    mb.add_prism(screen, h + 0.5, h + ROOF_SCREEN_H, "metal_charcoal", "roof_grey")


def _cores(mb, podium_h, tower_fh, floors, h):
    """レンガの塊 2 つ。西の端（南西の面にガラスの階段の帯）と北北東の階段室。"""
    west = _rect(*WEST_CORE_RECT)
    facade.add_solid(mb, west, podium_h, h + CORE_TOP, "brick_red", edges=[1, 2, 3])
    facade.add_solid(mb, west, h, h + CORE_TOP, "brick_red", edges=[0])
    facade.add_facade(mb, west, podium_h, tower_fh, 0, floors, wall="brick_red", glass="glass_dark",
                      seg=WEST_CORE_RECT[2] - WEST_CORE_RECT[0], sill=0.15, header=0.3, inset=0.3,
                      mullion=2.6, edges=[0])
    mb.add_ngon_flat(west, h + CORE_TOP, "roof_grey")

    core = _rect(*CORE_RECT)
    facade.add_facade(mb, core, podium_h, tower_fh, 0, floors, wall="brick_red", glass="glass_dark",
                      seg=2.2, sill=1.5, header=0.9, inset=0.28, mullion=0.5, edges=[2])
    facade.add_solid(mb, core, h, h + CORE_TOP, "brick_red")
    mb.add_ngon_flat(core, h + CORE_TOP, "roof_grey")


def build(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    _check_footprint(loop)
    h = b["height"]
    lv = b["levels"]
    podium_fh = 4.8
    podium_h = podium_fh * FLOORS_PODIUM
    floors = lv - FLOORS_PODIUM
    tower_fh = (h - podium_h) / floors
    _podium(mb, loop, podium_fh, podium_h)
    _tower(mb, podium_h, tower_fh, floors, h)
    _cores(mb, podium_h, tower_fh, floors, h)
    # 入口と看板は kcd_lib.entrances が置く
