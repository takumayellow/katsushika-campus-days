"""OSM (Overpass) の生データから葛飾キャンパスの幾何を抽出し、メートル座標の JSON に変換する。

入力: data/osm/raw_overpass.json  (Overpass API `out geom;` の出力)
出力: data/osm/campus.json

座標系:
  原点 = キャンパス敷地ポリゴンの重心
  x = 東 (m), z = 北 (m)   … Unity の左手系 (x 右, z 前) にそのまま載る
  Blender 側では (x, y=z_north, z=up) に読み替える

原点・投影・幾何のヘルパは tools/osm_common.py にある (寮への道を出す tools/osm_route.py と共通)。

使い方:
  python tools/osm_extract.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from osm_common import CAMPUS_WAY_ID, ROOT, origin_from  # noqa: E402
from osm_common import ccw as _ccw  # noqa: E402
from osm_common import centroid as _centroid  # noqa: E402
from osm_common import geom as _geom  # noqa: E402
from osm_common import make_projector as _make_projector  # noqa: E402
from osm_common import point_in_poly as _point_in_poly  # noqa: E402

RAW = ROOT / "data" / "osm" / "raw_overpass.json"
OUT = ROOT / "data" / "osm" / "campus.json"

# OSM 上の名称 → ゲーム内の正準 ID と、公式資料で裏取りした階数・高さ・用途。
# (階数は TUS LIFE / Wikipedia、高さは OSM 実測値優先。無ければ 階数×3.9m で補完)
BUILDING_CATALOG: dict[str, dict] = {
    "研究棟": {
        "id": "research1",
        "display": "第1研究棟",
        "levels": 11,
        "height": 49.5,
        "style": "lab_tower",
        "desc": "研究室・実験室・ゼミ室が集結する研究拠点。11 階建て、キャンパス最高高さ 44.95m。",
    },
    "講義棟": {
        "id": "lecture",
        "display": "講義棟",
        "levels": 7,
        "height": 30.0,
        "style": "lecture",
        "desc": "大小約 50 教室。最大 270 名の大教室、ラウンジ、保健管理センター。",
    },
    "管理棟": {
        "id": "research2",
        "display": "第2研究棟",
        "levels": 6,
        "height": 25.0,
        "style": "office",
        "desc": "旧・管理棟。1・2 階は 1,400 席の学生食堂、3 階に事務各課。情報工学科の研究室が入る。",
    },
    "体育館": {
        "id": "gym",
        "display": "体育館",
        "levels": 6,
        "height": 21.8,
        "style": "gym",
        "desc": "メインアリーナ・サブアリーナ・トレーニングルーム。2〜6 階はサークル部室。",
    },
    "第一実験棟": {
        "id": "lab1",
        "display": "第1実験棟",
        "levels": 4,
        "height": 17.0,
        "style": "lab_low",
        "desc": "各学科の特殊実験室と研究センター。",
    },
    "第二実験棟": {
        "id": "lab2",
        "display": "第2実験棟",
        "levels": 2,
        "height": 9.0,
        "style": "lab_low",
        "desc": "実験施設。",
    },
    "図書館": {
        "id": "library",
        "display": "図書館",
        "levels": 5,
        "height": 22.0,
        "style": "library",
        "desc": "キャンパスのシンボル。蔵書約 14 万冊、黙考書院、600 席の大ホール（3・4 階）、未来わくわく館。",
    },
}

# 名無し建物の同定 (OSM id → 正準情報)。高さ 47m の新棟は 2025 年竣工の共創棟。
UNNAMED_BUILDINGS: dict[int, dict] = {
    1552199070: {
        "id": "kyoso",
        "display": "共創棟",
        "levels": 11,
        "height": 47.0,
        "style": "kyoso",
        "desc": "2025 年竣工。薬学部の拠点。1 階にラーニングスクエア、スターバックス、ファミリーマート。",
    },
    1552188506: {
        "id": "greenhouse",
        "display": "温室",
        "levels": 1,
        "height": 3.4,
        "style": "greenhouse",
        "desc": "ガラス張りの温室。",
    },
}


def _load() -> list[dict]:
    with RAW.open(encoding="utf-8") as f:
        return json.load(f)["elements"]


def extract() -> dict:
    elements = _load()
    by_key = {(e["type"], e["id"]): e for e in elements}
    campus = by_key[("way", CAMPUS_WAY_ID)]
    campus_ll = _geom(campus)
    lat0, lon0 = origin_from(elements)
    project = _make_projector(lat0, lon0)
    campus_xy = _ccw([project(*p) for p in campus_ll])

    # 敷地 + 周辺 (公園・通り) を含む関心領域: 敷地ポリゴンを 120m 膨らませた矩形
    xs = [p[0] for p in campus_xy]
    ys = [p[1] for p in campus_xy]
    roi = (min(xs) - 120, min(ys) - 120, max(xs) + 120, max(ys) + 120)

    def in_roi(x: float, y: float) -> bool:
        return roi[0] <= x <= roi[2] and roi[1] <= y <= roi[3]

    buildings, paths, areas, points = [], [], [], []

    for e in elements:
        tags = e.get("tags", {})
        if e["type"] == "node":
            if "lat" not in e:
                continue
            x, y = project(e["lon"], e["lat"])
            if not in_roi(x, y):
                continue
            kind = None
            if tags.get("natural") == "tree":
                kind = "tree"
            elif tags.get("amenity") in ("bench", "cafe", "vending_machine", "bicycle_parking", "toilets", "post_box"):
                kind = tags["amenity"]
            elif tags.get("highway") == "street_lamp":
                kind = "lamp"
            elif tags.get("man_made") == "ceremonial_gate" or tags.get("amenity") == "place_of_worship":
                kind = "shrine"
            elif tags.get("highway") == "bus_stop":
                kind = "bus_stop"
            if kind:
                points.append({"kind": kind, "name": tags.get("name"), "x": x, "z": y})
            continue

        if e["type"] != "way" or "geometry" not in e:
            continue
        ll = _geom(e)
        xy = [project(*p) for p in ll]
        cx, cy = _centroid(xy)
        if not in_roi(cx, cy):
            continue
        on_campus = _point_in_poly(cx, cy, campus_xy)

        if "building" in tags:
            name = tags.get("name")
            meta = BUILDING_CATALOG.get(name or "") or UNNAMED_BUILDINGS.get(e["id"])
            if meta is None:
                if not on_campus and tags.get("building") != "dormitory":
                    # 周辺の民家・商業ビルは高さだけ持つ簡易ボリュームにする
                    lv = tags.get("building:levels")
                    h = tags.get("height")
                    meta = {
                        "id": f"bg_{e['id']}",
                        "display": name or "",
                        "levels": int(float(lv)) if lv else None,
                        "height": float(h) if h else (float(lv) * 3.2 if lv else 8.0),
                        "style": "background",
                        "desc": "",
                    }
                else:
                    meta = {
                        "id": f"dorm_{e['id']}" if tags.get("building") == "dormitory" else f"misc_{e['id']}",
                        "display": name or "",
                        "levels": int(float(tags.get("building:levels", 0))) or None,
                        "height": float(tags["height"]) if "height" in tags else 10.0,
                        "style": "dormitory" if tags.get("building") == "dormitory" else "misc",
                        "desc": "東京理科大学 葛飾国際学生寮。6 階建て。" if tags.get("building") == "dormitory" else "",
                    }
            buildings.append(
                {
                    **meta,
                    "osm_id": e["id"],
                    "osm_name": name,
                    "on_campus": on_campus,
                    "footprint": _ccw(xy),
                }
            )
        elif "highway" in tags:
            hw = tags["highway"]
            width = {
                "tertiary": 9.0,
                "secondary": 10.0,
                "residential": 6.0,
                "service": 4.0,
                "footway": 2.5,
                "path": 2.0,
                "steps": 2.0,
                "cycleway": 2.5,
                "pedestrian": 6.0,
            }.get(hw)
            if width is None:
                continue
            paths.append(
                {
                    "kind": hw,
                    "name": tags.get("name"),
                    "width": float(tags.get("width", width)),
                    "on_campus": on_campus,
                    "points": xy,
                }
            )
        elif tags.get("leisure") in ("park", "pitch", "playground", "garden") or tags.get("landuse") in ("grass", "recreation_ground") or tags.get("natural") in ("water", "wood", "scrub") or tags.get("amenity") == "parking" or tags.get("amenity") == "university":
            kind = tags.get("leisure") or tags.get("landuse") or tags.get("natural") or tags.get("amenity")
            areas.append({"kind": kind, "name": tags.get("name"), "on_campus": on_campus, "polygon": _ccw(xy)})

    # 建物を「キャンパス内 → 寮 → 背景」の順に並べ替えて読みやすく
    order = {"lab_tower": 0, "lecture": 1, "office": 2, "kyoso": 3, "library": 4, "gym": 5, "lab_low": 6, "greenhouse": 7, "dormitory": 8, "misc": 9, "background": 10}
    buildings.sort(key=lambda b: (order.get(b["style"], 99), b["id"]))

    return {
        "meta": {
            "source": "OpenStreetMap (ODbL) via Overpass API, 2026-09-20",
            "origin": {"lat": lat0, "lon": lon0},
            "axes": "x=east(m), z=north(m); Unity: (x, y=up, z); Blender: (x, y=z_north, z=up)",
            "campus_osm_way": CAMPUS_WAY_ID,
        },
        "campus_boundary": campus_xy,
        "buildings": buildings,
        "paths": paths,
        "areas": areas,
        "points": points,
    }


def main() -> None:
    data = extract()
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    b = data["buildings"]
    print(f"origin lat={data['meta']['origin']['lat']:.6f} lon={data['meta']['origin']['lon']:.6f}")
    print(f"buildings={len(b)} (campus={sum(x['on_campus'] for x in b)}), paths={len(data['paths'])}, areas={len(data['areas'])}, points={len(data['points'])}")
    for x in b:
        if x["style"] not in ("background",):
            fp = x["footprint"]
            xs = [p[0] for p in fp]
            ys = [p[1] for p in fp]
            print(f"  {x['id']:<12} {x['display']:<8} lv={x['levels']} h={x['height']:<5} bbox x[{min(xs):7.1f},{max(xs):7.1f}] z[{min(ys):7.1f},{max(ys):7.1f}] n={len(fp)}")
    from collections import Counter

    print("points:", Counter(p["kind"] for p in data["points"]))
    print("areas:", Counter(a["kind"] for a in data["areas"]))
    print("paths:", Counter(p["kind"] for p in data["paths"]))


if __name__ == "__main__":
    main()
