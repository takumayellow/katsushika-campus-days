# 寮（葛飾コミュニティハウス）の中を公式の写真に合わせ、居室の階・大浴場・屋上を足す

ラベル: area:blender, area:unity, quality

## 何が問題か

main にある寮の中（`blender/kcd_route/dorm.py`、`blender/README_dorm.md`）は、ありがちな寮の 1 階を組み合わせたものになっている。

- 16×38 m・天井 2.70 m の 1 フロアで、玄関ホール（郵便受け・掲示板・自販機・管理人室・寮長 `npc_dorm_1` / `npc_dorm_head`）、西のラウンジ、東の食堂がある。その奥は、飾りの扉が並ぶ行き止まりの廊下になっている。
- 実物の間取り・床・壁・家具・色を元にしていない。

PR #87（`dev/dorm-exterior`、未検証）は、公式の設備一覧から 1 階を組み直した。

- 風除室、ホール、管理人室、ラウンジ（橙の壁紙・黄色のソファ）、食堂兼カフェラウンジ、厨房、キッチンコーナー、湯上がり処と大浴場の入口、廊下、EV ホールを入れた。
- ただし次のことが残っている。
  - 間取りは推定である（公開された平面図が無い）。
  - 2 階（女子）と 3〜5 階（男子）の居室の階、各階のランドリー、トイレが無い。
  - Unity で確かめていない。

公式サイトの写真を見ると、1 階の色と家具は PR #87 の方向でおおむね合っている。一方、次の場所の写真もあり、ゲームにはまだ無い。

- 居室（11.34 ㎡）
- 大浴場
- サウナ
- 各階のキッチンコーナー
- ランドリー
- 屋上のスカイテラス

## やること

### 1. 調べる（写真をできるだけ多く集める）

- **1 階**: 風除室とオートロック、郵便受け、管理人室の窓口、食堂（カフェラウンジ）の配膳口と厨房、ラウンジ、湯上がり処、EV ホールと階段。
- **居室の階**: 廊下の幅と床・壁の色、居室の扉、キッチンコーナー、ランドリー、女子階（2 階）のラウンジ、IC キーでフロアを分ける扉。
- **居室**: 11.34 ㎡（約 7 帖）の間取り図に合わせ、ベッド・机・椅子・書棚・タンス・ロッカー・冷蔵庫・エアコン・カーテン・ベランダの位置と色を調べる。
- **大浴場・サウナ・脱衣所**（男女別）と、**屋上のスカイテラス**（ウッドデッキ・ベンチ・植栽・塔屋、キャンパスが見える向き）。
- **調べる先**:
  - 寮の公式ページ（https://tus-d.com/dormitory/kch/ ）。
  - 運営のドーミーの物件ページ（https://dormy-ac.com/placehall/shutoken/uk/13399/ 、https://dormy-ac.com/page/tus/sp/kch.html ）。
  - 寮生向けページ（https://tus-kch.com/kch_view.html ）と物件紹介サイト（https://749.jp/cd/9499/ ）。
  - 学生のブログ・SNS、地図サービスの写真。
- **写る範囲の記録**: 写真ごとに、どの部屋のどの向きか、読み取れる寸法（天井高・机の大きさ・廊下の幅）、素材と色を書き出す。

### 2. 仕様にまとめる

- 1 階の間取り: PR #87 の推定を、写真で確かめられた点と推定のままの点に分けて書く。
- 居室の階: 1 フロアぶんの間取り。廊下、居室の並び、キッチンコーナー、ランドリー、階段と EV を入れる。100 室（女子 23 室・男子 77 室）を全部作るかどうかも決める。
- 居室 1 室の中: 公式の間取り図どおりにする。
- 大浴場・サウナ・屋上を、中に入れるようにするか、扉だけにするかを決める（ユーザーに決めてもらう点）。
- 仕上げの一覧:
  - 食堂: 木目の明るい床、焦げ茶の角テーブルと白い座面の椅子、濃い色のブラインド、天井の間接照明の帯。
  - ラウンジ: 橙地に白い輪の柄の壁紙、黄色のソファ、焦げ茶の縦格子、ベージュのカーペット、観葉植物。
  - キッチン: 焦げ茶の下の戸棚、ステンレスの流し、平らな加熱面のコンロ、黒いフード。
  - ランドリー: 黄色い床、白い扉、洗濯機の上の棚に乾燥機。
  - 浴室: 白いタイル、黒いシャワーの仕切り。
