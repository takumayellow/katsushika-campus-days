# 葛飾キャンパスデイズ 遊び方

東京理科大学 葛飾キャンパスを歩き回り、学生たちと話し、1 日を過ごす三人称探索ゲームです。

## 起動

ブラウザ版: https://takumayellow.github.io/katsushika-campus-days/ を開くだけ（Chrome / Edge / Firefox、WebGL 2）。
セーブはブラウザ内に保存されるので、ブラウザのサイトデータを消すと消える。

Windows 版:

1. zip を展開し、`KatsushikaCampusDays/KatsushikaCampusDays.exe` を起動する。
2. Windows 10/11 64bit、DirectX 11 対応 GPU。インストールは不要。
3. セーブデータは `%USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\kcd_save.json` に 1 枠。

## タイトル画面

- 回転台の 3 人から主人公を選ぶ: **みらい**（理科大生）、**坊っちゃん**、**マドンナちゃん**。
- 「はじめから」で朝 8:30 のキャンパスから開始。「つづきから」でセーブを読む。
- 「クレジット」に地図データ（OpenStreetMap, ODbL）などの帰属表示がある。

## 操作

| 操作 | キーボード / マウス | ゲームパッド |
| --- | --- | --- |
| 移動 | W A S D / 矢印キー | 左スティック |
| 視点 | マウス移動 | 右スティック |
| ズーム | マウスホイール | |
| ダッシュ | Shift（押しっぱなし） | |
| ジャンプ | Space | |
| 話す / 調べる / 入る | E | |
| クエストログ | Tab | |
| ポーズ・設定・セーブ | Esc | |

## 1 日の流れ

- ゲーム内時間は朝 8:30 から進み、昼・夕方・夜で空と BGM、歩いている学生たちが変わる。
- 建物の入口に近づくと名前が出る。E で中に入ると、講義室・食堂・図書館・体育館・研究室・実験室・温室などの内部を歩ける。
- NPC（いなり・かなめ・そら・教授）に話しかけるとメインクエスト 7 本とサブクエスト 6 本が進む。
- キャンパスには隠しアイテム 23 個と写真スポット 6 か所がある。集めると称号が増える。
- 夜になると 1 日の終わりのリザルト画面が出て、達成度で S / A / B / C 評価が付く。

## 言語

設定画面で日本語 / English を切り替えられる。

## うまく動かないとき

- 画面が真っ暗、または起動しない: `KatsushikaCampusDays.exe -force-d3d12` で起動して DirectX 12 に切り替える（既定は DirectX 11）。
- ログ: `%USERPROFILE%\AppData\LocalLow\KCD\Katsushika Campus Days\Player.log`
