# ストーリーを作り直すための調査（#410）

通しで遊んで「次に何をどこでするか」が分かり、一日として面白いストーリーに作り直すための調査をまとめる。調べたのは 2026-10-29〜30 で、ゲームの事実は `dev/game` の c3d7679c で確かめた。外部の文章は要約し、出典には実際に開いて中身を確かめたページだけを載せた（Wikipedia は作品の概要と用語の確認にだけ使った）。調べた順のメモは #410 のコメントにある（[外部ツール](https://github.com/takumayellow/katsushika-campus-days/issues/410#issuecomment-6498472813)・[導線](https://github.com/takumayellow/katsushika-campus-days/issues/410#issuecomment-6498493518)・[面白さの構成](https://github.com/takumayellow/katsushika-campus-days/issues/410#issuecomment-6498508701)・[理科大の題材](https://github.com/takumayellow/katsushika-campus-days/issues/410#issuecomment-6499226265)）。

## 結論

- **導線**: 図書館の「迷子の本」が見つからないのは、探す本がゲームの中に物として無く、本の見た目も階（2F）もプレイヤーに伝えていないから。本を 2F の机に物として置いて拾わせ、依頼の場面で表紙を見せ、1F の司書に「2F の開架書架」と言わせる。ほかのクエストも「文面に建物・階・目印を書く → 近づくと音 → 見つけたら演出して線で消す」にそろえる。3D 空間に浮かぶ目印は使わない（#163）。
- **構成**: 今は「7 本の用事 → 18 時の夕日 → 20 時の達成率ランク」で、用事どうしをつなぐ筋が無い。登校の直後にいなりが「18 時に水盤で夕日を見よう。今日会った人を誘っておいで」と目的を一つだけ渡し、各クエストを「その人を 18 時に誘う理由」にする。12:00 と 17:00 のチャイムで一日を区切り、昼を誰と過ごすかを選ばせて、18 時に回収する。
- **理科大の題材**: 芯にできるのは 2 本。1881 年に小学校の教室を借りて毎夕ひらいた物理の講習所（夕方に器具を運び込み、夜更けに返した）と、製紙工場の跡に塀の無いキャンパスとして 2013 年に開いた葛飾キャンパス。『坊っちゃん』の話は小説と史実から引き、大学の公式キャラクターには台詞や設定を今より足さない。
- **外部ツール**: 会話やクエストを書く道具は入れない。今の JSON のまま、依存図・詰みの探索・案内の抜けを調べる Python の検査を `tools/` に足し、CI に載せる。

## 0. 今のゲームで分かっていること

Unity 側のファイルは `unity/KatsushikaCampusDays/Assets/` からのパスで書く。

### 0.1 クエストと一日

メインは 8 本ある。

| クエスト | 題 | すること |
|---|---|---|
| `q_orientation` | はじめての登校 | `gate_main` → `campus_mall` → `prof` と話す |
| `q_coffee` | メインストリートでひと休み | `mall_bench` → `sora` と話す |
| `q_gym` | 体育館まで 60 秒 | `gym` に入る（頼むのは `prof`） |
| `q_lecture` | はじめての講義 | `lecture` に入る → `poi_lecture_hall_seats` → `lecture` と話す |
| `q_library` | いなり先輩の取り置き | `library` に入る → `poi_library_counter` → `inari` と話す |
| `q_lunch` | 食堂のいちばん忙しい時間 | `milk` を拾う → `kaname` と話す |
| `q_park` | みらい公園の落とし物 | 葉を 5 枚拾う |
| `q_sunset` | 水盤に落ちる夕日 | 18:00 以降に `library_pond` へ行く |

`q_orientation`・`q_park`・`q_sunset` は自動で始まる。`q_park` は `q_library` の後に、`q_sunset` はほかのメイン 7 本をすべて終えた後に始まる。

サブは 10 本ある: `q_secret_spring_hunt`（20 番のメモ `c_dome_key` → `dome_door` → `inari`）、`q_sq_first_program`（`ta_306` → フラグ `first_program_ran` → `ta_306` → `lecture`）、`q_sq_greenhouse`（`q_gym` の後）、`q_sq_hello_world`「黒板の 1 バイト」（`board_203`・`302`・`402`・`502`・`602` の黒板 → フラグ `hello_world_reply` → `lecture`）、`q_sq_lab_notebook`（`q_coffee` の後。`lab1` → `c_lab_goggles` → `sora`）、`q_sq_lab_tour`（`research1` → `notice` → `lab_a` → `prof`）、`q_sq_lost_card`（`q_lunch` の後）、`q_sq_night_walk`（自動。19:00 以降に `gate_main`）、`q_sq_photo_walk`（自動。撮影ポイント 3 か所）、`q_sq_stray_book`（0.2 節）。

一日の流れ:

- **時計**: `Scripts/Runtime/World/DayNightCycle.cs` で、一日は実時間 720 秒（18 行目）、始まりは 8:30（20 行目）。実時間 1 秒がゲームの 2 分にあたり、8:30 から 20:00 までは約 345 秒（6 分弱）しかない。時計を止める `Paused`（122 行目）を立てるのはスモーク用の `SmokeDirector` だけなので、会話中も時計は進む（20 秒の会話でゲームの 40 分）。会話中に止まるのは、制限時間つきクエストのタイマーだけ（`QuestSystem.Tick` は `KCDInput.GameplayBlocked` の間は数えない）。
- **区切り**: 学校のチャイムは 12:00 と 17:00 に鳴る（`AudioManager.Scene.cs:196`）。
- **終わり**: 20:00 に `Data/Ending/result.json` のリザルトが出る。クエスト 0.45・収集物 0.3・写真 0.15・建物 0.1 の重みで達成率を出し、S（90 以上）「葛飾を歩ききった」から C「はじめての登校」までのランクを付ける。寮で寝る裏エンド（`Scripts/Runtime/World/DormEnding.cs`、#41）はランクも達成率も出さない。
- **目的**: 一日の目的はどこにも示されない。`q_orientation` は教授への挨拶で終わり、`q_sunset` の summary が「今日の用事をすべて済ませたら、その時間に水盤の前へ。」と言うだけ。
- **選択**: 選択肢（`t_choice`）は inari・kaname・prof・sora にある。選ぶと好感度が 0〜2 上がり、2 になると話題が一つ増える（`requiresAffinity`）。ただし選んだ結果は、18 時の夕日にも 20 時のリザルトにも見える形では出てこない（リザルトの重みにも好感度は入っていない）。

