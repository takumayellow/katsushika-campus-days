#!/usr/bin/env python3
"""Web ビューア（`build/viewer/`）を組み立てる。

やることは 3 つだけ。

1. Blender を headless で回して `build/viewer/models/*.glb` と `index.json` を書き出す
   （`blender/export_web_glb.py`）。
2. `docs/previews/*.png` を `build/viewer/previews/` へコピーする。
3. `web/viewer/*` を `build/viewer/` へコピーする。

配信ルートは `build/viewer/`。GitHub Pages では `/viewer/` に置く前提で、
HTML からの参照はすべて相対パス（`models/…` / `previews/…`）なので、

    python -m http.server 8766 --directory build/viewer

でもそのまま動く。

    python tools/build_viewer.py                # 全部
    python tools/build_viewer.py --skip-models  # GLB を作り直さない（HTML/CSS/JS だけ）
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"

WEB_SRC = os.path.join(ROOT, "web", "viewer")
PREVIEW_SRC = os.path.join(ROOT, "docs", "previews")
OUT = os.path.join(ROOT, "build", "viewer")


def log(msg: str) -> None:
    print("[viewer] %s" % msg, flush=True)


def find_blender(explicit: str | None) -> str:
    """Blender の実行ファイルを決める。見つからなければ落とす。"""
    for cand in (explicit, os.environ.get("KCD_BLENDER"), DEFAULT_BLENDER):
        if cand and os.path.isfile(cand):
            return cand
    found = shutil.which("blender")
    if found:
        return found
    raise SystemExit(
        "blender.exe が見つからない。--blender か環境変数 KCD_BLENDER で指定する。"
    )


def run_blender(blender: str, no_draco: bool) -> None:
    """GLB を書き出す。Blender の終了コードと index.json の存在で成否を判定する。"""
    models = os.path.join(OUT, "models")
    script = os.path.join(ROOT, "blender", "export_web_glb.py")
    cmd = [blender, "-b", "--python", script, "--",
           "--out", models.replace("\\", "/")]
    if no_draco:
        cmd.append("--no-draco")
    env = dict(os.environ, PYTHONIOENCODING="utf-8")
    log("Blender: %s" % os.path.basename(blender))
    proc = subprocess.run(cmd, cwd=ROOT, env=env, text=True, encoding="utf-8",
                          errors="replace", capture_output=True)
    for line in (proc.stdout or "").splitlines():
        if line.startswith("[glb]"):
            log(line[6:])
    if proc.returncode != 0:
        sys.stderr.write((proc.stdout or "")[-4000:])
        sys.stderr.write((proc.stderr or "")[-4000:])
        raise SystemExit("Blender の書き出しが失敗した（exit=%d）" % proc.returncode)
    if not os.path.isfile(os.path.join(models, "index.json")):
        raise SystemExit("index.json が作られていない: %s" % models)


def copy_tree(src: str, dst: str, suffixes: tuple[str, ...] | None = None) -> int:
    """src の直下のファイルを dst へコピーする（サブディレクトリは辿らない）。"""
    if not os.path.isdir(src):
        raise SystemExit("コピー元が無い: %s" % src)
    os.makedirs(dst, exist_ok=True)
    count = 0
    for name in sorted(os.listdir(src)):
        path = os.path.join(src, name)
        if not os.path.isfile(path):
            continue
        if suffixes and not name.lower().endswith(suffixes):
            continue
        shutil.copy2(path, os.path.join(dst, name))
        count += 1
    return count


def make_thumbs(previews: list[dict], dst_root: str, width: int = 480) -> int:
    """ギャラリー用の軽いサムネイル（JPEG）を `previews/thumbs/` に作る。

    原寸の PNG は 1 枚 1.4 MB ほどあってギャラリーの一覧には重い。縮小版を作り、
    index.json の各エントリへ `thumb` を書き足す（拡大表示は原寸の PNG のまま）。
    Pillow が無い環境では原寸のまま使う。
    """
    try:
        from PIL import Image
    except ImportError:
        log("Pillow が無いのでサムネイルは作らない（原寸を表示する）")
        return 0
    out_dir = os.path.join(dst_root, "thumbs")
    os.makedirs(out_dir, exist_ok=True)
    made = 0
    for item in previews:
        src = os.path.join(dst_root, item["file"])
        if not os.path.isfile(src):
            continue
        name = os.path.splitext(item["file"])[0] + ".jpg"
        dst = os.path.join(out_dir, name)
        with Image.open(src) as img:
            img = img.convert("RGB")
            ratio = width / float(img.width)
            if ratio < 1.0:
                img = img.resize((width, max(1, round(img.height * ratio))),
                                 Image.LANCZOS)
            img.save(dst, "JPEG", quality=82, optimize=True)
        item["thumb"] = "thumbs/" + name
        made += 1
    return made


def dir_size(path: str) -> int:
    total = 0
    for base, _dirs, files in os.walk(path):
        for name in files:
            total += os.path.getsize(os.path.join(base, name))
    return total


def main() -> None:
    ap = argparse.ArgumentParser(prog="build_viewer.py")
    ap.add_argument("--blender", help="blender.exe のパス")
    ap.add_argument("--skip-models", action="store_true",
                    help="GLB を作り直さず、プレビューと web/viewer だけ配置する")
    ap.add_argument("--no-draco", action="store_true", help="Draco 圧縮を使わない")
    args = ap.parse_args()

    t0 = time.time()
    os.makedirs(OUT, exist_ok=True)

    if args.skip_models:
        log("GLB の書き出しを飛ばす（--skip-models）")
    else:
        run_blender(find_blender(args.blender), args.no_draco)

    n_png = copy_tree(PREVIEW_SRC, os.path.join(OUT, "previews"), (".png",))
    log("プレビュー %d 枚をコピー" % n_png)

    n_web = copy_tree(WEB_SRC, OUT)
    log("web/viewer から %d ファイルをコピー" % n_web)

    index_path = os.path.join(OUT, "models", "index.json")
    with open(index_path, encoding="utf-8") as fh:
        index = json.load(fh)
    missing = [m["file"] for m in index["models"]
               if not os.path.isfile(os.path.join(OUT, "models", m["file"]))]
    if missing:
        raise SystemExit("index.json が指す GLB が無い: %s" % ", ".join(missing))
    for name in ("index.html", "viewer.js", "viewer.css"):
        if not os.path.isfile(os.path.join(OUT, name)):
            raise SystemExit("配信ルートに %s が無い" % name)
    lost = [p["file"] for p in index["previews"]
            if not os.path.isfile(os.path.join(OUT, "previews", p["file"]))]
    if lost:
        raise SystemExit("index.json が指すプレビューが無い: %s" % ", ".join(lost[:5]))

    n_thumb = make_thumbs(index["previews"], os.path.join(OUT, "previews"))
    if n_thumb:
        with open(index_path, "w", encoding="utf-8") as fh:
            json.dump(index, fh, ensure_ascii=False, indent=1)
        log("サムネイル %d 枚を生成" % n_thumb)

    models_bytes = sum(m["bytes"] for m in index["models"])
    log("GLB %d 件 / %.2f MB（配信ルート全体 %.2f MB）"
        % (len(index["models"]), models_bytes / 1e6, dir_size(OUT) / 1e6))
    log("完了 %.1f s -> %s" % (time.time() - t0, OUT))
    log("確認: python -m http.server 8766 --directory build/viewer")


if __name__ == "__main__":
    main()
