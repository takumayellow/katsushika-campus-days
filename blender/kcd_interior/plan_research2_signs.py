"""第2研究棟の看板（spec §5 の表の順。Empty の番号は呼んだ順に振られる）。

看板の裏に当たる面（黒い案内板・打放しの壁・えんじの帯・オレンジの帯・藤色の壁・柱）は
各階のモジュールが作る。ここは文言と位置だけを持つ。M は plan_research2 が作る家具の
MeshBuilder の辞書。同じ形の板が並ぶ所（窓口の札・2F の帯・EV の階数）は 2 枚目から
anchor=False（Empty を置かない）。
"""

import math

from . import kit
from . import signs
from . import plan_research2_dims as D

FACE_N = 0.0                 # 正面 +Y
FACE_S = math.pi             # 正面 -Y
FACE_W = math.pi * 0.5       # 正面 -X
FACE_E = -math.pi * 0.5      # 正面 +X
BLACK = "research2_sign_black"
ENJI = "research2_band_enji"
ORANGE = "research2_counter_orange"
WHITE = "research2_kiosk_white"
CHARCOAL = "research2_wall_charcoal"
SCREEN = "research2_monitor_black"
PAPER = "paper_white"
BLUE = "sign_plate_blue"
EXIT = "sign_exit_green"


def _flat(plate, out=0.01):
    """地と同じ材質の枠（板の縁が見えない貼り紙・印刷）。"""
    return dict(plate=plate, frame=plate, out=out)


def _lilac_pt(y):
    """藤色の壁の面の上で、y の高さ（平面の y）にある点と、壁の正面の向き。"""
    a, b = D.INNER[D.E_WC], D.INNER[D.E_WC + 1]
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    u = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    n = (-u[1], u[0])
    t = (y - a[1]) / u[1]
    return (a[0] + u[0] * t + n[0] * 0.02, a[1] + u[1] * t + n[1] * 0.02), math.atan2(-n[0], n[1])


# ---- 入口ホール（黒い案内板・立て看板・券売機の掲示）----
# (メッシュ, x, y, z, 向き, ja, en, 引数)
BOARD_Y = D.BLACK_BOARD[2] - 0.1          # 黒い案内板の南の面
HALL = (
    ("hall", 1.0, BOARD_Y, 1.1, FACE_S, "2F フードコート", "FOOD COURT",
     dict(w=2.6, h=0.5, **_flat(BLACK))),
    ("hall", 0.75, BOARD_Y, 2.45, FACE_S, "食堂1階\nそのまま1階で\n注文してください", "1F: order here",
     dict(w=0.8, h=0.65, **_flat(PAPER, 0.008))),
    ("hall", 0.75, BOARD_Y, 1.75, FACE_S, "食堂2階\n奥↑か手前→の\n階段で2階へ進む", "2F: take the stairs",
     dict(w=0.8, h=0.65, **_flat(PAPER, 0.008))),
    ("hall", 2.05, BOARD_Y, 2.3, FACE_S, "→\nトイレ\n多目的\nトイレ\n階段", "Restrooms\nStairs",
     dict(w=0.6, h=1.2, **_flat(CHARCOAL))),
    ("hall", D.A_FRAME[0], D.A_FRAME[1] - 0.03, 0.95, FACE_S, "11:45〜12:30\n理科大生\n優先時間帯",
     "11:45-12:30\nTUS students first", dict(w=0.6, h=0.9, **_flat(WHITE, 0.03))),
    ("hall", D.HALL_WALL_X, 8.6, 2.05, FACE_W, "【葛飾店 1F 専用券売機】\nキャッシュレス決済のみ",
     "1F Order Kiosks\nCashless only", dict(w=1.6, h=0.5, plate=WHITE, frame=WHITE, out=0.02)),
    ("hall", D.ENJI_PANEL[0] + 0.03, 10.0, 2.05, FACE_E, "【葛飾店 1F 専用券売機】", "1F Order Kiosks",
     dict(w=1.6, h=0.5, plate=WHITE, frame=WHITE, out=0.02)),
    ("hall", D.HALL_WALL_X + 0.15, 12.0, 1.5, FACE_E, "1階\n定食・丼\n500円〜",
     "1F Set Meals & Bowls\nfrom ¥500", dict(w=0.6, h=0.85, **_flat(PAPER, 0.008))),
)

# ---- 配膳口（えんじの帯の文字・窓口の札・呼出の画面）と返却口 ----
BAND_Y = D.KITCHEN_Y - 0.2                # えんじの帯の前面
SERVING_BANDS = (
    ("serving", -18.0, BAND_Y, 2.5, FACE_S, "本日のランチ", "TODAY'S LUNCH",
     dict(w=2.4, h=0.5, **_flat(ENJI, 0.02))),
    ("serving", -10.0, BAND_Y, 2.5, FACE_S, "麺", "NOODLES", dict(w=2.4, h=0.5, **_flat(ENJI, 0.02))),
    ("serving", -4.0, BAND_Y, 2.5, FACE_S, "丼・カレー", "DON・CURRY",
     dict(w=2.4, h=0.5, **_flat(ENJI, 0.02))),
)
# 窓口の札（帯の下面から吊る片面の白い札。x, ja, en）
WINDOW_LABELS = ((-19.2, "B", "Set B"), (-16.8, "D", "Set D"), (-10.0, "麺", "Noodles"),
                 (-5.0, "丼", "Rice Bowl"), (-3.0, "カレー", "Curry"))
