# Unity 側の組み立て方

`unity/KatsushikaCampusDays` は **エディタを開かずに** 作れる。シーンもマテリアルも
アニメーターも `Assets/Scripts/Editor/**` が生成するので、手順はコマンド 3 本だけ。

- Unity 6000.6.2f1 / URP 17.6.0
- Editor 本体: `C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe`
  （Hub 登録版。`C:\Program Files\Unity 6000.6.2f1\` は使わない）

## 主: コマンドラインから 3 本

`UNITY` に Editor のパスを入れておくと、あとはそのまま貼れる。

```bash
export UNITY='/c/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe'
export KCD='<リポジトリの絶対パス>'
```

### 1. 取り込み（FBX / JSON / テクスチャ）

```bash
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit \
  -projectPath "$KCD\unity\KatsushikaCampusDays" \
  -logFile "$KCD\unity\logs\import.log"
```

### 2. シーン生成（Title と Campus）

```bash
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit \
  -projectPath "$KCD\unity\KatsushikaCampusDays" \
  -executeMethod KCD.Editor.SceneBuilder.BuildAll \
  -logFile "$KCD\unity\logs\scene.log"
```

### 3. Windows ビルド

```bash
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit \
  -projectPath "$KCD\unity\KatsushikaCampusDays" \
  -executeMethod KCD.Editor.BuildPlayer.BuildWindows \
  -logFile "$KCD\unity\logs\build.log"
```

出力先は既定で `build/Windows/KatsushikaCampusDays.exe`。変えるときは
`-buildOutput <path>` を足す。`.exe` で終わらないパスを渡すとフォルダとして扱い、
その下に同じ名前の exe を置く。

### ログの確認

```bash
python tools/check_unity_log.py unity/logs/import.log unity/logs/scene.log unity/logs/build.log
```

`error CS`・`warning CS`・例外・NavMesh の失敗・シェーダの欠け（`Shader ... not found`）・
編集時のマテリアル複製（`Instantiating material ...`）の件数、`[KCD]` の進捗、保存したシーン、ビルド結果を並べる。
どれかが 1 件でもあるか、ビルド結果が Succeeded でなければ終了コード 1 を返すので、そのまま CI に置ける。
`unity/logs/tests_editmode.xml` のようにテスト結果の XML を渡すと、失敗したテストを拾う。
直せない理由が分かっている行は `KNOWN_LINES` に理由付きで載せてあり、`[KNOWN]` として件数だけ出す。

## 副: Unity.exe を直に叩く

`UNITY` を使わず毎回フルパスで書く形。Git Bash では `MSYS_NO_PATHCONV=1` を付けないと
`-projectPath` の `C:\...` がパス変換で壊れる。

```bash
MSYS_NO_PATHCONV=1 "/c/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "<リポジトリの絶対パス>\unity\KatsushikaCampusDays" \
  -executeMethod KCD.Editor.SceneBuilder.BuildAll \
  -logFile "<リポジトリの絶対パス>\unity\logs\scene.log"
```

PowerShell から叩くときは変換が無いので `&` と `--%` を使う。

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe' -batchmode -nographics -quit `
  -projectPath '<リポジトリの絶対パス>\unity\KatsushikaCampusDays' `
  -executeMethod KCD.Editor.SceneBuilder.BuildAll `
  -logFile '<リポジトリの絶対パス>\unity\logs\scene.log'
```

エディタを開いている場合は、同じ処理をメニューからも呼べる。

- `KCD / シーンを組み直す` → `SceneBuilder.BuildAll`
- `KCD / Windows ビルド` → `BuildPlayer.BuildWindows`

## 何がどこで作られるか

| 生成物 | 作る場所 |
| --- | --- |
| `Assets/Scenes/{Title,Campus}.unity` | `SceneBuilder` |
| キャンパス本体・MeshCollider・樹木 | `CampusStage` |
| 入口トリガー・名札・到達判定・拾い物 | `CampusProps` |
| プレイヤー・カメラ・NPC | `ActorFactory` |
| HUD・ミニマップ・会話・ポーズ | `UIFactory` / `UIKit` |
| マテリアル（URP Lit / KCD Toon） | `MaterialLibrary` |
| 日本語 TMP フォント `Assets/Fonts/KCD_JP.asset` | `FontLibrary` |
| TMP Essential Resources | `TmpEssentials` |
| `Assets/Generated/CampusNavMesh.asset` | `CampusStage.BakeNavMesh` |
| `Assets/Generated/Animators/*.controller` | `AnimatorFactory` |
| `Assets/Resources/KCD/{Quests,Dialogue}` | `DataBundler` |

`.meta` はすべて Unity に作らせる。コードからアセットを指すときは
`AssetDatabase.LoadAssetAtPath` か `Resources.Load` を使い、GUID は書かない。

## 注意

- `TmpEssentials` は `.unitypackage` を自前で展開する。`TMP_PackageResourceImporter.ImportResources`
  は取り込みを遅らせるので、`-quit` を付けたバッチでは最後まで走らない。
- `CharacterImporter.ResolveMaterials` は取り込みが終わったあとに走る。取り込みの最中は
  `AssetDatabase.CreateAsset` が禁止されるので、`OnAssignMaterialModel` では既存を探すだけにしてある。
- `unity/logs/` は `.gitignore` 済み。ログは残して構わない。
