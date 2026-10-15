"""図書館のピアノの曲 piano_library.wav (ショパンの夜想曲 作品 9-2 の冒頭).

録音の取得と ffmpeg での変換はしない。切り出しの設定と, commit 済みの wav・manifest.json の
エントリ・build_audio.py が食い違っていないかを見る。
"""

import ast
import inspect
import json
import os
import wave

import numpy as np
import pytest

import build_audio
import piano_nocturne as P
import synth

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PIANO_WAV = os.path.join(REPO, "unity", "KatsushikaCampusDays", "Assets", "Audio", "SE", "piano_library.wav")
MANIFEST = os.path.join(REPO, "tools", "audio", "manifest.json")


def _entry():
    with open(MANIFEST, encoding="utf-8") as fh:
        files = json.load(fh)["files"]
    found = [e for e in files if e["name"] == P.NAME]
    assert len(found) == 1
    return found[0]


def test_cut_is_one_phrase_with_fades():
    assert 0.0 <= P.START_SEC < P.END_SEC
    assert 20.0 <= P.END_SEC - P.START_SEC <= 50.0
    assert 2.0 <= P.FADE_OUT_SEC <= 4.0
    assert 0.0 < P.FADE_IN_SEC <= 0.1


def test_committed_wav_matches_the_cut():
    with wave.open(PIANO_WAV, "rb") as w:
        assert w.getframerate() == synth.SR
        assert w.getnchannels() == 2
        assert w.getsampwidth() == 2
        assert w.getnframes() == P.n_frames()


def test_manifest_entry_records_the_source():
    e = _entry()
    assert e["category"] == "SE"
    assert e["loop"] is False
    assert e["duration_sec"] == pytest.approx(P.n_frames() / synth.SR, abs=1e-3)
    assert e["bytes"] == os.path.getsize(PIANO_WAV)
    assert e["license"] == P.LICENSE == "CC0 1.0"
    assert e["source_url"] == P.SOURCE_URL
    assert e["source_sha256"] == P.SOURCE_SHA256
    assert e["source_range_sec"] == [P.START_SEC, P.END_SEC]
    assert e["fade_sec"] == [P.FADE_IN_SEC, P.FADE_OUT_SEC]


def test_build_audio_does_not_overwrite_the_recording():
    """build_audio.py se を回しても piano_library.wav を合成版で上書きしない。"""
    tree = ast.parse(inspect.getsource(build_audio))
    emitted = [
        arg.value
        for node in ast.walk(tree)
        if isinstance(node, ast.Call) and getattr(node.func, "id", None) == "_emit"
        for arg in node.args
        if isinstance(arg, ast.Constant)
    ]
    assert "SE" in emitted, "_emit の呼び方が変わったらこのテストも直す"
    assert P.NAME not in emitted


def test_verify_rejects_a_different_file(tmp_path):
    other = tmp_path / "other.ogg"
    other.write_bytes(b"not the recording")
    with pytest.raises(ValueError):
        P.verify(str(other))


def test_committed_wav_fades_out_to_silence():
    """末尾 50 ms は -60 dBFS 未満, 全体のピークは -3 dBFS より大きい。"""
    with wave.open(PIANO_WAV, "rb") as w:
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
    tail = x[-int(0.05 * synth.SR) * 2:]
    assert np.max(np.abs(tail)) < 10 ** (-60 / 20)
    assert np.max(np.abs(x)) > 10 ** (-3 / 20)
