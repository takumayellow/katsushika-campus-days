"""blender/ の純 Python 部分を Blender なしで試す (#68)。

bpy / bmesh / mathutils をスタブに差し替えてから kcd_lib / kcd_route を import する。
スタブの中身は stubs.py。建物内部（kcd_interior, build_interiors.py など）は dev/interior の担当なので
ここでは扱わない。
"""

import json
import os
import sys

import pytest

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.dirname(HERE)
REPO = os.path.dirname(BLENDER)

for _p in (BLENDER, HERE):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import stubs  # noqa: E402

stubs.install()

CAMPUS_JSON = os.path.join(REPO, "data", "osm", "campus.json")


@pytest.fixture(scope="session")
def campus_data():
    """build_campus.load_data と同じ形（座標はタプル）にした campus.json。"""
    import build_campus
    return build_campus.load_data(CAMPUS_JSON)


@pytest.fixture(scope="session")
def campus_frame(campus_data):
    import build_campus
    return build_campus.make_frame(campus_data)


@pytest.fixture(scope="session")
def campus_raw():
    with open(CAMPUS_JSON, encoding="utf-8") as fp:
        return json.load(fp)
