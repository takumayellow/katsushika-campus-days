"""葛飾コミュニティハウス 1F の専用什器（形だけ。Empty や c.* は触らない）。

dorm_interior.py から呼ぶ。座標は建物ローカル（+Y = 奥）。向きは kit.T と同じで、
ang = 0 が正面 +Y、+π/2 が正面 -X、-π/2 が正面 +X、π が正面 -Y。
色は館内写真（食堂・キッチン・浴室まわり）から読んだもの。
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior.kit import T                        # noqa: E402

WOOD_DARK = "desk_dark"        # 食堂のテーブル・椅子の枠・柱・キッチンの扉
TOP_WHITE = "plastic_white"


def cafe_chair(mb, x, y, ang=0.0):
    """食堂の椅子。こげ茶の木枠に白い座面（写真どおり）。約 64 三角形。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.21, -0.21, 0.42, 0.21, 0.21, 0.47, TOP_WHITE)
    t.box(mb, -0.21, -0.23, 0.47, 0.21, -0.18, 0.84, WOOD_DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * 0.18 - 0.02, sy * 0.18 - 0.02, 0.0,
                     sx * 0.18 + 0.02, sy * 0.18 + 0.02, 0.42, WOOD_DARK)


def stool(mb, x, y):
    """窓際カウンター用のハイスツール（白い座面・黒い脚）。"""
    kit.cyl(mb, x, y, 0.0, 0.66, 0.035, "metal_dark", seg=6, cap_top=False)
    kit.cyl(mb, x, y, 0.0, 0.03, 0.20, "metal_dark", seg=8)
    kit.cyl(mb, x, y, 0.66, 0.72, 0.19, TOP_WHITE, seg=10)


def microwave(mb, x, y, z, ang=0.0):
    """電子レンジ（正面 = ang の向き）。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.25, -0.19, 0.0, 0.25, 0.19, 0.30, "plastic_white")
    t.box(mb, -0.21, 0.19, 0.05, 0.08, 0.20, 0.26, "plastic_black")
    t.box(mb, 0.12, 0.19, 0.08, 0.21, 0.20, 0.24, "metal_dark")


def pot(mb, x, y, z):
    """電気ポット。"""
    kit.cyl(mb, x, y, z, z + 0.30, 0.10, "plastic_white", seg=8)
    kit.cyl(mb, x, y, z + 0.30, z + 0.34, 0.07, "plastic_black", seg=8)


def copier(mb, x, y, ang=0.0):
    """コピー機（カフェラウンジの設備）。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.32, -0.30, 0.0, 0.32, 0.30, 0.95, "plastic_white")
    t.box(mb, -0.30, -0.28, 0.95, 0.30, 0.26, 1.05, "chair_grey")
    t.box(mb, 0.08, 0.20, 1.05, 0.28, 0.30, 1.10, "plastic_black")
    t.box(mb, -0.28, 0.30, 0.18, 0.28, 0.31, 0.60, "chair_grey")


def ic_reader(mb, x, y, z, ang=0.0):
    """IC キーの読み取り部（黒い箱 + 青い表示）。壁に付ける前提で奥行き 4 cm。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.07, 0.0, 0.0, 0.07, 0.04, 0.20, "plastic_black")
    t.box(mb, -0.05, 0.04, 0.10, 0.05, 0.045, 0.17, "screen_blue")


def intercom_stand(mb, x, y, ang=0.0):
    """オートロックの集合玄関機（ステンレスの台 + テンキー + IC 読み取り）。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.20, -0.14, 0.0, 0.20, 0.14, 1.10, "stainless")
    t.box(mb, -0.20, -0.14, 1.10, 0.20, 0.10, 1.42, "stainless")
    t.box(mb, -0.15, 0.10, 1.16, 0.15, 0.12, 1.38, "plastic_black")
    t.box(mb, -0.09, 0.12, 1.26, 0.09, 0.125, 1.35, "screen_blue")
    ic_reader(mb, *t.p2(0.0, 0.14), z=0.92, ang=ang)


