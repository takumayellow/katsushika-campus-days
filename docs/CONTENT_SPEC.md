# CONTENT_SPEC — 収集物 / サブクエスト / ローカライズ / モブ / リザルトのデータ仕様

対象 Issue: #10（収集物・写真・称号）, #17（サブクエスト）, #22（ローカライズ）, #16（モブ・リザルト）。
データはすべて `unity/KatsushikaCampusDays/Assets/Data/` 配下の JSON。`MiniJson` で読む前提（未知キーは無視されるので、
既存ローダを壊さずキーを足せる）。ファイルは UTF-8・BOM 無し・LF・2 スペース。

このドキュメントは「データの形」と「それを読む C# の名前・読むキー」を決める。C# 本体は `unity-builder2` が実装する。

## 0. 共通の約束

| 項目 | 規則 |
|------|------|
| 屋外座標 | `campus.json` と同じ。`x` = 東 (m), `z` = 北 (m)。`y` はシーンを組むときに当たり判定の面へ降ろす（`CollectibleStage.OutdoorFloor`）。 |
| 屋内位置 | `Models/Interiors/<building>.json` の `empties` にある `poi_*` の名前。Unity 側は同名 Empty（FBX 内 Transform）を `transform.Find` で引き、その **ワールド座標** を使う。 |
| 建物 id | `campus.json` の `buildings[].id` のうち `on_campus: true` の 9 つ: `research1 research2 lecture kyoso library gym lab1 lab2 greenhouse`。 |
| 場所 id | `CampusProps.PlaceZones` の VisitZone: `gate_main campus_mall mall_bench library_pond`。写真スポット `ps_*` も VisitZone と三脚（`PhotoSpot`）として生成する（後述）。 |
| NPC id | `inari kaname sora prof`。 |
| 言語 | 日本語が正。既存 JSON は `text` / `title` / `summary` / `rewardText` をそのまま保ち、英語は `text_en` / `title_en` / `summary_en` / `rewardText_en` を **追加** した。ローダは `_en` を読まなくても動く。 |

### Resources へのコピー（DataBundler の拡張が必要）

現行 `DataBundler`（Editor）は `Assets/Data/Quests` と `Assets/Data/Dialogue` だけを `Assets/Resources/KCD/` へコピーする。
次の 4 フォルダを追加でコピーする:

```
Assets/Data/Collectibles → Assets/Resources/KCD/Collectibles
Assets/Data/Localization → Assets/Resources/KCD/Localization
Assets/Data/Mobs         → Assets/Resources/KCD/Mobs
Assets/Data/Ending       → Assets/Resources/KCD/Ending
```

---

## 1. `Collectibles/collectibles.json`

```jsonc
{
  "version": 1,
  "rarities": ["common", "uncommon", "rare", "legendary"],
  "collectibles": [ Collectible, ... ],   // 23 件（hidden 20 + quest 報酬 3）
  "photo_spots":  [ PhotoSpot,   ... ],   // 6 件
  "achievements": [ Achievement, ... ]    // 13 件
}
```

### Collectible

| キー | 型 | 意味 |
|------|----|------|
| `id` | string | `c_*`。`CollectableItem.ItemId` にそのまま使う。`QuestSystem.ReportCollect(id)` の引数になる。 |
| `name_ja` / `name_en` | string | 表示名 |
| `hint_ja` / `hint_en` | string | コレクション画面のヒント（未入手時に出す） |
| `building` | string \| null | null = 屋外 |
| `position` | `{x,z}` \| string \| null | 屋外は座標、屋内は `poi_*` 名、`source: "quest"` は null |
| `rarity` | string | `rarities` のいずれか |
| `source` | `"hidden"` \| `"quest"` | hidden = ワールドに置く（20 件）。quest = クエスト報酬でのみ入手（3 件、置かない）。 |

屋内 `position` の `poi_*` は必ず `building` の Interiors JSON に存在する（生成時に照合済み）。
屋外 20 件中 8 件の座標は既存の VisitZone / 公園の葉と重ならない位置に置いてある。神社の鈴 `c_shrine_bell` (226, 64) はキャンパス境界の外だが NavMesh bounds（中心 (13,10,-33)・サイズ (460,140,340)）の内側。

