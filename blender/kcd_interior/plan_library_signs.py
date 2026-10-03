"""図書館棟の看板 33 枚（spec §4 の表の順。Empty の番号は呼んだ順に振られる）。

看板の裏に当たる面（案内板・ゲートの柱・扉の上の壁・スタンド）は各階のモジュールが作る。
ここは文言と位置だけを持つ。M は plan_library が作る家具の MeshBuilder の辞書。
"""

import math

from . import signs
from . import plan_library_dims as D

FACE_N = 0.0                 # 正面 +Y
FACE_S = math.pi             # 正面 -Y
FACE_W = math.pi * 0.5       # 正面 -X
FACE_E = -math.pi * 0.5      # 正面 +X
GREY = "library_info_grey"
SCREEN = "library_signage_screen"
MOKKO = "library_mokko_board"
NUMBER = "plastic_white"
GROUP_X = (21.92, 26.16, 30.40, 34.64, 38.88)

# (メッシュ, x, y, z, 向き, ja, en, 引数)
WALL = (
    ("counter", -17.55, 25.5, 3.2, FACE_E, "OPAC の使い方", "How to use OPAC",
     dict(w=1.6, h=0.95, plate=SCREEN, frame="stainless")),
)
# 2 番目はキャレルの通路の吊り看板（WALL の 1 枚の後、残りの壁の看板の前に置く）
CARREL = ("carrel", -1.0, 53.9, D.C2, FACE_S, "閲覧席", "Study Carrels", dict(drop=0.6))
WALL_REST = (
    ("counter", -13.0, 25.0, 1.13, FACE_E, "受付", "Reception", dict(w=0.4, h=0.2)),
    ("entry", -0.65, 9.0, 1.2, FACE_S, "入館", "Entrance", dict(w=0.4, h=0.3)),
    ("entry", -2.95, 6.5, 1.5, FACE_E, "館内案内\n2F 開架書架・閲覧席・黙考書院\n1F 受付・ブックギャラリー",
     "Floor Guide\n2F Stacks / Carrels / Mokko\n1F Reception / Book Gallery",
     dict(w=2.4, h=1.6, plate=GREY)),
)
MEDIA = ("entry", -11.0, 15.0, D.C1, FACE_N, "メディアラウンジ", "Media Lounge",
         dict(w=2.4, h=0.46, drop=0.4))
WALL_1F = (
    ("lobby", 2.0, 20.0, 1.2, FACE_S, "企画展示", "Exhibition", dict(w=1.0, h=0.36)),
    ("lobby", -10.5, 31.9, 2.1, FACE_S, "シラバス指定図書（教科書）", "Course Reserves",
     dict(w=2.4, h=0.4)),
    ("counter", -12.2, 22.0, 1.9, FACE_E, "自動貸出機", "Self Checkout", dict(w=1.0, h=0.32)),
    ("lobby", -6.5, 19.0, 1.6, FACE_S, "新着図書 今週分", "New Arrivals", dict(w=1.2, h=0.36)),
    ("entry", -18.6, 17.8, 2.0, FACE_E, "ノートPC貸出ロッカー", "Laptop Lockers",
     dict(w=1.6, h=0.36)),
    ("counter", -14.45, 33.5, 2.4, FACE_E, "ITサポート室", "IT Support", dict(w=1.2, h=0.32)),
    ("counter", -14.45, 38.2, 2.4, FACE_E, "学習相談室", "Learning Support", dict(w=1.2, h=0.32)),
    ("counter", -15.4, 43.95, 2.4, FACE_S, "多目的室1", "Multipurpose Room 1",
     dict(w=1.2, h=0.32)),
    ("counter", -11.65, 43.95, 1.6, FACE_S, "多目的室2", "Multipurpose Room 2",
     dict(w=1.0, h=0.3)),
    ("gallery", 13.5, 9.05, 2.2, FACE_N, "文庫・新書", "Paperbacks", dict(w=1.2, h=0.36)),
)
HANG_2F = (
    ("gallery", 6.0, 24.0, D.C2, FACE_W, "ブックギャラリー", "Book Gallery",
     dict(w=2.4, h=0.5, drop=1.0)),
    ("stacks", 22.5, 16.6, D.C2, FACE_W, "開架書架", "Open Stacks", dict(w=1.8, h=0.46, drop=0.6)),
    ("west2f", -20.0, 40.0, D.C2, FACE_E, "参考図書", "Reference", dict(w=1.8, h=0.46, drop=0.6)),
)
GROUP = ("group", 20.5, 51.5, 6.2, FACE_W, "グループ学習室", "Group Study Rooms",
         dict(w=1.8, h=0.4))
WALL_UPPER = (
    ("mokko", -37.5, 45.45, 7.5, FACE_S, "黙 考 書 院", "Mokko Library",
     dict(w=2.0, h=0.5, plate=MOKKO)),
    ("foyer", 16.25, 10.0, 12.6, FACE_W, "大村ホール", "Omura Hall", dict(w=1.6, h=0.46)),
    ("foyer", 0.0, 18.45, 15.8, FACE_S, "大ホール", "Main Hall", dict(w=2.4, h=0.6)),
    ("foyer", 14.0, 18.45, 20.0, FACE_S, "2階席入口", "Balcony Entrance", dict(w=1.8, h=0.46)),
    ("foyer", -6.85, 10.2, 11.2, FACE_W, "インフォメーション", "Information", dict(w=1.2, h=0.3)),
    ("entry", 19.42, 12.0, 2.6, FACE_W, "未来わくわく館", "Mirai Wakuwaku-kan",
     dict(w=1.8, h=0.46)),
    ("entry", -19.42, 9.0, 2.6, FACE_E, "理科大サイエンス道場", "TUS Science Dojo",
     dict(w=2.0, h=0.46, plate=GREY)),
)
COUNTER_HANG = ("counter", -13.5, 25.5, D.C1, FACE_E, "貸出・返却", "Circulation Desk",
                dict(w=2.4, h=0.5, drop=0.7))
# 吊り看板の吊り元 (x, y, z_ceil)。plan_library_shell はこの真上に照明を置かない
HANGS = tuple((r[1], r[2], r[3]) for r in (CARREL, MEDIA) + HANG_2F + (COUNTER_HANG,))


def _wall(c, M, row):
    mesh, x, y, z, ang, ja, en, kw = row
    signs.wall(c, M[mesh], x, y, z, ang, ja=ja, en=en, **kw)


def _hang(c, M, row):
    mesh, x, y, z, ang, ja, en, kw = row
    signs.hanging(c, M[mesh], x, y, z, ang, ja=ja, en=en, **kw)


def build(c, M):
    for row in WALL:
        _wall(c, M, row)
    _hang(c, M, CARREL)
    for row in WALL_REST:
        _wall(c, M, row)
    _hang(c, M, MEDIA)
    for row in WALL_1F:
        _wall(c, M, row)
    for row in HANG_2F:
        _hang(c, M, row)
    _wall(c, M, GROUP)
    for k, x in enumerate(GROUP_X, 1):
        _wall(c, M, ("group", x, 53.45, 6.8, FACE_S, str(k), "Room %d" % k,
                     dict(w=1.2, h=0.9, plate=NUMBER)))
    for row in WALL_UPPER:
        _wall(c, M, row)
    _hang(c, M, COUNTER_HANG)
