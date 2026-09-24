"""各建物のプランが書き込む先。メッシュの束・Empty・プレビュー情報をまとめる。

Unity で MeshCollider を個別に付けられるよう、家具は床・壁とは別オブジェクトに
分ける。オブジェクト名は契約どおり:

    floor_<id>            床スラブ（歩行面）
    wall_<id>             外周壁・間仕切り・天井・階段など躯体
    furn_<id>_<nn>_<kind> 家具什器のグループ（nn は 2 桁連番）

座れる家具（ソファ・ベンチ・ラウンジチェア）は家具ヘルパが各 MeshBuilder の
seats に記録し、flush_seats() が seat_ Empty にまとめて書き出す。
"""

import hashlib
import random

from kcd_lib.mesh import MeshBuilder


def _id_salt(building_id):
    """建物 ID から安定した整数を作る。

    組み込みの hash() は PYTHONHASHSEED でプロセスごとに変わるので、同じ --seed でも
    実行のたびに家具の配置と三角形数がブレる。hashlib なら常に同じ値になる。
    """
    digest = hashlib.sha1(building_id.encode("utf-8")).digest()
    return int.from_bytes(digest[:4], "big") % 9973


class Ctx:
    def __init__(self, spec, seed=1709):
        self.spec = spec
        self.rng = random.Random(seed + _id_salt(spec.id))
        self.floor = MeshBuilder("floor_%s" % spec.id)
        self.wall = MeshBuilder("wall_%s" % spec.id)
        self.floor.seats = []
        self.wall.seats = []
        self.furns = []          # [(name, MeshBuilder)]
        self.empties = []        # [(name, (x, y, z))]
        self.lights = []         # [(x, y, z, energy, radius)] プレビュー専用
        self.cams = []           # [(suffix, loc, target, lens)]
        self.seats = 0           # 数えた座席数（README 用）
        self.notes = []
        self._npc = 0
        self._sign = 0
        self._seats_flushed = False

    # ---- メッシュ ----
    def furn(self, kind):
        name = "furn_%s_%02d_%s" % (self.spec.id, len(self.furns) + 1, kind)
        mb = MeshBuilder(name)
        mb.seats = []            # 座れる家具（furniture._seat が積む）
        self.furns.append((name, mb))
        return mb

    def builders(self):
        return [self.floor, self.wall] + [mb for _, mb in self.furns]

    def tris(self):
        n = 0
        for mb in self.builders():
            for f in mb.faces:
                n += max(0, len(f) - 2)
        return n

    # ---- Empty ----
    def _put(self, name, x, y, z=0.0):
        self.empties.append((name, (float(x), float(y), float(z))))
        return name

    def spawn(self, x, y, z=0.0):
        return self._put("spawn_%s" % self.spec.id, x, y, z)

    def exit(self, x, y, z=0.0):
        return self._put("exit_%s" % self.spec.id, x, y, z)

    def poi(self, name, x, y, z=0.0):
        return self._put("poi_%s_%s" % (self.spec.id, name), x, y, z)

    def npc(self, x, y, z=0.0):
        self._npc += 1
        return self._put("npc_%s_%d" % (self.spec.id, self._npc), x, y, z)

    def sign(self, x, y, z):
        self._sign += 1
        return self._put("sign_%s_%d" % (self.spec.id, self._sign), x, y, z)

    # ---- 座面 ----
    def seat_list(self):
        """家具ヘルパが記録した座面を、全 MeshBuilder ぶん並べて返す。"""
        out = []
        for mb in self.builders():
            out.extend(getattr(mb, "seats", None) or ())
        return out

    def flush_seats(self):
        """座面を Empty に書き出す。plan.build() のあと 1 回だけ呼ぶ。

            seat_<id>_<nn>       家具の外形の中心（床の高さ）
            seat_<id>_<nn>_f     正面の辺の中点（中心からの向きが座ったときの正面）
            seat_<id>_<nn>_s     側面の辺の中点（中心からの距離が幅の半分）
            seat_<id>_<nn>_a<k>  座る位置（床の高さ。PlayerController.SitAt のアンカー）

        Unity の SeatFactory.PlaceInterior がこれを読み、座る操作（SeatInteractable）と
        乗り上げ防止の見えない壁を付ける。戻り値は書き出した座面の数。
        """
        if self._seats_flushed:
            return 0
        self._seats_flushed = True
        seats = self.seat_list()
        for n, seat in enumerate(seats, 1):
            name = "seat_%s_%02d" % (self.spec.id, n)
            self._put(name, *seat["c"])
            self._put(name + "_f", *seat["f"])
            self._put(name + "_s", *seat["s"])
            for k, a in enumerate(seat["anchors"]):
                self._put("%s_a%d" % (name, k), *a)
        return len(seats)

    # ---- プレビュー ----
    def light(self, x, y, z, energy=300.0, radius=1.2):
        self.lights.append((x, y, z, energy, radius))

    def lights_from(self, centers, z, energy=260.0, step=1, radius=1.4):
        for i, (cx, cy) in enumerate(centers):
            if i % step == 0:
                self.light(cx, cy, z - 0.15, energy, radius)

    def cam(self, suffix, loc, target, lens=20.0):
        self.cams.append((suffix, loc, target, lens))

    def note(self, text):
        self.notes.append(text)