### PhotoSpot

| キー | 型 | 意味 |
|------|----|------|
| `id` | string | `ps_*`。**VisitZone の `PlaceId` としても使う**（サブクエスト `q_sq_photo_walk` が `visit` で参照）。 |
| `name_ja/en`, `caption_ja/en` | string | 撮影後に出すキャプション |
| `building`, `position` | 同上 | 屋外 3・屋内 3 |
| `look_dir` | `{x,z}` | 撮影の推奨向き（正規化済み）。屋内はその建物のローカル軸。カメラをこの向きへ 0.5 秒で補間するのに使う。 |

### Achievement

| キー | 型 | 意味 |
|------|----|------|
| `id` | string | `ach_*` |
| `name_ja/en`, `desc_ja/en` | string | |
| `condition` | object | 下表 |

| `condition.type` | 追加キー | 達成判定 |
|------------------|----------|----------|
| `collect_count` | `value` | `source == "hidden"` の入手数 ≥ value |
| `rarity_count` | `rarity`, `value` | 指定レア度の入手数 ≥ value（クエストの報酬も数える） |
| `photo_count` | `value` | 撮影済み写真スポット数 ≥ value（`photo_spots` に載った `ps_*` だけ） |
| `building_count` | `value` | 入った建物の種類数 ≥ value（寮は数えない） |
| `npc_count` | `value`, `npcs` | 話しかけた NPC のうち `npcs` に載った者の種類数 ≥ value。`npcs` を省くと全員を数える |
| `quest_count` | `value`, `scope` | 達成クエスト数 ≥ value（メイン 7 + サブ 6 = 13）。`scope: "main"` なら `side: true` のクエストを数えない |
| `quest_complete` | `value`(quest id) | そのクエストが達成済み |

`value` は 1 以上にする（0 以下は満たさない扱い）。数の条件は、集められる数（`Ending/result.json` の `totals`）を超えない（`ResultTotalsTests`）。

### Unity 側

- **`CollectibleCatalog`**（static / ScriptableObject 不要）: `Resources.Load<TextAsset>("KCD/Collectibles/collectibles")` を parse。`Get(id)`, `Hidden`, `PhotoSpots`, `Achievements`。
- **`CollectibleStage`**（Editor、`SceneBuilder` が `InteriorStage` の後に呼ぶ）: シーンを組むときに置く。`source == "hidden"` の 20 個は `ActorFactory.CreateCollectable` でレア度の色の宝石（`gem_<rarity>`）として置き、`ItemId = id`。屋外は `(x, z)` の地面（Ground レイヤーの面から 1.2 m までの上向きの面に載せる）、屋内は `Interior_<building>` 配下の `poi_*` の床（2 階の poi は 2 階の床）。写真スポットは同じ場所に `VisitZone`（`PlaceId = ps_*`、5×3×5 m）と三脚（`PhotoSpot`、レンズを `look_dir` へ向ける）を置く。写真スポットから 1 m 以内の隠しアイテム（`c_mall_pin` と `c_dome_key`）は、三脚と E の対象を取り合わないよう横へ 0.9 m ずらす。拾った隠しアイテムは、同じ日のうちにシーンを読み直しても `CollectableItem` が消す。
- **`PhotoSpot`**（三脚）: `E`（`ui.interact.photo`）でプレイヤーを `look_dir` へ向けてカメラを背後へ回し、`PhotoSystem.CaptureAt(ps_*)` で撮る → 撮影 SE → `ui.hud.photo_taken` トースト（スポットの名前）→ `DayStats.NotePhoto(ps_*)`。`ReportVisit(ps_*)` は `VisitZone` に入っただけで発火する（サブクエ用、`ui.hud.photo_spot` トースト）。
- **`AchievementBook`**（`GameManager.Achievements`）: `DayStats`（`CollectableItem` / `EntranceTrigger` / `NPCTalker` / `PhotoSystem` が記録する）と `QuestSystem` の達成から `condition` を判定し、新しく満たしたものを `ui.hud.achievement_unlocked` トーストで知らせる。`GameManager` はクエストの `Changed` か `DayStats.Version` が変わったフレームだけ数え直す。報酬が収集物のクエスト（`rewardType: "collectible"`）の `rewardId` は、達成したときに `QuestRewards` が `DayStats` に拾った物として記録する。称号と `DayStats` はまだセーブに載らない（起動中のみ。ロード直後は読み込んだ進行で取れている称号をトーストを出さずに獲得済みにする）。

