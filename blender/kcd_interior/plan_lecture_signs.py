"""講義棟の看板 36 枚（plan_lecture_dims.SIGNS の順。Empty の番号は呼んだ順に振られる）。

文言・位置・板の色は SIGNS の表が持つ。看板の裏に当たる壁・天井は各区画のモジュールが作る
（吊り看板の吊り元の真上には、天井の面が z = CEIL_CORR に要る）。
レジの札 4 枚（opts の "register"）は、生協のモジュールが決めたレジの位置に置く。
"""

from . import signs
from . import plan_lecture_dims as D

REGISTERS = 4


def _place(c, mb, row, at=None):
    kind, ja, en, w, h, x, y, z, ang, plate, opts = row
    kw = {k: v for k, v in opts.items() if k != "register"}
    if at is not None:
        x, y = at
    put = signs.wall if kind == "wall" else signs.hanging
    return put(c, mb, x, y, z, ang, ja=ja, en=en, w=w, h=h, plate=plate, **kw)


def build(c, mb, registers):
    """mb に看板を全部置く。registers はレジの札の板の中心 (x, y) を 4 つ（表の 1〜4 の順）。"""
    regs = tuple(registers)
    if len(regs) != REGISTERS:
        raise ValueError("レジの札の位置は %d 個（渡されたのは %d 個）" % (REGISTERS, len(regs)))
    for row in D.SIGNS:
        opts = row[10]
        _place(c, mb, row, regs[opts["register"]] if "register" in opts else None)


def hang_roots():
    """吊り看板の吊り元 (x, y, z)。天井の照明をこの真上に置かない・天井の面を張るのに使う。"""
    return tuple((r[5], r[6], r[7]) for r in D.SIGNS if r[0] == "hang")