- 大学のブランドは使わない（運営は共立メンテナンス。`README_dorm.md` のとおり）。

### 3. 作り直す

- **生成側**（`kcd_route/dorm.py`、PR #87 の `dorm_interior.py` と `dorm_interior_props.py`、要る家具・素材）は、屋内の生成を受け持つ別セッション（dev/interior）が、この Issue の仕様に沿って作る。PR #87 のレビュー指摘と、入口の位置（ENT-1）の件も片付ける。PR #87 の三角形は 11,192 / 30,000 なので、居室の階を 1 フロア足す余地はある。
- **ゲーム側**は main のセッションがやる。
  - `dorm.fbx` の組み込み（`DormStage`・`DormRoute`、SceneBuilder）。
  - 寮長の Empty（`npc_dorm_head`）と POI（kanrinin / lounge / shokudo / corridor_end）の置き直し。
  - 居室の階への階段・EV の動線。
  - 裏エンド（#41）の流れの確認と、実プレイでの確認。

## 受け入れ条件

- 1 階の主な場所（風除室・ホール・食堂・ラウンジ・キッチン）と、足した場所（居室・居室の階の廊下・ランドリー、作るなら大浴場と屋上）のそれぞれについて、実物の写真（またはその出典の URL）とゲームの同じ向きの画面を並べた比較が Issue にある。
- 間取り・天井の高さ・床と壁の色・家具の並びが、仕様に書いた実物の値に合っている。推定の部分は、推定だと仕様に書いてある。
- 寮長に話しかけられ、裏エンドまで通しで進める（#41）。
- 壁の抜け（#45）や、落ちて戻れない所が無い。段差は stepOffset 0.40 m で上がれる（#42）。EditMode・PlayMode のテストが通る。
- 見た目にも文言にも大学のブランドが出ていない。

## 写真の扱い

- 著作権のある写真はリポジトリに入れない。出典の URL と、そこから読み取った寸法・色・配置のメモだけを `docs/ref/interiors/dorm/` に残す。
- Commons など自由なライセンスの写真は、帰属表示つきで入れてよい。

## 調べてわかったこと（2026-09-24 時点）

### 物件の概要（https://tus-d.com/dormitory/kch/ の「物件概要」）

- 所在地は東京都葛飾区南水元 1-8-13。鉄筋コンクリート造の 5 階建てで、竣工は 2013 年。運営は株式会社共立メンテナンス。
- 全 100 室（女子フロア 23 室・男子フロア 77 室）。居室は洋室 11.34 ㎡（約 7 帖）。
- 男子は 3〜5 階、女子は 2 階で、異性のフロアには入れない。IC キーでフロアを管理し、集中玄関・オートロック・防犯カメラがある。寮長・寮母が住み込みで常駐する。
- 設備:
  - 大浴場は男女とも。個室シャワーもある。
  - サウナは女子がスチーム、男子がドライ。大浴場にマッサージチェアがある。
  - ラウンジは 1 階と女子フロア。キッチンコーナーは 1 階と各階。
  - ランドリーは全居室階にあり、洗濯機は無料、乾燥機もある。
  - 屋上に「スカイテラス」があり、葛飾キャンパスが見える。

### 写真（見て確かめたもの。出典はすべて https://tus-d.com/dormitory/kch/ 。ライセンスは不明なので著作物として扱う）