---

## 2. `Quests/q_sq_*.json`（サブクエスト 6 本）

既存 `QuestData.Parse` が読むキー（`id,title,summary,order,autoStart,rewardText,prerequisites[],steps[{id,text,type,target,count,timeLimit,minHour}]`）はそのまま。**追加キー**:

| キー | 型 | 意味 |
|------|----|------|
| `title_en`, `summary_en`, `rewardText_en` | string | 英語 |
| `steps[].text_en` | string | 英語 |
| `side` | bool | true = サブクエスト（クエストログで `ui.questlog.side` バッジ） |
| `rewardType` | `"collectible"` \| `"achievement"` | 達成時に付与する報酬の種類 |
| `rewardId` | string | `c_*` または `ach_*`。collectible なら `CollectibleCatalog` の `source: "quest"` の品を `DayStats` に記録（`QuestRewards`）、achievement ならその称号の `condition` が `quest_complete` = このクエストになっている（`AchievementBook` が判定してトーストを出す）。 |
| `giver` | string | 依頼主の NPC id（`prof` など）。`QuestData.FromJson` が全ステップの `QuestStep.Giver` に写す。`timeLimit > 0` のステップが時間切れで失敗したとき、この NPC に話しかけると計時がはじめから始まる（`QuestSystem.Report` の Talk 分岐）。セーブに計時は載らないので、ロード直後も同じく依頼主待ちになる。省略すると、時間切れのあと再挑戦する手段が無くなる。 |

| id | order | 開始 | 前提 | ステップ | 報酬 |
|----|-------|------|------|----------|------|
| `q_sq_greenhouse` | 11 | prof の話題 `t_sq_greenhouse_start` | q_gym | enter greenhouse → visit `poi_greenhouse_plants` → talk prof | `c_greenhouse_sprout` |
| `q_sq_lost_card` | 12 | kaname `t_sq_lost_card_start` | q_lunch | visit `poi_research2_ticket` → visit `poi_research2_tray_return` → talk kaname | `ach_cafeteria_regular` |
| `q_sq_lab_notebook` | 13 | sora `t_sq_lab_notebook_start` | q_coffee | enter lab1 → collect `c_lab_goggles` ×1 → talk sora | `c_sora_charm` |
| `q_sq_stray_book` | 14 | inari `t_sq_stray_book_start` | q_park | enter library → visit `poi_library_stacks` → `poi_library_desk` → `poi_library_counter` → talk inari | `c_inari_bookmark` |
| `q_sq_photo_walk` | 15 | autoStart | q_orientation | visit `ps_mall_view` → `ps_kyoso_facade` → `ps_pond_library` | `ach_photo_walk` |
| `q_sq_night_walk` | 16 | autoStart | q_sunset | visit `gate_main` (minHour 19) | `ach_full_day` |

**屋内 `visit` を動かすために必要なもの**: 屋内 `poi_*` を対象にする `visit` ステップは、その Empty の位置に `VisitZone(PlaceId = poi 名)` が要る。
`InteriorStage.AddPoiZones` が、全 Interiors の `poi_*` Empty に 4×3×4 m の VisitZone を自動生成する。
`q_sq_lab_notebook` の `collect c_lab_goggles` は隠しアイテム `c_lab_goggles`（`poi_lab1_lab_b`）をそのまま拾えばよい（先に拾っていた場合は `QuestHeldItems` が `DayStats` を見て即達成扱いにする）。屋外にいるあいだ、ミニマップの目的地（`QuestObjectiveLocator`）は第1実験棟の入口を指す。

### Dialogue の追加話題

