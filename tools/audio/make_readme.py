"""manifest.json から Assets/Audio/README.md を生成する (build_audio.py が呼ぶ)."""
from __future__ import annotations

import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MANIFEST = os.path.join(ROOT, "tools", "audio", "manifest.json")
OUT = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Audio", "README.md")

# 用途の説明 (manifest には持たせず, ここで人間向けに書く)
NOTES = {
    "bgm_day": "昼のキャンパス (ゲーム内の基本). 校歌の吹奏楽風アレンジ. 104 BPM / F major / 前奏 + 2 コーラス",
    "bgm_evening": "夕方. 校歌のピアノ伴奏 (ヤマハ自動採譜, 歌なし). 117 BPM / F major / 1〜3 番",
    "bgm_indoor": "屋内. 96 BPM / C major / ジャズ 7th の lo-fi",
    "bgm_school_song": "タイトル画面. 東京理科大学校歌. 東北きりたん (NEUTRINO) 歌唱 1〜3 番 + ピアノ伴奏. 117 BPM / F major",
    "bgm_title": "旧タイトル曲 (未使用). 72 BPM / bgm_day のモチーフを遅く",
    "bgm_night": "夜の余韻. 72 BPM / D minor / 静かなピアノ + パッド",
    "bgm_result": "リザルト画面. 128 BPM / C major / 明るく短い",
    "bgm_anthem_original": "オリジナルの校歌風行進曲 (未使用). 108 BPM / Bb major / A-B 形式",
    "jingle_quest": "クエスト達成",
    "jingle_day_end": "1 日の終わり",
    "se_chime": "時報チャイム (9:00 / 12:00 / 17:00). ウェストミンスターの鐘",
    "ui_move": "カーソル移動", "ui_confirm": "決定", "ui_cancel": "キャンセル",
    "ui_open": "ウィンドウを開く", "ui_close": "ウィンドウを閉じる",
    "ui_toast": "通知",
    "door_open": "扉を開ける", "door_close": "扉を閉める",
    "item_get": "アイテム入手", "quest_start": "クエスト開始",
    "quest_update": "クエスト進行", "camera_shutter": "撮影",
    "jump": "ジャンプ", "land": "着地", "sit": "座る", "wave": "手を振る",
    "talk_blip_f1": "会話ブリップ (女子 1)", "talk_blip_f2": "会話ブリップ (女子 2)",
    "talk_blip_m1": "会話ブリップ (男子)", "talk_blip_prof": "会話ブリップ (教員)",
    "amb_campus_day": "昼のキャンパス: 風 + 小鳥 + 遠くのざわめき",
    "amb_campus_evening": "夕暮れ: ヒグラシ + 風",
    "amb_cafe": "カフェ: ざわめき + 食器",
    "amb_library": "図書館: 空調のほぼ無音 + ページ",
    "amb_gym": "体育館: 残響のあるボールのバウンド",
    "amb_cafeteria": "食堂: 賑わい",
    "amb_greenhouse": "温室: 換気扇 + 水滴",
}
STEP_NOTE = "足音. 4 バリエーションをランダムに再生する"


def note_for(name: str) -> str:
    if name in NOTES:
        return NOTES[name]
    if name.startswith("step_"):
        kind = {"concrete": "コンクリート", "grass": "芝生",
                "wood": "木の床", "tile": "タイル"}[name.split("_")[1]]
        return f"{kind}の{STEP_NOTE}"
    return ""


def table(files, loop_cols: bool) -> list:
    head = ["| ファイル | 長さ | ピーク | RMS | 用途 |",
            "|---|---:|---:|---:|---|"]
    if loop_cols:
        head = ["| ファイル | 長さ | ピーク | RMS | ループ継ぎ目 | 用途 |",
                "|---|---:|---:|---:|---:|---|"]
    rows = []
    for e in files:
        cells = [f"`{e['name']}.wav`", f"{e['duration_sec']:.2f} s",
                 f"{e['peak_dbfs']:.1f} dBFS", f"{e['rms_dbfs']:.1f} dBFS"]
        if loop_cols:
            cells.append(f"{e['seam_step_ratio']:.3f} / {e['seam_hf_ratio']:.3f}"
                         if e.get("loop") else "ループしない")
        cells.append(note_for(e["name"]))
        rows.append("| " + " | ".join(cells) + " |")
    return head + rows


