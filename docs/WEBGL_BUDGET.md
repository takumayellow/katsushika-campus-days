# WebGL の実測と予算

Web 版（Unity WebGL を GitHub Pages で配信）の実測、予算、画質の段（Low / Medium / High）の設定をまとめる。
Windows 版のフレームの刻み（#15）もここに書く。元の記録は #70 と #15 のコメント。

## 1. 実測

### 1.1 測った条件

- PC 1 台（RTX 5070 Ti）、Edge 153、60 Hz の画面。どの値もこの 1 台だけで測った。内蔵 GPU や遅い CPU では測っていない。
- Playwright で Edge を開き、ビューポートは 1280×900。1.2〜1.4 はウィンドウありで、1.7 は headless（実 GPU、ANGLE の D3D11）で測った。1.7 のキャンバスは 1136×710。
- 「上限なし」は Edge を `--disable-gpu-vsync --disable-frame-rate-limit` で起動したとき。それ以外は 60 fps の上限あり（通常の設定）。
- 描画コールと三角形: WebGL の draw* 呼び出しをフックして、1 フレームごとに数えた。
- fps: requestAnimationFrame の間隔から出した。
- 1% low: 間隔の遅い 1% の平均から出した fps。
- 1 フレームの処理: requestAnimationFrame のコールバックにかかった時間。
- 場面: キャンパスの同じ地点に立つ（立ち）場合と、同じ道を歩く（歩き）場合。どちらも 10 秒前後。

### 1.2 木のまとめの効果（#70、ローカルのビルド、上限なし）

| 版 | 描画コール（立ち / 歩き） | 三角形（立ち） | fps（立ち / 歩き） | 1% low（立ち / 歩き） | 1 フレームの処理（立ち） |
|---|---|---|---|---|---|
| 木なし | 459 / 467 | 469k | 313.9 / 295.4 | 177.9 / 190.4 | 3.03 ms |
| 木 553 本を 1 本ずつ | 4,700 / 4,328 | 665k | 44.6 / 47.3 | 27.6 / 28.0 | 22.09 ms |
| 木を 128 m のマスごとにまとめた | 563 / 564 | 718k | 314.2 / 260.0 | 195.5 / 171.5 | 3.04 ms |

- まとめた版を 60 fps 上限で測ると、立ちは 59.95 fps（1% low 59.11）、歩きは 59.95 fps（1% low 59.17）だった。
- 木なしと 1 本ずつを比べると、描画コール 1 回あたりの処理は (22.09 − 3.03) ms ÷ (4,700 − 459) 回 = 4.49 µs になる。
  三角形の増えた分もこの差に含まれるので、コール 1 回の費用としては多めに見た値。
- 上限なしの fps は、三角形が 469k から 718k に増えても 313.9 から 314.2 で変わらなかった。この PC では GPU は律速になっていない。

### 1.3 公開版（PR #81、main 01437f9、60 fps 上限）

| 場面 | 描画コール（平均 / 最大） | 三角形 | fps | 1% low | 1 フレームの処理（平均 / p99） |
|---|---|---|---|---|---|
| 立ち | 573 / 573 | 742k | 59.9 | 59.2 | 4.02 / 5.90 ms |
| 歩き | 565 / 580 | 720k | 59.9 | 59.3 | 4.72 / 6.60 ms |

### 1.4 木を植える前の公開版（#15、60 fps 上限）

- キャンパスの初期位置: 459 draw、468,783 ポリゴン。
  - Unity の CPU は 5.64 ms（p99 8.0）。
  - GPU は 5.35 ms（p99 8.29）。
- モールを歩いたとき: 449〜484 draw。
- 上限なしでは、フレーム時間の中央値が 3.7〜4.0 ms だった。これはほぼ Unity のメインスレッドの時間で、律速は CPU（GL を呼ぶ側）。
- キャラ選択からキャンパスへ移るとき 750〜848 ms 止まった。このとき WASM のヒープが 1 回、256 から 307 MiB に拡張した。
- 歩いているときに 50〜65 ms の GPU スパイクが出た。原因はまだ分かっていない。
- data（71.5 MB）の 52.8% はメッシュ。フォントの TTF は 9.1 MB ある。

### 1.5 配信サイズ（2026-09-24、Release web-latest の webgl.zip を展開して測った値）

