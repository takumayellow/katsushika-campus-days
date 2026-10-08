"""講義棟（lecture）の寸法・POI・NPC・カメラ・看板の表。

数値の出典は docs/ref/interiors/lecture/spec.md（§0 の座標、§1 の間取り、§6 の POI・NPC・看板、§8 のカメラ）。
ローカル座標は原点 = entrance_lecture の真下の床、+X = 実物の東、+Y = 実物の北（屋内の奥）、Z が上。
"""

import math

# ---- 階 ----
FL = (0.0, 4.6, 8.8, 13.0, 17.2, 21.4, 25.6)   # 1F〜7F の床の高さ
F2, F3 = FL[1], FL[2]
CEIL_CORR = 3.0        # 中廊下・コアの前の天井
CEIL_101 = 4.1         # 101 の平天井
CEIL_104 = 3.0
CEIL_EAST_LOW = 4.0    # 東端ホールのうち 2F の床の下
CEIL_EAST_VOID = 8.5   # 東端ホールの 2 層吹き抜け（3F の床の下）
CEIL_3F = 12.4         # 3F 自習室
VOID_TOP = 25.1        # 中央の吹き抜けの頂部（7F の床の下面）
SLAB = 0.22            # 床スラブの厚み（shell.floor の既定）

# ---- 外周（spec §0。外接枠の内側の線は X -32.88〜49.52、Y 4.3〜54.62） ----
X_W = -28.6            # 西の外壁の外面
X_E = 49.82            # 東の外壁の外面（外接枠）
Y_S = 4.0              # 南の外壁の外面（外接枠）
Y_EAST_S = 8.0         # 東端ホールの南の外壁の外面
X_STEP = 28.6          # 101 の東の外壁（南の外壁が北へ折れる X）
Y_N_BUMP = 54.92       # 生協の北の張り出しの外面（外接枠）
X_BUMP_W = 35.1        # 張り出しの西の外面
N_WALL = ((-28.8, 50.61), (35.59, 50.14))       # 北の外壁の外面（斜めの線）


def north_y(x):
    """北の外壁の外面の Y（X での値）。"""
    (xa, ya), (xb, yb) = N_WALL
    return ya + (x - xa) * (yb - ya) / (xb - xa)


# ---- 南北の帯（1F〜3F 共通） ----
Y_ROOM_S1 = 19.8       # 南の室の廊下側の面
Y_101_N = 19.65        # 101 の黒板の壁の面（内法）
Y_CORE0 = 23.5         # コアの南面
Y_CORE1 = 31.5         # コアの北面
Y_ROOM_N0 = 35.1       # 北の室の廊下側の面

# ---- 1F の南の並び ----
LOBBY_X = (-3.25, 7.2)             # 南の入口ロビー
VESTIBULE = (-2.0, 4.3, 2.0, 7.0)  # 南の風除室 (x0, y0, x1, y1)
R101_X = (7.35, 28.25)
R101_DOORS = (9.0, 12.6, 23.3, 26.9)   # 扉の中心（幅 1.6・高さ 2.3）
R102_X = (-24.7, -3.4)
R102_DOORS = (-23.2, -19.6, -8.9, -5.3)
DOOR2_W, DOOR2_H = 1.6, 2.3            # 教室の両開き扉

# ---- 1F のコア（Y 23.5〜31.5） ----
WEST_LOBBY_X1 = -21.0
CORE_EV = (-21.0, -14.1)
CORE_WC_W = (-12.5, -2.8)
ATRIUM = (-2.8, 23.5, 6.6, 31.5)       # 吹き抜け (x0, y0, x1, y1)
CORE_WC_M = (6.6, 16.2)
SCREEN_X = (17.3, 18.0)                # 黒い腰壁 + ガラスの仕切り
ESC_X = (20.4, 33.8)                   # エスカレータ（平場を含む）
ESC_LAND_W = (20.4, 22.6)
ESC_LAND_E = (30.6, 33.8)
ESC_S_Y = (25.6, 27.4)                 # 南の列（東へ上る）
ESC_N_Y = (27.6, 29.4)                 # 北の列（西へ上る）
ESC_RISE, ESC_RUN = 0.2, 0.35
CAFE_DOORS_Y = (21.6, 33.3)            # 西の外壁の学生食堂への扉（閉じる）

