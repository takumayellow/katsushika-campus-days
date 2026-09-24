"""tools/ の純 Python 部分のテスト (#68)。

tools/*.py と tools/audio/*.py はスクリプトとして直接動かす前提で、パッケージになっていない。
ここで両方のディレクトリを sys.path に入れてから import する。
"""

import json
import os
import sys

import pytest

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
REPO = os.path.dirname(TOOLS)

for _p in (os.path.join(TOOLS, "audio"), TOOLS):
    if _p not in sys.path:
        sys.path.insert(0, _p)

OSM_DIR = os.path.join(REPO, "data", "osm")


def _read_json(name):
    with open(os.path.join(OSM_DIR, name), encoding="utf-8") as fp:
        return json.load(fp)


@pytest.fixture(scope="session")
def campus_json():
    return _read_json("campus.json")


@pytest.fixture(scope="session")
def route_json():
    return _read_json("route.json")
