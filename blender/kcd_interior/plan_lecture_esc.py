"""講義棟のエスカレータ 2 列 4 区間と 3F→4F の上り口（spec §1-3）。

段の付いた歩ける斜路（蹴上げ 0.2）として作る。南の列（Y 25.6〜27.4）は東へ、北の列
（Y 27.6〜29.4）は西へ上り、同じ列の 1F→2F と 2F→3F の区間は同じ X に重なる。
見た目はガラスの欄干、ステンレスのデッキと黒い手すり、白い腹板、段の縁は黄色。
1F の区間の下で腹が CLEAR より低い所は、白い板で床まで囲ってくぐれないようにする。
3F→4F の区間は上り口の 3 段の先を白い板で塞ぎ、斜路はその奥で 3F の天井の中へ消える。
段の面は上と蹴込みの 1 枚ずつ（段の端は腹板、下は腹が隠す）。
"""

from . import kit
from . import plan_lecture_dims as D
from . import plan_lecture_upper as UPPER

X_W, X_E = D.ESC_LAND_W[1], D.ESC_LAND_E[0]   # 斜路の西端 22.6・東端 30.6
RISE = D.ESC_RISE
DROP = 0.9            # 段の線から腹の下面まで
DECK = 0.3            # 段の線からデッキ（欄干の下端）まで
GLASS_TOP = 1.05      # 段の線から欄干の上端まで
RAIL = 1.1            # 段の線から手すりの中心まで（段鼻から 0.9）
RAIL_R = 0.04
NEWEL = 0.8           # 乗り口・降り口で欄干と手すりを水平に延ばす長さ
CLAD_T = 0.14         # 腹板の厚み（列の縁から段の端まで）
GLASS_IN = 0.07       # 列の縁から欄干の中心まで
GLASS_T = 0.03
SHEET_T = 0.06        # 腹・端の板・塞ぐ板の厚み
NOSE = 0.05           # 段の縁の黄色の奥行き
CLEAR = 1.8           # 1F の区間の下で、腹がこれより低い所は床まで囲う
STUB_SHOWN = 3        # 3F→4F で歩ける段の数
CEIL_3F = UPPER.Z_TOP

STEEL = "metal_gray"
NOSING = "lecture_tactile_yellow"
WHITE = "lecture_rail_white"
GLASS = "lecture_glass_rail"
DECK_MAT = "stainless"
BELT = "plastic_black"


class Flight:
    """1 区間。(xa, za) が下の端、(xb, zb) が上の端、y0〜y1 が列の幅。"""

    def __init__(self, name, y0, y1, xa, za, xb, zb):
        self.name = name
        self.y0, self.y1 = y0, y1
        self.xa, self.za, self.xb, self.zb = xa, za, xb, zb
        self.n = int(round((zb - za) / RISE))
        self.dir = 1.0 if xb > xa else -1.0
        self.run = (xb - xa) / self.n            # 符号つきの踏面

    def line(self, x):
        """段の線: 下の端 (xa, za) と上の端 (xb, zb) を結ぶ直線の高さ。"""
        return self.za + (x - self.xa) / (self.xb - self.xa) * (self.zb - self.za)

    def x_at(self, z):
        """段の線が高さ z になる x。"""
        return self.xa + (z - self.za) / (self.zb - self.za) * (self.xb - self.xa)

    def tread(self, i):
        """i 段目（1 始まり）の踏面の x の範囲（小さい順）と高さ。"""
        xs, xe = self.xa + (i - 1) * self.run, self.xa + i * self.run
        return min(xs, xe), max(xs, xe), self.za + i * RISE

    def open_x(self):
        """腹の下が開く x。1F の区間はそこまで床から囲う。2F 以上の区間は下の端。"""
        return self.x_at(CLEAR + DROP) if self.za == 0.0 else self.xa


def flights():
    """4 区間: 南の列は東へ、北の列は西へ上る。"""
    s0, s1 = D.ESC_S_Y
    n0, n1 = D.ESC_N_Y
    return (Flight("south_1f", s0, s1, X_W, 0.0, X_E, D.F2),
            Flight("south_2f", s0, s1, X_W, D.F2, X_E, D.F3),
            Flight("north_1f", n0, n1, X_E, 0.0, X_W, D.F2),
            Flight("north_2f", n0, n1, X_E, D.F2, X_W, D.F3))


def stub():
    """3F→4F（北の列、3F の東端から西へ）。上り口の STUB_SHOWN 段の先は塞ぐ。"""
    n0, n1 = D.ESC_N_Y
    return Flight("north_3f", n0, n1, X_E, D.F3, X_W, D.FL[3])


def stub_bar_x(f):
    """3F→4F を塞ぐ板の中心の x。板の手前の面が歩ける最後の段の奥の端に揃う。"""
    return f.xa + STUB_SHOWN * f.run + f.dir * SHEET_T * 0.5