# ---- 1F の北の並び（Y 35.1〜北の外壁） ----
N_ROOMS = (-28.6, -21.8, -14.2, 7.3, 21.6, 28.7, 31.0, 35.8)   # 境の X
HEALTH_DOOR_X = -17.0
R104_X = (7.3, 21.6)
R104_DOORS = (9.0, 20.0)
COOP_X = (35.8, 49.5)
COOP_KIOSK_X = (31.0, 35.8)            # 受付窓の小室
N_EXIT_X = (28.7, 31.0)                # 北の出口通路

# ---- 東端ホール ----
EAST_X = (28.6, 49.5)
EAST_Y = (8.0, 35.1)
EAST_VOID_A = (39.8, 20.7, 47.7, 30.4)     # 2 層吹き抜け（北）
EAST_VOID_B = (37.8, 8.2, 49.5, 20.7)      # 2 層吹き抜け（南）
SE_EV = (28.6, 10.5, 32.2, 15.2)
SE_STAIR = (32.2, 8.0, 37.8, 16.5)
SE_VESTIBULE = (42.9, 8.0, 46.6, 11.6)
DIRECT_STAIR_X = (47.7, 49.5)
DIRECT_STAIR_Y = (20.6, 30.4)
DIRECT_RISE, DIRECT_RUN, DIRECT_STEPS = 0.177, 0.30, 13   # 2 連

# ---- 2F・3F（X >= 16.4 だけ歩ける） ----
UPPER_X0 = 16.4
BRIDGE_2F = (39.8, 30.4, 49.3, 35.1)
LOUNGE_2F = ((39.7, 35.1, 46.5, 39.0), (39.7, 39.0, 43.2, 51.2))   # L 字（推定の分け方）
ARC_C = (35.7, 33.4)                   # 3F 自習室の楕円の中心
ARC_R = (9.2, 13.0)                    # 東西・南北の半径
TERRACE_X1 = 49.8
VENDING_3F = (37.8, 36.4, 41.5)        # X, Y0, Y1

# ---- 101（§4） ----
HALL_FLAT_Y = (16.9, 19.65)            # 平場
HALL_ROWS = 10
HALL_RISE = 0.12                       # 列 k の踏面は Y 16.9-k 〜 17.9-k、z 0.12k
HALL_BACK_Y = (4.3, 6.9)               # 後ろの通路（z 1.2）
HALL_CHAMFER = 2.5                     # 南の 2 つの角の斜めの切り欠き
HALL_BLOCKS = ((8.7, 13.5), (14.5, 21.1), (22.15, 26.95))
HALL_LECTERN = (17.75, 18.1)
HALL_RACK = (20.3, 18.6)
BOARDS = (("lecture_board_green", 13.5, 15.8), ("lecture_board_bluegreen", 15.8, 19.7),
          ("lecture_board_green", 19.7, 22.0))
BOARD_Z = (0.9, 2.1)
CLOCK_101 = (24.5, 3.0)

# ---- POI（spec §6-1。名前は変えない） ----
POIS = (
    ("atrium", 1.9, 27.5, 0.0),
    ("hall_booth", 20.3, 17.9, 0.0),
    ("seminar", 14.6, 42.0, 0.0),
    ("hall_stage", 17.75, 17.3, 0.0),
    ("hall_seats", 14.0, 12.4, 0.60),
    ("grand_stair", 48.6, 19.6, 0.0),
    ("lecture_room", -8.9, 21.0, 0.0),
    ("foyer_counter", 33.4, 34.0, 0.0),
    ("foyer", 2.5, 12.0, 0.0),
    ("coop", 40.0, 42.0, 0.0),
    ("lounge", 42.0, 28.0, F3),
    ("corridor", -6.0, 21.6, 0.0),
    ("health", -17.0, 33.6, 0.0),
)