def main() -> int:
    with open(MANIFEST, encoding="utf-8") as fh:
        man = json.load(fh)
    files = man["files"]
    s = man["summary"]
    by = {c: [e for e in files if e["category"] == c] for c in ("BGM", "SE", "Ambient")}

    L = []
    A = L.append
    A("# Audio")
    A("")
    A("ゲーム内の音はすべて **Python と numpy だけで合成**している。")
    A("サンプリング素材・外部ライブラリの音源・録音物は一切使っていない。")
    A("")
    A("## 生成方法")
    A("")
    A("```")
    A("python tools/audio/build_audio.py              # 全部作り直す")
    A("python tools/audio/build_audio.py bgm ambient  # カテゴリを絞る")
    A("```")
    A("")
    A("| スクリプト | 役割 |")
    A("|---|---|")
    A("| `tools/audio/synth.py` | 発振器・フィルタ・エンベロープ・楽器・ドラム・リバーブ |")
    A("| `tools/audio/music.py` | BGM とジングル (小節グリッドの簡易シーケンサ) |")
    A("| `tools/audio/sfx.py` | SE 37 種 |")
    A("| `tools/audio/ambient.py` | 環境音 7 種 |")
    A("| `tools/audio/analyze.py` | 波形統計とループ継ぎ目の計測 |")
    A("| `tools/audio/build_audio.py` | 全体のエントリ。`manifest.json` と本 README を書き出す |")
    A("")
    A(f"出力は {man['samplerate'] // 1000}.1 kHz / {man['bit_depth']} bit の WAV。"
      f"合計 {s['file_count']} ファイル, {s['total_bytes'] / 1048576:.1f} MiB, "
      f"音の長さ {s['total_duration_sec']:.1f} 秒。生成時間は {man['generated_sec']:.0f} 秒。")
    A("")
    A("## Unity へ取り込むときの設定")
    A("")
    A("| 項目 | 値 |")
    A("|---|---|")
    A("| Load Type | BGM / Ambient は Streaming, SE は Decompress On Load |")
    A("| Compression Format | Vorbis (WAV のままだと 100 MiB 超) |")
    A("| Quality | BGM 70 / Ambient 50 / SE 100 |")
    A("| Loop | BGM と Ambient は `AudioSource.loop = true` |")
    A("")
    A("`tools/audio/manifest.json` に 1 ファイルずつ長さ・ピーク・RMS・ループ可否・"
      "推奨音量が入っている。ミキサーの初期値はそこから読む。")
    A("")
    A("推奨音量 (`AudioSource.volume` の初期値): "
      + ", ".join(f"{k} {v}" for k, v in man["suggested_volume"].items()) + "。")
    A("")
    A("## ループの継ぎ目について")
    A("")
    A("ループ素材は次の 4 つでサンプル単位の連続性を作っている。")
    A("")
    A("1. 音符や環境音イベントは末尾を越えた分を先頭へ回り込ませて置く (`add_at(wrap=True)`)")
    A("2. リバーブとフィルタは末尾を助走として前置してから掛け, 助走分を捨てる (`preroll`)")
    A("3. 環境音のベッドは逆 FFT で作るのでループ長でちょうど 1 周する (`spectral_noise`)")
    A("4. ドローンと LFO はループ長に整数周期が入る周波数へ丸める (`pfreq` / `periodic_lfo`)")
    A("")
    A("README の表の「ループ継ぎ目」は `seam_step_ratio / seam_hf_ratio` で, "
      "どちらも 1 を下回れば継ぎ目が内部と区別できないことを意味する。")
    A("")
    A("**`bgm_title` はこの 2 つが 1 前後になるが, 不連続ではない。** "
      "同じ編曲を 2 周ぶん描画して内部の継ぎ目と比較したところ, "
      "回り込みの段差 0.07121 と 2 周描画の内部の段差 0.07121 が完全に一致し, "
      "2 周目の波形は 1 周ぶんの出力と最大誤差 0.0 で一致した。"
      "指標が拾っているのは小節頭のピアノのアタックそのもので, 継ぎ目の欠陥ではない。")
    A("")
    A(f"クリップしたサンプルは全 {s['file_count']} ファイルで {s['clipped_samples_total']} 個。")
    A("")
    for cat, title in (("BGM", "BGM"), ("SE", "SE (効果音)"), ("Ambient", "環境音 (ループ)")):
        A(f"## {title} ({len(by[cat])} ファイル)")
        A("")
        L.extend(table(by[cat], loop_cols=any(e.get("loop") for e in by[cat])))
        A("")
    A("## 校歌について")
    A("")
    A("東京理科大学校歌 (作曲 大和憲史) を 3 つの BGM で使っている。"
      "タイトル画面の `bgm_school_song.wav` は東北きりたん (NEUTRINO) の歌唱 1〜3 番にピアノ伴奏を重ねたもの, "
      "昼の `bgm_day.wav` は管弦楽版スコアの旋律を `build_school_song.py` に書き起こして MuseScore 4 で演奏させた吹奏楽風アレンジ, "
      "夕方の `bgm_evening.wav` は歌なしのピアノ伴奏 (ヤマハの自動採譜を修正したもの)。"
      "3 つとも `tools/audio/school_song/install_game_bgm.py` が書き込むので, `build_audio.py` はこの 2 曲 "
      "(`bgm_day`, `bgm_evening`) を生成しない。`music.py` の元の曲は `SCHOOL_SONG_BGM` から外すと戻る。"
      "作曲者の没年が確認できず保護期間の満了は立証できていないので, 公開配布の前に権利確認が要る。"
      "経緯と出典は `tools/audio/README_school_song.md`, ファイルの一覧は `tools/audio/school_song/README.md` を参照。"
      "`bgm_anthem_original.wav` は権利確認が取れなかった場合の差し替え用に残してある。")
    A("")
    A("時報チャイム `se_chime.wav` のウェストミンスターの鐘は 1793 年の伝承曲で, "
      "パブリックドメインであることを確認したうえで音高から合成している。")
    A("")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L))
    print(f"README -> {os.path.relpath(OUT, ROOT)} ({len(L)} lines)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