def _steps(mb, f, last, skip_riser=()):
    """1〜last 段目の蹴込み（下の端を向く）・黄色の段鼻・踏面。"""
    ya, yb = f.y0 + CLAD_T, f.y1 - CLAD_T
    for i in range(1, last + 1):
        _x0, _x1, z = f.tread(i)
        xr = f.xa + (i - 1) * f.run
        if i not in skip_riser:
            if f.dir > 0:
                mb.add_face([(xr, yb, z - RISE), (xr, ya, z - RISE), (xr, ya, z), (xr, yb, z)],
                            STEEL)
            else:
                mb.add_face([(xr, ya, z - RISE), (xr, yb, z - RISE), (xr, yb, z), (xr, ya, z)],
                            STEEL)
        xn, xk = xr + f.dir * NOSE, f.xa + i * f.run
        kit.plate(mb, min(xr, xn), ya, max(xr, xn), yb, z, NOSING)
        kit.plate(mb, min(xn, xk), ya, max(xn, xk), yb, z, STEEL)


def _vquad(mb, y, xs, xe, lo_s, lo_e, hi_s, hi_e, t, mat):
    """鉛直面 y の板: x=xs で lo_s〜hi_s、x=xe で lo_e〜hi_e。"""
    kit.thick_quad(mb, (xs, y, lo_s), (xe, y, lo_e), (xe, y, hi_e), (xs, y, hi_s), t, mat)


def _xquad(mb, x, y0, y1, z0, z1, t=SHEET_T, mat=WHITE):
    """鉛直面 x の板（列の幅いっぱい）。"""
    kit.thick_quad(mb, (x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1), t, mat)


def _truss(mb, f, x_end):
    """腹板（両側）・腹・端の板。x_end は上の端（3F→4F は天井に入る所）。"""
    xo = f.open_x()
    for y in (f.y0 + CLAD_T * 0.5, f.y1 - CLAD_T * 0.5):
        if xo != f.xa:
            _vquad(mb, y, f.xa, xo, 0.0, 0.0, f.line(f.xa) + DECK, f.line(xo) + DECK,
                   CLAD_T, WHITE)
        _vquad(mb, y, xo, x_end, f.line(xo) - DROP, f.line(x_end) - DROP,
               f.line(xo) + DECK, f.line(x_end) + DECK, CLAD_T, WHITE)
    z_lo, z_hi = f.line(xo) - DROP, f.line(x_end) - DROP
    kit.thick_quad(mb, (xo, f.y0, z_lo), (x_end, f.y0, z_hi), (x_end, f.y1, z_hi),
                   (xo, f.y1, z_lo), SHEET_T, WHITE)
    if xo != f.xa:
        _xquad(mb, xo, f.y0 + CLAD_T, f.y1 - CLAD_T, 0.0, z_lo)
    else:
        _xquad(mb, f.xa + f.dir * SHEET_T * 0.5, f.y0, f.y1, f.za - DROP, f.za)
    if x_end == f.xb:
        _xquad(mb, f.xb - f.dir * SHEET_T * 0.5, f.y0, f.y1, f.zb - DROP, f.zb)


def _balustrade(mb, f, x_end, top_newel=True):
    """デッキ・ガラスの欄干・黒い手すり。両端は水平に NEWEL だけ延ばす。"""
    path = [(f.xa - f.dir * NEWEL, f.za), (f.xa, f.za), (x_end, f.line(x_end))]
    if top_newel:
        path.append((f.xb + f.dir * NEWEL, f.zb))
    newels = [(f.xa - f.dir * NEWEL, f.xa, f.za)]
    if top_newel:
        newels.append((f.xb, f.xb + f.dir * NEWEL, f.zb))
    for y_edge, inward in ((f.y0, 1.0), (f.y1, -1.0)):
        yg = y_edge + inward * GLASS_IN
        yd = y_edge + inward * CLAD_T
        for (xa, za), (xb, zb) in zip(path, path[1:]):
            _vquad(mb, yg, xa, xb, za + DECK, zb + DECK, za + GLASS_TOP, zb + GLASS_TOP,
                   GLASS_T, GLASS)
            kit.tube(mb, (xa, yg, za + RAIL), (xb, yg, zb + RAIL), RAIL_R, BELT)
        z_s, z_e = f.line(f.xa) + DECK, f.line(x_end) + DECK
        kit.thick_quad(mb, (f.xa, y_edge, z_s), (x_end, y_edge, z_e), (x_end, yd, z_e),
                       (f.xa, yd, z_s), 0.02, DECK_MAT)
        for xs, xe, z in newels:
            kit.box_nb(mb, xs, min(y_edge, yd), z, xe, max(y_edge, yd), z + DECK, DECK_MAT)


def build_flight(mb, f):
    _steps(mb, f, f.n)
    _truss(mb, f, f.xb)
    _balustrade(mb, f, f.xb)


def build_stub(mb, f):
    """3F→4F: 上り口の段を見せ、その奥を白い板で天井まで塞ぐ。奥の段と腹板は天井の中へ。"""
    last = int((CEIL_3F - RISE - f.za) / RISE + 1e-9)
    _steps(mb, f, last, skip_riser=(STUB_SHOWN + 1,))
    x_end = f.x_at(CEIL_3F + DROP)
    _truss(mb, f, x_end)
    _balustrade(mb, f, x_end, top_newel=False)
    xb = stub_bar_x(f)
    _xquad(mb, xb, f.y0, f.y1, f.line(xb) - DROP, CEIL_3F)


def build(c):
    for f in flights():
        build_flight(c.wall, f)
    build_stub(c.wall, stub())