# ---- NPC（spec §6-2） ----
NPCS = (
    (17.75, 18.9, 0.0), (10.6, 14.4, 0.36), (18.0, 11.4, 0.72), (-2.2, 12.5, 0.0),
    (-12.0, 21.8, 0.0), (22.0, 32.0, 0.0), (42.0, 40.0, 0.0), (33.4, 36.0, 0.0),
    (12.0, 44.0, 0.0), (41.5, 31.0, F3),
)

SPAWN = (0.0, 7.6, 0.0)
EXIT = (0.0, 4.8, 0.0)
DOOR_GAP = (-1.6, 1.6)                 # 入口の開口（STANDARD、幅 3.2・高さ 2.6）
DOOR_H = 2.6

# ---- カメラ（spec §8。suffix は c.cam の書き方） ----
CAMS = (
    ("", (1.5, 7.8, 1.7), (2.0, 27.5, 5.0), 16.0),
    ("=interior_lecture_hall", (17.8, 5.5, 2.8), (17.8, 19.4, 1.6), 18.0),
    ("corridor", (-27.0, 21.6, 1.7), (30.0, 21.6, 1.7), 22.0),
    ("atrium", (1.9, 24.2, 1.5), (1.9, 27.5, 20.0), 14.0),
    ("coop", (40.5, 14.0, 1.7), (40.5, 35.1, 2.2), 18.0),
    ("lounge3f", (30.5, 32.5, 10.4), (43.0, 33.0, 9.8), 18.0),
)

