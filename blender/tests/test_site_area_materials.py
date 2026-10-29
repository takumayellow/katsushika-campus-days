"""運動場と遊び場の地面のマテリアル（#59）。"""

from kcd_lib import geom, site


def _sq(x0, size):
    return [(x0, 0.0), (x0 + size, 0.0), (x0 + size, size), (x0, size)]


def test_largest_pitch_is_grass_and_others_stay_soil():
    areas = [
        {"kind": "pitch", "polygon": _sq(0, 10)},
        {"kind": "pitch", "polygon": _sq(20, 80)},
        {"kind": "playground", "polygon": _sq(200, 20)},
        {"kind": "parking", "polygon": _sq(300, 20)},
        {"kind": "university", "polygon": _sq(400, 20)},
    ]
    assert site.area_materials(areas) == ["soil", "grass", "sand", "asphalt", None]


def test_no_pitch():
    assert site.area_materials([{"kind": "park", "polygon": _sq(0, 5)}]) == ["grass"]


def test_campus_json_football_pitch_is_grass(campus_data):
    """campus.json では約 7400 m2 のサッカー場が芝、2 面のテニスコートが土、遊び場は砂。"""
    areas = campus_data["areas"]
    mats = site.area_materials(areas)
    pitch = {round(abs(geom.poly_area(a["polygon"]))): m
             for a, m in zip(areas, mats) if a["kind"] == "pitch"}
    assert len(pitch) == 3
    largest = max(pitch)
    assert largest > 7000
    assert pitch[largest] == "grass"
    assert all(m == "soil" for k, m in pitch.items() if k != largest)
    assert all(m == "sand" for a, m in zip(areas, mats) if a["kind"] == "playground")
