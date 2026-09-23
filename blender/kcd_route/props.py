"""回廊の立体物（#41）— 沿道の建物・ブロック塀・ガードレール・門柱・街灯。

#45 の契約:
  **当たり判定に使う面は、必ず閉じた立体の面にする。**
  厚み 0 の板を 1 枚だけ置くと、裏から当たらないので PhysX がすり抜ける。
  ここで作るものは全部 add_prism / add_slab（側面 + 天面 + 底面）だけで組み立てる。
  窓も「壁から 4 cm 浮かせた 1 枚板」ではなく、壁をまたぐ厚み 6 cm の閉じた箱にする。
  build_route.py の closure チェック（1 面にしか使われていない辺の総延長 = 0 m）で毎回確かめる。
"""

import math
import zlib

from kcd_lib import geom

# --------------------------------------------------------------------------- #
#  沿道の建物
# --------------------------------------------------------------------------- #
FLOOR_H = 3.1           # 1 階ぶんの高さの目安
BASE_Z = -0.30          # 土台を地面の下まで下げて足元の隙間をなくす
WIN_W = 1.30
WIN_H = 1.25
WIN_SILL = 1.00         # 床から窓下端まで
WIN_DEPTH = 0.06        # 壁をまたぐ厚み（外へ 3 cm / 内へ 3 cm）
WIN_HEAD = 0.35         # 窓上端から階の天井（= 次の階の床）までの最小
MIN_WIN_EDGE = 3.0      # これより短い辺には窓を入れない
MIN_WIN_AREA = 40.0     # これより小さい建物には窓を入れない（物置・車庫）
MIN_WIN_H = 4.5         # これより低い建物には窓を入れない


def _floors(h):
    return max(1, int(round(h / FLOOR_H)))


WIN_REACH = 40.0        # 道からこれより遠い面には窓を貼らない（プレイヤーから見えない）
WIN_PROBE = 2.5


def facing_edges(b, refs, reach=WIN_REACH):
    """道から見える辺の番号。外向きに %.1f m 出た所が道に近い辺だけ残す。

    窓は当たり判定に効かない（Unity 側が glass* を collider から落とす）ただの飾りなので、
    裏側の見えない面に貼るぶんは三角形の無駄。""" % WIN_PROBE
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    n = len(loop)
    keep = set()
    for i in range(n):
        a, c = loop[i], loop[(i + 1) % n]
        m = geom.lerp(a, c, 0.5)
        nrm = geom.outward_normal(a, c)
        out = geom.add(m, geom.mul(nrm, WIN_PROBE))
        ins = geom.sub(m, geom.mul(nrm, WIN_PROBE))
        d_out = min((_dist_to_polyline(out, r) for r in refs), default=1e9)
        d_in = min((_dist_to_polyline(ins, r) for r in refs), default=1e9)
        if d_out <= reach and d_out <= d_in:
            keep.add(i)
    return keep