LABEL_Y = BAND_Y + 0.05                   # 吊り元（帯の下面の上）
LABEL_Z = 1.96                            # 札の中心の高さ
LABEL_H = 0.3
# 呼出の画面（黒い箱の前面に左右 2 面。x, ja, en）
SCREEN_DEPTH = 0.05                       # 箱の奥行き（文字の板の後ろ）
SCREEN_1F = (D.MONITOR_1F[0], BAND_Y - SCREEN_DEPTH, D.MONITOR_1F[2])
SCREEN_1F_HALVES = ((-0.3, "準備中の番号", "Preparing"), (0.3, "お呼出中の番号", "Available"))
TRAY_HANG = ("serving", 17.0, 33.0, D.C1, FACE_S, "食器返却口", "Tray Return",
             dict(w=1.8, h=0.42, drop=0.5, both=False))

# ---- 1F の柱とトイレ ----
COL_R = D.COL1 * 0.5
SEATS1 = (
    ("seats1", -4.6, 20.5 - COL_R - 0.01, 1.6, FACE_S, "自動販売券 →", "Ticket Machines →",
     dict(w=0.6, h=0.5, plate=WHITE, frame=BLUE, out=0.03)),
    ("seats1", -13.8, 29.0 - COL_R - 0.01, 1.5, FACE_S, "葛飾食堂\n週間メニュー", "Weekly Menu",
     dict(w=0.6, h=0.85, **_flat(PAPER, 0.008))),
)

# ---- 2F（藤色の壁のポスター・カウンターの帯・トレーの札・モバイルオーダー・呼出の画面）----
COUNTER_Y = D.COUNTER2_Y                  # オレンジの帯の前面
COUNTER_Z = D.Z2 + 2.6
# 帯の文字（窓口, ja, en）。C を先に置き（Empty あり）、A は Empty あり、B と D は 2 枚目扱い
COUNTER_TEXT = (
    ("C", "C エスニック ゴロゴロカレー", "C ETHNIC GOROGORO CURRY", True),
    ("A", "A ガパオライス", "A Gapao Rice", True),
    ("B", "B ベトナムフォー", "B Vietnamese Pho", False),
    ("D", "D ハッシュドビーフオムライス", "D Hashed Beef Omurice", False),
)
TRAY_2F = ("counters2", 7.5, COUNTER_Y, D.Z2 + 2.55, FACE_S, "トレー", "Trays",
           dict(w=0.6, h=0.3, **_flat(WHITE, 0.02)))
MOBILE = ("counters2", 5.0, COUNTER_Y, D.Z2 + 1.5, FACE_S, "モバイルオーダー\n＆\nキャッシュレス決済",
          "Mobile Order &\nCashless Payment", dict(w=0.8, h=1.1, **_flat(PAPER, 0.008)))
SCREEN_2F = (D.MONITOR_2F[0], COUNTER_Y - SCREEN_DEPTH, D.MONITOR_2F[2])
SCREEN_2F_HALVES = ((-0.3, "準備中", "Preparing"), (0.3, "お渡し可能", "Available"))
LILAC_Y = 12.6
LILAC_Z = D.Z2 + 1.6

# ---- 研究棟側（案内板・館名・EV の階数）----
RESEARCH = (
    ("research", D.INNER[D.E_EAST][0], 30.0, 1.6, FACE_W, "6F 会議室\n4F・5F 研究室\n3F 事務室\n1F・2F 食堂",
     "6F Meeting / 4-5F Labs\n3F Office / 1-2F Cafeteria",
     dict(w=1.4, h=1.6, plate=BLUE, frame="metal_white", out=0.04)),
    ("research", D.PART_X + 0.12, 21.0, 2.3, FACE_E, "第2研究棟", "Research Labs Building No.2",
     dict(w=1.6, h=0.5, plate=CHARCOAL, frame="metal_white", out=0.03)),
)
EV_Z = 2.75                               # EV の枠（高さ 2.6）の上

# ---- トイレの扉・非常口 ----
WC_SIGN = ("seats1", 6.6, D.WC[3] + 0.16, 2.3, FACE_N, "トイレ・多目的トイレ", "Restrooms",
           dict(w=1.6, h=0.5, plate=WHITE, frame="metal_white", out=0.03))
