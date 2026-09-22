"""投稿動画 (school_song_kiritan.mp4) を作る.

    python make_video.py

youtube-pipeline の make_video_v2 (integrations/kiritan_singing/scripts/make_video_v2.py) で作る.
楽譜 school_song_kiritan_piano.musicxml を小節の時刻に合わせてスクロールさせ (--sync-scroll),
歌詞のテロップを入れ, 音は ../audio/school_song_kiritan_mix.mp3 を載せる.
MuseScore.com の動画ダウンロードは無くなったので, こちらで作る.

make_video_v2 のうち, 次の 3 つだけ外から差し替える (youtube-pipeline のファイルは変えない).
- 楽譜と出力の置き場所: このフォルダと build/
- 立ち絵: 制服のきりたん (make_thumbnail.py と同じ)
- サムネ: make_thumbnail.py で作った thumbnail.png を使う. make_video_v2 のサムネは作らない

楽譜の時刻とミックスの時刻のずれは, 曲の頭から終わりまで 0.3 s 以内 (MuseScore の音との onset の相関で確認).
"""
from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
YP = Path(os.path.expanduser("~/dev/youtube-pipeline"))
SLUG = "school_song_kiritan_piano"
MIX = HERE.parent / "audio" / "school_song_kiritan_mix.mp3"
BUILD = HERE / "build"
OUT = HERE / "school_song_kiritan.mp4"

sys.path.insert(0, str(HERE))
import make_thumbnail  # noqa: E402

sys.path.insert(0, str(YP / "integrations" / "kiritan_singing" / "scripts"))
import make_video_v2 as mv  # noqa: E402


def main() -> int:
    BUILD.mkdir(exist_ok=True)
    mv.SCORES = HERE
    mv.OUT = BUILD
    mv.CACHE = BUILD / "cache"
    mv.KIRITAN_PNG = Path(make_thumbnail.KIRITAN)
    mv.make_thumbnail_v2 = lambda *a, **k: None

    mp4 = mv.make_video_v2(SLUG, "東京理科大学 校歌", with_lyrics=True, sync_scroll=True,
                           visual_mode="score-scroll", audio=MIX)
    shutil.move(str(mp4), OUT)
    print(OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