def massage_chair(mb, x, y, ang=0.0):
    """マッサージチェア（黒い合皮）。正面 = ang の向き。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.40, -0.45, 0.0, 0.40, 0.40, 0.44, "plastic_black")
    t.box(mb, -0.28, -0.40, 0.44, 0.28, 0.35, 0.54, "chair_grey")
    t.box(mb, -0.34, -0.58, 0.44, 0.34, -0.36, 1.30, "plastic_black")
    t.box(mb, -0.26, -0.40, 0.54, 0.26, -0.34, 1.18, "chair_grey")
    for sx in (-1, 1):
        t.box(mb, sx * 0.40 - sx * 0.12, -0.45, 0.44, sx * 0.40, 0.34, 0.76,
              "plastic_black")
    t.box(mb, -0.24, 0.40, 0.0, 0.24, 0.78, 0.36, "plastic_black")


def noren(mb, x, y, ang, mat, w=0.92, z0=1.40, z1=2.08):
    """暖簾（中央でふたつに割れた布 + ステンレスの竿）。正面 = ang の向き。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw - 0.04, -0.015, z1, hw + 0.04, 0.015, z1 + 0.03, "stainless")
    t.box(mb, -hw, -0.01, z0, -0.015, 0.01, z1, mat)
    t.box(mb, 0.015, -0.01, z0, hw, 0.01, z1, mat)


def kitchen_run(mb, x0, x1, yw, s, sinks=(), hobs=(), d=0.65):
    """壁付けのキッチン 1 列（こげ茶の扉・白い天板・IH・黒いレンジフード）。

    yw は壁の面、s = +1 なら壁から +Y へ、-1 なら -Y へ張り出す（= 正面の向き）。
    sinks / hobs は流し・IH の中心 x。
    """
    yf = yw + s * d
    kit.box(mb, x0, yw, 0.08, x1, yf, 0.86, WOOD_DARK)
    kit.box(mb, x0, yw, 0.0, x1, yf - s * 0.06, 0.08, "plastic_black")
    kit.box(mb, x0, yw, 0.86, x1, yf + s * 0.02, 0.90, TOP_WHITE)
    for xs in sinks:
        kit.box(mb, xs - 0.40, yw + s * 0.14, 0.84, xs + 0.40, yw + s * 0.54,
                0.905, "stainless")
        kit.box(mb, xs - 0.03, yw + s * 0.04, 0.90, xs + 0.03, yw + s * 0.10,
                1.22, "stainless")
        kit.box(mb, xs - 0.03, yw + s * 0.04, 1.16, xs + 0.03, yw + s * 0.28,
                1.22, "stainless")
    for xh in hobs:
        kit.box(mb, xh - 0.30, yw + s * 0.10, 0.90, xh + 0.30, yw + s * 0.56,
                0.912, "plastic_black")
        kit.box(mb, xh - 0.45, yw, 1.80, xh + 0.45, yw + s * 0.55, 2.20,
                "plastic_black")
        kit.box(mb, xh - 0.12, yw, 2.20, xh + 0.12, yw + s * 0.24, 2.70,
                "plastic_black")
    # 吊り戸棚（流しの上だけ）
    for xs in sinks:
        kit.box(mb, xs - 0.55, yw, 1.55, xs + 0.55, yw + s * 0.34, 2.20,
                WOOD_DARK)


def serving_counter(mb, x0, x1, y0, y1, h=0.95):
    """配膳口の前のカウンター（こげ茶 + 濃い天板）。"""
    kit.box(mb, x0, y0, 0.0, x1, y1, h - 0.04, WOOD_DARK)
    kit.box(mb, x0 - 0.03, y0 - 0.03, h - 0.04, x1 + 0.03, y1, h, "counter_dark")


def blinds(mb, x, y0, y1, z0, z1, side=-1):
    """窓の内側の横型ブラインド（半分まで下ろした状態）。side = 室内へ出る向き（x）。"""
    kit.box(mb, x, y0, z0, x + side * 0.03, y1, z1, "plastic_white")
    n = max(2, int((z1 - z0) / 0.12))
    for i in range(1, n):
        z = z0 + (z1 - z0) * i / n
        kit.box(mb, x + side * 0.03, y0, z - 0.005, x + side * 0.035, y1, z + 0.005,
                "chair_grey")
