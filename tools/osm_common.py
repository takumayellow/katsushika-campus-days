"""OSM (Overpass) の生データをゲーム座標に直す共通処理。

`tools/osm_extract.py` (キャンパス本体 → `data/osm/campus.json`) と
`tools/osm_route.py` (寮への道 → `data/osm/route.json`) が **同じ原点・同じ投影**を使うためのモジュール。
原点の決め方 (`origin_from`) はここ 1 か所にしかない。2 か所に書くと必ずずれるため。

座標系:
  原点 = キャンパス敷地ポリゴン (OSM way 175463006) の重心
         lat0 = 35.771909746153845, lon0 = 139.862967261538472
  x = 東 (m), z = 北 (m)   … Unity の左手系 (x 右, z 前) にそのまま載る
  投影 = 等距円筒近似。原点の緯度で 1 度 = 緯度 110954.786 m / 経度 90422.693 m。
         キャンパス〜寮 (半径 600 m) の範囲なら誤差 < 1 cm。

自己検査:
  python tools/osm_common.py --selfcheck [--raw data/osm/raw_route.json]

  - 原点と m/deg が上の既知値と一致するか
  - 生データがあれば: `origin_from()` の原点が `data/osm/campus.json` の `meta.origin` と一致し、
    生データから投影し直した頂点が committed な campus.json の座標と一致するか (頂点ごとの最大誤差 m)
  - 生データが無ければ: campus.json の頂点 (>= 20) の逆投影 → 再投影の往復誤差
"""
from __future__ import annotations

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

CAMPUS_WAY_ID = 175463006  # 東京理科大学 葛飾キャンパス (amenity=university)

# 自己検査の基準値 (campus.json を作ったときの実測)
EXPECTED_ORIGIN_LAT = 35.771909746153845
EXPECTED_ORIGIN_LON = 139.862967261538472
EXPECTED_M_PER_DEG_LAT = 110954.786
EXPECTED_M_PER_DEG_LON = 90422.693


def load_elements(path: Path) -> list[dict]:
    """Overpass API の `out geom;` 出力 (JSON) から elements を読む。"""
    with Path(path).open(encoding="utf-8") as f:
        return json.load(f)["elements"]


def centroid(points: list[tuple[float, float]]) -> tuple[float, float]:
    return (sum(p[0] for p in points) / len(points), sum(p[1] for p in points) / len(points))


def make_projector(lat0: float, lon0: float):
    """等距円筒近似。キャンパス規模 (数百 m) なら誤差 < 1cm。"""
    m_per_deg_lat = 111_132.954 - 559.822 * math.cos(2 * math.radians(lat0)) + 1.175 * math.cos(4 * math.radians(lat0))
    m_per_deg_lon = 111_412.84 * math.cos(math.radians(lat0)) - 93.5 * math.cos(3 * math.radians(lat0))

    def project(lon: float, lat: float) -> tuple[float, float]:
        return (round((lon - lon0) * m_per_deg_lon, 3), round((lat - lat0) * m_per_deg_lat, 3))

    return project


def point_in_poly(x: float, y: float, poly: list[tuple[float, float]]) -> bool:
    inside = False
    j = len(poly) - 1
    for i, (xi, yi) in enumerate(poly):
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def geom(e: dict) -> list[tuple[float, float]]:
    return [(p["lon"], p["lat"]) for p in e.get("geometry", [])]


def ring_area(pts: list[tuple[float, float]]) -> float:
    """符号付き面積 (shoelace)。正なら反時計回り。"""
    a = 0.0
    for i in range(len(pts)):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % len(pts)]
        a += x1 * y2 - x2 * y1
    return a / 2


