"""2D ジオメトリ・ユーティリティ。

座標系は DESIGN.md §3 に従う。campus.json の (x, z) を Blender の (x, y) にそのまま写し、
Blender の z を up とする。したがってこのモジュールの 2D 点はすべて Blender XY 平面上の点。
"""

import math

Vec2 = tuple


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def mul(a, s):
    return (a[0] * s, a[1] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1]


def cross(a, b):
    return a[0] * b[1] - a[1] * b[0]


def length(a):
    return math.hypot(a[0], a[1])


def normalize(a):
    n = length(a)
    return (a[0] / n, a[1] / n) if n > 1e-9 else (0.0, 0.0)


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def poly_area(poly):
    """符号付き面積。反時計回りで正。"""
    s = 0.0
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        s += x1 * y2 - x2 * y1
    return s * 0.5


def ensure_ccw(poly):
    return list(poly) if poly_area(poly) >= 0 else list(reversed(poly))


def dedup(poly, eps=1e-4):
    """連続する重複頂点と閉じ重複を落とす。"""
    out = []
    for p in poly:
        if not out or length(sub(p, out[-1])) > eps:
            out.append(tuple(p))
    while len(out) > 1 and length(sub(out[0], out[-1])) <= eps:
        out.pop()
    return out


def centroid(poly):
    a = poly_area(poly)
    if abs(a) < 1e-9:
        n = len(poly)
        return (sum(p[0] for p in poly) / n, sum(p[1] for p in poly) / n)
    cx = cy = 0.0
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        f = x1 * y2 - x2 * y1
        cx += (x1 + x2) * f
        cy += (y1 + y2) * f
    return (cx / (6 * a), cy / (6 * a))


def bbox(poly):
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    return (min(xs), min(ys), max(xs), max(ys))


def perimeter(poly):
    n = len(poly)
    return sum(length(sub(poly[(i + 1) % n], poly[i])) for i in range(n))


def outward_normal(p, q):
    """反時計回りポリゴンの辺 p->q の外向き法線（単位）。"""
    e = normalize(sub(q, p))
    return (e[1], -e[0])


def offset_polygon(poly, d):
    """マイター方式のオフセット。d>0 で外側、d<0 で内側。

    凹頂点で自己交差しうるので、建物の庇・パラペットなど小さい d でのみ使う。
    """
    poly = ensure_ccw(dedup(poly))
    n = len(poly)
    out = []
    for i in range(n):
        prev = poly[(i - 1) % n]
        cur = poly[i]
        nxt = poly[(i + 1) % n]
        n1 = outward_normal(prev, cur)
        n2 = outward_normal(cur, nxt)
        bis = add(n1, n2)
        bl = length(bis)
        if bl < 1e-6:
            out.append(add(cur, mul(n1, d)))
            continue
        bis = mul(bis, 1.0 / bl)
        cosh = dot(bis, n1)
        scale = d / max(cosh, 0.25)  # 鋭角でマイターが暴れるのを抑える
        out.append(add(cur, mul(bis, scale)))
    return out


def point_in_poly(pt, poly):
    x, y = pt
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            xint = (xj - xi) * (y - yi) / (yj - yi) + xi
            if x < xint:
                inside = not inside
        j = i
    return inside


def dist_point_segment(p, a, b):
    ab = sub(b, a)
    l2 = dot(ab, ab)
    if l2 < 1e-12:
        return length(sub(p, a))
    t = max(0.0, min(1.0, dot(sub(p, a), ab) / l2))
    return length(sub(p, add(a, mul(ab, t))))


def dist_point_poly_edges(p, poly):
    n = len(poly)
    return min(dist_point_segment(p, poly[i], poly[(i + 1) % n]) for i in range(n))


def resample(polyline, step):
    """折れ線を等間隔でサンプルした点列を返す（両端を含む）。"""
    if len(polyline) < 2:
        return list(polyline)
    out = [tuple(polyline[0])]
    carry = 0.0
    for i in range(len(polyline) - 1):
        a, b = polyline[i], polyline[i + 1]
        seg = length(sub(b, a))
        if seg < 1e-9:
            continue
        d = step - carry
        while d <= seg:
            out.append(lerp(a, b, d / seg))
            d += step
        carry = (carry + seg) % step
    return out


def fillet_vertex(poly, idx, radius, segments=10):
    """poly[idx] の角を半径 radius の円弧に置き換える（隣接辺長でクランプ）。"""
    n = len(poly)
    prev = poly[(idx - 1) % n]
    cur = poly[idx]
    nxt = poly[(idx + 1) % n]
    d1 = normalize(sub(prev, cur))
    d2 = normalize(sub(nxt, cur))
    ang = math.acos(max(-1.0, min(1.0, dot(d1, d2))))
    if ang < 1e-3 or abs(ang - math.pi) < 1e-3:
        return list(poly)
    t = radius / math.tan(ang * 0.5)
    t = min(t, length(sub(prev, cur)) * 0.48, length(sub(nxt, cur)) * 0.48)
    r = t * math.tan(ang * 0.5)
    p1 = add(cur, mul(d1, t))
    p2 = add(cur, mul(d2, t))
    bis = normalize(add(d1, d2))
    center = add(cur, mul(bis, r / math.sin(ang * 0.5)))
    a1 = math.atan2(p1[1] - center[1], p1[0] - center[0])
    a2 = math.atan2(p2[1] - center[1], p2[0] - center[0])
    while a2 - a1 > math.pi:
        a2 -= 2 * math.pi
    while a2 - a1 < -math.pi:
        a2 += 2 * math.pi
    arc = [(center[0] + r * math.cos(a1 + (a2 - a1) * k / segments),
            center[1] + r * math.sin(a1 + (a2 - a1) * k / segments))
           for k in range(segments + 1)]
    return list(poly[:idx]) + arc + list(poly[idx + 1:])


def longest_edge(poly):
    n = len(poly)
    best = (0.0, 0)
    for i in range(n):
        l = length(sub(poly[(i + 1) % n], poly[i]))
        if l > best[0]:
            best = (l, i)
    return best[1]


class Frame:
    """キャンパスのローカル軸 (u = 建物長手方向, v = それに直交)。"""

    def __init__(self, u_dir):
        self.u = normalize(u_dir)
        self.v = (-self.u[1], self.u[0])

    def uv(self, p):
        return (dot(p, self.u), dot(p, self.v))

    def xy(self, u, v):
        return (self.u[0] * u + self.v[0] * v, self.u[1] * u + self.v[1] * v)

    def rect(self, u0, v0, u1, v1):
        """(u,v) 矩形を反時計回りの XY ポリゴンにする。"""
        return [self.xy(u0, v0), self.xy(u1, v0), self.xy(u1, v1), self.xy(u0, v1)]

    def uv_bbox(self, poly):
        us = []
        vs = []
        for p in poly:
            u, v = self.uv(p)
            us.append(u)
            vs.append(v)
        return (min(us), min(vs), max(us), max(vs))
