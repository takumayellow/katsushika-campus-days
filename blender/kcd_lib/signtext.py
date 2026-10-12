"""看板の文字をメッシュにする（屋内の案内サイン・室名札・店の看板）。

フォントはリポジトリにコミットした blender/fonts/KCDSignJP-Bold.ttf（tools/make_sign_font.py が
Noto Sans JP から作る）だけを使う。字の輪郭は ttf.py が読み、2 次ベジェを許容誤差 tol（em 単位）で
折れ線にし、Douglas-Peucker で点を間引いてから三角形に割る。三角形分割は Blender の
mathutils.geometry.delaunay_2d_cdt（制約付きドロネー）に輪郭をそのまま渡し、各三角形の重心の
回転数（nonzero）で字の内側だけを残す。同じフォント・同じ文言・同じ板なら、どの機械でも同じ形になる。

板の上の並べ方:
- 日本語の行を上、英語の行をその下に小さく（EN_SCALE 倍）並べる。改行は "\\n"。
- 板の内側（縁から margin）に収まる最大の大きさにする。英語の行が長くて収まらないときは、
  英語だけを EN_MIN 倍まで縮め、それでも収まらなければ全体を縮める。
- 行ごとに中央・左・右寄せ（align）。

文言が空・空の行がある・フォントに無い字がある、のどれかなら ValueError で止まる（白紙の看板を作らない）。

このモジュールは bpy に依存しない（delaunay_2d_cdt だけを使うときに mathutils を読む）。
三角形分割は差し替えられるので、pytest では mathutils なしで並べ方と向きを確かめられる。
"""

import math
import os

from . import ttf

FONT_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                         "fonts", "KCDSignJP-Bold.ttf")

EN_SCALE = 0.55   # 英語の行の字の大きさ（日本語の行に対する比）
EN_MIN = 0.36     # 英語が長いときに縮めてよい下限（同上）
LEADING = 0.16    # 同じ言語の行どうしの間（その行の em に対する比）
GAP = 0.24        # 日本語の行と英語の行の間（日本語の em に対する比）
TOL = 0.012       # 輪郭を折れ線にするときの許容誤差の既定（em）。place は字の大きさから決める（tol_for）
TOL_M = 0.003     # 輪郭の許容誤差（m）。字の em の大きさで割って em に直す
TOL_MIN = 0.010   # 許容誤差（em）の下限・上限・刻み。刻むのは字の形を覚えておくため
TOL_MAX = 0.040
TOL_STEP = 0.005
MIN_SIZE = 0.03   # 字の em の大きさの下限（m）。これより小さくなる板と文言の組は ValueError
WELD = 2.5e-4     # 三角形の辺の長さの下限（m）。MeshBuilder の重複頂点のマージ（1e-4 m）で潰れない長さ
LIFT = 0.004      # 板の面から文字を浮かせる距離（m）。Z ファイティングを避ける
ALIGNS = ("center", "left", "right")

_FONTS = {}
_GLYPHS = {}


def font(path=FONT_PATH):
    f = _FONTS.get(path)
    if f is None:
        f = _FONTS[path] = ttf.Font(path)
    return f


def margin(w, h):
    """板の縁から文字までの余白（m）。"""
    return 0.12 * min(w, h) + 0.012


def tol_for(size):
    """字の em の大きさ size（m）に合う輪郭の許容誤差（em）。TOL_STEP に丸める。"""
    t = min(TOL_MAX, max(TOL_MIN, TOL_M / size))
    return round(round(t / TOL_STEP) * TOL_STEP, 3)


# ---- 文言の検査 ----
def lines_of(text, label, fnt=None):
    """文言を行に分ける。空・空の行・フォントに無い字は ValueError。"""
    if not isinstance(text, str) or not text.strip():
        raise ValueError("看板の文言（%s）が空" % label)
    lines = text.split("\n")
    for ln in lines:
        if not ln.strip():
            raise ValueError("看板の文言（%s）に空の行がある: %r" % (label, text))
    fnt = fnt or font()
    missing = sorted({ch for ch in text if ch != "\n" and not fnt.has(ch)})
    if missing:
        raise ValueError("看板の文言（%s）にフォントに無い字がある: %s"
                         % (label, " ".join("%s(U+%04X)" % (c, ord(c)) for c in missing)))
    return lines


