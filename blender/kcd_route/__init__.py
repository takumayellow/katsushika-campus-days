"""裏エンド「寮でぐーたら」(#41) の回廊 — 北西門から葛飾コミュニティハウスまでの屋外。

  ground.py  route.json の読み取りと選別、西へ足す地面の帯、寸法の assert
  roads.py   道路（route.json の roads のうち campus.json と重ならないもの）
  props.py   沿道の建物・ブロック塀・ガードレール・門柱

これらを使う入口は blender/build_route.py。寮そのもの（外観と屋内）は別の担当。
"""
