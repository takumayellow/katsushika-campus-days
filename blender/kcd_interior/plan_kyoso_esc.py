"""共創棟のエスカレーター 1 区間（spec §1-2）。1F の東寄りから +X へ、2F のスラブ A の東の縁まで上る。

段の線は esc_kyoso_1f_lo（下の端、床の高さ）と esc_kyoso_1f_hi（上の端、2F の床の高さ）を結ぶ
直線で、Unity の InteriorEscalatorStage がこの 2 点から斜めの板の当たり判定と Escalator を作る。
段の部品 escsteps_kyoso_1f は当たり判定を持たず、Escalator が段の線に沿って 1 段ぶんずつ動かす。
k 段目（0 始まり）の踏面は中心が段の線の上、1 段ぶん動くと k+1 段目の位置に重なる。
腹板・欄干・手すり・デッキは講義棟（plan_lecture_esc）と同じ寸法で c.wall に組む。
"""

from kcd_lib.mesh import MeshBuilder

from . import kit
from . import plan_kyoso_dims as D

FLIGHT = "1f"
DROP = 0.9            # 段の線から腹の下面まで
DECK = 0.3            # 段の線からデッキ（欄干の下端）まで
GLASS_TOP = 1.05      # 段の線から欄干の上端まで
RAIL = 1.1            # 段の線から手すりの中心まで
RAIL_R = 0.04
NEWEL = 0.8           # 乗り口・降り口で欄干と手すりを水平に延ばす長さ
CLAD_T = 0.14         # 腹板の厚み（列の縁から段の端まで）
GLASS_IN = 0.07       # 列の縁から欄干の中心まで
GLASS_T = 0.03
SHEET_T = 0.06        # 腹・端の板の厚み
NOSE = 0.05           # 段の縁の黄色の奥行き
CLEAR = 1.8           # 腹がこれより低い所は床まで囲う
SINK = 0.01           # 踏面を段の線から下げる量（止まった段が床・スラブの上面に出ない）

STEEL = "metal_gray"
NOSING = "lecture_tactile_yellow"
WHITE = "lecture_rail_white"
GLASS = "lecture_glass_rail"
DECK_MAT = "stainless"
BELT = "plastic_black"

XA, XB = D.ESC_X_LO, D.ESC_X_HI
Y0, Y1 = D.ESC_Y
RUN, RISE = D.ESC_RUN, D.ESC_RISE


def line(x):
    """段の線の高さ（下の端 (XA, 0) と上の端 (XB, Z2) を結ぶ）。"""
    return (x - XA) / (XB - XA) * D.Z2


def x_at(z):
    return XA + z / D.Z2 * (XB - XA)


def open_x():
    """腹の下が CLEAR 以上空く x。そこまでは腹板を床まで下ろして囲う。"""
    return x_at(CLEAR + DROP)


def tread(k):
    """k 段目（0 始まり）の踏面の x の範囲と高さ。"""
    xc = XA + RUN * k
    return xc - RUN * 0.5, xc + RUN * 0.5, RISE * k - SINK


def steps_mesh(bid):
    """段の部品（蹴込み・黄色の段鼻・踏面）。0 段目は床に沈んでいて、動くと床から出てくる。"""
    mb = MeshBuilder("escsteps_%s_%s" % (bid, FLIGHT))
    mb.seats = []
    ya, yb = Y0 + CLAD_T, Y1 - CLAD_T
    for k in range(D.ESC_N):
        xs, xe, z = tread(k)
        mb.add_face([(xs, yb, z - RISE), (xs, ya, z - RISE), (xs, ya, z), (xs, yb, z)], STEEL)
        kit.plate(mb, xs, ya, xs + NOSE, yb, z, NOSING)
        kit.plate(mb, xs + NOSE, ya, xe, yb, z, STEEL)
    return mb


def _vquad(mb, y, xs, xe, lo_s, lo_e, hi_s, hi_e, t, mat):
    """鉛直面 y の板: x=xs で lo_s〜hi_s、x=xe で lo_e〜hi_e。"""
    kit.thick_quad(mb, (xs, y, lo_s), (xe, y, lo_e), (xe, y, hi_e), (xs, y, hi_s), t, mat)


def _xquad(mb, x, y0, y1, z0, z1, t=SHEET_T, mat=WHITE):
    kit.thick_quad(mb, (x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1), t, mat)


def _truss(mb):
    """白い腹板。下の端から open_x までは床まで、その先は段の線の DROP 下まで。上の端はスラブの下へ。"""
    x0, xo, x1 = XA - RUN * 0.5, open_x(), D.ESC_LAND_X
    for y in (Y0 + CLAD_T * 0.5, Y1 - CLAD_T * 0.5):
        _vquad(mb, y, x0, xo, 0.0, 0.0, max(0.0, line(x0)) + DECK, line(xo) + DECK, CLAD_T, WHITE)
        _vquad(mb, y, xo, x1, line(xo) - DROP, line(x1) - DROP, line(xo) + DECK,
               line(x1) + DECK, CLAD_T, WHITE)
    z_lo, z_hi = line(xo) - DROP, line(x1) - DROP
    kit.thick_quad(mb, (xo, Y0, z_lo), (x1, Y0, z_hi), (x1, Y1, z_hi), (xo, Y1, z_lo),
                   SHEET_T, WHITE)
    _xquad(mb, xo, Y0 + CLAD_T, Y1 - CLAD_T, 0.0, z_lo)
    _xquad(mb, x1 - SHEET_T * 0.5, Y0, Y1, z_hi, D.Z_SLAB)


def _balustrade(mb):
    """ガラスの欄干・黒い手すり・ステンレスのデッキ。乗り口と降り口で NEWEL ぶん水平に延ばす。"""
    path = [(XA - NEWEL, 0.0), (XA, 0.0), (XB, D.Z2), (XB + NEWEL, D.Z2)]
    for y_edge, inward in ((Y0, 1.0), (Y1, -1.0)):
        yg = y_edge + inward * GLASS_IN
        yd = y_edge + inward * CLAD_T
        for (xa, za), (xb, zb) in zip(path, path[1:]):
            _vquad(mb, yg, xa, xb, za + DECK, zb + DECK, za + GLASS_TOP, zb + GLASS_TOP,
                   GLASS_T, GLASS)
            kit.tube(mb, (xa, yg, za + RAIL), (xb, yg, zb + RAIL), RAIL_R, BELT)
        kit.thick_quad(mb, (XA, y_edge, DECK), (XB, y_edge, D.Z2 + DECK),
                       (XB, yd, D.Z2 + DECK), (XA, yd, DECK), 0.02, DECK_MAT)
        for xs, xe, z in ((XA - NEWEL, XA, 0.0), (XB, XB + NEWEL, D.Z2)):
            kit.box_nb(mb, xs, min(y_edge, yd), z, xe, max(y_edge, yd), z + DECK, DECK_MAT)


def build(c):
    """腹板と欄干を c.wall に、段の部品を c.parts に足し、段の線の両端の Empty を置く。"""
    _truss(c.wall)
    _balustrade(c.wall)
    c.parts.append(steps_mesh(c.spec.id))
    c._put("esc_%s_%s_lo" % (c.spec.id, FLIGHT), XA, D.ESC_YC, 0.0)
    c._put("esc_%s_%s_hi" % (c.spec.id, FLIGHT), XB, D.ESC_YC, D.Z2)
