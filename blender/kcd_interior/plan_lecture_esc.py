"""講義棟のエスカレータ 2 列 10 区間（1F→6F。spec §1-3）。

段の付いた歩ける斜路（蹴上げ 0.2）として作る。南の列（Y 25.6〜27.4）は東へ、北の列
（Y 27.6〜29.4）は西へ上り、同じ列の各階の区間は同じ X に重なる。
見た目はガラスの欄干、ステンレスのデッキと黒い手すり、白い腹板、段の縁は黄色。
1F の区間の下で腹が CLEAR より低い所は、白い板で床まで囲ってくぐれないようにする。
段の面は上と蹴込みの 1 枚ずつ（段の端は腹板、下は腹が隠す）。
区間ごとに部品 wall_lecture_esc_<列>_<下の階> を分ける（点光源の数の上限。spec §1-13）。
段（蹴込み・段鼻・踏面）だけは区間ごとの部品 escsteps_lecture_<区間> に分け、Unity が 1 段ぶんずつ動かす
（当たり判定は付けない。InteriorEscalatorStage）。乗る板の両端は Empty esc_lecture_<区間>_lo / _hi。
"""

from . import kit
from . import plan_lecture_dims as D

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
LIFT = 0.005          # 動く段の踏面を、乗り口・降り口の床から浮かせる高さ
CLEAR = 1.8           # 1F の区間の下で、腹がこれより低い所は床まで囲う
TOP = 6               # 最上の区間の上の端の階（6F）

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
        """動く段の i 段目（1 始まり）が止まっているときの踏面の x の範囲（小さい順）と高さ（LIFT を除く）。
        1 段目は下の端の床の高さ、i 段目は xa + (i − 2)·run〜xa + (i − 1)·run。"""
        xs, xe = self.xa + (i - 2) * self.run, self.xa + (i - 1) * self.run
        return min(xs, xe), max(xs, xe), self.za + (i - 1) * RISE

    def open_x(self):
        """腹の下が開く x。1F の区間はそこまで床から囲う。2F 以上の区間は下の端。"""
        return self.x_at(CLEAR + DROP) if self.za == 0.0 else self.xa


def flights():
    """10 区間: 南の列は東へ、北の列は西へ上る。名前は <列>_<下の階>（"south_1f" など）。"""
    out = []
    for name, (y0, y1), xa, xb in (("south", D.ESC_S_Y, X_W, X_E), ("north", D.ESC_N_Y, X_E, X_W)):
        for i in range(TOP - 1):
            out.append(Flight("%s_%df" % (name, i + 1), y0, y1, xa, D.FL[i], xb, D.FL[i + 1]))
    return tuple(out)


def ends(f):
    """乗る板の下の端と上の端 (x, y, z)。段鼻の線と段の奥の線の中間の線の両端（踏面 1 段目の中央が下の端）。"""
    yc = (f.y0 + f.y1) * 0.5
    return (f.xa - f.run * 0.5, yc, f.za), (f.xb - f.run * 0.5, yc, f.zb)


def _steps(mb, f, last):
    """動く段の 1〜last 段目（Flight.tread の位置）: 蹴込み（下の端を向く）・黄色の段鼻・踏面。
    Unity が 1 段ぶん上へ動かすと、最上段が上の端の床にそろう。蹴込みは 1 段目も含めて RISE の高さ。"""
    ya, yb = f.y0 + CLAD_T, f.y1 - CLAD_T
    for i in range(1, last + 1):
        _x0, _x1, z = f.tread(i)
        xr = f.xa + (i - 2) * f.run
        if f.dir > 0:
            mb.add_face([(xr, yb, z - RISE), (xr, ya, z - RISE), (xr, ya, z), (xr, yb, z)], STEEL)
        else:
            mb.add_face([(xr, ya, z - RISE), (xr, yb, z - RISE), (xr, yb, z), (xr, ya, z)], STEEL)
        xn, xk = xr + f.dir * NOSE, xr + f.run
        kit.plate(mb, min(xr, xn), ya, max(xr, xn), yb, z + LIFT, NOSING)
        kit.plate(mb, min(xn, xk), ya, max(xn, xk), yb, z + LIFT, STEEL)


def _vquad(mb, y, xs, xe, lo_s, lo_e, hi_s, hi_e, t, mat):
    """鉛直面 y の板: x=xs で lo_s〜hi_s、x=xe で lo_e〜hi_e。"""
    kit.thick_quad(mb, (xs, y, lo_s), (xe, y, lo_e), (xe, y, hi_e), (xs, y, hi_s), t, mat)


def _xquad(mb, x, y0, y1, z0, z1, t=SHEET_T, mat=WHITE):
    """鉛直面 x の板（列の幅いっぱい）。"""
    kit.thick_quad(mb, (x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1), t, mat)


def _truss(mb, f, x_end):
    """腹板（両側）・腹・端の板。x_end は上の端。"""
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


def _balustrade(mb, f, x_end):
    """デッキ・ガラスの欄干・黒い手すり。両端は水平に NEWEL だけ延ばす。"""
    path = [(f.xa - f.dir * NEWEL, f.za), (f.xa, f.za), (x_end, f.line(x_end)),
            (f.xb + f.dir * NEWEL, f.zb)]
    newels = [(f.xa - f.dir * NEWEL, f.xa, f.za), (f.xb, f.xb + f.dir * NEWEL, f.zb)]
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


def build_flight(mb, steps, f):
    """腹板・欄干などの止まった部分を mb に、動く段を steps に作る。"""
    _steps(steps, f, f.n)
    _truss(mb, f, f.xb)
    _balustrade(mb, f, f.xb)


def build(c):
    for f in flights():
        build_flight(c.part("wall", "esc_" + f.name), c.part("escsteps", f.name), f)
        lo, hi = ends(f)
        c._put("esc_%s_%s_lo" % (c.spec.id, f.name), *lo)
        c._put("esc_%s_%s_hi" % (c.spec.id, f.name), *hi)
