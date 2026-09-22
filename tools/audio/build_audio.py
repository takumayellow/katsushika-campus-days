"""全ゲーム音声を numpy だけで合成し, Unity の Assets/Audio/ へ書き出す.

    python tools/audio/build_audio.py            # 全部生成
    python tools/audio/build_audio.py bgm se     # カテゴリを絞る

生成後, 各ファイルの波形統計 (長さ/ピーク/RMS/スペクトル重心/ループ継ぎ目) を
tools/audio/manifest.json に書き出す. Unity 側はこれを読んで loop 設定と音量を決める.
"""
from __future__ import annotations

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import analyze
import ambient
import music
import sfx
import synth as S

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
AUDIO_DIR = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Audio")
MANIFEST = os.path.join(ROOT, "tools", "audio", "manifest.json")

# カテゴリごとの推奨ミックス音量 (Unity の AudioSource.volume の初期値)
SUGGESTED_VOLUME = {"BGM": 0.55, "SE": 0.8, "Ambient": 0.35}

# 書き出しピーク (dBFS). 環境音は BGM の下に敷くので低め.
PEAK_DB = {"BGM": -1.0, "SE": -1.5, "Ambient": -3.0}

BGM_BUILDERS = {
    "bgm_day": music.build_day,
    "bgm_evening": music.build_evening,
    "bgm_indoor": music.build_indoor,
    "bgm_title": music.build_title,
    "bgm_night": music.build_night,
    "bgm_result": music.build_result,
    "bgm_anthem_original": music.build_anthem_original,
}
# 校歌の音源で差し替え中の BGM. ここでは生成せず school_song/install_game_bgm.py が書く
# (元の曲に戻すときはここから外す)
SCHOOL_SONG_BGM = {"bgm_day", "bgm_evening", "bgm_night", "bgm_indoor"}
JINGLE_BUILDERS = {
    "jingle_quest": music.build_jingle_quest,
    "jingle_day_end": music.build_jingle_day_end,
}


def _out(category: str, name: str) -> str:
    d = os.path.join(AUDIO_DIR, category)
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, name + ".wav")


def _emit(category: str, name: str, signal, loop: bool, entries: list) -> None:
    path = _out(category, name)
    S.write_wav(path, signal, peak_dbfs=PEAK_DB[category])
    info = analyze.analyze(path, loop=loop)
    info.update(
        name=name,
        category=category,
        loop=loop,
        path=os.path.relpath(path, ROOT).replace("\\", "/"),
        bytes=os.path.getsize(path),
        suggested_volume=SUGGESTED_VOLUME[category],
    )
    entries.append(info)
    flag = "LOOP" if loop else "one-shot"
    print(f"  {category}/{name}.wav  {info['duration_sec']:6.2f}s "
          f"peak {info['peak_dbfs']:+.1f} rms {info['rms_dbfs']:+.1f} dBFS  {flag}")


def build_bgm(entries: list) -> None:
    print("[BGM]")
    for name, fn in BGM_BUILDERS.items():
        if name in SCHOOL_SONG_BGM:
            print(f"  BGM/{name}.wav  skip (school_song/install_game_bgm.py)")
            continue
        _emit("BGM", name, fn(), True, entries)
    for name, fn in JINGLE_BUILDERS.items():
        _emit("BGM", name, fn(), False, entries)


def build_se(entries: list) -> None:
    print("[SE]")
    for name, sig in sfx.build_steps().items():
        _emit("SE", name, sig, False, entries)
    for name, sig in sfx.build_ui_and_game().items():
        _emit("SE", name, sig, False, entries)


def build_ambient(entries: list) -> None:
    print("[Ambient]")
    for name, fn in ambient.AMBIENTS.items():
        _emit("Ambient", name, fn(), True, entries)


CATEGORIES = {"bgm": build_bgm, "se": build_se, "ambient": build_ambient}


def write_manifest(entries: list, elapsed: float) -> None:
    """生成した entries を既存 manifest にマージして書き, Assets/Audio/README.md も更新する."""
    # 既存 manifest の他カテゴリ分は残す (部分生成しても壊れないように)
    merged = {}
    if os.path.exists(MANIFEST):
        try:
            with open(MANIFEST, encoding="utf-8") as fh:
                for e in json.load(fh).get("files", []):
                    merged[e["name"]] = e
        except (OSError, ValueError, KeyError):
            merged = {}
    for e in entries:
        merged[e["name"]] = e
    files = sorted(merged.values(), key=lambda e: (e["category"], e["name"]))

    clipped = sum(e["clipped_samples"] for e in files)
    loops = [e for e in files if e.get("loop")]
    manifest = {
        "generator": "tools/audio/build_audio.py (pure numpy synthesis, no sampled material)",
        "samplerate": S.SR,
        "bit_depth": 16,
        "generated_sec": round(elapsed, 1),
        "suggested_volume": SUGGESTED_VOLUME,
        "summary": {
            "file_count": len(files),
            "total_bytes": sum(e["bytes"] for e in files),
            "total_duration_sec": round(sum(e["duration_sec"] for e in files), 2),
            "clipped_samples_total": clipped,
            "min_loop_spec_corr": round(min([e["loop_spec_corr"] for e in loops], default=1.0), 4),
            "max_seam_step_ratio": round(max([e["seam_step_ratio"] for e in loops], default=0.0), 4),
            "max_seam_hf_ratio": round(max([e["seam_hf_ratio"] for e in loops], default=0.0), 4),
        },
        "files": files,
    }
    with open(MANIFEST, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)

    s = manifest["summary"]
    print(f"\n{s['file_count']} files, {s['total_bytes'] / 1048576:.1f} MiB, "
          f"{s['total_duration_sec']:.1f}s audio in {elapsed:.1f}s")
    print(f"clipped={s['clipped_samples_total']}  "
          f"loop_spec_corr>={s['min_loop_spec_corr']}  "
          f"seam_step<={s['max_seam_step_ratio']}  seam_hf<={s['max_seam_hf_ratio']}")
    print(f"manifest -> {os.path.relpath(MANIFEST, ROOT)}")

    import make_readme
    make_readme.main()


def main(argv: list) -> int:
    wanted = [a.lower() for a in argv[1:]] or list(CATEGORIES)
    unknown = [w for w in wanted if w not in CATEGORIES]
    if unknown:
        print(f"unknown category: {', '.join(unknown)} (choose from {', '.join(CATEGORIES)})")
        return 2

    t0 = time.time()
    entries: list = []
    for key in wanted:
        CATEGORIES[key](entries)
    write_manifest(entries, time.time() - t0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
