"""WebGL ビルドを GitHub Pages に載せる。

使い方:
    python tools/deploy_pages.py            # build/WebGL を zip → Release web-latest → Pages ワークフロー起動
    python tools/deploy_pages.py --build    # 先に Unity で WebGL ビルドしてから同じことをする
    python tools/deploy_pages.py --no-deploy   # zip を作るだけ
    python tools/deploy_pages.py --build --allow-dirty   # 未コミットの変更があってもビルド・配信する
    python tools/deploy_pages.py --smoke    # 載せる前に build/WebGL をローカル配信して E2E スモーク (#69)
    python tools/deploy_pages.py --allow-oversize   # 配信サイズの上限を超えていても配信する (#70)

Unity のビルドは CI ではライセンスの都合で回せないので、ここでローカルにビルドし、
成果物 zip を Release（タグ web-latest、prerelease）に置き換えて置き、
.github/workflows/pages.yml を workflow_dispatch で起動する。ワークフロー側は zip を展開して
Pages に載せるだけなので、git のサイズ制限（1 ファイル 100 MB）に当たらない。

ビルド元の記録 (#75):
    --build は Unity を起動する前に git rev-parse HEAD と
    git status --porcelain -- unity/KatsushikaCampusDays を取り、Unity プロジェクトの下に
    未コミットの変更（未追跡のファイルを含む）があれば止める。ビルド後、index.html の
    <meta name="kcd-build"> にコミット SHA と作業ツリーの状態（clean / dirty）を書き込む。
    配信するときは、この記録が無いビルドと dirty なビルドを止める。どちらも --allow-dirty で通す。

ファイル名 (#75):
    Build/ の中身は内容のハッシュ名（PlayerSettings.WebGL.nameFilesAsHashes）で出る。
    zip には index.html が参照する Build/ のファイルだけを入れ、前のビルドの残りを載せない。
    参照は buildUrl + "/<名前>" と, 段階読み込みのときの primaryDataUrls / secondaryDataUrls の
    配列から読む。loader・framework・wasm・data のどれかの参照を読み取れなければ zip を作らない。

ASTC の data (#187):
    Unity の BuildPlayer.BuildWebGL は, テクスチャを DXT で圧縮したビルドを build/WebGL に,
    ASTC で圧縮したビルドを build/WebGL-ASTC に出す。2 本で違うのは data だけなので, --build は
    ASTC の data を build/WebGL/Build/ に写し, index.html の astcDataUrl に名前を書き込む。
    どちらを読むかは index.html が端末の GPU を見て決める。ローダー・framework・wasm の名前が
    2 本で違えば, data が噛み合わないとみて止める。

配信サイズ (#70):
    zip を作った後, 1 台の端末が読むファイルの合計と data の大きさを測り, 上限（MAX_TOTAL_BYTES /
    MAX_DATA_BYTES, 決め方は定数のコメント）を超えていれば配信しない。--allow-oversize で通す。
    端末は DXT と ASTC の data のどちらか一方しか読まないので, 大きい方で測る。
    --no-deploy のときは超えていても zip を残し, 超えたことだけを知らせる。

遊び方のページ (#18):
    zip には docs/HOW_TO_PLAY.md から作った howto/index.html と, その画像（howto/img/）を足す。
    Pages は zip の中身をそのまま載せ, pages.yml はリポジトリを checkout しないので, ページは zip に
    入れておく。作り方は tools/howto_page.py。遊び方に変換できない書き方があるときは, 時間のかかる
    Unity のビルドより前に止める。ページの分は zip 全体（stored）と files に数え, 合計と data には
    数えない（ゲームを読む量ではない）。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import time
import zipfile
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

import howto_page

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "unity" / "KatsushikaCampusDays"
PROJECT_PATHSPEC = "unity/KatsushikaCampusDays"
LOG_DIR = ROOT / "unity" / "logs"
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe")
TAG = "web-latest"
ASSET_NAME = "webgl.zip"
SITE_URL = "https://takumayellow.github.io/katsushika-campus-days/"
SMOKE = ROOT / "e2e" / "run_webgl_smoke.py"
REPO_URL = "https://github.com/takumayellow/katsushika-campus-days"

# WebGL テンプレート（Assets/WebGLTemplates/KCD/index.html）の head に置いた目印。
STAMP_MARKER = "<!-- KCD_BUILD_STAMP -->"
STAMP_RE = re.compile(
    r'<meta name="kcd-build" content="commit=(?P<commit>[0-9a-f]{40}|unknown) '
    r'tree=(?P<tree>clean|dirty|unknown) built=(?P<built>[^"]*)">')
# テンプレートは Build/ のファイルを buildUrl + "/<ファイル名>" の形で参照する。
BUILD_REF_RE = re.compile(r'buildUrl \+ "/([^"\'\s)]+)')
# 段階読み込み（PROGRESSIVE_ASSET_LOADING）では data が buildUrl を付けない JSON の配列で入る。
# ローダーは配列の URL をそのまま取りに行くので, 中身は index.html からの相対パス。
DATA_URLS_RE = re.compile(r'\b(primaryDataUrls|secondaryDataUrls)\s*:\s*(\[[^\]]*\])')
# ローダーが起動に必ず読む Build/ のファイルの設定と, そのファイル名。data は dataUrl か primaryDataUrls のどちらか。
LOADER_PART_RES = (
    ("loaderUrl", re.compile(r'\bloaderUrl\s*=\s*buildUrl \+ "/([^"\'\s)]+)')),
    ("frameworkUrl", re.compile(r'\bframeworkUrl\s*:\s*buildUrl \+ "/([^"\'\s)]+)')),
    ("codeUrl", re.compile(r'\bcodeUrl\s*:\s*buildUrl \+ "/([^"\'\s)]+)')),
)
DATA_URL_RE = re.compile(r'\bdataUrl\s*:\s*buildUrl \+ "/')
DATA_NAME_RE = re.compile(r'\bdataUrl\s*:\s*buildUrl \+ "/([^"\'\s)]+)')
# テンプレートの ASTC の data の差し込み口と, 名前を書き込んだ後の形 (#187)。
ASTC_SLOT = "var astcDataUrl = null; // KCD_ASTC_DATA_URL"
ASTC_DATA_RE = re.compile(r'\bastcDataUrl\s*=\s*buildUrl \+ "/([^"\'\s)]+)')
# ASTC のビルドの出力先は, WebGL の出力先の名前にこれを付けたもの（BuildPlayer.AstcOutputSuffix）。
ASTC_DIR_SUFFIX = "-ASTC"
# Unity が付ける data の名前（内容のハッシュと拡張子）に使う文字。index.html の JS の文字列に書き込み,
# Build/ の下のファイル名にも使うので, パスの区切り・引用符・< > を通さない。
ASTC_DATA_NAME_RE = re.compile(r"[0-9A-Za-z][0-9A-Za-z._-]*")

MIB = 1024 * 1024
# 配信サイズの上限 (#70)。zip に入れた後の, 配信する各ファイルの大きさで測る。
# 基にした実測（2026-09-24 に Release web-latest の webgl.zip を展開して測った値）:
#   配信する 6 ファイルの合計   66,468,279 B = 63.39 MiB（#70 の公開版の記録 63.4 MiB と同じ）
#   そのうち data（gzip 済み）  55,476,397 B = 52.91 MiB。gzip を解くと 72,399,237 B = 69.05 MiB
#   build/WebGL をまるごと測ったとき 64.5 MB（#54。前のビルドの残りを含む）
# 合計の上限は 80 MiB。実測より 26%（16.6 MiB）上。音声（#28 #29 #34 #38）と室内（#45 ほか）が
#   まだ載っていないので, それが入る分の幅を残す。初めての読み込みの時間は合計にほぼ比例する
#   （2 回目からはブラウザのキャッシュが効く）。20 Mbps の回線で計算すると 63.4 MiB で約 27 秒,
#   80 MiB で約 34 秒かかる。これより長くは待たせない。
# data の上限は 64 MiB。実測より 21%（11.1 MiB）上。data は gzip を解いた中身が, 遊んでいる間ずっと
#   ブラウザのメモリ（Unity のヒープの外にある仮想のファイルシステム）に載る（実測の比 1.31 なら 64 MiB で
#   約 84 MiB）ので, タブのメモリの予算（docs/WEBGL_BUDGET.md）に響く。
#   wasm・framework・loader の大きさは Unity の版とコードの量で決まり, ほとんど変わらない。
#   ふくらむのは data なので, 合計より先に data で止まるよう, data の幅を狭くしてある。
# どちらも 1 台の端末が読む量で測る (#187)。端末が読むのは DXT か ASTC の data のどちらか一方なので,
#   大きい方の data で測る。zip と Pages に置く量は data 1 つ分増える。
MAX_TOTAL_BYTES = 80 * MIB
MAX_DATA_BYTES = 64 * MIB


@dataclass(frozen=True)
class PayloadSize:
    """配信する zip の中身の大きさ（バイト）。

    total と data は 1 台の端末が読む量。data は index.html が data として読むファイルの合計で,
    ASTC の data もあれば, DXT と ASTC の大きい方を取る。total は data 以外のファイルにその data を足したもの。
    stored は zip に入れた全ファイルの合計（Pages に置く量）, astc_data は ASTC の data の大きさ。
    page は遊び方のページ（howto/）の大きさ。zip と Pages には載るが, ゲームを読む量ではないので
    total にも data にも数えない。
    """

    total: int
    data: int
    files: int
    stored: int = 0
    astc_data: int = 0
    page: int = 0


@dataclass(frozen=True)
class GitState:
    """ビルドを始める時点のリポジトリの状態。"""

    commit: str
    dirty_paths: tuple[str, ...] = ()

    @property
    def dirty(self) -> bool:
        return bool(self.dirty_paths)

    @property
    def tree(self) -> str:
        return "dirty" if self.dirty else "clean"


@dataclass(frozen=True)
class BuildStamp:
    """index.html に書き込んだビルド元の記録。"""

    commit: str
    tree: str
    built: str

    @property
    def known(self) -> bool:
        return self.commit != "unknown"

    @property
    def clean(self) -> bool:
        return self.known and self.tree == "clean"


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    print("$", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run(cmd, **{"check": True, **kwargs})


def git_state(root: Path = ROOT, pathspec: str = PROJECT_PATHSPEC) -> GitState:
    """HEAD の SHA と, pathspec の下の未コミットの変更（未追跡を含む）を返す。

    ビルドに入るのは Unity プロジェクトの下だけなので, 見るのもそこに絞る。
    tools/ や docs/ の変更ではビルドは変わらない。
    """
    commit = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                            capture_output=True, text=True, check=True).stdout.strip()
    status = subprocess.run(
        ["git", "-C", str(root), "status", "--porcelain", "--untracked-files=all", "--", pathspec],
        capture_output=True, text=True, encoding="utf-8", check=True).stdout
    paths = tuple(line[3:] for line in status.splitlines() if line.strip())
    return GitState(commit=commit, dirty_paths=paths)


def utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def stamp_html(html: str, commit: str, tree: str, built: str) -> str:
    """index.html にビルド元の記録を書き込んだ文字列を返す。

    テンプレートの目印を置き換える。既に記録があれば書き換え, どちらも無ければ </head> の前に足す。
    """
    meta = f'<meta name="kcd-build" content="commit={commit} tree={tree} built={built}">'
    if STAMP_RE.search(html):
        return STAMP_RE.sub(lambda _: meta, html, count=1)
    if STAMP_MARKER in html:
        return html.replace(STAMP_MARKER, meta, 1)
    if "</head>" in html:
        return html.replace("</head>", f"  {meta}\n  </head>", 1)
    raise ValueError("index.html に </head> が無く, ビルド元の記録を書き込めない")


def read_stamp(html: str) -> BuildStamp | None:
    match = STAMP_RE.search(html)
    if match is None:
        return None
    return BuildStamp(commit=match["commit"], tree=match["tree"], built=match["built"])


def stamp_file(index: Path, state: GitState, built: str) -> BuildStamp:
    # newline="" で読み書きし, Unity が出した改行コードをそのまま残す。
    with open(index, encoding="utf-8", newline="") as f:
        html = f.read()
    with open(index, "w", encoding="utf-8", newline="") as f:
        f.write(stamp_html(html, state.commit, state.tree, built))
    return BuildStamp(commit=state.commit, tree=state.tree, built=built)


def data_url_arrays(html: str) -> dict[str, list[str]]:
    """primaryDataUrls / secondaryDataUrls の中身。無いものは入れない。"""
    arrays: dict[str, list[str]] = {}
    for key, text in DATA_URLS_RE.findall(html):
        try:
            urls = json.loads(text)
        except json.JSONDecodeError as error:
            raise ValueError(f"index.html の {key} を JSON の配列として読めない: {text}") from error
        if not isinstance(urls, list) or not all(isinstance(url, str) for url in urls):
            raise ValueError(f"index.html の {key} が文字列の配列ではない: {text}")
        arrays.setdefault(key, []).extend(urls)
    return arrays


def site_path(url: str) -> str:
    """index.html からの相対 URL を, サイトの根からのパスにする。サイトの外を指すものは止める。"""
    path = url[2:] if url.startswith("./") else url
    parts = path.split("/")
    if not path or path.startswith("/") or ":" in path or any(p in ("", ".", "..") for p in parts):
        raise ValueError(f"index.html がサイトの中のファイルとして読めない URL を参照している: {url!r}")
    return path


def referenced_paths(html: str) -> list[str]:
    """index.html が読む Build/ まわりのファイルの, サイトの根からのパス（重複なし, 出てきた順）。"""
    paths = [f"Build/{name}" for name in BUILD_REF_RE.findall(html)]
    for urls in data_url_arrays(html).values():
        paths.extend(site_path(url) for url in urls)
    return list(dict.fromkeys(paths))


def referenced_build_files(html: str) -> list[str]:
    """index.html が Build/ から読むファイル（loader, data, framework, wasm など）の Build/ からのパス。"""
    return [path[len("Build/"):] for path in referenced_paths(html) if path.startswith("Build/")]


def code_names(html: str) -> dict[str, list[str]]:
    """loader・framework・wasm の参照のファイル名。ASTC のビルドと突き合わせるのに使う。"""
    return {name: pattern.findall(html) for name, pattern in LOADER_PART_RES}


def astc_data_paths(html: str) -> list[str]:
    """index.html が ASTC の data として読むファイルの, サイトの根からのパス。"""
    return list(dict.fromkeys(site_path(f"Build/{name}") for name in ASTC_DATA_RE.findall(html)))


def astc_build_dir(build_dir: Path) -> Path:
    return build_dir.with_name(build_dir.name + ASTC_DIR_SUFFIX)


def attach_astc_data(build_dir: Path, astc_dir: Path) -> str:
    """ASTC のビルドの data を build_dir/Build/ に写し, index.html に名前を書き込む。写した名前を返す。

    BuildPlayer.BuildWebGL はテクスチャの圧縮の形式だけを変えて 2 本ビルドするので, ローダー・
    framework・wasm は同じ名前（内容のハッシュ）になる。違えば data が噛み合わないとみて止める。
    data を dataUrl の 1 つで読むビルドに限る。テンプレートも config.dataUrl だけを差し替える。
    """
    index = build_dir / "index.html"
    astc_index = astc_dir / "index.html"
    for path in (index, astc_index):
        if not path.is_file():
            raise FileNotFoundError(f"WebGL ビルドが見つからない: {path}")
    # newline="" で読み書きし, Unity が出した改行コードをそのまま残す。
    with open(index, encoding="utf-8", newline="") as f:
        html = f.read()
    astc_html = astc_index.read_text(encoding="utf-8")

    names = DATA_NAME_RE.findall(astc_html)
    if len(names) != 1:
        raise ValueError(f"{astc_index} から data の名前を 1 つに決められない: {names}")
    name = names[0]
    if not ASTC_DATA_NAME_RE.fullmatch(name):
        raise ValueError(f"{astc_index} の data の名前に使えない文字がある: {name!r}")
    if code_names(html) != code_names(astc_html):
        raise ValueError(f"{astc_dir} とローダー・framework・wasm が違う。2 本を同じ Unity の実行でビルドし直す: "
                         f"{code_names(html)} / {code_names(astc_html)}")
    if html.count(ASTC_SLOT) != 1:
        raise ValueError(f"{index} に ASTC の data の差し込み口（{ASTC_SLOT}）が 1 つだけ無い")
    source = astc_dir / "Build" / name
    if not source.is_file():
        raise FileNotFoundError(f"ASTC のビルドの data が無い: {source}")

    shutil.copy2(source, build_dir / "Build" / name)
    with open(index, "w", encoding="utf-8", newline="") as f:
        f.write(html.replace(ASTC_SLOT, f'var astcDataUrl = buildUrl + "/{name}";'))
    return name


def missing_loader_parts(html: str) -> list[str]:
    """ローダーが起動に必ず読むのに, index.html から参照を読み取れないもの。

    参照の書き方がテンプレートや Unity の版で変わると, そのファイルが「参照されていない」として
    zip から外れ, 動かないビルドが配信される。ここで欠けを見つけて止める。
    """
    missing = [name for name, pattern in LOADER_PART_RES if not pattern.search(html)]
    if not DATA_URL_RE.search(html) and not data_url_arrays(html).get("primaryDataUrls"):
        missing.append("data")
    return missing


def select_files(build_dir: Path) -> list[Path]:
    """zip に入れるファイル。Build/ の中は index.html が参照するものだけ。

    ハッシュ名にすると, 前のビルドのファイルが同じ名前で上書きされずに Build/ に残る。
    それを載せると配信サイズが毎回ふくらむので, 参照されていないものは外す。
    外すのは参照を読み取れた場合だけで, loader・framework・wasm・data のどれかの参照が
    読めなければ止める（読めないまま外すと, 前のビルドの残りと見分けが付かない）。
    """
    html = (build_dir / "index.html").read_text(encoding="utf-8")
    refs = referenced_paths(html)
    if not refs:
        raise ValueError("index.html から Build/ のファイルの参照を読み取れない")
    unread = missing_loader_parts(html)
    if unread:
        raise ValueError("index.html からローダーが読むファイルの参照を読み取れない: " + ", ".join(unread))

    missing = [path for path in refs if not (build_dir / path).is_file()]
    if missing:
        raise FileNotFoundError("index.html が参照するファイルが無い: " + ", ".join(missing))

    keep = set(refs)
    selected = []
    skipped = []
    for path in sorted(p for p in build_dir.rglob("*") if p.is_file()):
        rel = path.relative_to(build_dir).as_posix()
        if rel.startswith("Build/") and rel not in keep:
            skipped.append(rel)
            continue
        selected.append(path)
    if skipped:
        print(f"参照されていない {len(skipped)} 個を zip から外した: " + ", ".join(skipped))
    return selected


def extras_problem(taken: Sequence[str], extras: Sequence[tuple[str, bytes]]) -> str | None:
    """ビルドの外から足すファイル（extras）を zip に足せない理由。足せるなら None。"""
    names = [name for name, _ in extras]
    unsafe = [name for name in names if name.startswith("/") or "\\" in name or ".." in name.split("/")]
    if unsafe:
        return "サイトの根の外を指す名前がある: " + ", ".join(unsafe)
    clash = sorted({name for name in names if name in taken or names.count(name) > 1})
    if clash:
        return "同じ名前のファイルが 2 つある: " + ", ".join(clash)
    return None


def extra_info(name: str, date_time: tuple[int, int, int, int, int, int]) -> zipfile.ZipInfo:
    """ビルドの外から足すファイルの zip の項目。権限は 0644 に決める。

    upload-pages-artifact は誰でも読めるファイルでないと警告し, 権限を直す。ビルドのファイルは
    zf.write() が作業ツリーの権限を写すので, 足すファイルは権限をこちらで決める。
    """
    info = zipfile.ZipInfo(name, date_time=date_time)
    info.compress_type = zipfile.ZIP_STORED
    info.create_system = 3  # Unix。unzip が external_attr の権限を読む
    info.external_attr = (stat.S_IFREG | 0o644) << 16
    return info


def make_zip(build_dir: Path, out: Path, extras: Sequence[tuple[str, bytes]] = ()) -> int:
    """build_dir の配信するファイルを zip にする。extras は足すファイル（サイトの根からのパス, 中身）。"""
    index = build_dir / "index.html"
    if not index.is_file():
        print(f"WebGL ビルドが見つからない: {index}", file=sys.stderr)
        return 1

    try:
        files = select_files(build_dir)
    except (ValueError, FileNotFoundError) as error:
        print(f"zip を作れない: {error}", file=sys.stderr)
        return 1

    problem = extras_problem([path.relative_to(build_dir).as_posix() for path in files], extras)
    if problem is not None:
        print(f"zip を作れない: {problem}", file=sys.stderr)
        return 1

    out.parent.mkdir(parents=True, exist_ok=True)
    # .gz / .wasm / .data は既に圧縮済みなので、zip 自体は無圧縮で速く作る。
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_STORED) as zf:
        for path in files:
            zf.write(path, path.relative_to(build_dir).as_posix())
        now = time.localtime()[:6]
        for name, data in extras:
            zf.writestr(extra_info(name, now), data)

    size_mib = out.stat().st_size / (1024 * 1024)
    print(f"{out} : {len(files) + len(extras)} files, {size_mib:.1f} MiB")
    return 0


def data_paths(html: str) -> list[str]:
    """index.html が data として読むファイルの, サイトの根からのパス（重複なし, 出てきた順）。"""
    paths = [f"Build/{name}" for name in DATA_NAME_RE.findall(html)]
    for urls in data_url_arrays(html).values():
        paths.extend(site_path(url) for url in urls)
    return list(dict.fromkeys(paths))


def payload_size(zip_path: Path) -> PayloadSize:
    """1 台の端末が読むファイルの合計と, そのうち data の合計。zip 全体の合計も添える。"""
    with zipfile.ZipFile(zip_path) as zf:
        sizes = {info.filename: info.file_size for info in zf.infolist() if not info.is_dir()}
        html = zf.read("index.html").decode("utf-8")
    dxt_paths = data_paths(html)
    data = sum(sizes.get(path, 0) for path in dxt_paths)
    astc = sum(sizes.get(path, 0) for path in astc_data_paths(html) if path not in dxt_paths)
    # 遊び方のページはゲームを読む量ではないので, 合計に数えない。
    page = sum(size for name, size in sizes.items() if name.startswith(f"{howto_page.PAGE_DIR}/"))
    stored = sum(sizes.values())
    largest = max(data, astc)
    return PayloadSize(total=stored - page - data - astc + largest, data=largest, files=len(sizes),
                       stored=stored, astc_data=astc, page=page)


def to_mib(size: int) -> str:
    return f"{size / MIB:.1f} MiB"


def size_refusal(size: PayloadSize, max_total: int | None = None,
                 max_data: int | None = None) -> str | None:
    """配信サイズが上限を超えていれば, その理由。超えていなければ None。上限ちょうどは通す。"""
    max_total = MAX_TOTAL_BYTES if max_total is None else max_total
    max_data = MAX_DATA_BYTES if max_data is None else max_data
    over = []
    if size.total > max_total:
        over.append(f"配信するファイルの合計 {to_mib(size.total)} が上限 {to_mib(max_total)} を超えた")
    if size.data > max_data:
        over.append(f"data {to_mib(size.data)} が上限 {to_mib(max_data)} を超えた")
    if not over:
        return None
    return ("。".join(over) + "。docs/WEBGL_BUDGET.md を見て減らすか, "
            "承知の上なら --allow-oversize を付ける。")


def unity_build(unity: Path, build_dir: Path) -> None:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log = LOG_DIR / "webgl.log"
    run([
        str(unity), "-batchmode", "-nographics", "-quit",
        "-projectPath", str(PROJECT),
        # URP は作業中のプラットフォームの品質レベルで Shader を絞る。WebGL で起動しないと
        # PC 用の設定で絞られ, WebGL で建物が描かれなくなる。
        "-buildTarget", "WebGL",
        "-executeMethod", "KCD.Editor.BuildPlayer.BuildWebGL",
        "-buildOutput", str(build_dir),
        "-logFile", str(log),
    ])


def release_notes(stamp: BuildStamp | None) -> str:
    if stamp is None or not stamp.known:
        source = "ビルド元のコミットは記録されていない"
    else:
        state = "作業ツリーはきれい" if stamp.clean else "未コミットの変更を含む作業ツリー"
        source = f"ビルド元 {REPO_URL}/commit/{stamp.commit} ({state}, {stamp.built})"
    return f"WebGL ビルド。{source}。Pages 配信用の成果物なので、遊ぶには {SITE_URL} を開く。"


def release_exists(tag: str) -> bool:
    result = subprocess.run(["gh", "release", "view", tag, "--json", "tagName"],
                            capture_output=True, text=True)
    return result.returncode == 0


def upload_release(zip_path: Path, tag: str, notes: str) -> None:
    if not release_exists(tag):
        run(["gh", "release", "create", tag, "--prerelease", "--title", "WebGL (latest)",
             "--notes", notes])
    else:
        run(["gh", "release", "edit", tag, "--notes", notes])
    run(["gh", "release", "upload", tag, f"{zip_path}#{ASSET_NAME}", "--clobber"])


def local_smoke(build_dir: Path) -> int:
    """載せる前に、同じビルドを Pages と同じ条件（Content-Encoding なし）で配信してスモークを回す。"""
    return run([sys.executable, str(SMOKE), "--serve", str(build_dir)], check=False).returncode


def dispatch_workflow(tag: str) -> None:
    run(["gh", "workflow", "run", "pages.yml", "-f", f"tag={tag}"])


def deploy_refusal(stamp: BuildStamp | None) -> str | None:
    """配信を止める理由。止めないなら None。"""
    if stamp is None or not stamp.known:
        return ("このビルドにはビルド元のコミットの記録が無い。"
                "--build で作り直すか, 承知の上なら --allow-dirty を付ける。")
    if not stamp.clean:
        return (f"このビルドは未コミットの変更を含む作業ツリーから作られた (commit {stamp.commit[:12]})。"
                "コミットしてから --build で作り直すか, 承知の上なら --allow-dirty を付ける。")
    return None


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", action="store_true", help="先に Unity で WebGL ビルドする")
    parser.add_argument("--build-dir", default=str(ROOT / "build" / "WebGL"))
    parser.add_argument("--zip", default=str(ROOT / "dist" / ASSET_NAME))
    parser.add_argument("--unity", default=os.environ.get("UNITY_EXE", str(DEFAULT_UNITY)))
    parser.add_argument("--tag", default=TAG)
    parser.add_argument("--no-deploy", action="store_true", help="zip を作るだけ")
    parser.add_argument("--smoke", action="store_true",
                        help="載せる前にローカル配信で E2E スモークを回し、落ちたら載せない")
    parser.add_argument("--allow-dirty", action="store_true",
                        help=f"{PROJECT_PATHSPEC} の下に未コミットの変更があってもビルド・配信する")
    parser.add_argument("--allow-oversize", action="store_true",
                        help=f"配信サイズの上限（合計 {to_mib(MAX_TOTAL_BYTES)}, "
                             f"data {to_mib(MAX_DATA_BYTES)}）を超えていても配信する")
    return parser.parse_args(argv[1:])


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    build_dir = Path(args.build_dir)

    # 遊び方に変換できない書き方があれば, 時間のかかるビルドの前に止める。
    try:
        page = howto_page.site_files()
    except (ValueError, OSError) as error:
        print(f"遊び方のページを作れない: {error}", file=sys.stderr)
        return 1

    if args.build:
        state = git_state()
        if state.dirty:
            shown = ", ".join(state.dirty_paths[:10])
            more = f" ほか {len(state.dirty_paths) - 10} 件" if len(state.dirty_paths) > 10 else ""
            print(f"{PROJECT_PATHSPEC} の下に未コミットの変更が {len(state.dirty_paths)} 件ある: {shown}{more}",
                  file=sys.stderr)
            if not args.allow_dirty:
                print("コミットしてからビルドする。承知の上なら --allow-dirty を付ける。", file=sys.stderr)
                return 1
            print("--allow-dirty なので続ける。index.html には tree=dirty と書く。", file=sys.stderr)
        unity_build(Path(args.unity), build_dir)
        index = build_dir / "index.html"
        if not index.is_file():
            print(f"WebGL ビルドが見つからない: {index}", file=sys.stderr)
            return 1
        try:
            astc = attach_astc_data(build_dir, astc_build_dir(build_dir))
        except (ValueError, FileNotFoundError) as error:
            print(f"ASTC の data を組み込めない: {error}", file=sys.stderr)
            return 1
        print(f"ASTC の data: Build/{astc}")
        stamp_file(index, state, utc_now())

    index = build_dir / "index.html"
    stamp = read_stamp(index.read_text(encoding="utf-8")) if index.is_file() else None
    if stamp is not None:
        print(f"ビルド元: commit {stamp.commit} tree={stamp.tree} built={stamp.built}")

    if args.smoke and local_smoke(build_dir) != 0:
        print("E2E スモークが落ちたので載せない（結果は build/e2e/ の下）", file=sys.stderr)
        return 1

    zip_path = Path(args.zip)
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()
    rc = make_zip(build_dir, zip_path, extras=page)
    if rc != 0:
        return rc

    size = payload_size(zip_path)
    print(f"配信サイズ（1 台が読む分）: 合計 {to_mib(size.total)}（上限 {to_mib(MAX_TOTAL_BYTES)}）, "
          f"data {to_mib(size.data)}（上限 {to_mib(MAX_DATA_BYTES)}）")
    if size.astc_data:
        print(f"  ASTC の data {to_mib(size.astc_data)}, zip 全体 {to_mib(size.stored)}")
    if size.page:
        print(f"  遊び方のページ（{howto_page.PAGE_DIR}/）{to_mib(size.page)}（合計と data には数えない）")
    oversize = size_refusal(size)
    if args.no_deploy:
        if oversize is not None:
            print("配信するなら止まる: " + oversize, file=sys.stderr)
        return 0

    refusals = []
    for reason, allowed, flag in ((deploy_refusal(stamp), args.allow_dirty, "--allow-dirty"),
                                  (oversize, args.allow_oversize, "--allow-oversize")):
        if reason is None:
            continue
        if allowed:
            print(f"{flag} なので配信する: {reason}", file=sys.stderr)
        else:
            refusals.append(reason)
    if refusals:
        for reason in refusals:
            print(reason, file=sys.stderr)
        return 1

    upload_release(zip_path, args.tag, release_notes(stamp))
    dispatch_workflow(args.tag)
    print(f"Pages ワークフローを起動した。数分後に {SITE_URL} が更新される。")
    print("進捗: gh run list --workflow pages.yml")
    print("配信後の E2E は e2e-pages.yml が自動で走る（GPU なし）。実 GPU で確かめるなら:")
    print(f"    python {SMOKE.relative_to(ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
