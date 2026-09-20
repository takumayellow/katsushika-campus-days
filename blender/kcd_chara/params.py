"""キャラクター定義とプロポーション。

DESIGN.md §2 の表がここに落ちている。新しい NPC を足すときは `CHARACTERS` に
1 エントリ増やすだけでよい（髪型・衣装は既存のジェネレータを名前で選ぶ）。

プロポーションの単位について
---------------------------
`BUILDS` の半径系の値は **頭幅 (head_w) に対する比** で書く。`resolve()` が
`head_w / height` を掛けて従来どおりの「身長比」に直すので、body.py 側は
変更不要。頭幅基準にすることで「肩幅 = 頭幅 x 1.35」「腕の直径 <= 頭幅 x 0.22」
「首の直径 = 頭幅 x 0.35」といった作画の約束をそのまま数値で表現できる。
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
        head_w=0.862, head_d=0.880,
    ),
    # ちび（男子・3 頭身）
    "chibi_m": dict(
        shoulder_half=0.478, shoulder_ry=0.238,
        bust_rx=0.362, bust_ry=0.266, bust_bulge=0.000,
        underbust_rx=0.342, underbust_ry=0.252,
        waist_rx=0.330, waist_ry=0.246,
        hip_rx=0.352, hip_ry=0.262,
        crotch_rx=0.338, crotch_ry=0.252,
        neck_r=0.165,
        arm_r=(0.110, 0.100, 0.082), hand=0.132,
        leg_r=(0.222, 0.182, 0.140), foot=(0.196, 0.500, 0.150),
        arm_len=(0.116, 0.100, 0.064),
        head_w=0.884, head_d=0.900,
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
        height=1.15, heads=2.92, build="chibi_m", chibi=True,
        skin=_c("#EBBE9C"),
        hair_color=_c("#1A1A22"),
        eye_color=_c("#4A3324"),
        lash_color=_c("#171720"),
        brow_color=_c("#14141C"),
        blush_color=_c("#E98A78"), blush_strength=0.22,
        mouth_color=_c("#8E4038"), mouth_style="line", mouth_w=0.026,
        eye_style="sharp", eye_tilt=9.0,
        brow_width=0.020, brow_arch=0.18,
        hair="slickback", hair_accessory=None,
        outfit="kimono_botchan",
        face_layout=dict(eye_y=0.374, eye_dx=0.190, eye_rx=0.082, eye_ry=0.111),
        mat_colors={"hair": "#1A1A22", "eye_l": "#4A3324", "eye_r": "#4A3324"},
        pattern_scale=16.0,
        accessories=("furoshiki",),
        sign_items=("十字絣", "高下駄", "赤い風呂敷"),
    ),
    # --------------------------------------------------------------- madonna
    "madonna": dict(
        id="madonna",
        jp="マドンナちゃん",
        height=1.14, heads=2.95, build="chibi_f", chibi=True,
        skin=_c("#F5CDBA"),
        hair_color=_c("#8B5A2B"),
        eye_color=_c("#7E4620"),
        lash_color=_c("#3A2018"),
        brow_color=_c("#6E4520"),
        blush_color=_c("#FF8F9C"), blush_strength=0.58,
        mouth_color=_c("#C4394A"), mouth_style="smile", mouth_w=0.036,
        eye_style="round", eye_tilt=2.0, star_eyes=True,
        hair="long_blunt", hair_accessory="bow_red",
        outfit="kimono_madonna",
        face_layout=dict(eye_y=0.380, eye_dx=0.192, eye_rx=0.090, eye_ry=0.126),
        mat_colors={"hair": "#8B5A2B", "eye_l": "#8A4B22", "eye_r": "#8A4B22"},
        bow_scale=1.55,
        pattern_scale=11.0,
        accessories=("boots",),
        sign_items=("矢絣の振袖", "頭頂の大きな赤リボン", "編み上げブーツ"),
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
    h = p["height"]
    head_h = h / p["heads"]
    build = dict(BUILDS[p["build"]])
    head_w = head_h * build["head_w"]

    # 頭幅基準 -> 身長基準へ変換（body.py は「身長 x 値」で使う）
    k = head_w / h
    for key in _HEAD_RELATIVE:
        v = build[key]
        build[key] = tuple(x * k for x in v) if isinstance(v, tuple) else v * k
    p["build_params"] = build

    chin_z = h - head_h
    levels = BODY_LEVELS_CHIBI if p.get("chibi") else BODY_LEVELS
    lv = {kk: vv * chin_z for kk, vv in levels.items()}
    lv["chin"] = chin_z
    lv["top"] = h
    p["z"] = lv
    p["head_h"] = head_h
    p["head_w"] = head_w
    p["head_d"] = head_h * build["head_d"]
    return p
