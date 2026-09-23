"""キャラクター定義とプロポーション。

DESIGN.md §2 の表がここに落ちている。新しい NPC を足すときは `CHARACTERS` に
1 エントリ増やすだけでよい（髪型・衣装は既存のジェネレータを名前で選ぶ）。

プロポーションの単位について
---------------------------
`BUILDS` の半径系の値は **頭幅 (head_w) に対する比** で書く。`resolve()` が
`head_w / height` を掛けて従来どおりの「身長比」に直すので、body.py 側は
変更不要。頭幅基準にすることで「肩幅 = 頭幅 x 1.35」「腕の直径 <= 頭幅 x 0.22」
「首の直径 = 頭幅 x 0.35」といった作画の約束をそのまま数値で表現できる。

height / heads / silhouette_pad
-------------------------------
`height` と `heads` は **仕上がりのシルエット** を指す。すなわち

    height = 履物の底 → 髪の先端 の全高
    heads  = その全高 / 素体の頭高（顎 → 頭蓋の天端。髪は含まない）

素体は「床 → 頭蓋の天端」しか作らないので、髪が上へ、履物が床下へはみ出す。
`silhouette_pad = (髪, 履物)` にそのはみ出し量を head_h 比で宣言しておき、
`resolve()` が素体の高さからそのぶんを引く。これをやらないと宣言値と出来上がりが
食い違う（坊っちゃんは宣言 1.150m/2.92 頭身に対して実測 1.318m/3.36 頭身だった）。

pad は hair.py と cloth.py の形から決まる **実測値** なので、髪型や履物を変えたら
「髪パーツの最高 z - 頭蓋の天端 z」と「-（メッシュ最低 z）」を head_h で割って
測り直す。ずれた分はそのまま全高の誤差になる。
"""

from __future__ import annotations

from .tex import hex_rgb

# --------------------------------------------------------------------------
# 身体プロポーション
# --------------------------------------------------------------------------
#: 顎下（= 首から下の全長）を 1.0 としたときの各部位の高さ（通常頭身）
BODY_LEVELS = {
    "shoulder": 0.962,
    "bust": 0.860,
    "underbust": 0.810,
    "waist": 0.730,
    "hip": 0.630,
    "crotch": 0.580,
    "knee": 0.330,
    "ankle": 0.050,
}

#: 低頭身（ちび）用。頭:胴:脚 = 1:1:1 に寄せ、首をほぼ無くす
BODY_LEVELS_CHIBI = {
    "shoulder": 0.930,
    "bust": 0.800,
    "underbust": 0.745,
    "waist": 0.680,
    "hip": 0.575,
    "crotch": 0.500,
    "knee": 0.255,
    "ankle": 0.045,
}

#: 坊っちゃん専用。マドンナちゃんは公式の体型が違う（長い髪と大リボンで
#: シルエットが決まる）ので、確かめずに同じ数値へ巻き込まないよう分けてある。
#:
#: 公式 tus_chara01.jpg を頭蓋の高さ（78px）を 1 として顎から測ると
#: 帯の上端 0.60 / 袴の裾 1.26 / 下駄の台の上面 1.36 / 下駄の底 1.67。
#: 現行は帯 0.62（合っている）に対し 帯→床 が 1.41 と、公式 1.06 の 1.3 倍あった。
#: 「胴は合っていて、帯から下だけが長い」ので、ここは脚だけを詰める表になる。
BODY_LEVELS_BOTCHAN = {
    "shoulder": 0.936,
    "bust": 0.817,
    "underbust": 0.725,
    "waist": 0.570,
    "hip": 0.433,
    "crotch": 0.363,
    "knee": 0.183,
    "ankle": 0.045,
}

#: 頭幅基準で書く半径系のキー（resolve() が身長比へ変換する）
_HEAD_RELATIVE = (
    "shoulder_half", "shoulder_ry",
    "bust_rx", "bust_ry", "bust_bulge",
    "underbust_rx", "underbust_ry",
    "waist_rx", "waist_ry",
    "hip_rx", "hip_ry",
    "crotch_rx", "crotch_ry",
    "neck_r", "hand",
    "arm_r", "leg_r", "foot",
)