| ファイル | 大きさ | gzip を解いた大きさ |
|---|---|---|
| Build/WebGL.data.unityweb | 55,476,397 B（52.91 MiB） | 72,399,237 B（69.05 MiB） |
| Build/WebGL.wasm.unityweb | 10,855,158 B（10.35 MiB） | 40,976,685 B（39.08 MiB） |
| Build/WebGL.framework.js.unityweb | 75,003 B | 331,354 B |
| Build/WebGL.loader.js | 48,779 B | （gzip なし） |
| index.html | 8,087 B | （gzip なし） |
| TemplateData/style.css | 4,855 B | （gzip なし） |
| 合計 6 ファイル | 66,468,279 B（63.39 MiB） | |

- 配信の設定は gzip（webGLCompressionFormat 1）で、Data Caching が有効。2 回目からの読み込みでは、data をブラウザの IndexedDB / Cache API から読む。

### 1.6 テクスチャを ASTC で圧縮した data（#187、2026-10-12、ローカルのビルド）

`deploy_pages.py --build` は、テクスチャを DXT で圧縮したビルドと ASTC で圧縮したビルドを作り、ASTC の data を同じ Build/ に置く。
wasm・framework・loader は 2 本でハッシュまで同じだった。

| ファイル | 大きさ | gzip を解いた大きさ |
|---|---|---|
| data（DXT） | 58,793,471 B（56.07 MiB） | 78,290,087 B（74.66 MiB） |
| data（ASTC） | 63,216,436 B（60.29 MiB） | 82,939,829 B（79.10 MiB） |
| wasm | 10,963,928 B（10.46 MiB） | |
| framework・loader・index.html・style.css・ライセンス 2 つ | 157,974 B | |
| 1 台の端末が読む分（ASTC の data のとき） | 70.9 MiB | |
| zip 全体（data 2 つ） | 127.0 MiB | |

- data の中で違うのは data.unity3d（gzip を解いて 40,902,148 B と 45,551,890 B）と boot.config の build-guid だけ。
  ほかのファイル（global-metadata.dat、sharedassets0.resource など）はバイトまで同じ。
- ビルドレポートのテクスチャは DXT で 33.3 MB、ASTC で 35.5 MB。ASTC のブロックは 6×6。
- この PC の GPU（RTX 5070 Ti、ANGLE の D3D11）は ASTC を読めない。`?textures=astc` で開くと ASTC の data を読み、
  Unity がテクスチャを展開する（コンソールに `RGBA Compressed ASTC6X6 ... not supported, decompressing texture`）。
  smoke で比べると、展開の分だけ重くなる。スマホの GPU で DXT の data を読むときも、同じように展開する。

  | | DXT の data | ASTC の data（展開する） |
  |---|---|---|
  | キャンパスでの Unity のヒープ | 368.8 MiB | 442.5 MiB |
  | Campus のシーンの読み込み（scene_load） | 827 ms | 1274 ms |
  | コンソールのエラー | 0 件 | 0 件 |
- Unity 6（6000.6.2f1）では、`EditorUserBuildSettings.webGLBuildSubtarget` を変えてもビルドの形式は変わらない。
  `BuildPlayerOptions.subtarget` で渡す（`BuildPlayer.BuildWebGLVariant`）。

### 1.7 画質の段ごと（#70、2026-10-12、dev/game 98f02b3、ローカルのビルド、60 fps 上限）

`python e2e/run_webgl_perf.py --serve build/WebGL` で測った（測り方は 5.4）。段ごとに新しいプロファイルで開き、タイトルの設定画面で段を変えてから入った。

| 段 | 場面 | fps | 1% low | 1 フレームの処理（平均 / p99） | 描画コール（平均 / 最大） | 三角形（平均 / 最大） |
|---|---|---|---|---|---|---|
| Low | 立ち | 59.9 | 59.5 | 3.57 / 6.00 ms | 623 / 673 | 984k / 1,068k |
| Low | 歩き | 59.9 | 59.3 | 3.61 / 5.10 ms | 660 / 707 | 1,132k / 1,258k |
| Medium | 立ち | 59.9 | 59.0 | 4.16 / 6.40 ms | 636 / 684 | 1,118k / 1,204k |
| Medium | 歩き | 59.9 | 59.2 | 3.90 / 5.90 ms | 675 / 701 | 1,178k / 1,216k |
| High | 立ち | 59.9 | 59.4 | 3.73 / 5.50 ms | 819 / 914 | 1,465k / 1,614k |
| High | 歩き | 59.9 | 59.5 | 4.25 / 6.80 ms | 975 / 1,021 | 1,750k / 1,855k |

立ちと歩きは NPC と時計が動くので、回ごとに描く物が少し変わる。段の差だけを見るため、1 つのセッションでキャンパスに入ってポーズし（時間が止まる）、同じ絵のままポーズの設定画面で段を Low → Medium → High と変えて 3 秒ずつ測った。これを 2 回くり返し、描画コールと三角形は 2 回とも同じだった。