各 NPC の `topics` に `t_sq_<quest>_start / _active / _done` を **`t_idle` の直前** に挿入した（`SelectTopic` は先頭一致なので、既存話題の優先順位は変わらない）。
`_start` は `requiresCompletedQuest` + `once` + `startsQuest`、`_active` は `requiresActiveQuest`、`_done` は `requiresCompletedQuest` + `once`。
既存話題の `lines` は文字列から `{ "text", "text_en" }` オブジェクトに変え、話者付き行は `{ "speaker", "text", "text_en" }`。`DialogueData` は既にオブジェクト形式（speaker 省略時は NPC）を受け付ける。

---

## 3. `Localization/ja.json`, `en.json`

```jsonc
{ "locale": "ja", "display_name": "日本語", "strings": { "<key>": "<text>", ... } }   // 399 キー、ja/en で完全一致
```

キーの接頭辞:

| 接頭辞 | 内容 | 例 |
|--------|------|----|
| `ui.title.*` `ui.select.*` `ui.pause.*` `ui.settings.*` `ui.hud.*` `ui.interact.*` `ui.questlog.*` `ui.collection.*` `ui.result.*` `ui.controls.*` `ui.credits.*` | UI 文言 | `ui.pause.resume` = 「ゲームに戻る」 |
| `ui.building.<id>` `ui.place.<id>` `ui.npc.<id>` `ui.item.<id>` | 固有名 | `ui.building.kyoso` |
| `quest.<qid>.title/summary/reward` `quest.<qid>.<stepId>` | クエスト | `quest.q_gym.s1` |
| `dialogue.<npc>.<topicId>.<n>` | 会話行（0 始まり） | `dialogue.inari.t_start.2` |
| `item.<id>.name/hint` `photo.<id>.name/caption` `ach.<id>.name/desc` | 収集物 | |

`{0}` `{1}` は `string.Format` の引数（`ui.hud.time_remaining` = 「残り {0} 秒」）。
クレジットには `ui.credits.map` = 「地図データ © OpenStreetMap contributors (ODbL)」を含む。

### Unity 側

- **`Localization`**（static class `L`）: `L.Load(locale)` で `KCD/Localization/<locale>` を parse、`L.Get(key)`（無ければ key を返す）、`L.Format(key, args)`、`L.Locale`、`event OnLocaleChanged`。既定 `ja`。設定画面 `ui.settings.language` で切替、`PlayerPrefs["kcd.locale"]` に保存。
- 既存 UI（TitleScreen / PauseMenu / HUD / QuestLog / CharacterSelect）のハードコード文字列は `L.Get("ui.*")` に置き換える。時間帯表示は `ui.hud.time_*`。
- クエスト・会話は JSON 側の `text` / `text_en` を **直接** 使うのが簡単: `QuestData` / `DialogueData` に `TextEn` 等を足し、`L.Locale == "en" && !string.IsNullOrEmpty(TextEn)` なら英語を返す `LocalizedText` ヘルパを 1 つ用意する（`quest.*` / `dialogue.*` キーは外部ツール・検索用の写しで、ランタイムはどちらを読んでもよい）。

---

## 4. `Mobs/schedule.json`

```jsonc
{
  "spawn_stagger_seconds": 2.5,   // 同じ帯の中で 1 体ずつずらして出す
  "respawn_delay_seconds": 6.0,   // despawn 後に次を出すまで
  "avoid_player_radius": 1.2,
  "variants": [ Variant × 6 ],
  "bands":    [ Band × 4 ]
}
```

### Variant（見た目パラメータ。1 種のモブメッシュに色だけ当てる）

| キー | 型 | 意味 |
|------|----|------|
| `id` | string | `mob_a`〜`mob_f` |
| `hair`, `top`, `bottom`, `shoes`, `skin` | `#rrggbb` | `MaterialPropertyBlock` で `_BaseColor` を差し替えるスロット名 |
| `hair_style` | string | `short long mushroom ponytail crop bob`（メッシュ側で対応する髪パーツを表示、無ければ無視） |
| `height` | float | ルートの Y スケール（m 換算。1.62 = 基準 1.65 の 0.98 倍） |
| `bag` | string | `backpack tote shoulder none`（付属物、無ければ無視） |

### Band