#: 各部位の半径（頭幅に対する比）。head_w / head_d だけは頭の高さに対する比。
#: arm_len は上腕/前腕/手の長さで、身長に対する比。
BUILDS = {
    # 美少女（6 頭身）: 肩幅 = 頭幅 x 1.35、腕の直径 = 頭幅 x 0.22、首 = 頭幅 x 0.35
    "slim": dict(
        shoulder_half=0.675, shoulder_ry=0.300,
        bust_rx=0.480, bust_ry=0.340, bust_bulge=0.120,
        underbust_rx=0.410, underbust_ry=0.285,
        waist_rx=0.352, waist_ry=0.256,
        hip_rx=0.505, hip_ry=0.350,
        crotch_rx=0.455, crotch_ry=0.330,
        neck_r=0.175,
        arm_r=(0.110, 0.094, 0.066), hand=0.115,
        leg_r=(0.255, 0.175, 0.118), foot=(0.205, 0.640, 0.155),
        arm_len=(0.170, 0.150, 0.082),
        head_w=0.800, head_d=0.828,
    ),
    # 成人男性（教授）: 肩幅 = 頭幅 x 1.56、腕の直径 = 頭幅 x 0.22
    "sturdy": dict(
        shoulder_half=0.780, shoulder_ry=0.345,
        bust_rx=0.560, bust_ry=0.400, bust_bulge=0.015,
        underbust_rx=0.510, underbust_ry=0.365,
        waist_rx=0.478, waist_ry=0.345,
        hip_rx=0.520, hip_ry=0.380,
        crotch_rx=0.495, crotch_ry=0.365,
        neck_r=0.205,
        arm_r=(0.110, 0.095, 0.070), hand=0.130,
        leg_r=(0.300, 0.205, 0.140), foot=(0.245, 0.700, 0.185),
        arm_len=(0.172, 0.152, 0.086),
        head_w=0.828, head_d=0.850,
    ),
    # ちび（女子・3 頭身）: 肩幅 = 頭幅 x 0.86、手足は短く細く、首はほぼ無い
    "chibi_f": dict(
        shoulder_half=0.430, shoulder_ry=0.215,
        bust_rx=0.330, bust_ry=0.235, bust_bulge=0.045,
        underbust_rx=0.305, underbust_ry=0.215,
        waist_rx=0.285, waist_ry=0.205,
        hip_rx=0.345, hip_ry=0.245,
        crotch_rx=0.320, crotch_ry=0.235,
        neck_r=0.150,
        arm_r=(0.108, 0.098, 0.080), hand=0.125,
        leg_r=(0.205, 0.168, 0.128), foot=(0.180, 0.470, 0.140),
        arm_len=(0.112, 0.098, 0.062),
        # chibi_f はマドンナちゃん専用。公式 tus_chara02.jpg を実測すると
        # 頭の輪郭（髪込み）98px x 見えている頭の高さ 82px で、**横のほうが
        # 広い**（W/H = 1.195）。髪のかぶさるぶんは横 1.21 倍・縦 1.21 倍なので
        # 素の頭蓋は 81 x 68px = W/H 1.19。0.862 のままだと縦長の卵形になり、
        # シルエットの中で頭だけが 31% 小さく見えていた（輪郭幅/全高が
        # 公式 0.465 に対し 0.328）。
        head_w=1.180, head_d=0.985,
    ),
    # ちび（男子・3 頭身）
    "chibi_m": dict(
        # 公式と自分のプレビューを頭高で正規化し、行ごとに幅を比べて決めた。
        # 公式は「肩が一番広くて帯でくびれる逆台形」（幅/頭高: 肩 1.83 →
        # 帯 1.35）。こちらは肩 1.12 / 帯 1.65 と上下が逆さまだった。
        # 一律に太らせると帯から下が先に太くなるので、肩を広げて腰を絞る。
        # 首だけは公式でもほぼ見えないので太らせない。
        shoulder_half=0.560, shoulder_ry=0.272,
        bust_rx=0.412, bust_ry=0.300, bust_bulge=0.000,
        underbust_rx=0.372, underbust_ry=0.272,
        waist_rx=0.330, waist_ry=0.246,
        hip_rx=0.400, hip_ry=0.262,
        crotch_rx=0.390, crotch_ry=0.256,
        neck_r=0.165,
        # 公式の腕と脚は短くて太い。袖と袴から出る部分しか見えないので、
        # 手首と足首（各タプルの末尾）を優先して太らせる。細いまま残すと
        # ミトンと下駄だけが大きい、棒に刺さった団子に見える。
        arm_r=(0.118, 0.110, 0.098), hand=0.142,
        leg_r=(0.230, 0.196, 0.168), foot=(0.196, 0.500, 0.150),
        arm_len=(0.116, 0.100, 0.064),
        # 公式の頭は「角の丸い四角」で、輪郭の幅 91px が頭蓋の高さ 78px の
        # 1.17 倍ある。髪のかぶさるぶん（実測で頭幅の 1.19 倍に広がる）を
        # 引いて head_w = 1.17 / 1.19 ≒ 0.98。0.884 のままだと卵形になる。
        head_w=0.980, head_d=0.900,
    ),
}