### 0.2 迷子の本（`q_sq_stray_book`）

- 始まりは、`q_park` を終えた後の inari の話題 `t_sq_stray_book_start`（1 回だけ）。
- ステップは 5 つ: s1 `library` に入る → s2 `poi_library_stacks`「開架書架のいちばん奥を探す」→ s3 `poi_library_desk`「個人閲覧ブースを探す」→ s4 `poi_library_counter`「カウンターに本の場所を伝える」→ s5 `inari` と話す。
- s2・s3・s4 は、場所に着けば済む visit。判定は 3D の箱（`Scripts/Runtime/World/VisitZone.cs`、既定の大きさ 10×6×10）。探す本はゲームの中に物として無く、拾う場面も無い。s4 も司書とは話さない。
- s2 の (30.0, 5.0, 46.6) と s3 の (−1.0, 5.0, 56.5) は 2F、s4 の (−11.6, 0, 25.0) は 1F にある。「2F」はクエスト文にも台詞にも出てこない。
- ミニマップ（`Minimap.cs`）は、目的地が外なら出口を指し（13 行目）、見出しに今いる建物と階を出す（76 行目）。目的地の階との差を扱う処理は、grep した範囲では見つからなかった。
- 言葉の食い違いがある。クエスト文の「個人閲覧ブース」に対し、館内の吊り看板 `sign_library_2` は「閲覧席」/「Study Carrels」。図書館の仕様（`docs/ref/interiors/library/spec.md`）は、実物の 1F にある書架 14 連と個人閲覧ブース 66 席を作らず、14 連を 2F に移すと決めている（403 行目）。2F の閲覧席はキャレル 9 本で 68 席と、窓際のカウンター 12 席（128 行目）。看板の文言は「2F 開架書架・閲覧席・黙考書院」（260 行目）。
- 収集物 `c_library_bookmark`「閉架の栞」は s2 と同じ `poi_library_stacks` にあり、ヒントは「開架書架、14 連のいちばん奥」。本を物として置くなら、この栞と見分けがつくようにする。
- 1F の司書 `npc_library_1`（−15.5, 0, 25.5）はカウンターの近くに立っているが、`Data/Dialogue/` に図書館の NPC の会話ファイルは無い。

### 0.3 データどうしの食い違い

- `ja.json` の `quest.q_library.summary`（257 行目）と `dialogue.inari.t_active.1`（341 行目）は、大ホール（600 席）が屋上にあるように書いている。公式の説明では、600 席の大ホールは図書館棟の 3・4 階にある（3.1 節）。空中写真では図書館の屋根に八角形の金属ドームが見える（`docs/ref/aerial/README.md:44`）が、「八角形」は公式の文では確かめられなかった。`item.c_dome_key.hint` も「屋上のホールに続く鍵」と書いている。仕様の側は 3〜4F を大ホールにする案を出している（`spec.md:435`）。
- `docs/DESIGN.md:29` では主人公のみらいが「工学部情報工学科 2 年」だが、ゲームの中では新入生として扱われている（`prof.json:56` の「設備は入学式でしか見られん」、`prof.json:77` の「新入生向けに短い講義をする」、`lecture.json:115` の「毎年、新入生が来る前に黒板に書いておく」）。
- `docs/HOW_TO_PLAY.md:80` は「サブクエスト 6 本」と書いているが、今のサブは 10 本ある。
- 宝探しには「21 枚目のメモ（白紙）」がある（`ja.json` の「21 枚目は、あなたが持っていて。来年の春は、あなたが隠す番。」）。毎年受け継ぐ伝統は、すでにゲームの中にある。

## 1. 導線: 何をどこで探すかを伝える

### 1.1 技法

