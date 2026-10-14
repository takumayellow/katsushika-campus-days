"""共創棟の看板 23 枚（spec §2 の表の順。sign_kyoso_1..23 は呼ぶ順に付く）。座標は建物のローカル。"""

from . import plan_kyoso_dims as D
from . import signs

WOOD = "kyoso_wood_oak"
BOARD = "kyoso_chalkboard"


def build(c, mb):
    w, h = signs.wall, signs.hanging
    M, W, E = D.FACE_MALL, D.FACE_WEST, D.FACE_EAST
    # カフェ（公園側の帯）
    w(c, mb, 15.4, 34.37, 3.3, M, ja="カフェ", en="CAFE", w=3.6, h=0.42,
      plate="kyoso_sb_sage")
    w(c, mb, 12.14, 37.8, 3.65, E, ja="カフェ", en="CAFE", w=1.1, h=0.34, plate=WOOD)
    w(c, mb, 12.14, 38.4, 1.5, E, ja="いらっしゃいませ", en="Welcome", w=0.8, h=0.9,
      plate=BOARD, frame=WOOD)
    w(c, mb, 12.14, 37.4, 1.4, E, ja="ご注文はこちら", en="ORDER HERE", w=1.1, h=0.4,
      plate=BOARD, frame=WOOD)
    w(c, mb, D.STUDIO[0], 11.8, 3.2, E, ja="スタジオ", en="Studio", w=1.2, h=0.4)
    # コンビニ
    w(c, mb, -14.7, D.Y_PARK0 - 0.15, 4.0, M, ja="コンビニ", en="Convenience Store",
      w=4.4, h=0.42, plate="kyoso_fm_white")
    h(c, mb, -18.5, 35.0, D.Z_CEIL, M, ja="日用品", en="Daily Goods", w=0.6, h=0.9,
      drop=1.55, both=False)
    # 区画の名前
    h(c, mb, 9.4, 15.4, D.Z_VOID, M, ja="ラーニングスクエア1", en="Learning Square 1",
      w=2.4, h=0.5, drop=0.75)
    h(c, mb, 0.0, 7.6, D.Z_VOID, M, ja="エントランスホール", en="Entrance Hall", w=2.0,
      h=0.45, drop=1.15)
    w(c, mb, D.ATRIUM[0], 34.0, 9.0, W, ja="東京理科大学 葛飾キャンパス",
      en="TOKYO UNIVERSITY OF SCIENCE KATSUSHIKA CAMPUS", w=6.0, h=1.6,
      plate="kyoso_projection_wall")
    w(c, mb, D.ATRIUM[0] + 0.1, 34.0, 3.0, W, ja="ラーニングスクエア2",
      en="Learning Square 2", w=1.8, h=0.4)
    h(c, mb, -23.0, 15.5, D.Z_VOID, E, ja="2F ラウンジ", en="2F Lounge", w=1.4, h=0.4,
      drop=1.35, both=False)
    w(c, mb, D.CORE_W[0], 23.2, 2.6, E, ja="自動販売機", en="Vending Machines", w=1.4,
      h=0.34)
    # 2F
    h(c, mb, 4.0, 19.6, D.Z_CEIL2, M, ja="ラウンジ", en="Lounge", w=1.2, h=0.4, drop=0.6,
      both=False)
    w(c, mb, D.CORE_W[0], 26.4, D.Z2 + 2.1, E, ja="忘れ物", en="Lost & Found", w=1.0,
      h=0.4)
    w(c, mb, D.CORE_W[0], 22.6, D.Z2 + 2.2, E, ja="葛飾学生支援課",
      en="Student Support Office", w=1.6, h=0.4)
    # 東のコアの南の面
    y = D.CORE_E[1]
    w(c, mb, -21.0, y, 2.6, M, ja="エレベーター", en="Elevators", w=1.0, h=0.3)
    w(c, mb, -13.9, y, 1.5, M, ja="本学関係者以外の\n立ち入りは\nご遠慮ください",
      en="Authorized persons only", w=0.9, h=0.5, plate="plastic_white")
    w(c, mb, -2.6, 6.2, 1.5, M, ja="1階 フロアガイド", en="1F Floor Guide", w=1.2, h=0.4)
    w(c, mb, -16.5, y, 2.4, M, ja="トイレ", en="Restrooms", w=0.8, h=0.3)
    # カフェの箱の正面の黒板のメニュー
    for x, ja, en in ((13.6, "コーヒー", "COFFEE"),
                      (15.4, "ティー", "TEA"),
                      (17.2, "冷たいドリンク", "COLD DRINKS")):
        w(c, mb, x, 36.57, 2.4, M, ja=ja, en=en, w=1.0, h=0.7, plate=BOARD, frame=WOOD)