def ccw(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    if pts and pts[0] == pts[-1]:
        pts = pts[:-1]
    return pts if ring_area(pts) > 0 else list(reversed(pts))


def origin_from(elements: list[dict]) -> tuple[float, float]:
    """原点 (lat0, lon0) = キャンパス敷地ポリゴンの重心。**この決め方はここにしか書かない。**

    閉じたポリゴンの最後の点は最初の点と同じなので、重心を取る前に落とす。
    """
    by_key = {(e["type"], e["id"]): e for e in elements}
    campus_ll = geom(by_key[("way", CAMPUS_WAY_ID)])
    lon0, lat0 = centroid(campus_ll[:-1])
    return lat0, lon0


# --------------------------------------------------------------------------
# 自己検査
# --------------------------------------------------------------------------

def scales_of(project, lat0: float, lon0: float) -> tuple[float, float]:
    """projector そのものから 1 度あたりのメートルを取り出す (換算式を 2 度書かないため)。

    原点から 1 度ずらした点の座標がそのまま m/deg (3 桁に丸めた値) になる。
    """
    m_lon, _ = project(lon0 + 1.0, lat0)
    _, m_lat = project(lon0, lat0 + 1.0)
    return m_lat, m_lon


def _default_raw() -> Path | None:
    for p in (ROOT / "data" / "osm" / "raw_route.json", ROOT / "data" / "osm" / "raw_overpass.json"):
        if p.exists():
            return p
    return None


def selfcheck(raw: Path | None = None, campus_json: Path | None = None) -> int:
    """投影が campus.json と一致することを確かめる。戻り値 0 = OK。"""
    campus_json = Path(campus_json) if campus_json else ROOT / "data" / "osm" / "campus.json"
    cj = json.loads(campus_json.read_text(encoding="utf-8"))
    lat0 = cj["meta"]["origin"]["lat"]
    lon0 = cj["meta"]["origin"]["lon"]
    ok = True

    # 1) 原点
    d_lat = abs(lat0 - EXPECTED_ORIGIN_LAT)
    d_lon = abs(lon0 - EXPECTED_ORIGIN_LON)
    print(f"origin  campus.json lat={lat0!r} lon={lon0!r}")
    print(f"        expected    lat={EXPECTED_ORIGIN_LAT!r} lon={EXPECTED_ORIGIN_LON!r}  (d={d_lat:.3e}, {d_lon:.3e} deg)")
    ok &= d_lat < 1e-12 and d_lon < 1e-12

    # 2) 1 度あたりのメートル
    project = make_projector(lat0, lon0)
    m_lat, m_lon = scales_of(project, lat0, lon0)
    print(f"scale   lat {m_lat:.3f} m/deg (expected {EXPECTED_M_PER_DEG_LAT:.3f}), "
          f"lon {m_lon:.3f} m/deg (expected {EXPECTED_M_PER_DEG_LON:.3f})")
    ok &= abs(m_lat - EXPECTED_M_PER_DEG_LAT) < 1e-3 and abs(m_lon - EXPECTED_M_PER_DEG_LON) < 1e-3

    # 3) 頂点
    raw = Path(raw) if raw else _default_raw()
    if raw and Path(raw).exists():
        worst, n, checked = _check_against_raw(Path(raw), cj, project)
        print(f"vertex  raw={raw}  ways={checked}  vertices={n}  max error = {worst:.6f} m")
        ok &= n >= 20 and worst < 1e-3
        if n < 20:
            print("  !! 照合できた頂点が 20 未満")
    else:
        worst, n = _check_roundtrip(cj, project, m_lat, m_lon)
        print(f"vertex  (生データ無し) campus.json の頂点を逆投影 → 再投影: n={n}  max error = {worst:.6f} m")
        print("        生データを渡すと本物の照合ができる: --raw data/osm/raw_route.json")
        ok &= n >= 20 and worst < 1e-3

    print("SELFCHECK", "OK" if ok else "FAILED")
    return 0 if ok else 1


def _check_against_raw(raw: Path, cj: dict, project) -> tuple[float, int, int]:
    """生データから投影し直した頂点と committed な campus.json の座標を突き合わせる。"""
    elements = load_elements(raw)
    lat0, lon0 = origin_from(elements)
    o_lat = cj["meta"]["origin"]["lat"]
    o_lon = cj["meta"]["origin"]["lon"]
    if abs(lat0 - o_lat) > 1e-12 or abs(lon0 - o_lon) > 1e-12:
        print(f"  !! origin_from() が campus.json と違う: {lat0!r},{lon0!r} != {o_lat!r},{o_lon!r}")
        return (float("inf"), 0, 0)
    ways = {e["id"]: e for e in elements if e["type"] == "way" and "geometry" in e}

    want: list[tuple[int, list]] = [(CAMPUS_WAY_ID, cj["campus_boundary"])]
    for b in cj["buildings"]:
        want.append((b["osm_id"], b["footprint"]))

    worst, n, checked = 0.0, 0, 0
    for osm_id, committed in want:
        e = ways.get(osm_id)
        if e is None:
            continue
        got = ccw([project(*p) for p in geom(e)])
        if len(got) != len(committed):
            print(f"  !! way {osm_id}: 頂点数が違う {len(got)} != {len(committed)}")
            worst = float("inf")
            continue
        checked += 1
        for (gx, gz), (cx, cz) in zip(got, committed):
            worst = max(worst, abs(gx - cx), abs(gz - cz))
            n += 1
    return worst, n, checked


def _check_roundtrip(cj: dict, project, m_lat: float, m_lon: float) -> tuple[float, int]:
    """生データが無い環境用: campus.json の座標 → 緯度経度 → 座標 の往復誤差。"""
    lat0 = cj["meta"]["origin"]["lat"]
    lon0 = cj["meta"]["origin"]["lon"]
    pts = [tuple(p) for p in cj["campus_boundary"]]
    for b in cj["buildings"][:40]:
        pts.extend(tuple(p) for p in b["footprint"])
    worst, n = 0.0, 0
    for x, z in pts:
        lat = lat0 + z / m_lat
        lon = lon0 + x / m_lon
        gx, gz = project(lon, lat)
        worst = max(worst, abs(gx - x), abs(gz - z))
        n += 1
    return worst, n


def main() -> None:
    import argparse

    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--selfcheck", action="store_true", help="投影を campus.json と照合する")
    ap.add_argument("--raw", type=Path, default=None, help="Overpass の生データ (省略時 data/osm/raw_route.json → raw_overpass.json)")
    ap.add_argument("--campus-json", type=Path, default=None, help="照合先 (省略時 data/osm/campus.json)")
    args = ap.parse_args()
    if not args.selfcheck:
        ap.error("このモジュールは共通ライブラリです。単体で動かすなら --selfcheck")
    raise SystemExit(selfcheck(args.raw, args.campus_json))


if __name__ == "__main__":
    main()
