# Katsushika Campus Days（かつしかキャンパスデイズ）

東京理科大学 葛飾キャンパスを OpenStreetMap の実測フットプリントから 3D 再現し、
アニメ調の女の子（と公式マスコットのオマージュ）で歩き回る三人称探索アドベンチャー。

- エンジン: Unity 6 (6000.6.2f1, URP)
- アセット生成: Blender 4.5 LTS を headless Python で駆動（手作業ゼロで再生成できる）
- 設計書: [docs/DESIGN.md](docs/DESIGN.md)

## パイプライン

```
data/osm/raw_overpass.json ─ tools/osm_extract.py ─▶ data/osm/campus.json
                                                        │
blender/build_campus.py     ◀───────────────────────────┘  ─▶ unity/.../Assets/Models/Campus/*.fbx
blender/build_characters.py                                  ─▶ unity/.../Assets/Models/Characters/<id>/
Unity -batchmode -executeMethod KCD.Editor.SceneBuilder.BuildAll   ─▶ Scenes/Campus.unity
Unity -batchmode -executeMethod KCD.Editor.BuildPlayer.BuildWindows ─▶ build/Windows/
```

## クレジット

- 地図データ © OpenStreetMap contributors (ODbL)
- 「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクター。本作は非公式・非営利のファンメイドで、独自にモデリングしている。