def _dist_to_polyline(p, pts):
    return min(geom.dist_point_segment(p, pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def window_slots(b, spacing, keep=None):
    """(辺番号, その辺の窓の数) の列と、階数・階高。窓を貼らない建物は ([], 0, h) を返す。"""
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = max(2.5, float(b.get("height") or 8.0))
    if len(loop) < 3 or abs(geom.poly_area(loop)) < MIN_WIN_AREA or h < MIN_WIN_H:
        return [], 0, h
    n_fl = _floors(h)
    fh = h / n_fl
    if fh < WIN_SILL + WIN_H + WIN_HEAD:
        return [], 0, h
    slots = []
    n = len(loop)
    for i in range(n):
        if keep is not None and i not in keep:
            continue
        L = geom.length(geom.sub(loop[(i + 1) % n], loop[i]))
        if L < MIN_WIN_EDGE:
            continue
        k = max(1, int((L - 1.0) / spacing))
        k = min(k, int((L - 0.8) / WIN_W))     # 窓どうしがくっつかない上限
        if k >= 1:
            slots.append((i, k))
    return slots, n_fl, h


def window_count(b, spacing, keep=None):
    slots, n_fl, _h = window_slots(b, spacing, keep)
    return sum(k for _, k in slots) * n_fl


def build_house(mb, b, spacing, keep=None):
    """押し出しの躯体（閉じた角柱）＋ 窓（閉じた箱）。戻り値は (窓の数, 三角形の増分は呼び側で数える)。"""
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    if len(loop) < 3 or abs(geom.poly_area(loop)) < 4.0:
        return 0
    h = max(2.5, float(b.get("height") or 8.0))
    # 色は id から決める（hash() はプロセスごとに変わるので crc32）
    wall = "bg_wall_%d" % (zlib.crc32(b["id"].encode("utf-8")) % 6)
    # 側面 + 屋根 + 底面。底を付けるのが #45 対策の肝（開いた筒にしない）
    mb.add_prism(loop, BASE_Z, h, wall, "roof_grey", wall)

    slots, n_fl, _ = window_slots(b, spacing, keep)
    if not slots:
        return 0
    fh = h / n_fl
    n = len(loop)
    made = 0
    d = WIN_DEPTH * 0.5
    for i, k in slots:
        a = loop[i]
        c = loop[(i + 1) % n]
        e = geom.normalize(geom.sub(c, a))
        L = geom.length(geom.sub(c, a))
        nrm = geom.outward_normal(a, c)

        def P(t, off):
            return (a[0] + e[0] * t + nrm[0] * off, a[1] + e[1] * t + nrm[1] * off)

        for f in range(n_fl):
            zb = f * fh + WIN_SILL
            zt = zb + WIN_H
            if zt > h - WIN_HEAD:
                break
            for j in range(k):
                tc = L * 0.5 + (j - (k - 1) * 0.5) * max(spacing, WIN_W + 0.3)
                t0, t1 = tc - WIN_W * 0.5, tc + WIN_W * 0.5
                if t0 < 0.4 or t1 > L - 0.4:
                    continue
                mb.add_slab([P(t0, -d), P(t1, -d), P(t1, d), P(t0, d)],
                            zb, zt, "glass_dark")
                made += 1
    return made


def build_houses(mb, houses, spacing, keeps=None):
    wins = 0
    for b in houses:
        wins += build_house(mb, b, spacing, (keeps or {}).get(b["id"]))
    return {"houses": len(houses), "windows": wins, "window_spacing": round(spacing, 2)}


# --------------------------------------------------------------------------- #
#  ブロック塀・ガードレール・門柱
# --------------------------------------------------------------------------- #
WALL_T = 0.24           # 塀の厚み
WALL_Z0 = -0.20
WALL_Z1 = 1.70
CAP_T = 0.36            # 笠木
CAP_Z1 = 1.84
WALL_PIECE = 24.0       # 1 枚の最大長（長い三角形を作らない）
WALL_MIN_PIECE = 1.2    # これより短い切れ端は作らない

RAIL_H = (0.48, 0.62, 0.86, 1.00)
RAIL_T = 0.09
POST_T = 0.14
POST_Z = (-0.10, 1.05)

PILLAR = 0.55
PILLAR_Z = (-0.25, 2.90)
PILLAR_CAP = 0.76
PILLAR_CAP_Z = 3.06

ROAD_GAP_MARGIN = 1.0   # 舗装の外に何 m 余分に開けるか（開けた所はガードレールで塞ぐ）


def _band(a, e, nrm, t0, t1, half):
    """a + e*t 上の t0..t1 を、法線方向に ±half 広げた長方形。"""
    def P(t, o):
        return (a[0] + e[0] * t + nrm[0] * o, a[1] + e[1] * t + nrm[1] * o)
    return [P(t0, -half), P(t1, -half), P(t1, half), P(t0, half)]


def _merge(intervals, total):
    """区間を [0, total] にクランプして併合する。"""
    out = []
    for t0, t1 in sorted((max(0.0, min(a, b)), min(total, max(a, b))) for a, b in intervals):
        if t1 <= t0:
            continue
        if out and t0 <= out[-1][1] + 1e-6:
            out[-1][1] = max(out[-1][1], t1)
        else:
            out.append([t0, t1])
    return [tuple(x) for x in out]


def _complement(holes, total):
    runs = []
    cur = 0.0
    for t0, t1 in holes:
        if t0 - cur > WALL_MIN_PIECE:
            runs.append((cur, t0))
        cur = max(cur, t1)
    if total - cur > WALL_MIN_PIECE:
        runs.append((cur, total))
    return runs


def road_holes(seg, roads, road_width, step=0.5):
    """塀の 1 辺 seg=(A, B) のうち、道の舗装に載ってしまう区間。戻り値は [(t0, t1)]。

    交点を解くのではなく 0.5 m ごとに「舗装の縁からの距離」を見る。横切る道だけでなく、
    塀の線と並走してしまう道も拾えるようにするため（塀を道の上に立てない）。
    開けた所にはガードレールを立てるので、ここが多少広くても外へは出られない。"""
    a, b = seg
    L = geom.length(geom.sub(b, a))
    if L < 1e-6:
        return []
    e = geom.mul(geom.sub(b, a), 1.0 / L)
    near = []
    for r in roads:
        pts = geom.dedup(r["points"])
        if len(pts) >= 2:
            near.append((pts, road_width(r) * 0.5 + 0.5))
    if not near:
        return []
    runs = []
    cur = None
    n = int(L / step) + 1
    for i in range(n + 1):
        t = min(L, i * step)
        p = (a[0] + e[0] * t, a[1] + e[1] * t)
        on = any(_dist_to_polyline(p, pts) < half for pts, half in near)
        if on and cur is None:
            cur = t
        elif not on and cur is not None:
            runs.append((cur, t))
            cur = None
    if cur is not None:
        runs.append((cur, L))
    # 舗装の外へ ROAD_GAP_MARGIN だけ余分に開ける（塀が路肩に食い込まないように）
    return _merge([(t0 - ROAD_GAP_MARGIN, t1 + ROAD_GAP_MARGIN) for t0, t1 in runs], L)


def _seg_cross(a, b, c, d):
    r = (b[0] - a[0], b[1] - a[1])
    s = (d[0] - c[0], d[1] - c[1])
    den = r[0] * s[1] - r[1] * s[0]
    if abs(den) < 1e-12:
        return None
    t = ((c[0] - a[0]) * s[1] - (c[1] - a[1]) * s[0]) / den
    u = ((c[0] - a[0]) * r[1] - (c[1] - a[1]) * r[0]) / den
    if -1e-9 <= t <= 1 + 1e-9 and -1e-9 <= u <= 1 + 1e-9:
        return t, u
    return None


def house_holes(seg, houses, overlap=0.30):
    """建物が塀の代わりをしている区間。戻り値は [(t0, t1)]。

    ここにはガードレールを立てない（建物そのものが壁になる）ので、開け過ぎると
    プレイヤーがすり抜けられる隙間になる。だから標本点ではなく厳密な交点で測り、
    さらに両端を overlap だけ縮めて、塀を建物の中へ食い込ませる。"""
    a, b = seg
    L = geom.length(geom.sub(b, a))
    if L < 1e-6:
        return []
    lo = (min(a[0], b[0]) - 1.0, min(a[1], b[1]) - 1.0)
    hi = (max(a[0], b[0]) + 1.0, max(a[1], b[1]) + 1.0)
    holes = []
    for h in houses:
        x0, z0, x1, z1 = h["bbox"]
        if x1 < lo[0] or x0 > hi[0] or z1 < lo[1] or z0 > hi[1]:
            continue
        loop = geom.ensure_ccw(geom.dedup(h["footprint"]))
        ts = []
        n = len(loop)
        for i in range(n):
            cr = _seg_cross(a, b, loop[i], loop[(i + 1) % n])
            if cr is not None:
                ts.append(cr[0] * L)
        ts.sort()
        # 交点の間で、中点が建物の中にある区間だけが「建物の中」
        bounds = [0.0] + ts + [L]
        for i in range(len(bounds) - 1):
            t0, t1 = bounds[i], bounds[i + 1]
            if t1 - t0 < 1e-6:
                continue
            m = (t0 + t1) * 0.5
            p = (a[0] + (b[0] - a[0]) * m / L, a[1] + (b[1] - a[1]) * m / L)
            if geom.point_in_poly(p, loop):
                if t1 - t0 > 2 * overlap + 0.2:
                    holes.append((t0 + overlap, t1 - overlap))
    return _merge(holes, L)


def add_block_wall(mb, seg, runs):
    """seg の runs（開けない区間）にブロック塀を立てる。戻り値は延べ長さ。"""
    a, b = seg
    L = geom.length(geom.sub(b, a))
    e = geom.mul(geom.sub(b, a), 1.0 / L)
    nrm = (-e[1], e[0])
    total = 0.0
    for t0, t1 in runs:
        n = max(1, int(math.ceil((t1 - t0) / WALL_PIECE)))
        step = (t1 - t0) / n
        for i in range(n):
            s0 = t0 + i * step
            s1 = s0 + step
            mb.add_slab(_band(a, e, nrm, s0, s1, WALL_T * 0.5), WALL_Z0, WALL_Z1, "concrete_light")
            mb.add_slab(_band(a, e, nrm, s0, s1, CAP_T * 0.5), WALL_Z1, CAP_Z1, "concrete_grey")
        total += t1 - t0
    return total


def add_guardrail(mb, seg, t0, t1):
    """道を横切る所に立てる「通行止め」のガードレール（2 段の横桟 + 支柱 2 本）。"""
    a, b = seg
    L = geom.length(geom.sub(b, a))
    e = geom.mul(geom.sub(b, a), 1.0 / L)
    nrm = (-e[1], e[0])
    t0 = max(0.0, t0)
    t1 = min(L, t1)
    if t1 - t0 < 0.6:
        return 0
    for z0, z1 in ((RAIL_H[0], RAIL_H[1]), (RAIL_H[2], RAIL_H[3])):
        mb.add_slab(_band(a, e, nrm, t0, t1, RAIL_T * 0.5), z0, z1, "metal_grey")
    for t in (t0 + POST_T, t1 - POST_T):
        mb.add_slab(_band(a, e, nrm, t - POST_T * 0.5, t + POST_T * 0.5, POST_T * 0.5),
                    POST_Z[0], POST_Z[1], "metal_grey")
    return 1


def add_gate_pillar(mb, x, y):
    """門柱（閉じた箱 + 笠木）。"""
    h = PILLAR * 0.5
    rect = [(x - h, y - h), (x + h, y - h), (x + h, y + h), (x - h, y + h)]
    mb.add_slab(rect, PILLAR_Z[0], PILLAR_Z[1], "concrete_light")
    c = PILLAR_CAP * 0.5
    cap = [(x - c, y - c), (x + c, y - c), (x + c, y + c), (x - c, y + c)]
    mb.add_slab(cap, PILLAR_Z[1], PILLAR_CAP_Z, "concrete_dark")


# --------------------------------------------------------------------------- #
#  街灯（閉じた立体版。kcd_lib.props.add_lamp は円柱の天面が開いているので使わない）
# --------------------------------------------------------------------------- #
LAMP_H = 4.6
LAMP_R = 0.09


def add_street_lamp(mb, x, y, ang):
    mb.add_cylinder(x, y, -0.15, LAMP_H, LAMP_R, "metal_white",
                    seg=6, cap_top=True, cap_bottom=True)
    ca, sa = math.cos(ang), math.sin(ang)

    def P(du, dv):
        return (x + ca * du - sa * dv, y + sa * du + ca * dv)

    head = [P(-0.30, -0.16), P(0.62, -0.16), P(0.62, 0.16), P(-0.30, 0.16)]
    mb.add_slab(head, LAMP_H - 0.26, LAMP_H, "metal_white")
    glow = [P(-0.20, -0.11), P(0.52, -0.11), P(0.52, 0.11), P(-0.20, 0.11)]
    mb.add_slab(glow, LAMP_H - 0.36, LAMP_H - 0.26, "light_panel")


def place_lamps(mb, road, spacing=40.0, offset=6.0):
    """道の片側に等間隔で街灯を並べる。戻り値は本数。"""
    pts = geom.dedup(road["points"])
    if len(pts) < 2:
        return 0
    samples = geom.resample(pts, spacing)
    n = 0
    for i, p in enumerate(samples):
        nxt = samples[min(i + 1, len(samples) - 1)]
        prv = samples[max(i - 1, 0)]
        d = geom.normalize(geom.sub(nxt, prv))
        if d == (0.0, 0.0):
            continue
        side = (-d[1], d[0])
        q = geom.add(p, geom.mul(side, offset))
        add_street_lamp(mb, q[0], q[1], math.atan2(-side[1], -side[0]))
        n += 1
    return n


# --------------------------------------------------------------------------- #
#  塀の輪 — Annex の南・西・北を囲む。東 (x = WALL_X) は Unity 側の Wall_West が
#  受け持つので、門柱と短い返しだけを立てる。
# --------------------------------------------------------------------------- #
GATE_RETURN = 8.0       # 門柱から南北へ伸ばす返しの長さ


def ring_segments(annex, gate_z, wall_x):
    """塀を立てる辺。(名前, 始点, 終点) の列。"""
    x0, z0, x1, z1 = annex
    return [
        ("south", (x0, z0), (x1, z0)),
        ("west", (x0, z0), (x0, z1)),
        ("north", (x0, z1), (x1, z1)),
        ("east_s", (wall_x, gate_z[0] - GATE_RETURN), (wall_x, gate_z[0])),
        ("east_n", (wall_x, gate_z[1]), (wall_x, gate_z[1] + GATE_RETURN)),
    ]


def build_fence(mb, annex, gate_z, wall_x, roads, houses, width_of):
    """輪を作って、道の所は開けてガードレール、建物の所は開けっぱなしにする。"""
    stats = {"segments": 0, "ring_m": 0.0, "wall_m": 0.0, "guardrails": 0,
             "road_gap_m": 0.0, "house_gap_m": 0.0, "road_gaps": 0, "house_gaps": 0,
             "pieces": 0, "house_gap_max_m": 0.0}
    for name, a, b in ring_segments(annex, gate_z, wall_x):
        seg = (a, b)
        L = geom.length(geom.sub(b, a))
        stats["ring_m"] += L
        rh = road_holes(seg, roads, width_of)
        hh = house_holes(seg, houses)
        holes = _merge(list(rh) + list(hh), L)
        runs = _complement(holes, L)
        stats["wall_m"] += add_block_wall(mb, seg, runs)
        stats["pieces"] += sum(max(1, int(math.ceil((t1 - t0) / WALL_PIECE))) for t0, t1 in runs)
        for t0, t1 in rh:
            stats["guardrails"] += add_guardrail(mb, seg, t0 + 0.12, t1 - 0.12)
            stats["road_gap_m"] += t1 - t0
        for t0, t1 in hh:
            stats["house_gap_m"] += t1 - t0
            # ガードレールを立てない穴（＝建物が塞ぐ穴）のうち、いちばん広いもの
            stats["house_gap_max_m"] = max(stats["house_gap_max_m"], t1 - t0)
        stats["road_gaps"] += len(rh)
        stats["house_gaps"] += len(hh)
        stats["segments"] += 1
        del name
    for z in gate_z:
        add_gate_pillar(mb, wall_x, z)
    stats["gate_pillars"] = len(gate_z)
    for k in ("ring_m", "wall_m", "road_gap_m", "house_gap_m", "house_gap_max_m"):
        stats[k] = round(stats[k], 1)
    return stats


# Unity の ActorFactory.BodyRadius = 0.28 m。つまり直径 0.56 m より狭い隙間は通れない。
PLAYER_DIAMETER = 0.56


def check_enclosure(annex, gate_z, wall_x, roads, houses, width_of, step=0.05):
    """輪のどこにも「通り抜けられる隙間」が無いことを測る。

    輪を 5 cm 刻みで歩いて、その点が 塀 / ガードレール / 建物 のどれかで塞がっているかを見る。
    塞がっていない区間のいちばん長いものを返す。これが PLAYER_DIAMETER 未満なら通れない。"""
    worst = (0.0, None)
    total_open = 0.0
    for name, a, b in ring_segments(annex, gate_z, wall_x):
        seg = (a, b)
        L = geom.length(geom.sub(b, a))
        rh = road_holes(seg, roads, width_of)
        hh = house_holes(seg, houses)
        runs = _complement(_merge(list(rh) + list(hh), L), L)
        rails = [(t0 + 0.12, t1 - 0.12) for t0, t1 in rh]
        loops = []
        for h in houses:
            x0, z0, x1, z1 = h["bbox"]
            if (x1 < min(a[0], b[0]) - 1.0 or x0 > max(a[0], b[0]) + 1.0
                    or z1 < min(a[1], b[1]) - 1.0 or z0 > max(a[1], b[1]) + 1.0):
                continue
            loops.append(geom.ensure_ccw(geom.dedup(h["footprint"])))
        covered = _merge(list(runs) + rails, L)
        open_t = None
        n = int(L / step) + 1
        for i in range(n + 1):
            t = min(L, i * step)
            ok = any(t0 - 1e-9 <= t <= t1 + 1e-9 for t0, t1 in covered)
            if not ok:
                p = (a[0] + (b[0] - a[0]) * t / L, a[1] + (b[1] - a[1]) * t / L)
                ok = any(geom.point_in_poly(p, lp) for lp in loops)
            if not ok and open_t is None:
                open_t = t
            elif ok and open_t is not None:
                total_open += t - open_t
                if t - open_t > worst[0]:
                    worst = (t - open_t, "%s @ t=%.1f..%.1f m" % (name, open_t, t))
                open_t = None
        if open_t is not None:
            total_open += L - open_t
            if L - open_t > worst[0]:
                worst = (L - open_t, "%s @ t=%.1f..%.1f m" % (name, open_t, L))
    return {"worst_open_m": round(worst[0], 3), "worst_open_at": worst[1],
            "open_total_m": round(total_open, 2), "player_diameter_m": PLAYER_DIAMETER}