| 保存名（`research/others/misc/dorm/`） | 画像 URL | 写っているもの |
|---|---|---|
| dorm_kch_dining.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/meals_img_main_pc@2x.jpg | 食堂で食事する学生 3 人。焦げ茶のテーブルと椅子、盆の定食、奥に濃い色のブラインド、右奥に白い業務用の機器と白い天板の台 |
| dorm_kch_lounge_facility.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/facility_img_lounge_pc@2x.png | 3 枚組。<br>・木目の明るい床に焦げ茶の角テーブルと白い座面の椅子。<br>・白い天板の配膳カウンター、電子レンジ、ガラス扉の冷蔵庫、木目の戸棚と厨房の窓。<br>・天井に照明の帯、濃い色のブラインド。<br>・壁沿いのカウンター席と複合機、観葉植物の奥の格子。<br>・ガラス格子の間仕切り。 |
| dorm_kch_lounge_relax.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/relax_img_lounge_pc@2x.png | 橙地に白い輪の柄の壁紙の前に黄色の 2 人掛けソファと焦げ茶のローテーブル。焦げ茶の縦格子、ベージュのカーペット、観葉植物、小窓 |
| dorm_kch_bath.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/relax_img_bath_pc@2x.png | 3 枚組。<br>・白いタイルの大浴場。壁沿いに細長い浴槽と手すり、高窓と腰の高さの窓、黒いシャワーの仕切りが並ぶ。<br>・奥から見た同じ浴場。<br>・脱衣所の化粧台。木の天板、鏡、ガラスの仕切り、白い丸椅子。 |
| dorm_kch_kitchen.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/facility_img_kitchen_pc@2x.png | 上はきのこを炒めるフライパンの接写。下は向かい合う 2 列のキッチン。焦げ茶の下の戸棚、ステンレスの流し、平らな加熱面のコンロ、黒いフード、突き当たりに白い扉、黄色い木目の床 |
| dorm_kch_laundry.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/facility_img_laundry_pc@2x.png | 縦型の洗濯機（左右）と、棚の上の乾燥機。黄色い床の通路の奥に白い扉が 2 枚 |
| dorm_kch_room01.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/rooms_gallery_img_01@2x.jpg | 居室の机で勉強する学生。明るい木の天板の机、白い卓上ライト、電話機、ベージュのカーテン |
| dorm_kch_roomplan.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/rooms_floor_img.png | 居室の間取り図。<br>・入口側: 扉、冷蔵庫、ロッカー、タンス。<br>・奥: すのこベッド、机・椅子・書棚、エアコン、カーテン。<br>・窓の外: ベランダ。 |
| dorm_kch_terrace.jpg | https://tus-d.com/wp-content/themes/wp-tusd-theme/assets/image/kch/relax_img_terrace_pc@2x.png | 屋上のスカイテラス。濃い色のウッドデッキ、ベンチ 4 脚、周りの植栽、白い塔屋、フェンス。遠くに建物が見える |

同じページにある未保存の写真:

- 居室: rooms_gallery_img_02〜06、rooms_desk_image01〜03
- 食事: meals_img_sub1〜3
- 浴場まわり: relax_img_sauna（サウナ）、relax_img_chair（マッサージチェア）
- 設備と防犯: facility_img_service / rental、safety_img_*
- 行事: community_event_img_01〜07

dormy-ac.com・tus-kch.com・749.jp の写真は、今回は取っていない（#41 で参照済みのページ）。

### ゲームの今

- main の `dorm.py`: 屋内は 16×38 m・天井 2.70 m、Z_PART 3.20、Z_TOP 4.45。POI は kanrinin / lounge / shokudo / corridor_end。
- PR #87 は OPEN・未検証。1 階の屋内は 11,192 / 30,000 三角形、Empty は 58/58。足した素材は wallpaper_orange / fabric_yellow / floor_carpet_gold / door_frosted。
- #41 は OPEN。裏エンドの入口と「寮」の読み方（A: コミュニティハウスで帰宅エンド、B: 国際学生寮 (383, 28)）が、まだユーザーの決定待ちで残っている。この Issue は葛飾コミュニティハウスの中だけを扱う。

関連: #41（隠しエンドと寮への道）, PR #87（寮の外観と 1 階の屋内）, #45, #42, #71, #53, #110（図書館で同じことをする Issue）
