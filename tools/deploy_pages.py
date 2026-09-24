"""WebGL ビルドを GitHub Pages に載せる。

使い方:
    python tools/deploy_pages.py            # build/WebGL を zip → Release web-latest → Pages ワークフロー起動
    python tools/deploy_pages.py --build    # 先に Unity で WebGL ビルドしてから同じことをする
    python tools/deploy_pages.py --no-deploy   # zip を作るだけ
    python tools/deploy_pages.py --build --allow-dirty   # 未コミットの変更があってもビルド・配信する
    python tools/deploy_pages.py --smoke    # 載せる前に build/WebGL をローカル配信して E2E スモーク (#69)

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
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

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
# ローダーが起動に必ず読む Build/ のファイルの設定。data は dataUrl か primaryDataUrls のどちらか。
LOADER_PART_RES = (
    ("loaderUrl", re.compile(r'\bloaderUrl\s*=\s*buildUrl \+ "/')),
    ("frameworkUrl", re.compile(r'\bframeworkUrl\s*:\s*buildUrl \+ "/')),
    ("codeUrl", re.compile(r'\bcodeUrl\s*:\s*buildUrl \+ "/')),
)
DATA_URL_RE = re.compile(r'\bdataUrl\s*:\s*buildUrl \+ "/')


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


def make_zip(build_dir: Path, out: Path) -> int:
    index = build_dir / "index.html"
    if not index.is_file():
        print(f"WebGL ビルドが見つからない: {index}", file=sys.stderr)
        return 1

    try:
        files = select_files(build_dir)
    except (ValueError, FileNotFoundError) as error:
        print(f"zip を作れない: {error}", file=sys.stderr)
        return 1

    out.parent.mkdir(parents=True, exist_ok=True)
    # .gz / .wasm / .data は既に圧縮済みなので、zip 自体は無圧縮で速く作る。
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_STORED) as zf:
        for path in files:
            zf.write(path, path.relative_to(build_dir).as_posix())

    size_mib = out.stat().st_size / (1024 * 1024)
    print(f"{out} : {len(files)} files, {size_mib:.1f} MiB")
    return 0


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
    return parser.parse_args(argv[1:])


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    build_dir = Path(args.build_dir)

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
    rc = make_zip(build_dir, zip_path)
    if rc != 0 or args.no_deploy:
        return rc

    refusal = deploy_refusal(stamp)
    if refusal is not None:
        if not args.allow_dirty:
            print(refusal, file=sys.stderr)
            return 1
        print("--allow-dirty なので配信する: " + refusal, file=sys.stderr)

    upload_release(zip_path, args.tag, release_notes(stamp))
    dispatch_workflow(args.tag)
    print(f"Pages ワークフローを起動した。数分後に {SITE_URL} が更新される。")
    print("進捗: gh run list --workflow pages.yml")
    print("配信後の E2E は e2e-pages.yml が自動で走る（GPU なし）。実 GPU で確かめるなら:")
    print(f"    python {SMOKE.relative_to(ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
