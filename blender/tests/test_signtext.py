"""看板の文字（kcd_lib.signtext / kcd_lib.ttf）と同梱フォント blender/fonts/KCDSignJP-Bold.ttf。"""

import os
import struct

import pytest

from kcd_interior import signs
from kcd_lib import signtext, ttf

FONT_DIR = os.path.dirname(signtext.FONT_PATH)
OFL = os.path.join(FONT_DIR, "KCDSignJP-OFL.txt")


def _names(fnt):
    """name 表の Windows / Unicode BMP の欄 {nameID: 文字列}。"""
    data = fnt._table("name")
    count, start = struct.unpack_from(">HH", data, 2)
    out = {}
    for i in range(count):
        pid, eid, _lang, nid, length, off = struct.unpack_from(">6H", data, 6 + 12 * i)
        if (pid, eid) == (3, 1):
            out[nid] = bytes(data[start + off:start + off + length]).decode("utf-16-be")
    return out


def test_font_is_the_renamed_modified_version():
    """OFL 1.1 の改変版なので予約名 'Source' を名前に使わず、著作権とライセンスの欄は残す。"""
    names = _names(signtext.font())
    assert names[1] == "KCD Sign JP" and names[6] == "KCDSignJP-Bold"
    for nid in (1, 3, 4, 6):
        assert "Source" not in names[nid] and "Noto" not in names[nid], names[nid]
    assert "Adobe" in names[0]
    assert "Open Font License" in names[13]
    assert names[14].startswith("http")


def test_license_text_ships_with_the_font():
    with open(OFL, encoding="utf-8") as fp:
        text = fp.read()
    assert "Reserved Font Name 'Source'" in text
    assert "SIL OPEN FONT LICENSE Version 1.1" in text
    assert "KCDSignJP-Bold.ttf" in text


def test_font_stays_small():
    """第 1 水準までに絞ったサブセット（約 1.0 MB）。JIS X 0208 全部だと 2.4 MB になる。"""
    assert os.path.getsize(signtext.FONT_PATH) < 1_300_000


@pytest.mark.parametrize("ch", list("AZaz09・ーあアｱ漢総館丼①"))
def test_font_has_the_sign_characters(ch):
    assert signtext.font().has(ch)


def test_lines_of_rejects_bad_text():
    with pytest.raises(ValueError, match="空"):
        signtext.lines_of("  ", "ja")
    with pytest.raises(ValueError, match="空の行"):
        signtext.lines_of("A\n\nB", "en")
    with pytest.raises(ValueError, match="U\\+9F98"):
        signtext.lines_of("龘", "ja")   # 第 1 水準・EXTRA のどちらにも無い
    assert signtext.lines_of("第1研究棟\n館内案内", "ja") == ["第1研究棟", "館内案内"]


def test_layout_fits_inside_the_margin():
    w, h = 1.4, 0.4
    _em, chars = signtext.layout("中教室", "Classroom", w, h)
    m = signtext.margin(w, h)
    fnt = signtext.font()
    upm = fnt.units_per_em
    for ch, x, base, size in chars:
        right = x + fnt.advance(fnt.glyph_id(ch)) / upm * size
        assert -w * 0.5 + m - 1e-9 <= x and right <= w * 0.5 - m + 1e-9, ch
        assert -h * 0.5 + m - 1e-9 <= base + fnt.descender / upm * size, ch
        assert base + fnt.ascender / upm * size <= h * 0.5 - m + 0.02 * size, ch


def test_layout_refuses_text_too_small_to_read():
    with pytest.raises(ValueError, match="小さすぎる"):
        signtext.layout("関係者以外立入禁止", "Staff Only", 0.30, 0.10)


def test_layout_rejects_unknown_align():
    with pytest.raises(ValueError, match="align"):
        signtext.layout("温室", "Greenhouse", 1.2, 0.36, align="justify")


def test_snap_merges_points_across_contours():
    a = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
    b = [(1.0005, 0.0), (2.0, 0.0), (2.0, 1.0), (1.0, 1.0004)]
    out = signtext.snap([a, b], 0.002)
    assert out[1][0] == (1.0, 0.0) and out[1][3] == (1.0, 1.0)
    pts = {p for poly in out for p in poly}
    for p in pts:
        for q in pts:
            assert p == q or abs(p[0] - q[0]) + abs(p[1] - q[1]) >= 0.002


def test_snap_drops_collapsed_contours():
    tiny = [(0.0, 0.0), (0.0005, 0.0), (0.0, 0.0005)]
    assert signtext.snap([tiny], 0.002) == []


def test_fine_kanji_outline_has_no_near_duplicate_points():
    """重なりを取り除いた継ぎ目（総 など）でも、TOL / 4 より近い別の点の組が残らない。"""
    tol = signtext.TOL
    polys, _adv = signtext.outline("総", tol)
    pts = [p for poly in polys for p in poly]
    gap = tol * 0.25
    for i, p in enumerate(pts):
        for q in pts[i + 1:]:
            assert p == q or abs(p[0] - q[0]) >= gap or abs(p[1] - q[1]) >= gap


def test_text_material_follows_the_plate():
    """暗い板には白い字、明るい板には黒い字。"""
    assert signs.text_mat("sign_plate_blue") == signs.TEXT_LIGHT
    assert signs.text_mat(signs.TEXT_LIGHT) == signs.TEXT_DARK
    with pytest.raises(ValueError, match="パレットに無い"):
        signs.text_mat("no_such_plate")


def test_missing_font_is_a_clear_error(tmp_path):
    with pytest.raises((OSError, ttf.FontError)):
        ttf.Font(str(tmp_path / "none.ttf"))


def _ears(contours):
    import stubs
    out = []
    for c in contours:
        out.extend(tuple(c[i] for i in t) for t in stubs.tessellate_polygon([c]))
    return out


class _Frame:
    def p(self, dx, dy, dz):
        return (dx, dy, dz)


class _Mesh:
    def __init__(self):
        self.faces = []

    def add_face(self, pts, mat):
        self.faces.append((pts, mat))


def _placed(text, side):
    mb = _Mesh()
    n = signtext.place(mb, _Frame(), 1.2, 0.4, text, "x", face=0.01, side=side, candidates=_ears)
    assert n == len(mb.faces) > 0
    return [p for pts, _m in mb.faces for p in pts]


@pytest.mark.parametrize("side", (1, -1))
def test_text_reads_left_to_right_from_its_side(side):
    """side の側から見て、行の頭が左・字の向きも正しい（鏡文字にならない）。

    side = +1 なら +y 側から -y を見るので読み手の右は -x、side = -1 なら +x。
    """
    right = -side
    pts = _placed("LT", side)
    top = max(p[2] for p in pts)
    upper = [p for p in pts if p[2] > top - 0.02]       # 両方の字の上端だけ
    # L の上端は縦画だけ、T の上端は横画全体。左の字（L）の上端の点は右の字（T）の点より左にある
    xs = sorted(p[0] * right for p in upper)
    gap = max(range(len(xs) - 1), key=lambda i: xs[i + 1] - xs[i])
    left_group, right_group = xs[:gap + 1], xs[gap + 1:]
    assert max(left_group) - min(left_group) < max(right_group) - min(right_group)
    # 1 字だけでも鏡にならない: L の上端（縦画）は字の左寄り
    pts = _placed("L", side)
    ja = [p for p in pts if p[2] > 0.0]               # 英語の行（下）を除く
    top = max(p[2] for p in ja)
    stem = [p[0] * right for p in ja if p[2] > top - 0.02]
    every = [p[0] * right for p in ja]
    assert max(stem) < (min(every) + max(every)) * 0.5
