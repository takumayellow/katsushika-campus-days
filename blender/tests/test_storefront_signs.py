"""建物の外に店の看板を出さない (#181)。

共創棟 1F のスターバックスとファミリーマートは、外から見て店と分かる必要がない。建物の外の看板は
入口の立て看板（建物名）だけにし、店名もブランドの色の板も出さない。店名は会話文とクエスト文にだけ出る。
"""

import pytest

from kcd_lib import buildings, geom, mats
from kcd_lib.mesh import MeshBuilder

# 店のブランドを指すマテリアル名の断片。キャンパスの外の背景のローソンも含める。
BRANDS = ("starbucks", "familymart", "lawson")


def _branded(names):
    return sorted(n for n in names if any(b in n.lower() for b in BRANDS))


def test_palette_has_no_brand_colors():
    assert _branded(mats.PALETTE) == []


@pytest.fixture(scope="module")
def building_materials(campus_data, campus_frame):
    """build_campus と同じ順で構内の建物を組み、建物ごとに使ったマテリアル名を返す。"""
    on_campus = []
    for b in campus_data["buildings"]:
        loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
        if len(loop) >= 3 and (b.get("style") or "background") in buildings.BUILDERS and b.get("on_campus"):
            on_campus.append((b, loop))

    ctx = {
        "entrance": [],
        "sign": [],
        "footprints": [(b["id"], loop, float(b.get("height") or 10.0)) for b, loop in on_campus],
        "bg_window_spacing": 2.4,
    }
    ctx["uvbb"] = {fid: campus_frame.uv_bbox(fp) for fid, fp, _h in ctx["footprints"]}

    used = {}
    for b, _loop in on_campus:
        mb = MeshBuilder("bld_%s" % b["id"])
        buildings.BUILDERS[b["style"]](mb, b, campus_frame, ctx)
        used[b["id"]] = list(mb.mat_names)
    return used


def test_kyoso_is_built(building_materials):
    assert building_materials.get("kyoso"), "共創棟を組めていない"


def test_no_building_wears_a_store_sign(building_materials):
    branded = {bid: _branded(names) for bid, names in building_materials.items() if _branded(names)}
    assert branded == {}