| 段 | 描画コール | 三角形 | Medium との差 |
|---|---|---|---|
| Low | 579 | 908k | −50 回、−198k |
| Medium | 629 | 1,106k | 0 |
| High | 831 | 1,482k | +202 回、+376k |

- どの段も 60 fps の上限に張り付き、1 フレームの処理は平均 3.0〜4.3 ms、p99 は 6.8 ms 以下だった。この PC では段で fps は変わらない。
- 同じビルドを上限なし（ウィンドウあり、キャンバス 1136×710）で 2 回ずつ測ると、fps は Low 221〜274、Medium 203〜249、High 228〜258 で、段の差は回ごとのばらつきの中に入った。この PC は GPU ではなく CPU（1 フレームの処理）で律速している。
- キャンパスに入った直後の Unity のヒープは、どの段も 368.8 MiB だった。
- 段が変わったことは、設定画面の「画質」の行（低 / 中 / 高）を撮った画像で確かめた。ブラウザのコンソールに出る `[KCD] graphics tier=` は起動したときの段（Medium）で、設定画面で変えたあとには出ない。

## 2. 予算

超えそうな変更をするときは、同じ Issue で測り直し、この表を更新する。

予算の値は公開版（1.3、描画コール最大 580、三角形 742k）をもとに決めた。そのあとの変更で、既定の Medium の三角形は予算を超え、描画コールは予算に並んだ（1.7）。High は両方とも超える。予算を上げるか描く量を減らすかは #70 で決める。

| 項目 | 予算 | 今 | 決め方 |
|---|---|---|---|
| 描画コール（キャンパス、1 フレーム） | 700 | Medium で最大 701、High で最大 1,021（1.7、歩き） | 今の最大に約 20% の幅を足した。1.2 の傾きで見積もると 120 回で +0.54 ms、CPU が 4 倍遅い端末では +2.2 ms。今の 4.0〜4.7 ms も 4 倍にすると 16〜19 ms で、60 fps の 16.7 ms を割るおそれがある。そのため、増やす余裕は小さく取った（見積もり。遅い CPU では未計測） |
| 三角形（キャンパス、1 フレーム） | 900k | Medium で平均 1,118k〜1,178k、High で 1,465k〜1,750k（1.7） | 今に約 20% の幅を足した。この PC では GPU が律速ではない（1.2）。内蔵 GPU は未計測なので、そこで測るまでは仮の値 |
| テクスチャ | 1 枚は 2048 px まで。1 つのシーンに載る分の合計は 64 MiB まで（DXT で見積もる） | 見積もり 29〜50 MiB（下の内訳） | 64 MiB は、2048² の BC3（8 bpp）に mip を付けた 5.3 MiB が 12 枚の量。今の 2048² は 8 枚 |
| 配信サイズ | 合計 80 MiB、data 64 MiB | 70.9 MiB、data 60.3 MiB（1.6） | tools/deploy_pages.py の MAX_TOTAL_BYTES と MAX_DATA_BYTES（理由は定数のコメント）。1 台の端末が読む量（DXT と ASTC の data の大きい方）で測る。超えたら配信しない |
| Unity のヒープ（WASM のメモリ） | 512 MiB（仮） | 368.8 MiB（キャンパスに入った直後、1.7） | 初期値は 256 MiB。拡張は Geometric（1 回に 20%、1 回の上限 96 MiB）なので、256 → 307 → 369 → 442 → 531 MiB になる。キャンパスに入った直後で 2 回拡張している。512 MiB は 4 回目の拡張をしない線。ピークを測っていないので仮の値 |

### 2.1 テクスチャの内訳（見積もり）

見積もりの前提:

- WebGL の圧縮形式は、テクスチャごとの上書き（.meta の WebGL の overridden）を指定していない。ビルドの形式は `BuildPlayer.BuildWebGL` が
  DXT と ASTC の 2 本で決める（1.6）。表は DXT で見積もった。
- ビルドした data の中身では確かめていない。
- 透明の無い画像を BC1（4 bpp）、ある画像を BC3（8 bpp）とし、どちらになるか分からない画像は幅で書いた。