# ---- 看板（spec §6-3） ----
# (kind, ja, en, w, h, x, y, z, ang, plate, opts)
#   kind "wall": (x, y, z) は壁の面から 0.01 m 浮かせた板の中心
#   kind "hang": (x, y, z) は天井の吊り元
S, N, E = math.pi, 0.0, -math.pi * 0.5     # 正面が -Y / +Y / +X
BLACK, WHITE = "lecture_sign_black", "lecture_sign_white"
BLUE, BAND = "sign_plate_blue", "lecture_coop_band"
SIGNS = (
    ("wall", "1F", "1st Floor", 0.7, 0.6, 9.0, 23.49, 2.2, S, BLACK, {}),
    ("hang", "エスカレーター", "Escalator", 1.6, 0.42, 20.4, 21.6, CEIL_CORR, E, BLUE, {}),
    ("hang", "学生食堂へ", "To Cafeteria", 1.6, 0.42, -25.0, 27.5, CEIL_CORR, E, BLUE, {}),
    ("wall", "エレベーター", "Elevator", 1.0, 0.3, -17.6, 23.49, 2.4, S, BLUE, {}),
    ("wall", "女子トイレ", "Women", 0.8, 0.3, -7.8, 23.49, 2.4, S, BLUE, {}),
    ("wall", "男子トイレ", "Men", 0.8, 0.3, 14.0, 23.49, 2.4, S, BLUE, {}),
    ("wall", "多目的トイレ", "Accessible Toilet", 1.0, 0.3, -17.6, 31.51, 2.4, N, BLUE, {}),
    ("wall", "101", "Room 101", 0.42, 0.9, 11.45, 19.81, 1.8, N, WHITE, {}),
    ("wall", "101", "Room 101", 0.42, 0.9, 24.45, 19.81, 1.8, N, WHITE, {"anchor": False}),
    ("wall", "102", "Room 102", 0.42, 0.9, -10.15, 19.81, 1.8, N, WHITE, {}),
    ("wall", "103", "Room 103", 0.42, 0.9, -3.5, 35.09, 1.8, S, WHITE, {}),
    ("wall", "104", "Room 104", 0.42, 0.9, 10.3, 35.09, 1.8, S, WHITE, {}),
    ("wall", "保健管理センター", "Health Care Center", 1.4, 0.36, -18.6, 35.09, 2.3, S, BLUE, {}),
    ("wall", "中央監視室", "Central Monitoring Room", 1.0, 0.3, 25.2, 35.09, 2.3, S, BLUE, {}),
    ("hang", "出口", "Exit", 0.9, 0.36, 0.0, 7.6, CEIL_CORR, 0.0, "sign_exit_green", {}),
    ("hang", "出口", "Exit", 0.9, 0.36, 44.75, 10.5, CEIL_CORR, 0.0, "sign_exit_green",
     {"anchor": False}),
    ("hang", "出口", "Exit", 0.9, 0.36, 29.85, 49.5, CEIL_CORR, E, "sign_exit_green",
     {"anchor": False}),
    ("wall", "東京理科大学生協", "Tokyo University of Science Co-op", 3.2, 0.5,
     40.0, 35.09, 3.0, S, BAND, {}),
    ("wall", "店舗入口はこちら→", "Store Entrance →", 1.2, 0.3, 36.6, 35.09, 1.5, S, BLUE, {}),
    ("hang", "入口", "Entrance", 0.9, 0.36, 38.0, 36.0, CEIL_CORR, 0.0, BLUE, {}),
    ("wall", "理科大グッズ 販売中", "TUS Goods on Sale", 1.0, 0.36, 44.0, 46.0, 1.6, S, BLUE, {}),
    ("wall", "コーヒー ¥100\nアイスコーヒー ¥185", "Coffee ¥100\nIced Coffee ¥185", 0.8, 1.0,
     37.0, 33.0, 0.9, S, "lecture_board_green", {}),
    ("wall", "1", "Register 1", 0.36, 0.3, 0.0, 0.0, 1.35, S, BLUE, {"register": 0}),
    ("wall", "2", "Register 2", 0.36, 0.3, 0.0, 0.0, 1.35, S, BLUE, {"register": 1}),
    ("wall", "3", "Register 3", 0.36, 0.3, 0.0, 0.0, 1.35, S, BLUE, {"register": 2}),
    ("wall", "4", "Register 4", 0.36, 0.3, 0.0, 0.0, 1.35, S, BLUE, {"register": 3}),
    ("hang", "サービスカウンター", "Service Counter", 1.4, 0.36, 42.0, 38.5, CEIL_CORR, S, BLUE,
     {"both": False}),
    ("hang", "スタッフのいるレジで\nお会計ください", "Please pay at a staffed register", 1.6, 0.5,
     40.0, 38.5, CEIL_CORR, S, BLUE, {"both": False}),
    ("wall", "2F", "2nd Floor", 0.7, 0.6, 18.01, 27.5, F2 + 2.2, E, BLACK, {}),
    ("wall", "201", "Room 201", 0.42, 0.9, 26.0, 19.81, F2 + 1.8, N, WHITE, {}),
    ("wall", "ラウンジ", "Lounge", 1.2, 0.36, 38.5, 35.09, F2 + 2.4, S, BLUE, {}),
    ("wall", "3F", "3rd Floor", 0.7, 0.6, 18.01, 27.5, F3 + 2.2, E, BLACK, {}),
    ("wall", "302", "Room 302", 0.42, 0.9, 20.0, 19.81, F3 + 1.8, N, WHITE, {}),
    ("wall", "310", "Room 310", 0.42, 0.9, 18.0, 35.09, F3 + 1.8, S, WHITE, {}),
    ("wall", "311", "Room 311", 0.42, 0.9, 30.0, 35.09, F3 + 1.8, S, WHITE, {}),
    ("wall", "自習室", "Self-Study Room", 1.2, 0.36, 35.0, 35.09, F3 + 2.4, S, BLUE, {}),
)
