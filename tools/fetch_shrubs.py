"""低木と草むらのメッシュを Kenney の Nature Kit (CC0 1.0) から取り、変換済みの JSON にする (#78)。

`data/shrubs/kenney_shrubs.json` を作る。ゲームに入れるのは、この JSON から Blender（blender/kcd_lib/shrubs.py）が
作り直した低木と草むらだけで、Kenney の OBJ・材質・zip はリポジトリに入れない。

- 取るのは低木 6 点（plant_bush*）と草むら 4 点（grass*）の頂点と三角形だけ。Kenney の材質（単色）は読まない。
  色は kcd_lib/shrubs.py が CampusColors の色から付ける。
- zip の中身は展開せず、OBJ の `v` と `f` の行だけを数として読む。ダウンロードしたものの中のスクリプトは実行しない。
- 軸は Kenney のまま（Y が上）。Blender の Z 上への向きと大きさは kcd_lib/shrubs.py が決める。

使い方:

    python tools/fetch_shrubs.py                  # 取得して JSON を書く
    python tools/fetch_shrubs.py --check          # zip から作り直した結果が、今の JSON と一致するか確かめる
    python tools/fetch_shrubs.py --zip PATH       # 落とさず、手元の zip を使う（SHA-256 は確かめる）

zip は data/shrubs/.cache/ に残す（gitignore 済み）。2 回目からは落とし直さない。キャッシュの zip の SHA-256 が
ZIP_SHA256 と違うとき（ZIP_SHA256 を差し替えた後など）は、落とし直して置き換える。
落としたものは <名前>.part に書き、SHA-256 が合ってから正式名に置く（壊れたものを正式名で残さない）。
JSON も <名前>.part に書いてから置き換える（書きかけの JSON を正式名で残さない）。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "data" / "shrubs" / "kenney_shrubs.json"
CACHE = ROOT / "data" / "shrubs" / ".cache"
CACHE_ZIP = CACHE / "kenney_nature-kit.zip"

SOURCE = {
    "name": "Nature Kit",
    "version": "2.1",
    "author": "Kenney (www.kenney.nl)",
    "page": "https://kenney.nl/assets/nature-kit",
    "license": "CC0 1.0",
    "license_url": "https://creativecommons.org/publicdomain/zero/1.0/",
}
ZIP_URL = "https://kenney.nl/media/pages/assets/nature-kit/37ac38a37b-1677698939/kenney_nature-kit.zip"
ZIP_SHA256 = "fa7974a0d342bfe63c38664ba9f8ec1a4aab8ea25f099bdc56870e33588c4d9d"
OBJ_DIR = "Models/OBJ format/"

BUSHES = (
    "plant_bush",
    "plant_bushDetailed",
    "plant_bushLarge",
    "plant_bushLargeTriangle",
    "plant_bushSmall",
    "plant_bushTriangle",
)
GRASSES = ("grass", "grass_large", "grass_leafs", "grass_leafsLarge")

# 1 点の OBJ はどれも数 KB。想定より大きいものは別物として読まない
MAX_OBJ_BYTES = 200_000
MAX_VERTICES = 1000
MAX_ZIP_BYTES = 40_000_000
VERTEX_DECIMALS = 3


def parse_obj(text: str) -> tuple[list[list[float]], list[list[int]]]:
    """OBJ の `v` と `f` の行だけを読み、頂点と三角形（0 始まり）を返す。四角形以上は扇に割る。"""
    vertices: list[list[float]] = []
    faces: list[list[int]] = []
    for number, line in enumerate(text.splitlines(), 1):
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            if len(fields) < 4:
                raise ValueError(f"{number} 行目: v の座標が 3 つ無い")
            coords = [float(value) for value in fields[1:4]]
            if not all(math.isfinite(value) for value in coords):
                raise ValueError(f"{number} 行目: v の座標が有限の数でない")
            vertices.append([round(value, VERTEX_DECIMALS) for value in coords])
        elif fields[0] == "f":
            if len(fields) < 4:
                raise ValueError(f"{number} 行目: f の頂点が 3 つ無い")
            corners = []
            for token in fields[1:]:
                index = int(token.split("/")[0])
                # OBJ の負の番号は「そこまでに書いた頂点の末尾から数える」
                corners.append(index - 1 if index > 0 else len(vertices) + index)
            faces.append(corners)

    triangles: list[list[int]] = []
    for corners in faces:
        for k in range(1, len(corners) - 1):
            triangles.append([corners[0], corners[k], corners[k + 1]])
    for triangle in triangles:
        for index in triangle:
            if not 0 <= index < len(vertices):
                raise ValueError(f"頂点の番号が範囲外: {index}（頂点 {len(vertices)} 個）")
    if not vertices or not triangles:
        raise ValueError("頂点か三角形が無い")
    return vertices, triangles


def read_models(archive: zipfile.ZipFile) -> dict[str, dict]:
    """zip から低木と草むらの OBJ を読み、モデル名 → {group, vertices, triangles} にする。展開はしない。"""
    models: dict[str, dict] = {}
    for group, names in (("bush", BUSHES), ("grass", GRASSES)):
        for name in names:
            info = archive.getinfo(f"{OBJ_DIR}{name}.obj")
            if info.file_size > MAX_OBJ_BYTES:
                raise SystemExit(f"{name}.obj が {info.file_size} B ある（上限 {MAX_OBJ_BYTES} B）")
            vertices, triangles = parse_obj(archive.read(info).decode("utf-8"))
            if len(vertices) > MAX_VERTICES:
                raise SystemExit(f"{name}.obj の頂点が {len(vertices)} 個ある（上限 {MAX_VERTICES}）")
            models[name] = {"group": group, "vertices": vertices, "triangles": triangles}
    return models


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fp:
        for chunk in iter(lambda: fp.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url: str, destination: Path) -> Path:
    """url を destination と同じ場所の一時ファイル（<名前>.part）に落として、そのパスを返す。正式名には書かない。"""
    destination.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=120) as response:
        data = response.read(MAX_ZIP_BYTES + 1)
    if len(data) > MAX_ZIP_BYTES:
        raise SystemExit(f"{url} が {MAX_ZIP_BYTES} B を超える")
    partial = destination.with_name(destination.name + ".part")
    partial.write_bytes(data)
    return partial


def fetch_zip(explicit: Path | None) -> Path:
    """SHA-256 の合った zip のパスを返す。

    explicit（--zip）は落とさずにそのまま使い、無いか SHA-256 が違えば止まる（手元の zip は消さない）。
    explicit が無ければ CACHE_ZIP を使い、無いか SHA-256 が違えば落とし直して置き換える。
    """
    if explicit is not None:
        if not explicit.exists():
            raise SystemExit(f"{explicit} が無い")
        actual = sha256_of(explicit)
        if actual != ZIP_SHA256:
            raise SystemExit(f"{explicit} の SHA-256 が違う: {actual}（期待 {ZIP_SHA256}）")
        return explicit
    path = CACHE_ZIP
    if path.exists() and sha256_of(path) == ZIP_SHA256:
        return path
    print(f"取得: {ZIP_URL}")
    partial = download(ZIP_URL, path)
    downloaded = sha256_of(partial)
    if downloaded != ZIP_SHA256:
        # キャッシュには照合の済んだ zip だけを置く
        partial.unlink()
        raise SystemExit(f"{ZIP_URL} の SHA-256 が違う: {downloaded}（期待 {ZIP_SHA256}）")
    os.replace(partial, path)
    return path


def _rows(items: list[list], width: int) -> str:
    """JSON の配列を width 個ずつ 1 行に並べる（頂点の数百行を縦に並べない）。"""
    chunks = []
    for start in range(0, len(items), width):
        row = items[start:start + width]
        chunks.append(", ".join(json.dumps(item, separators=(",", ":"), allow_nan=False) for item in row))
    return ",\n      ".join(chunks)


def render(models: dict[str, dict]) -> str:
    """JSON 文字列を組む。モデルは名前順、頂点と三角形は 6 個ずつ 1 行。"""
    lines = ["{"]
    lines.append(f'  "source": {json.dumps(SOURCE, ensure_ascii=False)},')
    lines.append(f'  "zip": {json.dumps({"url": ZIP_URL, "sha256": ZIP_SHA256})},')
    lines.append('  "axes": "Y_up",')
    lines.append('  "models": {')
    entries = []
    for name in sorted(models):
        model = models[name]
        entries.append(
            f'    "{name}": {{\n'
            f'      "group": "{model["group"]}",\n'
            f'      "vertices": [\n      {_rows(model["vertices"], 6)}\n      ],\n'
            f'      "triangles": [\n      {_rows(model["triangles"], 8)}\n      ]\n'
            f"    }}"
        )
    lines.append(",\n".join(entries))
    lines.append("  }")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="書かずに、今の JSON が zip から作った結果と一致するか確かめる")
    parser.add_argument("--zip", type=Path, default=None, help="落とさず、この zip を使う")
    args = parser.parse_args(argv)

    archive_path = fetch_zip(args.zip)
    with zipfile.ZipFile(archive_path) as archive:
        models = read_models(archive)
    text = render(models)
    triangles = {name: len(model["triangles"]) for name, model in models.items()}
    print(f"{SOURCE['name']} {SOURCE['version']} / {SOURCE['license']}: " +
          ", ".join(f"{name} {count}" for name, count in triangles.items()))

    if args.check:
        current = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else ""
        if current != text:
            print(f"{OUTPUT.relative_to(ROOT)} が zip から作った結果と違う", file=sys.stderr)
            return 1
        print("一致")
        return 0

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    partial = OUTPUT.with_name(OUTPUT.name + ".part")
    with partial.open("w", encoding="utf-8", newline="\n") as fp:
        fp.write(text)
    os.replace(partial, OUTPUT)
    print(f"書いた: {OUTPUT.relative_to(ROOT)}（{len(text)} B）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
