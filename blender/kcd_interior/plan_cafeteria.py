"""食堂（第2研究棟の 1〜2F）: 1F 食堂 + 2F 食堂（カフェテリア）。研究棟側のロビーは plan_research2。

寸法は plan_research2_dims、躯体は plan_research2_shell、床の板張り・天井のカセット・窓の
ロールスクリーンは plan_research2_finish、1F / 2F の中身は plan_research2_1f / _2f、
看板は plan_research2_signs が持つ。ここは組み立ての順番だけを決める。

西の端の階段室の扉（北西の塊の南の壁）は 1F・2F にあり、研究棟の上の階の廊下の西の端の扉と
同じ階段室につながる。階段室の中は作らない。扉の前で E を押すと、もう一方の階や研究棟の 3F〜6F の
扉の前へ移る（Unity の InteriorStairStage）。

Empty（ほかは plan_research2_dims の表のとおり）:
    st_cafeteria_west_<階>     西の端の階段室の扉から STAIR_OUT 前の床。<階> は 1F・2F
    st_cafeteria_west_<階>_f   そこから 1 m 正面（扉を背にする）
"""

from . import furn_research2 as FR
from . import kit, shell
from . import plan_research2_1f as P1
from . import plan_research2_2f as P2
from . import plan_research2_dims as D
from . import plan_research2_finish as FIN
from . import plan_research2_shell as S
from . import plan_research2_signs as PS
from . import plan_research2_upper as U

FURN = ("hall", "serving", "seats1", "stairs", "counters2", "seats2", "terrace")
WEST_FLOORS = (("1F", D.Z1, D.C1), ("2F", D.Z2, D.C2))   # 階、床、天井
SIGN_OUT = 0.3            # 避難誘導灯を壁の面から出す距離（研究棟の上の階と同じ）
SIGN_DROP = 0.26          # 天井から避難誘導灯の上端まで（同上）


def west_stair_point():
    """西の端の階段室の扉の前 ((x, y), (dx, dy))。(dx, dy) は扉を背にした正面。"""
    return (D.WEST_STAIR_X, D.NW_Y - shell.WALL - U.STAIR_OUT), (0.0, -1.0)


def _west_stair(c):
    """西の端の階段室の閉じた扉と避難誘導灯、扉の前の Empty を 1F・2F に置く。"""
    face = D.NW_Y - shell.WALL
    (x, y), (dx, dy) = west_stair_point()
    for floor, z, ceil in WEST_FLOORS:
        U.door_boxes(c.wall, kit.T(D.WEST_STAIR_X, face, z, D.FACE_S), plate=False)
        shell.exit_sign(c.wall, D.WEST_STAIR_X, face - SIGN_OUT, ceil - SIGN_DROP, D.FACE_S)
        name = "st_%s_west_%s" % (c.spec.id, floor)
        c._put(name, x, y, z)
        c._put(name + "_f", x + dx, y + dy, z)


def _seats(c, sink):
    """座面の Empty を D.SEATS の順に並べる（家具ヘルパの記録は使わない）。"""
    for _name, mb in c.furns:
        mb.seats = []
    for x, y, z, ang in D.SEATS:
        FR.seat_mark(sink, x, y, z - D.SEAT_H, ang)


def build(c):
    S.build(c, PS.HANGS)
    FIN.build(c, PS.HANGS)
    M = {kind: c.furn(kind) for kind in FURN}
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)

    seats1 = P1.build(c, M)
    seats2, terrace = P2.build(c, M)
    c.seats += seats1 + seats2
    PS.build(c, M)
    _west_stair(c)

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    _seats(c, M["hall"])
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens)
    c.note("1F 食堂（配膳口・返却口・窓際の下がり天井）+ 2F 食堂（カフェテリア。木の天井・"
           "厨房前のカウンター・テラス）")
    c.note("客席 1F %d 席・2F %d 席（テラスの %d 席は入れないので数えない）" % (seats1, seats2, terrace))
    c.note("西の端の階段室の扉（1F・2F。研究棟の上の階の西の端の扉とつながる）")
