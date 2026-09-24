# 葛飾キャンパスデイズ 遊び方

東京理科大学 葛飾キャンパスを歩き回り、学生たちと話し、1 日を過ごす三人称探索ゲームです。

## 起動

ブラウザ版: https://takumayellow.github.io/katsushika-campus-days/ を開くだけ（Chrome / Edge / Firefox、WebGL 2）。
セーブはブラウザ内に保存されるので、ブラウザのサイトデータを消すと消える。

Windows 版:

1. zip を展開し、`KatsushikaCampusDays/KatsushikaCampusDays.exe` を起動する。
2. Windows 10/11 64bit、DirectX 11 対応 GPU。インストールは不要。
3. セーブデータは `%USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\kcd_save.json` に 1 枠。

## 動作環境

| | Windows 版 | ブラウザ版 |
| --- | --- | --- |
| OS / ブラウザ | Windows 10 / 11 の 64bit | PC の Chrome / Edge / Firefox（WebGL 2 が動くもの） |
| グラフィック | DirectX 11 対応 GPU（DirectX 12 でも起動できる） | WebGL 2 対応 GPU |
| 画面 | 枠なしの全画面ウィンドウで起動。F でウィンドウ表示と切り替え | ページ内に表示。F で全画面 |
| 入力 | キーボードとマウス。ゲームパッドのボタンも割り当ててある | キーボードとマウス |
| インストール | 不要（zip を展開するだけ） | 不要 |

## タイトル画面

- 回転台の 3 人から主人公を選ぶ: **みらい**（理科大生）、**坊っちゃん**、**マドンナちゃん**。
- 「はじめから」で朝 8:30 のキャンパスから開始。「つづきから」でセーブを読む。
- C でクレジット、O で設定を開く（ゲームパッドは Y でクレジット、View で設定）。
- 主人公は ← → か A / D で選び、Enter で決める（ゲームパッドは十字キーの左右と A）。
- 「クレジット」に地図データ（OpenStreetMap, ODbL）などの帰属表示がある。地図データの帰属はタイトル画面の右下にも出ている。

## 操作

ゲームパッドのボタン名は Xbox コントローラーの配置で書いている。

| 操作 | キーボード / マウス | ゲームパッド |
| --- | --- | --- |
| 移動 | W A S D / 矢印キー | 左スティック |
| 視点 | マウス移動 | 右スティック |
| ズーム | マウスホイール | なし |
| ダッシュ | Shift（押しっぱなし） | LB または LT（押しっぱなし） |
| ジャンプ | Space | A |
| 話す / 調べる / 入る / 座る | E または Enter | X |
| クエストログ | Tab | View |
| ポーズ・設定・セーブ | Esc | Menu |
| フォトモード | P（Enter / E / Space で撮る、P / Esc で戻る） | Y（A / X で撮る、Y / B で戻る） |
| 手を振る | Q | RB |
| 全画面とウィンドウの切り替え | F | なし |
| クイックセーブ / クイックロード | F5 / F9 | なし |

- 座っているときは、移動・ジャンプ・E のどれかで立ち上がる。
- 撮った写真は Windows 版では `%USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\Photos` に PNG で保存される。

## 1 日の流れ

- ゲーム内時間は朝 8:30 から進み、昼・夕方・夜で空と BGM、歩いている学生たちが変わる。
- 建物の入口に近づくと名前が出る。E で中に入ると、講義室・食堂・図書館・体育館・研究室・実験室・温室などの内部を歩ける。
- NPC（いなり・かなめ・そら・教授）に話しかけるとメインクエスト 7 本とサブクエスト 6 本が進む。
- キャンパスには隠しアイテム 23 個と写真スポット 6 か所がある。集めると称号が増える。
- 夜になると 1 日の終わりのリザルト画面が出て、達成度で S / A / B / C 評価が付く。

## 言語

設定画面で日本語 / English を切り替えられる。

## 既知の問題

2026-09-24 時点。直っていない不具合の一覧は https://github.com/takumayellow/katsushika-campus-days/issues?q=is%3Aopen+label%3Abug にある。

- スマートフォンとタブレットのブラウザでは操作できない。キーボードとマウスのある PC で開く（#73）。
- ブラウザ版はゲーム画面をクリックするとマウスカーソルが画面に取られる。Esc でカーソルが戻り、F で全画面になる（#48）。
- 走るとスカートから脚が突き抜けて見える（#49）。
- 建物の中の窓の外に景色が無い（#60）。

## うまく動かないとき

- 画面が真っ暗、または起動しない: `KatsushikaCampusDays.exe -force-d3d12` で起動して DirectX 12 に切り替える（既定は DirectX 11）。
- ログ: `%USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\Player.log`

## クレジット

- 地図データ © OpenStreetMap contributors (ODbL)。キャンパスの建物・通路・緑地と、学生寮までの道と沿道の建物は OpenStreetMap のデータから作っている。ライセンス: https://www.openstreetmap.org/copyright
- 音楽・フォントなど、ほかの素材の出典と権利はゲーム内のクレジット画面（タイトル画面で C）と、Windows 版の zip に同梱した CREDITS.txt（リポジトリでは docs/CREDITS.md）にまとめている。
- 本作は非公式・非営利のファン作品です。