| 技法 | 例 | 出典 | KCD ならこうする |
|---|---|---|---|
| 1. 次の目印を前の地点から見せる（weenie） | ディズニーランドは区域ごとに遠くから見える目印を置いて人を引き寄せる。マリオ オデッセイの砂の国は、町 → 白い塔 → 逆さピラミッドの順に、一つ前の地点から次が見える | [The Level Design Book: Disneyland](https://book.leveldesignbook.com/studies/irl/disneyland)、[Game Developer: What Mario Learned from Mickey Mouse, Part 3](https://www.gamedeveloper.com/design/what-mario-learned-from-mickey-mouse---part-3-decision-making-and-weenies) | 外では図書館の屋根のドームを目印にする。館内では入口 → 中央ロビー → 段状ギャラリーの階段 → 2F の吊り看板「開架書架」と、一つ前の地点から次が見えるかを確かめる |
| 2. 寄りたくなる物を見える所に置く（引力） | BotW は祠を目立たせ、塔から見える位置に次の目標を置いた。A Short Hike は山頂を見える目標にし、目を引く目印と収集物で新しい区域へ誘った | [ファミ通: CEDEC 2017 BotW](https://www.famitsu.com/news/201708/31140870.html)、[Game Developer: Road to the IGF, A Short Hike](https://www.gamedeveloper.com/business/road-to-the-igf-adam-robinson-yu-s-i-a-short-hike-i-)、[80.lv: GDC A Short Hike Postmortem](https://80.lv/articles/gdc-a-short-hike-postmortem) | 迷っても楽しいのは目標が見えているときなので、見えない本を探させない。書架の通路の入口から見える窓際の机に本を置く |
| 3. 視線を遮り、回り込むと見せる（三角形の法則） | BotW は三角形の山などで視線を遮り、回り込むと次の目標が現れるようにした | [ファミ通: CEDEC 2017 BotW](https://www.famitsu.com/news/201708/31140870.html) | 書架の列の切れ目を通るたびに、奥の窓際の明かりが見える並びにする |
| 4. 光と色で示す | Left 4 Dead は光と色で進む先を示す。触れる物に塗る黄色い印は目立つが没入を損なうと批判され、消せる設定にするなら印なしで導ける作りが別に要る | [The Level Design Book: Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding)、[Wikipedia: Yellow paint in video games](https://en.wikipedia.org/wiki/Yellow_paint_debate) | 浮かぶ目印を外した分（#163）を館内の明かりで補う。本のある机のスタンドだけ点け、本の背を館内のほかの本に無い色にする |
| 5. 文面の言葉を看板にそろえる | 看板は背景と見なされ、見落とされやすい | [The Level Design Book: Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding) | 「個人閲覧ブース」を看板どおり「閲覧席」に、「開架書架のいちばん奥」を「2F の開架書架、14 本目の裏の通路」に直す。看板だけに頼らず、台詞でも言わせる |
| 6. 台詞と日誌で階・方角・目印を伝える | Morrowind は目的地の目印を出さず、NPC が口で道を教えて日誌に残る | [CBR: Morrowind's Missing Feature](https://www.cbr.com/bethesda-elder-scrolls-morrowind-games-without-quest-markers/) | いなりの依頼を「2F に上がって、窓側の開架書架の 14 本目の裏」のように言わせ、右上の追跡表示の 1 行にも「2F」を入れる |
| 7. NPC の道案内 | NPC の先導やパンくずは「状況に応じて使う」段の手がかりにあたる | [The Level Design Book: Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding) | 1F の司書（`npc_library_1`）に「2F の開架書架」、2F の書架のそばに立つ NPC（`npc_library_7`）に「奥の窓際に誰かが本を置いていった」と言わせる |
| 8. 求めたときだけ向きを示す（控えめな案） | TLOU2 はボタンを押すとカメラが次の行き先を向く。Ghost of Tsushima は HUD の代わりに、呼ぶと目標の方向へ風が吹く | [PlayStation Blog: TLOU2 accessibility](https://blog.playstation.com/2020/06/09/the-last-of-us-part-ii-accessibility-features-detailed/)、[Game Developer: Vorticles in Ghost of Tsushima](https://www.gamedeveloper.com/design/using-vorticles-to-simulate-wind-in-i-ghost-of-tsushima-i-) | 追跡表示を押したとき（スマホはタップ）だけ、1 秒カメラを目的地へ向ける。押したときしか出ないので #163 の方針と矛盾しない。設定で切れるようにする |
| 9. 探す物の見た目を先に見せる | Hidden Folks は画面下の帯に探す対象を並べる。TotK の図鑑センサーは図鑑に載せた物を探す対象にする | [itch.io: Hidden Folks](https://adriaan.itch.io/hidden-folks)、[GAME Watch: TotK 図鑑センサー](https://game.watch.impress.co.jp/docs/news/1508412.html) | 依頼の場面で表紙（色・題）を見せ、Tab のクエストログにも表紙の絵を出す |
| 10. 文字の手がかりで範囲を絞る | Hidden Folks は対象を押すと、区域を示す手がかりか物を名指しする手がかりが出る | [Game Developer: Hidden Folks](https://www.gamedeveloper.com/design/building-thousands-of-tiny-interactions-into-i-hidden-folks-i-) | 「14 連のいちばん奥」「66 席のどこか」を「14 本目の裏、窓際の机の上」のように 1 か所に絞り、ログに残す |
| 11. 落とし物に持ち主の手がかりを付ける | あつまれ どうぶつの森の落とし物は、調べると持ち主の性格に合った手がかりが読める | [Nookipedia: Lost item](https://nookipedia.com/wiki/Lost_item) | 拾ったときの説明文で「いなりの本だ」と分かるようにする |
| 12. 近づくと音で知らせる | TLOU2 の聴覚モードは対象の位置で音が鳴り、高さの差で音程が変わる。TotK の図鑑センサーは近いと音で知らせ、上下にも反応する | 8 の PlayStation Blog、9 の GAME Watch | 本から数 m でページをめくる音を鳴らす。1F にいる間は追跡表示に「2F」と出す |
| 13. 探した場所を地図に残す | バイオハザード RE:2 の地図は、探し残した部屋を赤、探し終えた部屋を青で示す | [PCGamesN: RE2 remake の地図](https://www.pcgamesn.com/resident-evil-2/resident-evil-2-remake-capcom-map) | Tab の地図とミニマップで、見て回った候補に印を付け、同じ所を何度も歩かせない |
| 14. 手がかりの記録から行き先を選ばせる | Outer Wilds の Ship Log は情報どうしの関係を示して新しい場所へ導き、ループしても記録は消えない。作り手は「指示ではなく示唆」を目指した | [The Ship Log in Outer Wilds (PDF)](https://paigecuthbertson.com/pdfs/The_Ship_Log_in_Outer_Wilds.pdf)、[Point'n Think: Loan Verneau](https://www.pointnthink.fr/en/loan-verneau-creative-lead-at-mobius-digital-on-outer-wilds/) | 「もう一日歩く」でも消えない手がかりの一覧を Tab に置く。項目を選ぶと、ミニマップの輪がそこに出る |
| 15. やることリストを線で消す | Untitled Goose Game はやることリストの項目を線で消していく | [Panic Podcast S01E01](https://podcast.panic.com/episodes/s01e01/transcript/)、[Game Developer: Behind the HONK](https://www.gamedeveloper.com/design/behind-the-honk-an-i-untitled-goose-game-i-q-a) | 追跡表示とログの行を、済んだ瞬間に線で消してから次の行を出す |
| 16. 見つけた瞬間の演出と、一日の振り返り | なつもん！の絵日記は、繰り返す毎日の小さな出来事を特別にし、後から見返せるご褒美にしている | [Real Sound: なつもん！インタビュー](https://realsound.jp/tech/2023/10/post-1450690.html) | 本を拾ったら本に寄るカットと効果音を出す。20:00 に「今日見つけたもの」を絵日記のように残す |
| 17. 手がかりを気付かれやすさで重ねる | 手がかりを、気付かれにくい順に音・動物 → 光と色・看板・構図 → NPC の先導・weenie → 常に出る UI の 4 段に分ける | [The Level Design Book: Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding) | 迷子の本で「配置と明かり → 学生と司書の台詞 → ミニマップの輪」の 3 段がそろっているかを点検する。今は台詞の範囲が広すぎ、輪に階が出ない |
| 18. 遊んだ人の移動経路で確かめる | BotW はテストプレイヤーの移動経路をヒートマップにして確かめた | [ファミ通: CEDEC 2017 BotW](https://www.famitsu.com/news/201708/31140870.html) | 図書館に入ってから本を見つけるまで、位置を数秒おきに記録して平面図に重ね、迷う場所と時間を測る |

### 1.2 迷子の本の直し方

3 つの案を比べた。

- **案 A 見せてから探させる**（技法 2・4・9・12・15・16）: 依頼の場面で表紙を見せ、s2・s3 の visit を一つの collect にまとめる。本は 2F 開架書架の 14 本目の裏の突き当たり、窓際の閲覧机に置く。机のスタンドだけを点け、本から数 m で紙の音を鳴らす。拾ったら本に寄るカットと効果音を出し、追跡表示の行を線で消す。
- **案 B 候補を絞って消し込む**（技法 10・13・14・15）: 候補を 2〜3 か所にして Tab のログに並べ、着いた候補に「ここには無い」と印を付けて地図にも残す。外れの場所で次の手がかりを拾わせる。
- **案 C 人に聞くほど場所が具体的になる**（技法 6・7・17）: 司書が「2F の開架書架」、2F の NPC が「奥の窓際」と、区画 → 列 → 机の順に絞って教える。

勧めるのは A と C の組み合わせ。ステップは次のようにする。

1. `library` に入る（今の s1 のまま）。
2. 1F の司書 `npc_library_1` と話す。司書が「2F の開架書架、14 本目の裏の窓際」と教える（会話ファイルを新しく作る）。
3. 本を拾う（collect）。置き場所と演出は案 A のとおり。
4. カウンターで司書に本を渡す（talk）。
5. `inari` と話す。

本は机の上に置き、表紙の色を館内のほかの本に無い色にして、同じ通路の `c_library_bookmark`（本と本のあいだの栞）と見分けられるようにする。`q_sq_stray_book` の報酬は収集物 `c_inari_bookmark`（いなりの二枚目の押し花の栞）なので、本に栞を挟む演出を足すなら、この報酬と重ならないようにする。

### 1.3 全クエストに効くきまり

1. 文面に建物・階・目印を書く。言葉は館内の看板にそろえる。
2. 探す物は collect にして、ゲームの中に物を置く。場所に着くだけの visit で「探す」と書かない。
3. 目的地の階をミニマップの見出しか追跡表示に出す。
4. 近づくと音で知らせ、見つけたら演出して行を線で消す。
5. 受注中に行き先を聞ける話題を置く。今は自動で始まる 5 本（`q_orientation`・`q_park`・`q_sunset`・`q_sq_night_walk`・`q_sq_photo_walk`）に、受注中に行き先を教える話題（`requiresActiveQuest`）が無い。
6. 3D 空間に浮かぶ目印は出さない（#163）。求めたときだけの補助（1.1 の技法 8）は設定で切れるようにする。

## 2. 面白さの構成

### 2.1 今の問題

- 一日の目的が示されない。用事を 7 本済ませると夕日に呼ばれるだけで、用事どうしにつながりが無い（0.1 節）。
- 選択肢は好感度を動かし、2 まで上がると話題が一つ増えるが、18 時の夕日にも 20 時のリザルトにも響かない。
- 8:30 から 20:00 までが実時間で 6 分弱しかなく、会話中も時計が進む。人の予定を追う遊びを足すなら、先に時間の流れ方を決める必要がある（2.4 節）。

### 2.2 観点

| 観点 | 例 | 出典 | KCD ならこうする |
|---|---|---|---|
| 目的は最初に一つだけ示し、サブはそれに効かせる | A Short Hike は「電波は山頂だけ」で山頂を目指させ、金の羽で登れる範囲が広がる。Lil Gator Game は忙しい姉をまた遊ばせるために公園の子を集める。TOEM は頼まれごとを解くとスタンプがもらえ、集まると次の地域へ行ける。Alba はホテル建設に反対する署名を、小さな善行で集める | [Wikipedia: A Short Hike](https://en.wikipedia.org/wiki/A_Short_Hike)、[Wikipedia: Lil Gator Game](https://en.wikipedia.org/wiki/Lil_Gator_Game)、[Wikipedia: Toem](https://en.wikipedia.org/wiki/Toem)、[Pocket Tactics: Alba](https://www.pockettactics.com/alba-a-wildlife-adventure/nature) | 手帳の 1 行目を「18:00 水盤で夕日（誘った人 0）」にする。サブの報酬を「18 時に行くという返事」か「黒板の 1 バイト」にそろえる |
| 馴染んでから好奇心を引く | Outer Wilds の初期案の隕石は、まだ村を知らないプレイヤーには響かなかった | [Game Developer: Live, die, repeat (Outer Wilds)](https://www.gamedeveloper.com/design/live-die-repeat-how-i-outer-wilds-i-piques-curiosity-in-an-ambivalent-solar-system) | 18 時の誘いは、その人と会って話した後に出す |
| 一日を起承転結で組む | Super Mario 3D Land はステージを導入 → 複雑化 → ひねり → 習熟の 4 段で組んだ | [Game Developer: The Structure of Fun](https://www.gamedeveloper.com/design/the-structure-of-fun-learning-from-i-super-mario-3d-land-i-s-director) | 起 8:30 の登校、承 午前（12:00 のチャイムで締める）、転 午後から 17:00 のチャイム、結 18:00 の水盤。チャイムで BGM が消える間を章の区切りに使う |
| 人の予定を手帳に書き、全部は回れない中で選ばせる | ムジュラの仮面は 3 日では足りない仕組みで緊張を作り、人の予定が手帳に書き込まれる（周期は初め 1 週間で、3 日に落ち着いた）。Pentiment は期限を意図して置き、全部は回れない。Dead Rising は時計が実時間より速く、依頼を落としてもゲームは続く | [社長が訊く: ムジュラの仮面 3D](https://www.nintendo.co.jp/3ds/interview/ajrj/vol1/index6.html)、[Nintendo Life: Iwata Asks, Majora's Mask 3D](https://www.nintendolife.com/news/2015/02/iwata_asks_returns_for_the_legend_of_zelda_majoras_mask_3d)、[MMORPG.com: Pentiment](https://www.mmorpg.com/interviews/the-rpg-files-building-obsidians-pentiment-interview-with-game-director-josh-sawyer-2000126821)、[Wikipedia: Dead Rising](https://en.wikipedia.org/wiki/Dead_Rising_%28video_game%29) | 手帳に「12:00 かなめの昼休み」「17:00 そらがゼミを決める」と人の予定を出し、30 分前に知らせる。「もう一日歩く」で翌日に回せるので、締め切りは柔らかくてよい |
| 懸かっているものを先に見せ、後で回収する | Witcher 3 と Cyberpunk 2077 のクエスト設計は、選ばせる前に懸かっているものを示し、前の選択を後で見える形で回収する。The Walking Dead（2012）は、相手の見方が変わったことを通知で知らせる | [Game Developer: Key takeaways from the quest design of Cyberpunk 2077 and The Witcher 3](https://www.gamedeveloper.com/marketing/10-key-takeaways-from-the-quest-design-of-cyberpunk-2077-and-the-witcher-3)、[Wikipedia: The Walking Dead (2012)](https://en.wikipedia.org/wiki/The_Walking_Dead_%282012_video_game%29) | 12:00 に「昼を誰と過ごすか」を選ばせ、選ぶ前に「18 時の最後の会話が変わる」と示す。選んだときに「そらは覚えている」のような小さな通知を出す |
| エンドは一つにして差分を付ける | Night in the Woods は、エンドが一番関わった友人で変わる | [Wikipedia: Night in the Woods](https://en.wikipedia.org/wiki/Night_in_the_Woods) | エンドは 18 時の水盤の一つのまま、来た人と昼の相手で最後の会話を変える |
| 時間の圧を弱める | 居心地のよいゲームの報告は、時間の圧が取り逃しへの焦りを生んで心地よさを損なうとし、活動を任意参加にすることを勧めている | [Project Horseshoe 2017: Cozy Games](https://www.projecthorseshoe.com/reports/featured/ph17r3.htm) | 誘えなかった人は手帳に残し、翌日に誘い直せるようにする |
| 一日を振り返る | なつもん！の絵日記（1.1 の技法 16） | [Real Sound: なつもん！インタビュー](https://realsound.jp/tech/2023/10/post-1450690.html) | 20:00 のリザルトは「今日の一枚」（写真散歩の写真）と会った人の絵日記を主役にし、ランクは添える程度にする |

### 2.3 骨組みの案

**案 A（勧める）: 18 時に誰と夕日を見るか**

1. 8:30 の登校の最後に、いなりが「18 時に水盤で夕日を見よう。今日会った人を誘っておいで」と言う。
2. 午前の用事は、その人に 18 時の返事をもらう理由にする（体育館まで走る、取り置きの本を受け取る、など）。
3. 12:00 のチャイムで、昼を誰と過ごすかを選ぶ（かなめの食堂 / そら / いなり）。
4. 午後は、そらのゼミ選びを縦糸にする。研究室をのぞく・実験ノート・306 の HELLO WORLD を一緒に見るたびに、そらの迷いが一つ減る。
5. 17:00 のチャイムでそらが決め、プレイヤーはまだ返事をくれていない人を迎えに行く。
6. 18:00 の水盤には返事をくれた人が並び、昼の相手で最後の会話が変わる。
7. 20:00 は絵日記で一日を振り返る。来なかった人は翌日に誘い直せる。

誰の 18 時にもつながらない用事は、つなぐ相手を決めるか外す。寮で寝る裏エンドは残す。

**案 B: 黒板の 1 バイトと 21 枚目の白紙**

2F〜6F の黒板を、その階の人の頼みを解くと読めるようにする。17:00 に 7F の黒板に WORLD を書き足し、宝探しの 21 枚目の白紙（0.3 節）を「来年の春は、あなたが隠す番」という後日談にする。

A と B は両立する。A を一日の筋にし、B を理科大らしさの柱か後日談にする。

### 2.4 先に決めること

- **時間の流れ方**: 会話中は時計を止める（勧める）か、一日に置く用事を減らす。今のまま人の予定を手帳に足すと、会話だけで予定に間に合わなくなる。
- **主人公の学年**: ゲームの中の扱いに合わせて新入生にそろえ、`docs/DESIGN.md:29` を直す。

## 3. 理科大の題材

### 3.1 確かめた事実

| 項目 | 事実 | 出典 |
|---|---|---|
| 創立 | 1881 年 6 月の新聞広告で東京物理学講習所を告知した。土日を除く毎夕、実験を主とする物理を教える。創立者 21 名は東京大学を出た若い理学士が中心 | [大学の沿革（PDF）](https://www.tus.ac.jp/about/data/data/file/wp22_03.pdf)、[大学の歴史](https://www.tus.ac.jp/about/university/history/) |
| 夜の教室 | 1881 年 9 月 11 日に開校。九段下の稚松小学校の一部を借り、夕方に器具を運び込んで夜更けに返した。千代田区の観光サイトが発祥の地として紹介している | [大学の沿革（PDF）](https://www.tus.ac.jp/about/data/data/file/wp22_03.pdf)、[千代田区観光協会](https://visit-chiyoda.tokyo/app/spot/detail/40) |
| 苦しい時期 | 生徒は当初 20 人ほどで、1 人の時期もあった。1885 年に 21 名中 16 名が維持同盟を結んで学校を支えた | [大学の沿革（PDF）](https://www.tus.ac.jp/about/data/data/file/wp22_03.pdf) |
| 物理学校と神楽坂 | 1883 年に東京物理学校と改称。1906 年 9 月に神楽坂の校舎が落成した。1923 年からの二部制で、第二部は 17:20〜21:10 | 同上 |
| 建学の精神 | 「理学の普及を以て国運発展の基礎とする」 | [建学の精神](https://www.tus.ac.jp/about/university/vision/philosophy/) |
| 実力主義 | 真に実力を身に付けた学生だけを卒業させる伝統を守り、最終学年に卒業研究を置く | [教育の方針](https://www.tus.ac.jp/about/faculty/policy/) |
| 小説の物理学校 | 『坊っちゃん』の主人公は物理学校を 3 年で出た数学教師。兄にもらった六百円を三年に割って学資にし、席順は下から数える方だった。初出は 1906 年 4 月で、神楽坂の校舎の落成と同じ年 | [青空文庫『坊っちゃん』](https://www.aozora.gr.jp/cards/000148/card752.html) |
| 大学の説明 | 三代目校長が漱石と親交があり、主人公が物理学校出なのも「その関係と思われます」と、断定せずに紹介している | [大学の歴史](https://www.tus.ac.jp/about/university/history/) |
| 近代科学資料館 | 1991 年に 1906 年の校舎の正面を再現した | [近代科学資料館](https://www.tus.ac.jp/info/setubi/museum/) |
| 公式キャラクター | 坊っちゃんは創立 125 周年の記念に生まれた（125 周年は 2006 年にあたる）。マドンナちゃんは女子中高生向けの「科学のマドンナ」の広報役。作られた年・作者・人物設定・使用条件の記載は無い | [大学のシンボル](https://www.tus.ac.jp/about/university/symbol/)、[学報 191 号（PDF）](https://www.tus.ac.jp/about/information/publication/magazine/file/no191.pdf) |
| サイトの利用条件 | 著作権は大学などにあり、許可のない転載・複製を断っている。キャラクターの使用申請の窓口は見つからなかった | [サイトポリシー](https://www.tus.ac.jp/sitepolicy/) |
| 敷地の前歴 | 三菱製紙中川工場（2003 年に操業停止）の跡地が、2013 年に公園とキャンパスなどになった。大学は 2009 年 3 月に用地を取得し、2013 年 4 月に開設した | [葛飾区: 地域のあゆみ](https://www.city.katsushika.lg.jp/history/child/2-10-1-102.html)、[大学の沿革（PDF）](https://www.tus.ac.jp/about/data/data/file/wp22_03.pdf) |
| 新宿の地名 | 北条氏が整えた葛西の新しい宿場で「葛西新宿」と呼ばれ、のちに新宿（にいじゅく）になった | [葛飾区: 新宿の由来](https://www.city.katsushika.lg.jp/history/child/3-4-134.html) |
| 公園 | 葛飾にいじゅくみらい公園は 2013 年 4 月 1 日に開園し、キャンパスを囲む。区の科学教育センター「未来わくわく館」は図書館棟の中にある | [葛飾区: にいじゅくみらい公園](https://www.city.katsushika.lg.jp/institution/1000096/1006886.html) |
| 塀の無いキャンパス | 塀が無く誰でも入れる「学園パーク型」で、中央のモールは約 250 m。図書館は 1・2 階（区民も使える）、3・4 階が 600 席の大ホール。設計は「都市公園と一体となったキャンパス」をうたう | [ぶんせき 2017 年 5 月号（PDF）](https://www.jsac.or.jp/bunseki/pdf/bunseki2017/201705konnichiwa.pdf)、[日建設計](https://www.nikken.co.jp/ja/projects/education/tokyo_university_of_science.html) |
| 建物の数字 | 講義棟の大教室は最大 270 名、食堂は全 1,400 席、図書館は約 14 万冊、大ホールは 600 名 | [葛飾キャンパスの紹介](https://www.tus.ac.jp/tuslife/campus/katsushika/) |
| 共創棟と薬学部 | 2024 年 10 月に共創棟が竣工し、2025 年 4 月に薬学部が野田から移った | [大学のニュース](https://www.tus.ac.jp/today/archive/20241024_1017.html)、[大学ジャーナル](https://univ-journal.jp/255915/) |
| 商店会 | 2013 年 8 月、金町駅からキャンパスへ向かう道の商店会が「金町理科大商店会」に改称した | [学報 191 号（PDF）](https://www.tus.ac.jp/about/information/publication/magazine/file/no191.pdf) |
| 理大祭 | 葛飾の理大祭は 11 月に開かれる（2025 年は 11 月 22〜23 日） | [大学のニュース](https://www.tus.ac.jp/today/archive/20251122_3677.html) |
| 情報工学科 | 2016 年に葛飾で設置。1 年でプログラミング演習と工学基礎実験、4 年で卒業研究があり、4 年次は 1 年間研究室に所属する | [工学部情報工学科](https://www.tus.ac.jp/academics/faculty/engineering/information/)、[情報工学科のサイト](https://www.it.tus.ac.jp/about/department/index.html) |

資料どうしの食い違いが 2 つあった。先進工学部への改称年（沿革は 2021 年、10 周年の記事の注記は 2023 年）と、神楽坂への移転年（千代田区の観光サイトは 1904 年、大学の沿革は 1905 年に土地を買い 1906 年に校舎が落成）。どちらも大学の沿革を正とした。

### 3.2 ゲームとの重なり

- **合っている点**: 大教室 270 席、食堂 1,400 席、主人公の「新宿 みらい」とにいじゅくみらい公園、情報工学科、宝探しのメモの「キャンパスが開いた春」（2013 年 4 月の開設と矛盾しない）。
- **食い違い**: 大ホールの位置（0.3 節）。公式では図書館棟の 3・4 階で、屋上ではない。

### 3.3 公式キャラクターの扱い

大学のシンボルのページには、誕生の理由と役目しか書かれていない。人物設定と使用条件は公開されておらず、サイトポリシーは許可のない転載・複製を断っている。`docs/CREDITS.md` にも大学の許諾の記録は無い。物語の芯は小説『坊っちゃん』（青空文庫）と物理学校の史実に置き、公式キャラクターには台詞や設定を今より足さないのが安全と考える（推測であり、法的な助言ではない）。語り手は自作の NPC（いなり・教授）にする。

### 3.4 筋の案

1. **夜の講習所**（17:00 以降、教授）: 1881 年の「夕方に器具を運び込み、夜更けに返す」を再現する。実験棟から大教室へ器具を運び、返し終えたら教授の一言で締める。時刻の目安は物理学校第二部の 17:20〜21:10。
2. **20 枚のメモの由来**（いなり、`q_secret_spring_hunt`）: 宝探しを始めた「最初の学生」を、生徒が 1 人でも教室を開き続けた講習所の話に重ねる。創立者 21 名と、21 枚目の白紙（0.3 節）を重ねて終わる。
3. **紙の工場から本へ**（`q_library` / `q_sq_stray_book`）: 頼まれる本を、この土地が製紙工場だった頃の郷土の本にする。「紙」を手がかりに、公園 → 図書館 1F のカウンター → 2F の棚と順に案内する。
4. **塀の無いキャンパス**（公園から図書館棟、午後）: 未来わくわく館に来た地域の子どもが迷っていて、プレイヤーが案内する。探す物と行き先を、子どもの台詞で具体的に言わせられる。
5. **朝の商店会**（`q_orientation`）: 金町理科大商店会の看板を最初の目印にし、約 250 m のモールを歩いて講義棟の前へ向かわせる。
6. **実力主義と卒業研究**（`q_sq_lab_tour`、4 年生の NPC、夕方）: 4 年生が 1 年間の卒業研究と実力主義の伝統を語り、坊っちゃんの「席順は下から数える方」でも卒業した話と対にして、新入生の不安に応える。
7. **六百円と 600 席**（大ホール、教授）: 小説で学資にした六百円と、大ホールの 600 席が同じ数字であることを一言添える。「1906 年は小説の初出と神楽坂の校舎の落成が重なる年」を 20:00 の振り返りに添える。
8. **理大祭の前の日**（11 月、`q_sunset`）: 準備をする学生が多い日にして、夕焼けを山場にする。ただし今の宝探しは春の話なので、季節をどちらに合わせるかを先に決める。

## 4. 外部ツール

### 4.1 比較

| 道具 | 合うところ | 問題 | ライセンス・費用 | 出典 |
|---|---|---|---|---|
| Yarn Spinner for Unity | .yarn は普通のテキストで AI も書ける。`when:` 付きのノードグループは「条件に合う話題を一つ選ぶ」今の仕組みに近い。VS Code 拡張で図・エラー表示・プレビューが出る | クエストを持たない。話題 88 個を移し、日英の文は行 ID と CSV に変わる。Unity 6.5 以降で IL2CPP のビルドが失敗する不具合が v3.2.8 で直ったばかりで、WebGL で動くという記載は公式の文書に無い。2.x では WebGL で起動時に落ちる報告があった（YarnSpinner-Unity の issue 163。KCD の #163 とは別） | MIT。Git URL / OpenUPM での導入は無料 | [Yarn Spinner Help](https://yarnspinner.dev/help/)、[Node groups](https://docs.yarnspinner.dev/write-yarn-scripts/advanced-scripting/node-groups)、[YarnSpinner-Unity](https://github.com/YarnSpinnerTool/YarnSpinner-Unity)、[YarnSpinner-Unity issue 163](https://github.com/YarnSpinnerTool/YarnSpinner-Unity/issues/163) |
| ink + ink-unity-integration | .ink はテキストで AI も書け、Inky でその場で遊べる | NPC ごとの話題選びとクエストは自前で作る。統合の README にローカライズの記載が無い | MIT、無料 | [ink](https://github.com/inkle/ink)、[ink-unity-integration](https://github.com/inkle/ink-unity-integration) |
| Twine（Twine 2 / Tweego / Cradle） | ブラウザで遊べる下書きには使える | Unity への持ち込み役の Cradle は 2021 年 8 月から更新が無い | Twine 2 は GPL-3.0、Tweego は BSD-2-Clause、Cradle は MIT | [Twine](https://github.com/klembot/twinejs)、[Tweego](https://github.com/tmedwards/tweego)、[Cradle](https://github.com/daterre/Cradle) |
| articy:draft X | 図で書けて、importer にローカライズがある | データが専用ソフトの中にあり、AI がテキストで書けない。importer は Asset Store の EULA なので公開リポジトリに置けない | 無料版は 1 プロジェクト 700 オブジェクトまで | [articy:draft X FREE](https://www.articy.com/en/articydraft/free/) |
| Arcweave | ブラウザで図を書き、JSON を出せる | クラウドの画面で書くので AI が直接書けない | Basic は無料だが 200 items まで・商用不可 | [Arcweave pricing](https://arcweave.com/pricing) |
| Dialogue System for Unity | 会話とクエストの両方を持つ | Lua とデータベースに移し、クエストの仕組みも置き換える。Asset Store の EULA は製品に組み込んだ形でしか配布を認めないので、本体を公開リポジトリに置けない | $95 | [Asset Store Terms](https://unity.com/legal/as-terms) |

どの道具もクエストのステップ（talk / visit / enter / collect / flag）を持たない。今の不満（どこで何を探すか、何がしたいか）は書く道具ではなく案内の中身の問題なので、会話 16 本・話題 88 個・日英の文・会話のテスト・フォントの道具（`tools/make_ui_font.py`）を作り直す手間に見合わない。入れる場合も、`make_ui_font.py` の `TEXT_SUFFIXES` は .yarn / .ink を見ないので、フォントから文字が欠ける。

### 4.2 勧めるもの: 自前の検査

今のテストは、JSON が読めるか・id とファイル名・前提のクエストの実在・日英のキー（`DataFilesTests`・`LocalizationKeysTests`）、貪欲になぞる 3 通りの順序での到達性（`QuestAchievabilityTests`）、ステップの対象がシーンにあるか（`CampusQuestTargetsTests`）、一日を通してランク S まで行くか（PlayMode の `FullDayPlaythroughTests`）を見ている。足りないのは次の 3 つ。

- 分かるかどうかを見ていない。受注中に行き先を教える話題の有無も、ステップの文に行き先が書いてあるかも見ていない（1.3 節の 5）。
- 到達性は 3 通りの順序だけで、取り返せない状態（詰み）を探していない。依存の図も無い（話題 88 個・ステップ 52 個）。
- Unity のテストは CI で回らない。`pytest.yml` は `Assets/**/*.json` の変更で起動するが、クエストや会話の中身を見る Python のテストが無い。

そこで `tools/quest_graph.py` を作る。

1. `Data/Quests` と `Data/Dialogue` の JSON を読み、前提クエスト・`startsQuest`・`requiresActiveQuest` / `requiresCompletedQuest` / `requiresStep`・フラグを辺にする。話題の選び方は `DialogueSystem.SelectTopic` と同じく、上から最初に条件に合うものとする。
2. `--mermaid` で依存図を書き出す（GitHub は Markdown の mermaid ブロックを図にする）。
3. 状態（ステップの位置・立ったフラグ・使った 1 回だけの話題・拾った数）を全部たどり、全クエスト完了に戻れない状態を、そこまでの最短手順つきで出す。`minHour` と `timeLimit` は初めは「待てば満たせる」とみなし、そう出力に書く。
4. 受注中の各ステップに、行き先を教える話題か、ステップの文に行き先の名前があるかを見て、無いものを一覧にする（初めは警告だけ）。
5. `--transcript` で、状態ごとの「受注中のクエスト・ステップの文・話せる話題」をテキストに出す。
6. `tools/tests/test_quest_graph.py` を足し、`pytest.yml` の CI に載せる。`QuestAchievabilityTests` と同じく「18 本すべて完了できる」という結論になることも確かめる。

この規模なら専用のモデル検査器は要らず、総当たりで足りる見込み（似た道具の [quest-engine](https://github.com/Other-Now/quest-engine) は 246 状態を全部たどっている）。図は [GitHub の Mermaid 対応](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/creating-diagrams)でそのまま描ける。検査はビルドに入らないので、WebGL のサイズは増えない。

### 4.3 LLM のプレイテスト

使うなら、`--transcript` の出力を読ませて「次に何をどこでするか」を答えさせ、外れた所の文面を直す補助に限る。CI の合否には使わない。

- 使える面: LLM の成績が、人の感じる難しさと強く相関したという報告がある（Wordle・Slay the Spire、[arXiv 2410.02829](https://arxiv.org/abs/2410.02829)）。TITAN は MMORPG 2 本でタスク達成率 95% を出し、未知の不具合を 4 件見つけた（[arXiv 2509.22170](https://arxiv.org/abs/2509.22170)）。ボードゲームのルールブックから遊び手の感想を出す試みもある（MeepleLM、[arXiv 2601.07251](https://arxiv.org/abs/2601.07251)）。
- 限界: 人が書いたテキストアドベンチャーでは、上位のエージェントでも 15% に届かない（[arXiv 2504.14128](https://arxiv.org/abs/2504.14128)）。

## 5. 確かめたこと / 確かめていないこと

確かめたこと:

- 0 節のゲームの事実は、`dev/game` の c3d7679c のクエスト・会話・ローカライズの JSON、`DayNightCycle.cs`・`QuestSystem.cs`・`Minimap.cs`・`VisitZone.cs`・`DormEnding.cs`・`Data/Ending/result.json`、図書館の `spec.md` を読んで確かめた。
- 1〜4 節の出典は、どれも開いて中身を確かめた。

確かめていないこと:

- 実際に遊んだ感想。0 節はコードとデータを読んだ結果で、実機では確かめていない。ミニマップが目的地の階の差を扱わないことは、grep した範囲での判断。
- 開けなかったページ（GDC Vault、Zelda 系の wiki、Android Central、Red Bull、GamesRadar）の内容は使っていない。
- 理科大: 公式キャラクターの作られた年と作者、図書館の「八角形」「水盤」の公式の記述、2026 年の理大祭とオープンキャンパスの日程、「科学のマドンナ」の開始年。
- ツール: articy の有料版の価格、Dialogue System と Arcweave の WebGL 対応、Yarn Spinner v3.2.8 以降が Unity 6.6 の WebGL で動くか。どの道具も試しておらず、入れたときの WebGL ビルドの増分も測っていない。