#: 顔テクスチャ上の配置（v=0 が顎の少し下、v=1 が頭頂）
#: 目の中心は顔の高さの 1/2 よりわずかに下、目幅は顔幅の約 1/4
FACE_DEFAULT = dict(eye_y=0.398, eye_dx=0.192, eye_rx=0.086, eye_ry=0.113)



def _c(h: str):
    return hex_rgb(h)


# --------------------------------------------------------------------------
# キャラクター
# --------------------------------------------------------------------------

CHARACTERS: dict[str, dict] = {
    # ----------------------------------------------------------------- mirai
    "mirai": dict(
        id="mirai",
        jp="新宿 みらい",
        height=1.58, heads=6.0, build="slim",
        skin=_c("#F4C9B2"),
        hair_color=_c("#C9A27E"),
        eye_color=_c("#5ED6DE"),
        lash_color=_c("#4A2B2B"),
        brow_color=_c("#A9835F"),
        blush_color=_c("#FF9198"), blush_strength=0.52,
        mouth_color=_c("#C8434B"), mouth_style="smile", mouth_w=0.038,
        eye_style="round", eye_tilt=4.0,
        hair="bob", hair_accessory="pin_blue",
        outfit="seifuku",
        face_layout=dict(FACE_DEFAULT),
        sign_items=("青いヘアピン", "トートバッグ", "緑のリボン"),
        mat_colors={"hair": "#C9A27E", "eye_l": "#5ED6DE", "eye_r": "#5ED6DE",
                    "metal": "#4FA3E8"},
        accessories=("tote",),
    ),
    # --------------------------------------------------------------- botchan
    "botchan": dict(
        id="botchan",
        jp="坊っちゃん",
        # 公式 tus_chara01.jpg の実測: 全高 221px / 頭蓋（髪の下）81px = 2.73 頭身。
        # 2.83 に留めているのは、髪の逆立ちぶんを頭に数えない実装上の都合で
        # 2.73 まで詰めると胴が潰れて見えるため（見た目は 35% 前後で合う）。
        # silhouette_pad は組み上げたメッシュの実測値（髪の天端と下駄の底を
        # head_h で割ったもの）。髪型・履物を変えたら測り直すこと。
        height=1.15, heads=2.83, silhouette_pad=(0.183, 0.257),
        build="chibi_m", chibi=True, levels=BODY_LEVELS_BOTCHAN,
        skin=_c("#EBBE9C"),
        hair_color=_c("#1A1A22"),
        eye_color=_c("#4A3324"),
        lash_color=_c("#171720"),
        brow_color=_c("#14141C"),
        blush_color=_c("#E98A78"), blush_strength=0.0,
        # 公式イラストどおりの男の子: 点目・太い直線眉・への字口・
        # 逆立てた短髪。
        #
        # 公式 tus_chara01.jpg を「顎 y=99 / 地髪の天端 y=20（頭高 79px）/
        # 頭幅 89px」で実測すると
        #     目の中心  顎から 0.513 頭高   目の大きさ 8 x 13 px
        #     眉の中心  顎から 0.67  頭高   眉の長さ 16〜26px・太さ 4〜6px
        #     口の中心  顎から 0.266 頭高   口幅 18px
        #     目の間隔（中心間）34px = 頭幅の 0.38
        # これに対し統合前は 目 0.347 頭高・目の高さ 0.082 頭高・口幅 0.10 頭幅・
        # 目の間隔 0.50 頭幅 と、「小さい目鼻が顔の下半分に寄り、額だけが
        # 異様に広い」状態だった。額が広く見えた原因は生え際ではなく
        # 目鼻の位置なので、ここを公式の実測値へ動かす。
        #   eye_y  0.352 -> 0.510   eye_dx 0.175 -> 0.130
        #   eye_ry 0.044 -> 0.078   mouth_w 0.030 -> 0.060
        mouth_color=_c("#5A2A22"), mouth_style="frown", mouth_w=0.060,
        eye_style="dot", eye_tilt=0.0,
        brow_width=0.030, brow_arch=0.0,
        hair="crew", hair_accessory=None,
        outfit="kimono_botchan",
        face_layout=dict(eye_y=0.510, eye_dx=0.130, eye_rx=0.034, eye_ry=0.078),
        mat_colors={"hair": "#1A1A22", "eye_l": "#1A1A22", "eye_r": "#1A1A22"},
        pattern_scale=16.0,
        accessories=("furoshiki",),
        bag_mount="shoulder",
        sign_items=("十字絣", "高下駄", "赤い風呂敷"),
    ),
    # --------------------------------------------------------------- madonna
    "madonna": dict(
        id="madonna",
        jp="マドンナちゃん",
        # 公式 tus_chara02.jpg (332x240) の実測。髪の天端 y=24 / 顎 y=106 /
        # ブーツの底 y=235 → 全高 211px、見えている頭 82px = 2.57 頭身。
        # 髪を除いた頭蓋は 68px なので 211/68 = 3.10 …だが heads は
        # 「素体の頭高」に対する比なので、髪のはみ出しは silhouette_pad で
        # 別に引く。3.10 のままだと胴が長く見えるので 2.98 に留める。
        height=1.14, heads=2.98, silhouette_pad=(0.210, 0.018),
        build="chibi_f", chibi=True,
        skin=_c("#F5CDBA"),
        # 公式の髪は明るい黄土色 #AA6D26。#8B5A2B は暗くて赤黒い。
        hair_color=_c("#AA6D26"),
        # 公式の目は白目も虹彩も無い真っ黒な縦長楕円 12x22px に、
        # 4 方向の白いキラ星が 1 個だけ。茶色の虹彩は描かれていない。
        eye_color=_c("#15110F"),
        lash_color=_c("#15110F"),
        brow_color=_c("#6E4520"), brow_style="none",  # 公式は眉が 1 本も無い
        nose=False,                                    # 公式は鼻も無い
        # 公式の頬紅は実測 (250,196,168)。#FF8F9C を 0.58 で乗せると
        # (250,169,169) になり、赤すぎるうえに位置が目の真下ではなく
        # 目の下端に食い込んでいた（レンダを拡大して確認）。
        blush_color=_c("#FFAF8E"), blush_strength=0.55,
        # 公式 332x240 実測: 頬紅は幅 15px x 高さ 8px、中心 x は目の中心と
        # 同じ、中心 y は顎から 12.5px 上。アトラス UV は u 1.0 = 117px /
        # v 1.0 = 67px なので dx=0.184 / y=0.187 / rx,ry はぼかしぶん 1.12 倍。
        blush_layout=dict(dx=0.184, y=0.187, rx=0.072, ry=0.066),
        # 公式の口は唇でも塗りでもなく、黒い弧 1 本（幅 20px = 頭蓋の 0.29）。
        mouth_color=_c("#241A16"), mouth_style="ink_smile", mouth_w=0.064,
        eye_style="ink", eye_tilt=2.0, star_eyes=True,
        hair="long_blunt", hair_accessory="bow_red",
        outfit="kimono_madonna",
        # 頭蓋 81x68px 基準。目の中心は顎から 30.8px(0.45 頭高)で、
        # 0.380 のままでは目鼻が顔の下 1/3 に寄って額だけ広く見えていた。
        # u の 1 単位は uv_box の都合でワールド 1.44*head_w、v は 1.015*head_h。
        face_layout=dict(eye_y=0.460, eye_dx=0.184, eye_rx=0.051, eye_ry=0.159),
        # 公式から色を抜いた実測値（袴 #9066A8 / リボン #E4475B /
        # ブーツ #AE7D25）。既定の #4C2A70 / #D8222F / #7A4A28 は
        # どれも 2 段暗く、並べると別人の配色に見えた。
        mat_colors={"hair": "#AA6D26", "eye_l": "#15110F", "eye_r": "#15110F",
                    "cloth_hakama_purple": "#9066A8",
                    "ribbon_red": "#E4475B",
                    "boots_brown": "#AE7D25", "metal": "#8A6218"},
        # リボンは公式で 65x32px = 頭幅の 0.66。1.55 では 1.12 頭幅あった。
        bow_scale=0.80,
        # 矢羽根 1 個の間隔は公式で胴幅の 1/5.4。11.0 では 1/15 で
        # 遠目にピンストライプだった。
        pattern_scale=5.0,
        accessories=("boots",),
        sign_items=("ハート柄の振袖", "頭頂の赤リボン", "編み上げブーツ"),
    ),
    # ----------------------------------------------------------------- inari
    "inari": dict(
        id="inari",
        jp="花之木 いなり",
        height=1.53, heads=6.0, build="slim",
        skin=_c("#F3C7B0"),
        hair_color=_c("#2A2432"),
        eye_color=_c("#8E5FC0"),
        lash_color=_c("#201826"),
        brow_color=_c("#2A2432"),
        blush_color=_c("#FF98A2"), blush_strength=0.46,
        mouth_color=_c("#C8434B"), mouth_style="smile", mouth_w=0.034,
        eye_style="round", eye_tilt=6.0,
        hair="twintail", hair_accessory="glasses_red",
        tail_ribbon=True,
        outfit="seifuku",
        face_layout=dict(eye_y=0.418, eye_dx=0.188, eye_rx=0.078, eye_ry=0.100),
        mat_colors={"hair": "#2A2432", "eye_l": "#8E5FC0", "eye_r": "#8E5FC0",
                    "cloth_ribbon_green": "#2E6FBF"},
        accessories=(),
        sign_items=("赤縁メガネ", "ツインテール", "結び目の紫リボン"),
    ),
    # ---------------------------------------------------------------- kaname
    "kaname": dict(
        id="kaname",
        jp="中川 かなめ",
        height=1.60, heads=6.0, build="slim",
        skin=_c("#F3C6AA"),
        hair_color=_c("#7A4A2A"),
        eye_color=_c("#C07A3A"),
        lash_color=_c("#38221A"),
        brow_color=_c("#6A3F22"),
        blush_color=_c("#FF9A94"), blush_strength=0.50,
        mouth_color=_c("#C8434B"), mouth_style="smile", mouth_w=0.038,
        eye_style="round", eye_tilt=3.0,
        hair="ponytail", hair_accessory="bandana",
        outfit="seifuku_apron",
        face_layout=dict(eye_y=0.420, eye_dx=0.189, eye_rx=0.079, eye_ry=0.099),
        mat_colors={"hair": "#7A4A2A", "eye_l": "#C07A3A", "eye_r": "#C07A3A",
                    "cloth_skirt_navy": "#7A3F46", "cloth_ribbon_green": "#D9A441"},
        accessories=(),
        sign_items=("エプロン", "ポニーテール", "三角巾"),
    ),
    # ------------------------------------------------------------------ sora
    "sora": dict(
        id="sora",
        jp="金町 そら",
        height=1.55, heads=6.0, build="slim",
        skin=_c("#F4CBB8"),
        hair_color=_c("#8FD3E8"),
        eye_color=_c("#4F9BD8"),
        lash_color=_c("#2E4658"),
        brow_color=_c("#79BBD2"),
        blush_color=_c("#FF9BA6"), blush_strength=0.48,
        mouth_color=_c("#C8434B"), mouth_style="smile", mouth_w=0.035,
        eye_style="round", eye_tilt=1.0,
        hair="short", hair_accessory=None,
        outfit="seifuku_hoodie",
        face_layout=dict(eye_y=0.419, eye_dx=0.189, eye_rx=0.080, eye_ry=0.102),
        mat_colors={"hair": "#8FD3E8", "eye_l": "#4F9BD8", "eye_r": "#4F9BD8",
                    "cloth_ribbon_green": "#00843D", "cloth_skirt_navy": "#3A4E6E"},
        accessories=("sneakers",),
        sign_items=("水色ショート", "パーカー", "スニーカー"),
    ),
    # ------------------------------------------------------------------ prof
    "prof": dict(
        id="prof",
        jp="教授",
        height=1.72, heads=6.4, build="sturdy",
        skin=_c("#E4BA9E"),
        hair_color=_c("#DCDCE0"),
        eye_color=_c("#7E97B8"),
        lash_color=_c("#3A3A44"),
        brow_color=_c("#C8C8CE"),
        blush_color=_c("#D89080"), blush_strength=0.16,
        mouth_color=_c("#8E4038"), mouth_style="line", mouth_w=0.028,
        eye_style="sharp", eye_tilt=2.0,
        brow_width=0.014, brow_arch=0.12,
        hair="short_old", hair_accessory="glasses_gray",
        outfit="labcoat",
        face_layout=dict(eye_y=0.421, eye_dx=0.190, eye_rx=0.076, eye_ry=0.094),
        mat_colors={"hair": "#DCDCE0", "eye_l": "#7E97B8", "eye_r": "#7E97B8",
                    "glasses": "#AEB4BE"},
        accessories=("necktie",),
        sign_items=("白衣", "銀縁眼鏡", "ネクタイ"),
    ),
}

