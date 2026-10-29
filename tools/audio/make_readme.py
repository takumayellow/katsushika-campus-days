"""manifest.json から Assets/Audio/README.md を生成する (build_audio.py が呼ぶ)."""
from __future__ import annotations

import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MANIFEST = os.path.join(ROOT, "tools", "audio", "manifest.json")
OUT = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Audio", "README.md")

# 用途の説明 (manifest には持たせず, ここで人間向けに書く)
NOTES = {
    "bgm_day": "昼のキャンパス (ゲーム内の基本). 校歌のピアノ伴奏 (bgm_evening と同じ). 吹奏楽版が直ったら差し替える (#28)",
    "bgm_evening": "夕方. 校歌のピアノ伴奏 (ヤマハ自動採譜, 歌なし). 117 BPM / F major / 1〜3 番",
    "bgm_indoor": "屋内. 校歌のピアノ伴奏を 0.92 倍のテンポにして 3.2 kHz 以上を落とし短い残響 (school_song/make_variants.py). music.py の lo-fi 版に戻すには SCHOOL_SONG_BGM から外す (#28)",
    "bgm_school_song": "タイトル画面. 東京理科大学校歌. 東北きりたん (NEUTRINO) 歌唱 1〜3 番 + ピアノ伴奏. 117 BPM / F major",
    "bgm_title": "旧タイトル曲 (未使用). 72 BPM / bgm_day のモチーフを遅く",
    "bgm_night": "夜. 校歌のピアノ伴奏を 0.8 倍のテンポにして 2.4 kHz 以上を落とす (school_song/make_variants.py). music.py の D minor 版に戻すには SCHOOL_SONG_BGM から外す (#28)",
    "bgm_result": "旧リザルト画面の曲 (未使用). 128 BPM / C major / 明るく短い",
    "bgm_anthem_original": "オリジナルの校歌風行進曲 (未使用). 108 BPM / Bb major / A-B 形式",
    "jingle_quest": "クエスト達成. F major のアルペジオ (木琴 soft_mallet + ピアノ)",
    "jingle_day_end": "1 日の終わり (リザルト画面). F major の IV-V-I (ピアノ)",
    "piano_library": "図書館大ホールのピアノを弾く (PianoPerformance). ショパンの夜想曲 作品 9-2 の冒頭の一節 (演奏 Peter Johnston, Musopen, CC0). 鳴る間 BGM は絞られる (0.15 倍)",
    "se_chime": "時報チャイム (12:00 昼休み / 17:00 下校). ウェストミンスターの鐘 E major 4 フレーズ 16 音を校内放送のスピーカー風に. 鳴る間 BGM は止まる (#38)",
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
    "amb_cafe": "カフェ (共創棟): 静かな空調 + 離れた席の小さな話し声. 食器の金属音は入れない",
    "amb_library": "図書館 (ほかの建物の既定): 空調のほぼ無音 + たまにページをめくる音",
    "amb_gym": "体育館: 静かな空調 + ときどきボールをつく音とシューズのキュッ (広い残響)",
    "amb_cafeteria": "食堂 (第 2 研究棟): 静かな空調 + あちこちの席の小さな話し声. トレイや食器の音は入れない",
    "amb_greenhouse": "温室: 小さな換気扇のうなり + ときどき葉から落ちる水滴",
}
STEP_NOTE = "足音. 4 バリエーションをランダムに再生する"


def note_for(name: str) -> str:
    if name in NOTES:
        return NOTES[name]
    if name.startswith("step_"):
        kind = {"concrete": "コンクリート", "grass": "芝生",
                "wood": "木の床", "tile": "タイル・樹脂の床",
                "carpet": "カーペット"}[name.split("_")[1]]
        return f"{kind}の{STEP_NOTE}"
    return ""


