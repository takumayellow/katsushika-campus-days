"""TrueType（glyf 形式）のフォントから字の輪郭と送り幅を読む。bpy も fontTools も使わない純 Python。

屋内の看板の文字（signtext.py）が使う。読む表は head / maxp / hhea / hmtx / OS/2 / cmap / loca / glyf。
cmap は (3,10) の形式 12 か (3,1)・(0,*) の形式 4。複合グリフは読まない
（tools/make_sign_font.py が作る blender/fonts/KCDSignJP-Bold.ttf には無い）。
"""

import struct


class FontError(ValueError):
    pass


class Font:
    """1 つの .ttf。座標はフォント単位（units_per_em で割ると em）。"""

    def __init__(self, path):
        self.path = path
        with open(path, "rb") as fp:
            self.data = fp.read()
        self._dir = self._read_directory()

        head = self._table("head")
        self.units_per_em = struct.unpack_from(">H", head, 18)[0]
        self._loca_long = struct.unpack_from(">h", head, 50)[0] == 1
        self.num_glyphs = struct.unpack_from(">H", self._table("maxp"), 4)[0]

        hhea = self._table("hhea")
        n_metrics = struct.unpack_from(">H", hhea, 34)[0]
        hmtx = self._table("hmtx")
        self._adv = [struct.unpack_from(">H", hmtx, 4 * i)[0] for i in range(n_metrics)]

        os2 = self._table("OS/2")
        self.ascender, self.descender = struct.unpack_from(">hh", os2, 68)

        self._cmap = self._read_cmap()
        loca = self._table("loca")
        n = self.num_glyphs + 1
        if self._loca_long:
            self._loca = struct.unpack_from(">%dI" % n, loca, 0)
        else:
            self._loca = tuple(2 * v for v in struct.unpack_from(">%dH" % n, loca, 0))
        self._glyf_off = self._dir["glyf"][0]

    # ---- 表 ----
    def _read_directory(self):
        num = struct.unpack_from(">H", self.data, 4)[0]
        out = {}
        for i in range(num):
            tag, _check, off, length = struct.unpack_from(">4sIII", self.data, 12 + 16 * i)
            out[tag.decode("latin-1")] = (off, length)
        return out

    def _table(self, tag):
        if tag not in self._dir:
            raise FontError("%s: %s の表が無い" % (self.path, tag))
        off, length = self._dir[tag]
        return memoryview(self.data)[off:off + length]

    def _read_cmap(self):
        cmap = self._table("cmap")
        n = struct.unpack_from(">H", cmap, 2)[0]
        subs = {}
        for i in range(n):
            pid, eid, off = struct.unpack_from(">HHI", cmap, 4 + 8 * i)
            fmt = struct.unpack_from(">H", cmap, off)[0]
            subs[(pid, eid, fmt)] = off
        for key in ((3, 10, 12), (0, 4, 12), (0, 6, 12)):
            if key in subs:
                return _cmap12(cmap, subs[key])
        for key in ((3, 1, 4), (0, 3, 4), (0, 1, 4), (0, 0, 4)):
            if key in subs:
                return _cmap4(cmap, subs[key])
        raise FontError("%s: 読める cmap が無い（%s）" % (self.path, sorted(subs)))

    # ---- 字 ----
    def glyph_id(self, ch):
        """字のグリフ番号。フォントに無ければ 0。"""
        return self._cmap.get(ord(ch), 0)

    def has(self, ch):
        return self.glyph_id(ch) != 0

    def advance(self, gid):
        """送り幅（フォント単位）。"""
        return self._adv[gid] if gid < len(self._adv) else self._adv[-1]

    def contours(self, gid):
        """輪郭の並び。1 本の輪郭は [(x, y, on_curve), ...]（閉じている。最後の点から最初の点へ戻る）。"""
        start, end = self._loca[gid], self._loca[gid + 1]
        if end <= start:
            return []
        d = self.data
        off = self._glyf_off + start
        n_contours = struct.unpack_from(">h", d, off)[0]
        if n_contours < 0:
            raise FontError("%s: グリフ %d は複合グリフ（tools/make_sign_font.py で作り直す）" % (self.path, gid))
        p = off + 10
        ends = struct.unpack_from(">%dH" % n_contours, d, p)
        p += 2 * n_contours
        n_inst = struct.unpack_from(">H", d, p)[0]
        p += 2 + n_inst
        n_pts = ends[-1] + 1 if ends else 0

        flags = []
        while len(flags) < n_pts:
            f = d[p]
            p += 1
            flags.append(f)
            if f & 0x08:
                r = d[p]
                p += 1
                flags.extend([f] * r)
        flags = flags[:n_pts]

        xs = []
        v = 0
        for f in flags:
            if f & 0x02:
                dv = d[p]
                p += 1
                v += dv if f & 0x10 else -dv
            elif not f & 0x10:
                v += struct.unpack_from(">h", d, p)[0]
                p += 2
            xs.append(v)
        ys = []
        v = 0
        for f in flags:
            if f & 0x04:
                dv = d[p]
                p += 1
                v += dv if f & 0x20 else -dv
            elif not f & 0x20:
                v += struct.unpack_from(">h", d, p)[0]
                p += 2
            ys.append(v)

        out = []
        s = 0
        for e in ends:
            out.append([(xs[i], ys[i], bool(flags[i] & 0x01)) for i in range(s, e + 1)])
            s = e + 1
        return out


def _cmap4(cmap, off):
    seg2 = struct.unpack_from(">H", cmap, off + 6)[0]
    seg = seg2 // 2
    ends = struct.unpack_from(">%dH" % seg, cmap, off + 14)
    starts = struct.unpack_from(">%dH" % seg, cmap, off + 16 + seg2)
    deltas = struct.unpack_from(">%dh" % seg, cmap, off + 16 + 2 * seg2)
    ro_base = off + 16 + 3 * seg2
    ranges = struct.unpack_from(">%dH" % seg, cmap, ro_base)
    out = {}
    for i in range(seg):
        s, e, dl, ro = starts[i], ends[i], deltas[i], ranges[i]
        if s == 0xFFFF:
            continue
        for c in range(s, e + 1):
            if ro == 0:
                g = (c + dl) & 0xFFFF
            else:
                addr = ro_base + 2 * i + ro + 2 * (c - s)
                g = struct.unpack_from(">H", cmap, addr)[0]
                if g:
                    g = (g + dl) & 0xFFFF
            if g:
                out[c] = g
    return out


def _cmap12(cmap, off):
    n = struct.unpack_from(">I", cmap, off + 12)[0]
    out = {}
    for i in range(n):
        s, e, g = struct.unpack_from(">III", cmap, off + 16 + 12 * i)
        if e < s or e > 0x10FFFF:
            raise FontError("cmap 12 の範囲が壊れている: U+%X..U+%X" % (s, e))
        for c in range(s, e + 1):
            out[c] = g + (c - s)
    return out