EXIT_HANGS = (
    # 入口の内側は窓際の下がり天井の下
    ("hall", 0.0, 7.0, D.SOFFIT_Z, FACE_N, "非常口", "EXIT",
     dict(w=0.62, h=0.26, drop=0.25, plate=EXIT)),
    ("seats2", 3.5, 17.6, D.C2, FACE_N, "非常口", "EXIT", dict(w=0.62, h=0.26, drop=0.25, plate=EXIT)),
)
# 吊り看板の吊り元 (x, y, z_ceil)。plan_research2_shell は天井（C1・C2）から吊るものの周りに照明を置かない
HANGS = tuple((r[1], r[2], r[3]) for r in (TRAY_HANG,) + EXIT_HANGS) + tuple(
    (x, LABEL_Y, D.BAND1[0]) for x, _ja, _en in WINDOW_LABELS)


def _wall(c, M, row, anchor=True):
    mesh, x, y, z, ang, ja, en, kw = row
    return signs.wall(c, M[mesh], x, y, z, ang, ja=ja, en=en, anchor=anchor, **kw)


def _hang(c, M, row):
    mesh, x, y, z, ang, ja, en, kw = row
    signs.hanging(c, M[mesh], x, y, z, ang, ja=ja, en=en, **kw)


def _window_labels(c, M):
    z_root = D.BAND1[0]
    drop = z_root - (LABEL_Z + LABEL_H * 0.5)
    for k, (x, ja, en) in enumerate(WINDOW_LABELS):
        signs.hanging(c, M["serving"], x, LABEL_Y, z_root, FACE_S, ja=ja, en=en, w=0.6,
                      h=LABEL_H, drop=drop, plate=WHITE, both=False, anchor=k == 0)


def _screen(c, M, mesh, pos, halves, z_ceil=None, w=0.6, h=0.7):
    """呼出の画面: (x, y) の後ろに奥行き SCREEN_DEPTH の黒い箱、前面に左右 2 面の文字（左に Empty）。

    z_ceil を与えると箱の上から天井へ 2 本の吊り棒。
    """
    x, y, z = pos
    hw = max(abs(dx) for dx, _ja, _en in halves) + w * 0.5 + 0.04
    hh = h * 0.5 + 0.04
    kit.box(M[mesh], x - hw, y, z - hh, x + hw, y + SCREEN_DEPTH, z + hh, SCREEN)
    for k, (dx, ja, en) in enumerate(halves):
        _wall(c, M, (mesh, x + dx, y, z, FACE_S, ja, en,
                     dict(w=w, h=h, **_flat(SCREEN, 0.06))), anchor=k == 0)
    if z_ceil is None:
        return
    ym = y + SCREEN_DEPTH * 0.5
    for dx in (-0.4, 0.4):
        kit.box_nb(M[mesh], x + dx - 0.015, ym - 0.015, z + hh, x + dx + 0.015, ym + 0.015,
                   z_ceil, "metal_gray")


def _counter_bands(c, M):
    centers = {name: (a + b) * 0.5 for name, a, b in D.COUNTERS2}
    for name, ja, en, anchor in COUNTER_TEXT:
        _wall(c, M, ("counters2", centers[name], COUNTER_Y, COUNTER_Z, FACE_S, ja, en,
                     dict(w=2.4, h=0.48, **_flat(ORANGE, 0.02))), anchor=anchor)


def _lilac(c, M):
    (x, y), ang = _lilac_pt(LILAC_Y)
    signs.wall(c, M["seats2"], x, y, LILAC_Z, ang,
               ja="2F Cafeteria\n営業時間 11:00-14:00\nレギュラーメニュー\n週替わりメニュー\n¥550〜",
               en="2F Menu", w=1.0, h=1.4, plate=BLUE, frame="metal_white", out=0.02)


def _ev(c, M):
    x = D.EV[2]
    for z, ja, en in ((D.Z1 + EV_Z, "1", "1F"), (D.Z2 + EV_Z, "2", "2F")):
        for k, y in enumerate(D.EV_Y):
            _wall(c, M, ("research", x, y, z, FACE_E, ja, en,
                         dict(w=0.3, h=0.2, **_flat(SCREEN, 0.03))), anchor=k == 0)


def build(c, M):
    for row in HALL:
        _wall(c, M, row)
    for row in SERVING_BANDS:
        _wall(c, M, row)
    _window_labels(c, M)
    _screen(c, M, "serving", SCREEN_1F, SCREEN_1F_HALVES, D.C1)
    _hang(c, M, TRAY_HANG)
    for row in SEATS1:
        _wall(c, M, row)
    _lilac(c, M)
    _counter_bands(c, M)
    _wall(c, M, TRAY_2F)
    _wall(c, M, MOBILE)
    _screen(c, M, "counters2", SCREEN_2F, SCREEN_2F_HALVES)
    for row in RESEARCH:
        _wall(c, M, row)
    _ev(c, M)
    _wall(c, M, WC_SIGN)
    for row in EXIT_HANGS:
        _hang(c, M, row)