| テクスチャ | 数と大きさ | 見積もり |
|---|---|---|
| キャラの顔（Models/Characters/*/face.png） | 2048²、mip あり、7 枚 | 18.7〜37.3 MiB |
| ミニマップ（Textures/minimap.png） | 2048²、mip なし、1 枚 | 2〜4 MiB |
| 屋内の背景（Generated/Backdrops、dev/game） | 2048×512、mip あり、10 枚（透明なし） | 全部で 6.7 MiB |
| TMP のフォントのアトラス（KCD_JP） | 1024² の Alpha8、Dynamic で足りなくなると 1 枚ずつ増える | 1 枚 1 MiB |
| 小物（着物の柄、ハート、水面、ミニマップの記号） | 256² 以下 | 0.5 MiB 未満 |
| 合計（全部が同時に載ったとき） | | 29〜50 MiB |

- 顔の画像には、FBX の横の *.fbm/ にも同じ画像の写しがある。マテリアルが参照しているのは外の face.png だけで、*.fbm/ の写しはどのマテリアルからも参照されていない（GUID を grep して確かめた）。そのため、ビルドに入るのは 1 枚ずつと見なした。
- 面のテクスチャ（Textures/surfaces）は 512² が 9 枚と外壁の煉瓦 1024² が 1 枚。.meta の既定（DefaultTexturePlatform）を圧縮（textureCompression 1）にしてあり、
  BC1 に mip を付けて 512² が 1 枚 0.17 MiB、煉瓦が約 0.67 MiB、全部で約 2.2 MiB。
  .meta は tools/fetch_textures.py が data/textures/TextureImporter.meta.template から書く。圧縮の設定は tools/tests/test_fetch_textures.py が見張る。
- DXT を扱えず ASTC を扱える GPU（iPhone と多くの Android）では、ページが ASTC の data を読むので、テクスチャは圧縮したまま GPU に載る。
- DXT も ASTC も扱えない GPU では、Unity がソフトウェアで展開する。そのときは RGBA32 になり、2048² に mip を付けて 1 枚 21.3 MiB に増える。
- 圧縮しない設定（textureCompression 0）のテクスチャは、どちらのビルドでもそのまま載る。512² の RGB に mip を付けると 1 枚 1.0 MB になる。
- 描画に使う面（シャドウマップ・色・深度）は画質の段（3 章）ごとに変わるので、この表には入れていない。

### 2.2 タブのメモリ

Web 版がブラウザに確保させる主なメモリは、次の 3 つを足した量になる。

- Unity のヒープ（WASM のメモリ）。拡張すると縮まない。
- data の gzip を解いた中身（69.05 MiB）。
  - Emscripten の仮想のファイルシステムに入る。
  - Unity のヒープの外にあり、遊んでいる間ずっと載っている（Unity の手引き webgl-memory）。
- JavaScript のヒープ。

GPU のメモリ（テクスチャと描画に使う面）は、これとは別に GPU のプロセスに載る。

## 3. 画質の段

設定画面（タイトルとポーズの「設定」）の「画質」の行で、◀ ▶ を押して選ぶ。

- 表示: 日本語では 低 / 中 / 高、英語では Low / Medium / High。
- 保存: PlayerPrefs の `KCD.QualityTier` に low / medium / high を入れる。
- 保存が無いときの既定: Web 版は Medium、Windows 版は High。スマホ・タブレットのブラウザ（`Application.isMobilePlatform`）で開いた Web 版は Low（3.6）。
- エディタ: ビルド先を WebGL にしていれば Web 版と同じ扱いになる。

### 3.1 段ごとの値

| 項目 | Low | Medium | High |
|---|---|---|---|
| renderScale | 0.7 | 0.8 | 1.0 |
| 影の距離 | 30 m | 50 m | 50 m |
| 影のカスケード | 1 | 1 | 4 |
| シャドウマップ（主光源） | 512 px | 1024 px | 2048 px |
| MSAA | なし | なし | 2x |
| ポストプロセス（全カメラ） | 切る | 描く | 描く |
| 木を描く距離 | 300 m | 制限なし | 制限なし |

- Medium の値は Mobile_RPAsset（Web 版の品質レベル）とまったく同じ。そのため、Web 版の既定のままなら写しを作らず、見た目も描く量も今と変わらない。
- High の値は PC_RPAsset（Windows 版の品質レベル）に MSAA 2x を足したもの。Windows 版の既定の見た目は MSAA の分だけ変わる。
- 表に無い項目は、起動した品質レベルのアセットの値のまま変えない。ソフトシャドウ・HDR・深度テクスチャ・拡大のフィルタがこれにあたる。
  - Web 版は Mobile_RPAsset、Windows 版は PC_RPAsset の値になる。
  - QualitySettings では、PC のレベルは WebGL を、Mobile のレベルは Standalone を除外している。そのため、ビルドごとに使うアセットは 1 つだけ。

### 3.2 切り替え方

- URP のアセット（Assets/Settings/*_RPAsset）は書き換えない。
  - 起動した品質レベルのアセットを Object.Instantiate で写す。
  - 写しに段の値を入れ、QualitySettings.renderPipeline を写しに差し替える。
  - 段の値が元のアセットと同じときは、写しを作らない。
  - エディタでは、再生を止めるときに元のアセットへ戻し、写しを捨てる。
  - 実装は Runtime/Core/QualityManager.cs と QualityPipeline.cs。
- ポストプロセスは、カメラの UniversalAdditionalCameraData.renderPostProcessing で切る。
  - 切ったカメラを覚えておき、戻すときはそのカメラだけ戻す。
  - シーンを読むたびに入れ直す。
- 木は、TreeChunkCombiner がまとめたマスの MeshRenderer を、カメラからの距離で止める（Runtime/World/TreeChunkDistance.cs）。
  - 距離は、マスの箱の一番近い点までで測る。
  - forceRenderingOff で止める。
- 起動時に `[KCD] graphics tier=<段> renderScale=... shadow=...m/...cascade/...px msaa=... post=on|off trees=...m|all pipeline=... vSyncCount=... targetFrameRate=...` を 1 行ログに出す。
  - pipeline は使っているアセットの名前。写しに差し替えたときは `copy of <元の名前>` になる。
  - GameManager の `[KCD] quality=` とは別の行。

### 3.3 項目を選んだ理由（URP 17.6.0 のソースで確かめた）

パスは com.unity.render-pipelines.universal からの相対パス。

- 影のカスケードの数は、実行中に変えてよい。
  - Editor/ShaderBuildPreprocessor.cs 875 行のコメント「Cascade count can be changed at runtime, so include both of them」のとおり、主光源の影があれば、カスケードありとなしの両方の変種がビルドに残る。
- ソフトシャドウは段で動かさない。
  - 同じファイルの 806 行で、アセットの supportsSoftShadows が切ってあるとソフトシャドウの変種が削られる。
  - Web 版の Mobile_RPAsset はソフトシャドウを切っている。そのため、Web 版で段から入れても描けない。
- Low の renderScale を 0.5 にしなかった理由:
  - 拡大のフィルタは Auto。1/renderScale が整数になり画面の大きさも割り切れると、URP は最近傍（Point）を選ぶ（Runtime/UniversalRenderPipeline.cs 2585〜2606 行）。
  - Point の変種は、アセットのフィルタが Point のときしか残らない（Editor/ShaderBuildPreprocessor.cs 697〜701 行、Editor/ShaderScriptableStripper.cs 874〜879 行）。
  - 0.7 と 0.8 では 1/renderScale が整数にならないので、線形の拡大になる。
- LOD bias は段に入れなかった。
  - アセットにもシーンにもプレハブにも LODGroup が 1 つも無いので、変えても何も変わらない。
  - 遠くの木は、代わりに距離で止める。
- 木の距離を 300 m にした理由:
  - キャンパスの霧は Linear の 220〜620 m。300 m では霧が 2 割で、木はまだ 8 割見えている。
  - trees.json の 553 本は 37 個のまとまりになる。キャンパスの中で位置を変えて数えると、300 m より遠いまとまりは平均約 3 個、最も多い位置で 16 個（見積もり）。
  - まとまり 1 つは、マテリアル 2 つ × パス 2 つ（輪郭と本体）で 4 コール。このため、減るのは平均で最大約 12 コール、最も多い位置で最大約 64 コール。視野の外のまとまりはもともと描かれないので、実際の減りはこれ以下（見積もり）。
  - 250 m にすると平均 6.5 個（最大 19 個）、350 m にすると平均 0.6 個（最大 12 個）が止まる。
- Low でポストプロセスを切ると、次のものも一緒に消える。
  - #72 で数えたポストプロセスのパスは 1 フレームに 20 本ある。UberPost 1、LutBuilder 1、Bloom 16（1 + 5 + 5 + 5）、ScalingSetup 1、FinalPost 1。
  - 切ると、このうち拡大の 1 本を除いた約 19 本がなくなる。
  - 同時に次のものも消える。
    - FXAA（ゲームのカメラは FXAA、Editor/PostProcessFactory.cs 52 行）。FXAA は FinalPost の中で掛かり、FinalPost はポストプロセスを描くカメラがあるときしか走らない（Runtime/UniversalRendererRenderGraph.cs 1385 行と 1397 行）。
    - トーンマッピング（Neutral）。
    - LUT に焼く色の調整。コントラスト +6、彩度 +10、露出 +0.05、ホワイトバランスがこれにあたる。
    - Bloom。
    - ビネット（0.22）。
  - Low は、輪郭のギザギザが出る。明るいところは Neutral で丸めずに切れる。色も少し淡くなる。
  - 見た目を受け入れられなければ、代わりの案がある。Low でもポストプロセスは描き、Volume の Bloom だけを切る。これで 20 本のうち Bloom の 16 本が減り、FXAA と色は残る。
- High の MSAA 2x: 4x は、描く面のメモリと帯域が 2x の倍になる。High でも輪郭の荒れを抑える最小の値として 2x にした。
- シャドウマップのメモリ（URP の主光源のシャドウマップは 16 bit、Runtime/Passes/MainLightShadowCasterPass.cs 31 行）:
  - 512² は 0.5 MiB、1024² は 2 MiB、2048² は 8 MiB。
  - カスケードは 1 枚のアトラスを分けて使うので、High の 4 カスケードでも 8 MiB のまま。

### 3.4 段ごとの描く量（Medium との比べ）

描画コールと三角形は 1.7 の実測。そのほかの行は設定の値から出した見積もり。

| | Low | Medium（Web 版の既定） | High |
|---|---|---|---|
| 描く画素（1280×900 のとき） | 896×630、0.77 倍 | 1024×720 | 1280×900、1.56 倍 |
| ポストプロセスのパス | 約 −19 本 | 20 本 | 20 本 |
| 影を落とす物の描画 | 30 m 以内だけ、1 カスケード | 50 m 以内、1 カスケード | 50 m 以内、4 カスケード（重なるカスケードの数だけ描くので最大 4 倍） |
| シャドウマップ | 0.5 MiB | 2 MiB | 8 MiB |
| MSAA | なし | なし | 2x（色と深度の面が 2 サンプル） |
| 木の描画コール（見積もり） | 平均で最大約 −12、最も多い位置で最大約 −64 | 0 | 0 |
| 描画コール・三角形（ポーズ中の実測） | −50 回、−198k | 629 回、1,106k | +202 回、+376k |

- どの段も、テクスチャとメッシュは足していない。配信サイズに効くのはコードだけ（wasm が数十 KB 以内の見込み、未計測）。

### 3.5 Web 版で High を選んだとき

Web 版でも High を選べる。PC の Web 版の既定は Medium なので、High になるのは設定画面で選んだ人だけ。描画コールと三角形は 1.7 で測った。GPU のメモリは見積もりで、測っていない。

描画コールと三角形:

- Medium から増えるのは、影を落とす物の描画だけ。影の距離は同じ 50 m で、カスケードが 1 から 4 になる。物はカスケードの範囲に掛かる数だけ描かれる。描く画素とポストプロセスのパスの数は描画コールを増やさない。
- 実測では、ポーズ中の同じ絵で Medium より +202 回、+376k 増えた（1.7）。歩きの最大は 1,021 回、1,855k で、2 章の予算（700 回、900k）を超える。
- 1.2 の傾き（1 回 4.49 µs）で見積もると、+202 回で +0.91 ms。この PC では 1 フレームの処理は High でも平均 4.25 ms で、60 fps の上限に張り付いた。

GPU のメモリ（ビューポート 1280×900、devicePixelRatio 1 のとき）:

- PC では、テンプレート（WebGLTemplates/KCD/index.html）は config.devicePixelRatio を入れないので、Unity はブラウザの devicePixelRatio のまま描く（スマホ・タブレットは 3.6）。
- 色の面は HDR の 32 bit（B10G11R11、4 B/画素）。深度の面は D32_SFloat_S8_UInt（8 B/画素と見なした。com.unity.render-pipelines.core の Runtime/Utilities/CoreUtils.cs 1944〜1951 行で、WebGL はこの形式になる）。
- Medium: 1024×720 = 737,280 画素。色 4 B + 深度 8 B = 12 B/画素で 8.44 MiB。
- High: 1280×900 = 1,152,000 画素。MSAA 2x の色 8 B + MSAA 2x の深度 16 B + 解決した色 4 B = 28 B/画素で 30.76 MiB。
- 差は +22.32 MiB。シャドウマップの +6 MiB（2 → 8 MiB）と合わせて、High は Medium より約 +28 MiB。
- devicePixelRatio 2 の画面（表示の拡大 200% など）では画素が 4 倍になる。Medium は 33.75 MiB、High は 123.05 MiB で、差は約 +89 MiB（シャドウマップを足すと約 +95 MiB）。
- ポストプロセスの途中の面（Bloom の縮小など）とブラウザの画面の面は数えていない。途中の面は描く画素に比例して増え、画面の面はどの段でも同じ。
- 端末が B10G11R11 に描けないときは、色の面が 64 bit（8 B/画素）になり、色の分が倍になる。

描く量:

- 色と深度に書くサンプルの数は、Medium の約 3.1 倍（画素 1.56 倍 × MSAA 2）。ピクセルシェーダーを走らせる回数は画素の数に比例し、約 1.56 倍（MSAA でも、ピクセルシェーダーは 1 画素に 1 回で、サンプルごとには走らない）。

### 3.6 スマホ・タブレットで開いたとき（#187、見積もり、実機では未計測）

テンプレートは、UA が iPhone / iPad / iPod / Android か、指だけで操作する端末（`pointer: coarse` かつ `hover: none` で、細かいポインタが無い）なら、
`config.devicePixelRatio` に `min(devicePixelRatio, 1.5)` を入れる。画質の段は、保存が無ければ Low になる。

横持ちの 844×390（CSS px）、devicePixelRatio 3 の画面で比べる。描く面は renderScale を掛けた大きさ、画面の面はキャンバスの大きさ。

| | キャンバス | 描く面（renderScale） | 描く画素 | 描く面の色と深度（12 B/画素） | 画面の面（4 B/画素） |
|---|---|---|---|---|---|
| 上限なし・Medium（変える前） | 2532×1170 | 2026×936（0.8） | 1,896,336 | 21.70 MiB | 11.30 MiB |
| 上限 1.5・Medium | 1266×585 | 1013×468（0.8） | 474,084 | 5.43 MiB | 2.83 MiB |
| 上限 1.5・Low（既定） | 1266×585 | 886×410（0.7） | 363,260 | 4.16 MiB | 2.83 MiB |

- Playwright のスマホのエミュレーション（844×390、DPR 3、`is_mobile`）で、キャンバスが 1266×585 になり、設定の画質が「低」で始まることを確かめた。
  Unity のヒープは読み込み直後 256.0 MiB、キャンパスで 368.75 MiB で、同じビルドを PC の画面で動かした smoke（`deploy_pages.py --smoke`）の 256.0 MiB / 368.8 MiB と同じだった。
- 描く画素は、変える前の 0.19 倍（約 5 分の 1）。ピクセルシェーダーを走らせる回数も同じ割合で減る。
- GPU のメモリは、描く面と画面の面で 33.00 MiB から 6.99 MiB へ、約 26 MiB 減る。シャドウマップ（2 B/画素）も 1024² の 2 MiB から 512² の 0.5 MiB になり、1.5 MiB 減る。
- Low はポストプロセスを切るので、Bloom の縮小などの途中の面も無くなる（3.3）。
- 文字と UI は Unity のキャンバスの中で描くので、上限 1.5 では DPR 3 の画面で少しぼやける。844×390 のエミュレーションで、HUD とメニューの文字が読めることは確かめた。
- iPadOS の Safari は UA が Mac を名乗るため、`Application.isMobilePlatform` が偽になり、画質の既定は Medium のまま。描く解像度の上限は、指だけの端末の判定で掛かる。
- ヒープと data の中身は PC と同じ（2.2）。スマホのブラウザがタブに許すメモリは、端末ごとに違い、ここでは測れない。

## 4. Windows 版のフレームの刻み（#15）

- Windows 版の品質レベルは、Mobile も PC も vSyncCount 0 だった。そのため、上限なしで回っていた。
- 起動時に、Windows / macOS / Linux の Player で次のように入れる（Runtime/Core/FramePacing.cs、QualityManager の起動時に呼ぶ）。
  - vSyncCount 1。
  - targetFrameRate −1（vSync に任せる）。
- 起動引数で vSync を切ったときは、vSyncCount 0、targetFrameRate 60 にする。
  - 切り方は `-kcd-vsync off`（`0`、`false`、`-kcd-vsync=off` の形も可）。
- Web 版とエディタには触らない。
  - Web 版は、ブラウザの requestAnimationFrame で回る。Unity の手引き（webgl-performance）は、上限を掛けないなら targetFrameRate を既定の −1 にして、刻みをブラウザに任せるよう勧めている。
  - エディタでは、開発者の QualitySettings を再生のたびに書き換えないため。

vSync を設定画面の項目にしない。理由:

- Web 版では vSync を選べない（ブラウザが決める）。行を出しても、効くのは Windows 版だけになる。
- vSync を切って得をするのは、計測で上限なしの fps を見たいときだけ。それは起動引数で足りる。
- 切ると、画面のティアリングが出て、見えないフレームに電力を使う。60 fps の上限を入れても、画面の書き換えとはずれる。

## 5. まだ測っていないことと測り方

測ったら 1 と 2 の表へ足し、この表の状態を変える。

| 項目 | 状態 |
|---|---|
| 読み込み時間（初回 / 2 回目、キャンパスへの遷移） | 未計測 |
| Unity のヒープのピーク（キャンパスを歩き回ったあと、屋内を回ったあと） | 未計測 |
| テクスチャのメモリ（2.1 の見積もりの確かめ） | 未計測 |
| 段ごとの fps・1% low・描画コール・三角形 | Web 版は計測済み（1.7）。Windows 版は未計測 |
| Web 版の High の GPU のメモリ（3.5 の見積もりの確かめ） | 未計測。描画コールと三角形は計測済み（1.7） |
| 遅い CPU（CPU 4x throttle）と内蔵 GPU での fps | 未計測 |

### 5.1 読み込み時間

- `python e2e/run_webgl_smoke.py`（公開版）か、`python e2e/run_webgl_smoke.py --serve build/WebGL`（ローカルのビルド）で測る。
  - build/e2e/<対象>-<日時>/metrics.json の timings_ms に、ページを開いてからの ms が入る。
  - 入る項目: dom_content_loaded、loader_loaded、first_progress、download_done、unity_ready、first_drawn_frame、campus_after_enter。
  - Playwright は毎回新しいプロファイルで開くので、これは初回の読み込みの値になる。
- Edge の開発者ツール（F12）の Network で、転送量と時間を見る。
  - 回線を絞るには、Throttling で遅い回線を選ぶ。
  - 2 回目の読み込みを見るときは、Disable cache を切って開き直す。
  - 初回を見るときは、Application の Storage で Clear site data を押す。data は IndexedDB と Cache API に入るので、Disable cache だけでは初回にならない。
- 参考の計算（計測ではない）: 20 Mbps の回線なら、63.4 MiB の転送に約 27 秒、80 MiB に約 34 秒かかる。
- キャンパスへの遷移: wf/scene-load が入ると、Player.log に `[KCD] scene_load scene=<名前> ms=<実時間> frames=<フレーム数>` が出る。
  - Web 版では、ブラウザのコンソールに同じ行が出る。

### 5.2 ヒープ

- metrics.json の heap.ready と heap.campus を見る。
  - wasm_memory（Unity のヒープ全体、バイト）と、js_heap_used / js_heap_total が入る。
  - WASM のメモリは縮まないので、遊び終えたときの wasm_memory がその回のピークになる。
- 回り方: タイトル → キャンパス → 屋内を数棟 → タイトル → キャンパス。
- Edge の開発者ツールの Memory でも見られる。
- Development Build を Profiler につなぐと、Memory モジュールの Total Reserved が Unity の側から見た値になる。

### 5.3 テクスチャのメモリ

- Development Build を Profiler につなぎ、Memory モジュールの Textures を見る。Memory Profiler パッケージのスナップショットなら、1 枚ずつ分かる。
- Edge のタスク マネージャー（Shift+Esc）の GPU プロセスの行でも見られる。ただし、ほかのタブの分も入るので、ほかのタブは閉じて測る。

### 5.4 段ごとの fps と描画コール

- Web 版は `e2e/run_webgl_perf.py` で測る。Edge を headless で開くので、ウィンドウは出ず、マウスも取らない。音も出さない。
  - ローカルのビルド: `python e2e/run_webgl_perf.py --serve build/WebGL`。公開版: `--url https://takumayellow.github.io/katsushika-campus-days/`。
  - 上限なしで測るときは `--uncapped` を付ける。段を絞るときは `--tiers low,high`。
  - 段ごとに、設定画面で段を変えてから、立ちと歩きを `--seconds` 秒（既定 10 秒）ずつ測る。そのあとポーズ中の同じ絵で段だけを変えて `--frozen-seconds` 秒（既定 3 秒）ずつ、`--frozen-rounds` 回（既定 2 回）測る。
  - 結果は build/e2e/perf-<日時>/ の perf.json と、段ごとの画像（設定画面・立ち・歩き・ポーズ中）。設定画面の画像で、段が変わったことを確かめる。
- Low では、立つ地点から 300 m より遠い木が消えているかを、low-stand.png と medium-stand.png を比べて確かめる。
- Web 版の High の GPU のメモリは、Edge のタスク マネージャー（Shift+Esc）の GPU プロセスの行で、Medium と High の差を見る（3.5 の見積もりは約 +28 MiB）。タスク マネージャーはウィンドウありの Edge でしか開けない。
- Windows 版は Player.log で確かめる。
  - 場所: %USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\Player.log。
  - 確かめる行: `[KCD] graphics tier=` と `[KCD] quality=`。
  - fps は、`-kcd-vsync off` で起動したときに 60 で止まることと、既定で画面の周期に合うことを見る。
- 遅い CPU は、Edge の開発者ツールの Performance で CPU の 4x slowdown を掛けて測る。