| キー | 型 | 意味 |
|------|----|------|
| `id` | string | `morning lunch afternoon evening` |
| `from_hour`, `to_hour` | float | `GameManager.GameTimeHours` がこの範囲のとき有効（帯外は徐々に despawn） |
| `count` | int | 同時に出す人数（12〜24） |
| `walk_speed` | float | `NavMeshAgent.speed` (m/s) |
| `routes[]` | | `weight` で抽選。`waypoints[]` を順に歩き、`end: "despawn"` で最終点で消える |

### Waypoint（4 種）

| 形 | 解決方法 |
|----|----------|
| `{"place": "gate_main"}` | `CampusProps.PlaceZones` の VisitZone 位置 |
| `{"entrance": "kyoso"}` | `Models/Interiors/<id>.json` の `entrance_world`（= `EntranceTrigger` の位置） |
| `{"x": 99.2, "z": -71.7, "wait": 40}` | campus 座標。`wait` 秒（実時間）その場で待つ（省略 0） |
| `{"poi": "poi_kyoso_lounge"}` | 屋内 Empty（現データでは未使用。将来の屋内モブ用） |

帯ごとの流れ: 朝 = 正門→講義棟/共創棟/食堂/図書館、昼休み = 講義棟・第1研究棟→**第2研究棟 1F 食堂** と **共創棟**、午後 = 実験棟・図書館・体育館へ分散、夕方 = 各棟→**正門**。

### Unity 側

- **`MobScheduler`**（MonoBehaviour）: 帯切替を `GameManager.GameTimeHours` で監視し、`count` を満たすよう `spawn_stagger_seconds` 間隔で `MobWalker` を生成。route は `weight` 抽選、最初の waypoint に出現して `NavMeshAgent.SetDestination` で順に進む。到達判定 `remainingDistance < 0.6`。
- **`MobWalker`**: variant の色を `MaterialPropertyBlock` で適用、`NPCWander` は使わない（経路固定）。既存の歩行アニメを流用。
- 帯の重なり（11.0〜11.5 は無帯）はそのまま「人が減る」時間として扱う。

---

## 5. `Ending/result.json`

```jsonc
{
  "day_end_hour": 20.0,
  "weights": { "quests": 0.45, "collectibles": 0.30, "photos": 0.15, "buildings": 0.10 },
  "totals":  { "quests": 13,   "collectibles": 20,   "photos": 6,    "buildings": 9 },
  "ranks": [ { "rank": "S", "min_percent": 90, "title_ja", "title_en", "comment_ja", "comment_en" }, A 70, B 45, C 0 ]
}
```

達成率 = Σ weights[k] × (達成数[k] / totals[k]) × 100。`ranks` を上から見て `percent >= min_percent` の最初のものを採用。

### Unity 側

- **`DayEndEvaluator`**: `GameTimeHours >= day_end_hour` になったら（または `q_sq_night_walk` 達成後にポーズメニューから「一日を終える」）、`QuestSystem` / `AchievementSystem` から数を集めて `ResultData` を作る。
- **`ResultScreen`**（UI）: `ui.result.*` で見出し、ランク文字・`title_*`・`comment_*` を表示。「もう一日歩く」= 時刻を 08:30 に戻して進行を保持、「タイトルへ」。

---

## 6. 変更したファイル一覧

| パス | 変更 |
|------|------|
| `Data/Collectibles/collectibles.json` | 新規 |
| `Data/Quests/q_sq_greenhouse.json` ほか `q_sq_*` 6 本 | 新規 |
| `Data/Quests/q_orientation.json` ほか既存 7 本 | `title_en / summary_en / rewardText_en / steps[].text_en` を追加（既存キーは不変） |
| `Data/Dialogue/{inari,kaname,sora,prof}.json` | 行を `{text, text_en}` 化、`t_sq_*` 話題 3 件ずつを `t_idle` 直前に追加 |
| `Data/Localization/ja.json`, `en.json` | 新規（399 キー） |
| `Data/Mobs/schedule.json` | 新規 |
| `Data/Ending/result.json` | 新規 |

生成スクリプトは使い捨て（scratchpad）。再生成が必要なら本仕様から書き直す。
