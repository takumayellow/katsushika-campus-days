"""共創棟の看板 23 枚（spec §2 の表の順。sign_kyoso_1..23 は呼ぶ順に付く）。"""

from . import plan_kyoso_dims as D
from . import signs

WOOD = "kyoso_wood_oak"
BOARD = "kyoso_chalkboard"


def build(c, mb):
    w, h = signs.wall, signs.hanging
    N, S, W, E = D.FACE_N, D.FACE_S, D.FACE_W, D.FACE_E
    w(c, mb, -9.0, 9.0, 3.3, S, ja="カフェ", en="CAFE", w=3.6, h=0.42,
      plate="kyoso_sb_sage")
    w(c, mb, -13.5, 12.4, 3.65, W, ja="カフェ", en="CAFE", w=1.1, h=0.34, plate=WOOD)
    w(c, mb, -4.1, 7.4, 1.5, W, ja="いらっしゃいませ",
      en="Welcome", w=0.8, h=0.9, plate=BOARD, frame=WOOD)
    w(c, mb, -4.1, 8.4, 1.4, W, ja="ご注文はこちら", en="ORDER HERE",
      w=1.1, h=0.4, plate=BOARD, frame=WOOD)
    w(c, mb, 7.0, 11.8, 1.5, W, ja="ラウンジは\nこちらです！→",
      en="Lounge →", w=0.8, h=1.0, plate=BOARD, frame=WOOD)
    w(c, mb, -38.0, 9.2, 3.6, E, ja="コンビニ",
      en="Convenience Store", w=4.4, h=0.42, plate="kyoso_fm_white")
    h(c, mb, -42.6, 10.6, 4.6, E, ja="日用品", en="Daily Goods", w=0.6,
      h=0.9, drop=1.55, both=False)
    h(c, mb, -25.0, 9.6, 5.0, W, ja="ラーニングスクエア1", en="Learning Square 1", w=2.4,
      h=0.5, drop=0.6)
    h(c, mb, 0.0, 7.6, D.Z_VOID, N, ja="エントランスホール", en="Entrance Hall", w=2.0, h=0.45,
      drop=1.15)
    w(c, mb, -30.1, 7.0, 9.0, N, ja="東京理科大学 葛飾キャンパス",
      en="TOKYO UNIVERSITY OF SCIENCE KATSUSHIKA CAMPUS", w=6.0, h=1.6,
      plate="kyoso_projection_wall")
    w(c, mb, 7.0, 9.6, 2.3, W, ja="カフェラウンジ", en="Cafe Lounge", w=1.4, h=0.4)
    h(c, mb, 15.6, 6.0, 4.6, W, ja="ラウンジ2", en="Lounge 2", w=1.2, h=0.4, drop=1.3,
      both=False)
    h(c, mb, 23.6, 6.0, 4.6, W, ja="ラウンジ1", en="Lounge 1", w=1.2, h=0.4, drop=1.3,
      both=False)
    h(c, mb, -15.0, 5.6, D.Z_CEIL2, W, ja="ラウンジ", en="Lounge", w=1.2, h=0.4, drop=0.6,
      both=False)
    w(c, mb, -2.0, 12.2, 7.3, W, ja="忘れもの", en="Lost & Found", w=1.0, h=0.4)
    w(c, mb, -2.0, 9.5, 7.6, W, ja="葛飾学生支援課", en="Student Support Office", w=1.6, h=0.4)
    w(c, mb, -55.3, 6.8, 2.7, S, ja="エレベーター", en="Elevators", w=1.0, h=0.3)
    w(c, mb, -50.5, 6.8, 1.5, S, ja="本学関係者以外の\n立ち入りは\nご遠慮ください",
      en="Authorized persons only", w=0.9, h=0.5, plate="plastic_white")
    w(c, mb, 3.6, 6.2, 1.5, S, ja="1階 フロアガイド", en="1F Floor Guide", w=1.2, h=0.4)
    w(c, mb, -52.0, 6.8, 2.4, S, ja="トイレ", en="Restrooms", w=0.8, h=0.3)
    for x, ja, en in ((-11.2, "コーヒー", "COFFEE"),
                      (-9.0, "ティー", "TEA"),
                      (-6.8, "冷たいドリンク", "COLD DRINKS")):
        w(c, mb, x, 10.6, 2.6, S, ja=ja, en=en, w=1.0, h=0.7, plate=BOARD, frame=WOOD)
