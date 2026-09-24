#!/usr/bin/env bash
# 面のテクスチャと色を変えた前後を、Blender で同じ視点から描いて左右に並べる（#59 の比較用）。
#   before: <base>（既定 origin/main）の campus.fbx を、<base> の MaterialLibrary の単色で
#   after : 作業ツリーの campus.fbx を、作業ツリーの色と Assets/Textures/surfaces の画像で
# 樹木は after の FBX と同じフォルダの trees.fbx / trees.json を両方に置く（差をマテリアルだけにする）。
# Blender は 1 本ずつ順に回す。
#
# 使い方（Git Bash）:
#   BLENDER="/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" \
#     bash blender/tools_view/surfaces_before_after.sh --out-dir docs/progress/59
#
# 出力: <out-dir>/<YYYYMMDD>-blender-<視点>-before-after.jpg（6 視点、各 2566x720）
# 引数: --out-dir（必須） --fbx <after の campus.fbx> --base <before の ref> --date YYYYMMDD
#       --blender <Blender の実行ファイル>（無ければ環境変数 BLENDER。どちらかが必須）
# 環境変数: PY（Pillow の入った Python。既定は python3、読めなければ python）、
#           SAMPLES（EEVEE のサンプル数、既定 32）
# 仮の FBX・描いた PNG・ログは一時フォルダに置き、終わったら消す。
set -euo pipefail

# Windows の blender.exe / python.exe / git.exe には C:/... の形で渡す。MSYS のパス変換を
# 切った環境（MSYS_NO_PATHCONV=1 など）でも /c/... のまま届かないように
native() { if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else printf '%s\n' "$1"; fi; }
abs_dir() { local d; d="$(cd "$1" && pwd)" || return 1; native "$d"; }

HERE="$(abs_dir "$(dirname "${BASH_SOURCE[0]}")")"
ROOT="$(abs_dir "$HERE/../..")"
CAMPUS_FBX=unity/KatsushikaCampusDays/Assets/Models/Campus/campus.fbx

die() { echo "surfaces_before_after.sh: $*" >&2; exit 1; }

OUT_DIR=""
AFTER_FBX="$ROOT/$CAMPUS_FBX"
BASE=origin/main
DATE="$(date +%Y%m%d)"
BLENDER="${BLENDER:-}"
PY="${PY:-}"
SAMPLES="${SAMPLES:-32}"
while [ $# -gt 0 ]; do
    case "$1" in  # -h は冒頭のコメントをそのまま出す
        -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;;
        --out-dir|--fbx|--base|--date|--blender) [ $# -ge 2 ] || die "$1 に値がありません" ;;
        *) die "知らない引数: $1" ;;
    esac
    case "$1" in
        --out-dir) OUT_DIR="$2" ;;
        --fbx) AFTER_FBX="$2" ;;
        --base) BASE="$2" ;;
        --date) DATE="$2" ;;
        --blender) BLENDER="$2" ;;
    esac
    shift 2
done

[ -n "$OUT_DIR" ] || die "--out-dir を渡してください"
# 日付はファイル名に入り、ref は git の引数になるので、形を先に確かめる
[[ "$DATE" =~ ^[0-9]{8}$ ]] || die "--date は YYYYMMDD で渡してください: $DATE"
case "$BASE" in -*) die "--base に - で始まる値は渡せません: $BASE" ;; esac
[ -n "$BLENDER" ] || die "Blender を --blender か環境変数 BLENDER で渡してください"
command -v "$BLENDER" > /dev/null || die "Blender がありません: $BLENDER"
if [ -z "$PY" ]; then  # python.org の Windows 版には python3 が無いので python も試す
    for c in python3 python; do
        "$c" -c "import PIL" 2> /dev/null && { PY="$c"; break; }
    done
    [ -n "$PY" ] || die "python3 でも python でも Pillow を読めません（PY で Pillow の入った Python を指定できます）"
else
    command -v "$PY" > /dev/null || die "Python がありません: $PY"
    "$PY" -c "import PIL" || die "$PY で Pillow を読めません"
fi
[ -f "$AFTER_FBX" ] || die "after の FBX がありません: $AFTER_FBX"
AFTER_FBX="$(abs_dir "$(dirname "$AFTER_FBX")")/$(basename "$AFTER_FBX")"
git -C "$ROOT" rev-parse --verify --quiet "$BASE^{commit}" > /dev/null \
    || die "before の ref がありません: ${BASE}（git fetch origin が要るかもしれません）"
# 出力先は描く前に作る（最後の並べる段で失敗すると、描いた PNG ごと消えるので）
mkdir -p "$OUT_DIR" || die "--out-dir を作れません: $OUT_DIR"
OUT_DIR="$(abs_dir "$OUT_DIR")"

TMP_BASE="${TMPDIR:-/tmp}"
TMP="$(mktemp -d "${TMP_BASE%/}/kcd_surfaces.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
TMP="$(native "$TMP")"

# before の FBX は ref から取り出す（作業ツリーは書き換えない）
BEFORE_FBX="$TMP/before_campus.fbx"
git -C "$ROOT" show "$BASE:$CAMPUS_FBX" > "$BEFORE_FBX"

TREES=()
TREES_DIR="$(dirname "$AFTER_FBX")"
if [ -f "$TREES_DIR/trees.fbx" ] && [ -f "$TREES_DIR/trees.json" ]; then
    TREES=(--trees-fbx "$TREES_DIR/trees.fbx" --trees-json "$TREES_DIR/trees.json")
fi

render() {  # render <fbx> <flat|textured> <色を読む ref（空なら作業ツリー）>
    local log="$TMP/$2.log"
    echo "[run] $2: $1"
    # --python-exit-code が無いと、スクリプトが例外で落ちても Blender は 0 で終わる
    if ! "$BLENDER" -b --factory-startup --python-exit-code 1 \
            --python "$HERE/surfaces_before_after.py" -- \
            --fbx "$1" --mode "$2" --out-dir "$TMP/renders" --palette-ref "$3" \
            --samples "$SAMPLES" ${TREES[@]+"${TREES[@]}"} > "$log" 2>&1; then
        tail -n 30 "$log" >&2
        die "Blender が $2 で失敗しました"
    fi
    grep -E '^\[(palette|tex|trees|render|done)\]' "$log" || true
}

render "$BEFORE_FBX" flat "$BASE"
render "$AFTER_FBX" textured ""
"$PY" "$HERE/compose_before_after.py" --renders "$TMP/renders" --out-dir "$OUT_DIR" --date "$DATE" \
    --base "${BASE#origin/}"