ALL_IDS = tuple(CHARACTERS.keys())


def resolve(char_id: str) -> dict:
    if char_id not in CHARACTERS:
        raise KeyError(f"未知のキャラクター id: {char_id}（有効: {', '.join(ALL_IDS)}）")
    p = dict(CHARACTERS[char_id])
    head_h = p["height"] / p["heads"]
    # 素体は「床 → 頭蓋の天端」しか勘定しないので、そのまま組むと髪が上へ、
    # 履物が床下へはみ出して、宣言した身長・頭身どおりに仕上がらない。
    # はみ出しぶんを先に差し引いて素体を縮め、出来上がりが宣言値になるようにする。
    pad_hair, pad_sole = p.get("silhouette_pad", (0.0, 0.0))
    h = p["height"] - head_h * (pad_hair + pad_sole)
    # 以降のモジュール（body/cloth/hair/rig/outline/render）は p["height"] を
    # 長さの基準に使うので、ここで素体の高さに差し替える。宣言値は別名で残す。
    p["silhouette_h"] = p["height"]
    p["height"] = h
    build = dict(BUILDS[p["build"]])
    head_w = head_h * build["head_w"]

    # 頭幅基準 -> 身長基準へ変換（body.py は「身長 x 値」で使う）
    k = head_w / h
    for key in _HEAD_RELATIVE:
        v = build[key]
        build[key] = tuple(x * k for x in v) if isinstance(v, tuple) else v * k
    p["build_params"] = build

    chin_z = h - head_h
    levels = p.get("levels") or (BODY_LEVELS_CHIBI if p.get("chibi")
                                 else BODY_LEVELS)
    lv = {kk: vv * chin_z for kk, vv in levels.items()}
    lv["chin"] = chin_z
    lv["top"] = h
    p["z"] = lv
    p["head_h"] = head_h
    p["head_w"] = head_w
    p["head_d"] = head_h * build["head_d"]
    p["body_height"] = h
    return p