def loop_cell(e) -> str:
    """ループ素材は継ぎ目の指標. 校歌の曲は録音から作るので指標が無いが, ゲームはループで鳴らす."""
    if e.get("loop"):
        return f"{e['seam_step_ratio']:.3f} / {e['seam_hf_ratio']:.3f}"
    if e["category"] == "BGM" and not e["name"].startswith("jingle_"):
        return "末尾の無音から先頭へ"
    return "ループしない"


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
            cells.append(loop_cell(e))
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
    A("ゲーム内の音は **Python と numpy で合成**している。")
    A("例外は校歌の BGM (下の「校歌について」) と, 図書館のピアノの曲 `piano_library.wav` (CC0 の録音から切り出したもの。"
      "下の「図書館のピアノの曲について」)。")
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
    A("| `tools/audio/sfx.py` | SE 41 種 |")
    A("| `tools/audio/ambient.py` | 環境音 7 種 |")
    A("| `tools/audio/analyze.py` | 波形統計とループ継ぎ目の計測 |")
    A("| `tools/audio/build_audio.py` | 全体のエントリ。`manifest.json` と本 README を書き出す |")
    A("| `tools/audio/piano_nocturne.py` | 図書館のピアノの曲を録音から切り出す (`build_audio.py` は作らない) |")
    A("")
    A(f"出力は {man['samplerate'] // 1000}.1 kHz / {man['bit_depth']} bit の WAV。"
      f"合計 {s['file_count']} ファイル, {s['total_bytes'] / 1048576:.1f} MiB, "
      f"音の長さ {s['total_duration_sec']:.1f} 秒。生成時間は {man['generated_sec']:.0f} 秒。")
    A("")
    A("## Unity へ取り込むときの設定")
    A("")
    A("`Assets/Scripts/Editor/AudioImportRules.cs` (AssetPostprocessor) が取り込みのたびに次の表のとおりに設定し, "
      "EditMode の `AudioImportRulesTests` が確かめる。`.meta` を手で直しても次の取り込みで戻る。"
      "「曲」は `BGM/` のうち `jingle_` で始まらないもの。")
    A("")
    A("| 項目 | 既定 (Windows など) | WebGL |")
    A("|---|---|---|")
    A("| Load Type | 曲・ジングル・環境音は Streaming, SE は Decompress On Load "
      "| 曲は Compressed In Memory, ジングル・環境音・SE は Decompress On Load |")
    A("| Compression Format | Vorbis (WAV のままだと 100 MiB 超) | AAC (WebGL の既定。前のビルドと同じ) |")
    A("| Sample Rate Setting | 規則では変えない (`.meta` のまま) "
      "| 44.1 kHz に上書き (WebGL の既定と同じ。WebGL は Preserve Sample Rate を受け付けない) |")
    A("| Quality | 曲とジングル 0.7 / 環境音 0.5 / SE 1.0 | 既定と同じ (AAC で 160 / 128 / 192 kbps) |")
    A("| Preload Audio Data | SE だけ | ジングルと SE |")
    A("| Load In Background | SE 以外 | (共通) |")
    A("| Force To Mono | 使わない (1ch のクリップを 2D で鳴らすと, 左右が同じステレオより左右とも 3 dB 小さくなる。"
      "#28 で計測) | (共通) |")
    A("| Loop | 曲と環境音は `AudioSource.loop = true`, ジングルと SE は 1 回 | (共通) |")
    A("")
    A("WebGL で環境音を Decompress On Load にするのは, 圧縮のままだと Firefox と Safari が 128 KiB 以上のクリップを "
      "`<audio>` 要素で鳴らし, ループのたびに間が空くため (Chrome と Edge はどの設定でも展開して鳴らす)。"
      "ジングルと SE を先に読み込むのは, WebGL では読み終わるまで `AudioClip.length` が 0 で, "
      "ジングル・チャイム・ピアノは長さを使って BGM を絞るため。")
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
    A("校歌の 5 曲 (下の「校歌について」) は録音から作るので上の 4 つを通っておらず, `manifest.json` の `loop` も false だが, "
      "ゲームはほかの曲と同じくループで鳴らす。どれも終わりに約 1.5 秒の無音 (-60 dBFS 未満) があり, "
      "それが次の周の頭までの間になる。先頭の無音は 5〜6 ms で, 継ぎ目の段差は 0 (#28 で計測)。")
    A("")
    A("WebGL では AAC で書き出すので, 先頭に数百サンプル (多くは 448) の無音が, 末尾に 280〜883 サンプルの埋め草が入る。"
      "校歌の 5 曲は終わりの無音に紛れるが, 環境音はループのたびに短く途切れる (Chrome で約 10 ms)。"
      "AAC のままでは消せない (#28 で計測)。")
    A("")
    A(f"クリップしたサンプルは全 {s['file_count']} ファイルで {s['clipped_samples_total']} 個。")
    A("")
    for cat, title in (("BGM", "BGM"), ("SE", "SE (効果音)"), ("Ambient", "環境音 (ループ)")):
        A(f"## {title} ({len(by[cat])} ファイル)")
        A("")
        L.extend(table(by[cat], loop_cols=any(e.get("loop") for e in by[cat])))
        A("")
    A("## 屋内の環境音について")
    A("")
    A("建物の中の 5 本 (図書館・体育館・温室・カフェ・食堂) は, 屋内で風の音と「チンカン」という金属音が聞こえると"
      "指摘されたので作り直した。風のようにうねる帯域ノイズのベッドと食器の金属音 (`dish_clink`) はやめ, "
      "150 Hz より上をほとんど含まない定常な空調音 (`room_tone`) に, 部屋に合った金属でない音をまばらに置くだけにしている。"
      "書き出しのピークも `ambient.PEAK_DB` で -15〜-21 dBFS に下げ, 屋外の 2 本より RMS で 12〜28 dB 小さい。")
    A("")
    A("## 校歌について")
    A("")
    A("東京理科大学校歌 (作曲 大和憲史) を 5 つの BGM で使っている。"
      "タイトル画面の `bgm_school_song.wav` は東北きりたん (NEUTRINO) の歌唱 1〜3 番にピアノ伴奏を重ねたもの, "
      "昼の `bgm_day.wav` と夕方の `bgm_evening.wav` は歌なしのピアノ伴奏 (ヤマハの自動採譜を修正したもの)。"
      "夜の `bgm_night.wav` と屋内の `bgm_indoor.wav` も同じピアノ伴奏をテンポとフィルタで変えたもの (`school_song/make_variants.py`)。"
      "吹奏楽風アレンジ (`build_school_song.py`) は旋律・和音が公式譜とずれているので, 直すまでゲームに入れない (#28)。"
      "5 曲とも `tools/audio/school_song/install_game_bgm.py` (夜と屋内は同じフォルダの `make_variants.py`) が書き込み, "
      "`build_audio.py` は生成しない "
      "(`bgm_day`, `bgm_evening`, `bgm_night`, `bgm_indoor` は `SCHOOL_SONG_BGM` で飛ばす)。"
      "`music.py` の元の曲は `SCHOOL_SONG_BGM` から外すと戻る。"
      "作曲者の没年が確認できず保護期間の満了は立証できていないので, 公開配布の前に権利確認が要る (作詞者も同じ)。"
      "経緯と出典は `tools/audio/README_school_song.md`, ファイルの一覧は `tools/audio/school_song/README.md` を参照。"
      "`bgm_anthem_original.wav` は権利確認が取れなかった場合の差し替え用に残してある。")
    A("")
    A("時報チャイム `se_chime.wav` のウェストミンスターの鐘は 1793 年の伝承曲で, "
      "パブリックドメインであることを確認したうえで音高から合成している。"
      "音型は日本の学校のチャイムと同じ E major の 4 フレーズ 16 音 (ドミレソ / ソレミド / ミドレソ / ソレミド, ド = E) で, 移調も省略もしていない。"
      "校内放送のホーンスピーカー越しに聞こえるよう 240 Hz〜3.8 kHz に絞ってから校舎の残響を付けている。"
      "鳴らすのは 12:00 (昼休み) と 17:00 (下校) だけで, ゲーム側 (`AudioManager.PlayChime`) は鳴っている間 BGM を止め "
      "(校歌の BGM は F major なので重ねると濁る), 音量も SE の 0.45 倍で鳴らす。同時に HUD へ時刻のトーストを出す (#38)。")
    A("")
    A("## 図書館のピアノの曲について")
    A("")
    A("図書館大ホールのピアノの椅子に座ると鳴る `piano_library.wav` は, ショパンの夜想曲 変ホ長調 作品 9-2 の冒頭の一節。"
      "Musopen が公開した Peter Johnston の演奏の録音 (CC0, Wikimedia Commons) から, 弱起の B♭ から 4 小節目で E♭ に落ち着くまでを切り出し, "
      "主題を繰り返す次の弱起の手前で終わるよう末尾 2.5 秒をフェードアウトしている。"
      "取得 URL・SHA256・切り出す区間は `tools/audio/piano_nocturne.py` にあり, `manifest.json` のこの曲のエントリにも書き出す。")
    A("")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L))
    print(f"README -> {os.path.relpath(OUT, ROOT)} ({len(L)} lines)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
