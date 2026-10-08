"""第2研究棟の多角形の床・天井を凸の面に分ける道具。

外形は 15 角形で、床と天井には矩形の穴（階段の吹き抜け）がある。三角形分割に穴を
渡さずに済むよう、X の区切りで縦の帯に切り、帯ごとに台形を作ってから水平の線で
穴と外周の閉じの線を切り落とす。出来る面はすべて凸。
"""

import math

EPS = 1e-6


def _clip_y(poly, c, keep_below):
    """凸の多角形を水平線 y = c で切り、keep_below なら y <= c 側を残す。"""
    out = []
    n = len(poly)
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]
        pin = p[1] <= c + EPS if keep_below else p[1] >= c - EPS
        qin = q[1] <= c + EPS if keep_below else q[1] >= c - EPS
        if pin:
            out.append(p)
        if pin != qin:
            t = (c - p[1]) / (q[1] - p[1])
            out.append((p[0] + (q[0] - p[0]) * t, c))
    return out


def _clean(poly):
    out = []
    for p in poly:
        if not out or math.hypot(p[0] - out[-1][0], p[1] - out[-1][1]) > 1e-5:
            out.append(p)
    if len(out) > 1 and math.hypot(out[0][0] - out[-1][0], out[0][1] - out[-1][1]) <= 1e-5:
        out.pop()
    return out


def _area(poly):
    return 0.5 * sum(poly[i][0] * poly[(i + 1) % len(poly)][1]
                     - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))


def strips(poly, holes=(), xmin=None, xmax=None, ymin=None, ymax=None):
    """多角形 poly（単純・向き不問）から矩形の穴を抜き、凸の多角形（反時計回り）の列で返す。

    xmin / xmax / ymin / ymax を与えるとその外を切り落とす。
    """
    n = len(poly)
    xs = {p[0] for p in poly}
    for h in holes:
        xs.update((h[0], h[2]))
    lo = min(xs) if xmin is None else max(xmin, min(xs))
    hi = max(xs) if xmax is None else min(xmax, max(xs))
    xs = sorted(x for x in xs | {lo, hi} if lo - EPS <= x <= hi + EPS)
    out = []
    for x0, x1 in zip(xs, xs[1:]):
        if x1 - x0 < 1e-4:
            continue
        xm = (x0 + x1) * 0.5
        spans = []
        for i in range(n):
            a, b = poly[i], poly[(i + 1) % n]
            if min(a[0], b[0]) <= xm <= max(a[0], b[0]) and abs(b[0] - a[0]) > EPS:
                def y_at(x, a=a, b=b):
                    return a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0])
                spans.append((y_at(xm), y_at(x0), y_at(x1)))
        spans.sort()
        for k in range(0, len(spans) - 1, 2):
            bot, top = spans[k], spans[k + 1]
            pieces = [[(x0, bot[1]), (x1, bot[2]), (x1, top[2]), (x0, top[1])]]
            if ymin is not None:
                pieces = [_clip_y(p, ymin, False) for p in pieces]
            if ymax is not None:
                pieces = [_clip_y(p, ymax, True) for p in pieces]
            for hx0, hy0, hx1, hy1 in holes:
                if not (hx0 - EPS <= xm <= hx1 + EPS):
                    continue
                cut = []
                for p in pieces:
                    if len(p) >= 3:
                        cut.append(_clip_y(p, hy0, True))
                        cut.append(_clip_y(p, hy1, False))
                pieces = cut
            for p in pieces:
                p = _clean(p)
                if len(p) >= 3 and abs(_area(p)) > 1e-4:
                    out.append(p if _area(p) > 0 else list(reversed(p)))
    return out


def fill(mb, poly, z, mat, down=False, **kw):
    """strips の面を高さ z に張る（down=True なら下向き）。張った面の数を返す。"""
    parts = strips(poly, **kw)
    for p in parts:
        pts = [(q[0], q[1], z) for q in p]
        mb.add_face(list(reversed(pts)) if down else pts, mat)
    return len(parts)


def chain_offset(chain, d):
    """折れ線 chain の左手へ d だけずらした折れ線（角はマイター。端は辺の法線）。"""
    def normal(a, b):
        dx, dy = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dy)
        return (-dy / L, dx / L)

    out = []
    m = len(chain)
    for i, p in enumerate(chain):
        if i == 0:
            n = normal(chain[0], chain[1])
            out.append((p[0] + n[0] * d, p[1] + n[1] * d))
            continue
        if i == m - 1:
            n = normal(chain[-2], chain[-1])
            out.append((p[0] + n[0] * d, p[1] + n[1] * d))
            continue
        n1, n2 = normal(chain[i - 1], p), normal(p, chain[i + 1])
        bx, by = n1[0] + n2[0], n1[1] + n2[1]
        bl = math.hypot(bx, by)
        cosh = max((bx * n1[0] + by * n1[1]) / bl, 0.25)
        s = d / cosh
        out.append((p[0] + bx / bl * s, p[1] + by / bl * s))
    return out


def seg_dist(p, a, b):
    """点 p から線分 a-b までの距離。"""
    dx, dy = b[0] - a[0], b[1] - a[1]
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 < EPS else max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / L2))
    return math.hypot(p[0] - a[0] - dx * t, p[1] - a[1] - dy * t)


def inside(p, poly):
    """点 p が多角形 poly の内側か（偶奇則）。"""
    x, y = p
    res = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            res = not res
        j = i
    return res


def along(chain, t):
    """折れ線 chain の全長の比率 t の点と、その点の辺の単位方向。"""
    lens = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(chain, chain[1:])]
    s = max(0.0, min(1.0, t)) * sum(lens)
    for (a, b), L in zip(zip(chain, chain[1:]), lens):
        if s <= L + EPS:
            u = (b[0] - a[0]) / L, (b[1] - a[1]) / L
            return (a[0] + u[0] * s, a[1] + u[1] * s), u
        s -= L
    a, b = chain[-2], chain[-1]
    L = lens[-1]
    return b, ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
