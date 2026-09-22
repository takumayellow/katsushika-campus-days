"""投稿動画 (school_song_kiritan.mp4) を YouTube に上げる.

    python upload_youtube.py [--privacy unlisted|public|private] [--dry-run]

youtube-pipeline の integrations/kiritan_singing/scripts/upload.py と同じ関数 (yp.core.youtube.upload) で上げる.
upload.py の説明文は立ち絵を公式素材, 譜面を NEUTRINO 同梱と決め打ちしているので, 今回の素材に合わせてここで書く.
認証は youtube-pipeline の既定 (credentials/client_secret.json と youtube_token.json, upload.py と同じチャンネル) を使う.
"""
from __future__ import annotations

import argparse
import io
import os
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
YP = Path(os.path.expanduser("~/dev/youtube-pipeline"))
MP4 = HERE / "school_song_kiritan.mp4"
THUMB = HERE / "thumbnail.png"

TITLE = "【歌ってみた】東京理科大学 校歌 / 東北きりたん (NEUTRINO)"
DESCRIPTION = """\
【歌ってみた】東京理科大学 校歌 (1〜3 番) — 東北きりたん (NEUTRINO)

作詞: 佐治巌
作曲: 大和憲史
歌唱: 東北きりたん (NEUTRINO Singer Character Library)
音声合成: NEUTRINO https://studio-neutrino.com/
伴奏: ピアノ (Yamaha の自動採譜を手で直したもの)
楽譜表示: MuseScore

立ち絵: A・Loveる さん「きりたん立ち絵素材」(制服差分)
東北きりたん: 東北ずん子・ずんだもんプロジェクト (SSS LLC.) https://zunko.jp/

東北きりたん公式ガイドライン (https://zunko.jp/guideline.html) と立ち絵素材の利用規約に沿った非商用の個人利用です。
東京理科大学の公式の動画ではありません。

#歌ってみた #東北きりたん #NEUTRINO #東京理科大学 #校歌
"""
TAGS = ["歌ってみた", "東北きりたん", "NEUTRINO", "東京理科大学", "理科大", "校歌", "楽譜"]


def main(argv: list) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--privacy", choices=["unlisted", "public", "private"], default="unlisted")
    ap.add_argument("--dry-run", action="store_true", help="上げずにタイトルと説明文だけ出す")
    args = ap.parse_args(argv[1:])

    print(f"{TITLE} (privacy={args.privacy})\n\n{DESCRIPTION}\ntags: {TAGS}")
    if args.dry_run:
        return 0
    if not MP4.exists():
        raise SystemExit(f"{MP4} が無い. 先に replace_audio.py で作る")

    os.environ.setdefault("YT_CREDENTIALS_DIR", str(YP / "credentials"))
    sys.path.insert(0, str(YP / "src"))
    from yp.core.youtube import upload  # type: ignore

    video_id = upload(
        video_path=MP4, title=TITLE, description=DESCRIPTION, privacy=args.privacy,
        thumbnail=THUMB if THUMB.exists() else None, tags=TAGS,
        playlist_query="歌ってみた", create_playlist_title="歌ってみた - 東北きりたん",
    )
    print(f"https://www.youtube.com/watch?v={video_id}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