# ---- 輪郭 ----
def flatten(contour, upm, tol):
    """TrueType の輪郭 [(x, y, on), ...] を em 単位の折れ線 [(x, y), ...] にする（閉じた輪郭）。"""
    m = len(contour)
    if m < 3:
        return []
    s = 1.0 / upm
    pts = [(x * s, y * s, on) for x, y, on in contour]
    start = next((i for i, p in enumerate(pts) if p[2]), None)
    if start is None:   # 全部が制御点: 最後と最初の中点から始める
        a, b = pts[-1], pts[0]
        pts = [((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, True)] + pts
    else:
        pts = pts[start:] + pts[:start]
    exp = []
    n = len(pts)
    for i in range(n):
        p, q = pts[i], pts[(i + 1) % n]
        exp.append(p)
        if not p[2] and not q[2]:
            exp.append(((p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5, True))
    out = []
    L = len(exp)
    i = 0
    while i < L:
        p = exp[i]
        out.append((p[0], p[1]))
        c = exp[(i + 1) % L]
        if c[2]:
            i += 1
            continue
        e = exp[(i + 2) % L]
        dx = p[0] - 2.0 * c[0] + e[0]
        dy = p[1] - 2.0 * c[1] + e[1]
        k = max(1, int(math.ceil(math.sqrt(math.hypot(dx, dy) / (4.0 * tol)))))
        for j in range(1, k):
            t = j / k
            u = 1.0 - t
            out.append((u * u * p[0] + 2 * u * t * c[0] + t * t * e[0],
                        u * u * p[1] + 2 * u * t * c[1] + t * t * e[1]))
        i += 2
    return out


def _seg_dist(p, a, b):
    vx, vy = b[0] - a[0], b[1] - a[1]
    wx, wy = p[0] - a[0], p[1] - a[1]
    vv = vx * vx + vy * vy
    t = 0.0 if vv <= 0.0 else max(0.0, min(1.0, (wx * vx + wy * vy) / vv))
    return math.hypot(wx - t * vx, wy - t * vy)


def _dp_open(pts, tol):
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        best, bi = -1.0, -1
        for i in range(a + 1, b):
            d = _seg_dist(pts[i], pts[a], pts[b])
            if d > best:
                best, bi = d, i
        if bi >= 0 and best > tol:
            keep[bi] = True
            stack.append((a, bi))
            stack.append((bi, b))
    return [p for p, k in zip(pts, keep) if k]


def simplify(poly, tol, gap=None):
    """閉じた折れ線を Douglas-Peucker で間引く（どの点も元の折れ線から tol 以内）。

    間引いたあと、直前の点から gap（既定 tol / 4）以内の点を落とす。
    """
    gap = tol * 0.25 if gap is None else gap
    n = len(poly)
    if n <= 3 or tol <= 0.0:
        return list(poly)
    a = min(range(n), key=lambda i: (poly[i][0], poly[i][1]))
    pts = poly[a:] + poly[:a]
    far = max(range(n), key=lambda i: (pts[i][0] - pts[0][0]) ** 2 + (pts[i][1] - pts[0][1]) ** 2)
    if far == 0:
        return []
    first = _dp_open(pts[:far + 1], tol)
    second = _dp_open(pts[far:] + [pts[0]], tol)
    out = first[:-1] + second[:-1]
    clean = []
    for p in out:
        if not clean or math.hypot(p[0] - clean[-1][0], p[1] - clean[-1][1]) > gap:
            clean.append(p)
    while len(clean) > 1 and math.hypot(clean[0][0] - clean[-1][0], clean[0][1] - clean[-1][1]) <= gap:
        clean.pop()
    return clean


def signed_area(poly):
    s = 0.0
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        s += a[0] * b[1] - b[0] * a[1]
    return 0.5 * s


def snap(polys, gap):
    """どの 2 点も gap 以上離れるように、gap より近い点を先に出た点へ寄せる（輪郭をまたいでも寄せる）。

    重なりを取り除いたフォントは、画の継ぎ目で別の輪郭の点が 0.001 em ほどの所に並ぶ。三角形に割ると
    そこに短い辺ができ、重複頂点のマージで三角形が潰れるので先に 1 点にまとめる。寄せたあと、
    同じ輪郭で隣どうしが同じ点になったものは 1 つにし、3 点に満たない輪郭は捨てる。
    """
    cell = {}
    out = []
    for poly in polys:
        q = []
        for p in poly:
            gx, gy = int(math.floor(p[0] / gap)), int(math.floor(p[1] / gap))
            hit = None
            for ix in (gx - 1, gx, gx + 1):
                for iy in (gy - 1, gy, gy + 1):
                    for r in cell.get((ix, iy), ()):
                        if math.hypot(p[0] - r[0], p[1] - r[1]) < gap:
                            hit = r
                            break
                    if hit:
                        break
                if hit:
                    break
            if hit is None:
                hit = (p[0], p[1])
                cell.setdefault((gx, gy), []).append(hit)
            if not q or q[-1] != hit:
                q.append(hit)
        while len(q) > 1 and q[0] == q[-1]:
            q.pop()
        if len(q) >= 3:
            out.append(q)
    return out


def outline(ch, tol=TOL, fnt=None):
    """字の輪郭（em 単位の閉じた折れ線の並び）と送り幅（em）。

    曲線を tol / 2 で折れ線にし、tol / 2 で間引き、tol / 4 より近い点を 1 点にまとめる（snap。
    元の輪郭からのずれは tol 以内）。面積が tol^2 以下の輪郭は捨てる。
    """
    fnt = fnt or font()
    gid = fnt.glyph_id(ch)
    if gid == 0:
        raise ValueError("フォントに無い字: %r" % ch)
    upm = fnt.units_per_em
    polys = [simplify(flatten(c, upm, tol * 0.5), tol * 0.5, gap=tol * 0.25)
             for c in fnt.contours(gid)]
    out = [p for p in snap(polys, tol * 0.25) if abs(signed_area(p)) > tol * tol]
    return out, fnt.advance(gid) / upm


# ---- 三角形分割 ----
def winding(px, py, segs):
    """点 (px, py) の回転数。segs は (x0, y0, x1, y1) の並び。"""
    w = 0
    for x0, y0, x1, y1 in segs:
        if y0 <= py:
            if y1 > py and (x1 - x0) * (py - y0) - (px - x0) * (y1 - y0) > 0:
                w += 1
        elif y1 <= py and (x1 - x0) * (py - y0) - (px - x0) * (y1 - y0) < 0:
            w -= 1
    return w


def keep_inside(tris, contours):
    """候補の三角形から、重心の回転数が 0 でないもの（字の内側）だけを反時計回りにして返す。"""
    segs = []
    for c in contours:
        n = len(c)
        for k in range(n):
            a, b = c[k], c[(k + 1) % n]
            segs.append((a[0], a[1], b[0], b[1]))
    out = []
    for a, b, c in tris:
        ar = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
        if abs(ar) < 1e-12:
            continue
        cx = (a[0] + b[0] + c[0]) / 3.0
        cy = (a[1] + b[1] + c[1]) / 3.0
        if winding(cx, cy, segs) == 0:
            continue
        out.append((a, b, c) if ar > 0 else (a, c, b))
    return out


def cdt_candidates(contours):
    """Blender の制約付きドロネーで、輪郭の内側の候補三角形を作る（穴の中も含む）。"""
    from mathutils import Vector
    from mathutils.geometry import delaunay_2d_cdt
    verts, faces = [], []
    for c in contours:
        base = len(verts)
        verts.extend(Vector(p) for p in c)
        faces.append(list(range(base, base + len(c))))
    vo, _eo, fo = delaunay_2d_cdt(verts, [], faces, 1, 1e-6)[:3]
    out = []
    for f in fo:
        if len(f) != 3:
            raise RuntimeError("delaunay_2d_cdt が三角形でない面を返した")
        out.append(tuple((vo[i][0], vo[i][1]) for i in f))
    return out


def min_edge(tris):
    """三角形の辺の長さの最小（三角形が無ければ inf）。"""
    best = math.inf
    for a, b, c in tris:
        best = min(best, math.hypot(b[0] - a[0], b[1] - a[1]), math.hypot(c[0] - b[0], c[1] - b[1]),
                   math.hypot(a[0] - c[0], a[1] - c[1]))
    return best


def glyph(ch, tol=TOL, candidates=None, fnt=None):
    """字の (三角形の並び（em 単位・反時計回り）, 送り幅（em）, 辺の長さの最小（em）)。字ごと・tol ごとに覚えておく。"""
    fnt = fnt or font()
    candidates = candidates or cdt_candidates
    key = (fnt.path, ch, tol, candidates)
    hit = _GLYPHS.get(key)
    if hit is None:
        polys, adv = outline(ch, tol, fnt)
        tris = tuple(keep_inside(candidates(polys), polys)) if polys else ()
        hit = _GLYPHS[key] = (tris, adv, min_edge(tris))
    return hit


def fitted_glyph(ch, size, tol=None, candidates=None, fnt=None):
    """大きさ size（m）で置く字の glyph()。

    tol を渡さなければ tol_for(size) から始め、辺が WELD より短くなる字は TOL_MAX まで tol を粗くする。
    それでも短ければ ValueError（重複頂点のマージで三角形が潰れ、数えた三角形の数と合わなくなるため）。
    """
    if tol is not None:
        steps = [tol]
    else:
        steps = []
        t = tol_for(size)
        while t <= TOL_MAX + 1e-9:
            steps.append(t)
            t = round(t + TOL_STEP, 3)
    for t in steps:
        hit = glyph(ch, t, candidates, fnt)
        if hit[2] * size >= WELD:
            return hit
    raise ValueError("字 %r（U+%04X）が %.3f m では細かすぎて三角形が潰れる（板を大きくするか文言を短く）"
                     % (ch, ord(ch), size))


# ---- 並べ方 ----
class Line:
    __slots__ = ("text", "lang", "scale", "width")

    def __init__(self, text, lang, width):
        self.text = text
        self.lang = lang
        self.scale = 1.0 if lang == "ja" else EN_SCALE
        self.width = width  # em（scale を掛ける前）


def _width(text, fnt):
    upm = fnt.units_per_em
    return sum(fnt.advance(fnt.glyph_id(ch)) for ch in text) / upm


def layout(ja, en, w, h, align="center", max_em=None, fnt=None):
    """板（幅 w・高さ h、中心が原点）の上の字の位置。

    返り値: (em, [(ch, x, baseline, size), ...])。x は字の原点（左端の送り位置）、size は
    その字の em の大きさ（m）。u は読む人から見て右、v は上。
    """
    if align not in ALIGNS:
        raise ValueError("align は %s のどれか: %r" % (ALIGNS, align))
    if w <= 0 or h <= 0:
        raise ValueError("板の大きさが正でない: %r x %r" % (w, h))
    fnt = fnt or font()
    lines = [Line(t, "ja", _width(t, fnt)) for t in lines_of(ja, "ja", fnt)]
    lines += [Line(t, "en", _width(t, fnt)) for t in lines_of(en, "en", fnt)]
    m = margin(w, h)
    iw, ih = w - 2.0 * m, h - 2.0 * m
    if iw <= 0 or ih <= 0:
        raise ValueError("板が小さすぎて文字が入らない: %r x %r" % (w, h))

    def height(en_scale):
        hh, prev = 0.0, None
        for ln in lines:
            s = 1.0 if ln.lang == "ja" else en_scale
            if prev is not None:
                hh += GAP if prev != ln.lang else LEADING * s
            hh += s
            prev = ln.lang
        return hh

    wj = max((ln.width for ln in lines if ln.lang == "ja"), default=0.0)
    we = max((ln.width for ln in lines if ln.lang == "en"), default=0.0)
    em = ih / height(EN_SCALE)
    if wj > 0:
        em = min(em, iw / wj)
    if max_em:
        em = min(em, max_em)
    en_scale = EN_SCALE
    if we * em * EN_SCALE > iw:
        en_scale = max(EN_MIN, iw / (we * em))
        if we * em * en_scale > iw:
            em = iw / (we * en_scale)
    for ln in lines:
        ln.scale = 1.0 if ln.lang == "ja" else en_scale
    smallest = min(ln.scale for ln in lines) * em
    if smallest < MIN_SIZE:
        raise ValueError("看板の文字が小さすぎる（%.3f m < %.2f m。板を大きくするか文言を短く）: %r / %r"
                         % (smallest, MIN_SIZE, ja, en))

    desc = -fnt.descender / fnt.units_per_em   # em の箱の下端から基線まで
    total = height(en_scale) * em
    top = total * 0.5
    out = []
    prev = None
    for ln in lines:
        size = ln.scale * em
        if prev is not None:
            top -= (GAP * em) if prev != ln.lang else (LEADING * size)
        bottom = top - size
        base = bottom + desc * size
        lw = ln.width * size
        if align == "center":
            x = -lw * 0.5
        elif align == "left":
            x = -iw * 0.5
        else:
            x = iw * 0.5 - lw
        upm = fnt.units_per_em
        for ch in ln.text:
            out.append((ch, x, base, size))
            x += fnt.advance(fnt.glyph_id(ch)) / upm * size
        top = bottom
        prev = ln.lang
    return em, out


def place(mb, frame, w, h, ja, en, *, face, side=1, dz=0.0, mat="plastic_white",
          align="center", tol=None, max_em=None, candidates=None, fnt=None):
    """板の面に文字を貼る。三角形の数を返す。

    frame: 板のローカル座標 -> 建物座標の写像（kit.T のように p(dx, dy, dz) を持つもの）。
    板の面はローカルの y = side * face の平面で、文字は side の向き（side=+1 なら +y 側）から読む。
    板の中心はローカルの (0, side * face, dz)。tol（em）を渡さなければ字の大きさから決める（fitted_glyph）。
    """
    if side not in (1, -1):
        raise ValueError("side は +1 か -1: %r" % side)
    fnt = fnt or font()
    _em, glyphs = layout(ja, en, w, h, align, max_em, fnt)
    y = side * (face + LIFT)
    n = 0
    for ch, x, base, size in glyphs:
        tris = fitted_glyph(ch, size, tol, candidates, fnt)[0]
        for tri in tris:
            mb.add_face([frame.p(-side * (x + gx * size), y, dz + base + gy * size)
                         for gx, gy in tri], mat)
        n += len(tris)
    return n
