"""各建物のプランが書き込む先。メッシュの束・Empty・プレビュー情報をまとめる。

Unity で MeshCollider を個別に付けられるよう、家具は床・壁とは別オブジェクトに
分ける。オブジェクト名は契約どおり:

    floor_<id>            床スラブ（歩行面）
    wall_<id>             外周壁・間仕切り・天井・階段など躯体
    furn_<id>_<nn>_<kind> 家具什器のグループ（nn は 2 桁連番）
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
        self.furns = []          # [(name, MeshBuilder)]
        self.empties = []        # [(name, (x, y, z))]
        self.lights = []         # [(x, y, z, energy, radius)] プレビュー専用
        self.cams = []           # [(suffix, loc, target, lens)]
        self.seats = 0           # 数えた座席数（README 用）
        self.notes = []
        self._npc = 0
        self._sign = 0

    # ---- メッシュ ----
    def furn(self, kind):
        name = "furn_%s_%02d_%s" % (self.spec.id, len(self.furns) + 1, kind)
        mb = MeshBuilder(name)
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
