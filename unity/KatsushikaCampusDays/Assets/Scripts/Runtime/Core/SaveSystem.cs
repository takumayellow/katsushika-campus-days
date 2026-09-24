using System;
using System.IO;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 1 スロットだけの素朴なセーブ。Application.persistentDataPath に JSON で置く（Web 版は PlayerPrefs、SaveStore 参照）。
    /// F5 でセーブ、F9 でロード。ポーズメニューとタイトルの「つづきから」からも呼ぶ。
    /// 自動セーブ（クエスト達成・建物の出入り・一日の終わり）は <see cref="AutoSave"/> がここを呼ぶ。
    /// 中身の形と、壊れた入力の読み方は <see cref="SaveData"/> / <see cref="SaveDataRules"/>。
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "kcd_save.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>タイトルの「つづきから」で読んだセーブ。位置と屋内はキャンパスの最初のフレームで当てる。</summary>
        private static SaveData _pending;

        /// <summary>セーブが存在するか（読めるかどうかは見ない。見るなら <see cref="Read"/>）。</summary>
        public static bool HasSave => SaveStore.Exists(FilePath);

        /// <summary>今の状態をセーブの形にする（書かない）。</summary>
        public static SaveData Capture()
        {
            var data = new SaveData { SaveVersion = SaveData.CurrentVersion };

            GameManager manager = GameManager.Instance;
            data.CharacterId = manager.SelectedCharacterId;
            data.TimeHours = manager.GameTimeHours;
            data.DayNumber = manager.DayNumber;
            data.Quests = manager.Quests != null ? manager.Quests.Capture() : new QuestProgress();

            data.Buildings = DayStats.SortedBuildingIds();
            data.Collected = DayStats.SortedCollectedIds();
            data.PhotoSpots = DayStats.SortedPhotoSpotIds();
            data.Talked = DayStats.SortedTalkedIds();

            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                Vector3 position = player.transform.position;
                data.HasPosition = true;
                data.PlayerX = position.x;
                data.PlayerY = position.y;
                data.PlayerZ = position.z;
                data.PlayerYaw = player.transform.eulerAngles.y;
            }

            // 屋内モデルはキャンパスの遠く（x ≥ 1200 m）にあるので、位置だけ書くと読むときに
            // 「キャンパスの外」に見える。どの建物にいて、出たらどこへ戻るかも書く (#61)。
            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null && loader.IsInside)
            {
                Vector3 back = loader.ReturnPosition;
                data.InteriorId = loader.CurrentId;
                data.ReturnX = back.x;
                data.ReturnY = back.y;
                data.ReturnZ = back.z;
                data.ReturnYaw = loader.ReturnYaw;
            }

            return data;
        }

        /// <summary>現在の状態を書き出す。失敗しても例外は投げない。</summary>
        public static bool Save()
        {
            SaveData data;
            try
            {
                data = Capture();
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] セーブする状態を集められません: " + error.Message);
                return false;
            }

            return Write(data);
        }

        /// <summary>
        /// 今の状態を、翌朝のスポーンに立っていることにして書く（<see cref="SaveDataRules.AtSpawn"/>）。
        /// 一日の終わりに「タイトルへ」を選んだとき、朝に戻したあと、タイトルへ移る前に呼ぶ。失敗しても例外は投げない。
        /// </summary>
        public static bool SaveAtSpawn(bool hasSpawn, Vector3 spawn, float spawnYaw)
        {
            SaveData data;
            try
            {
                data = SaveDataRules.AtSpawn(Capture(), hasSpawn, spawn, spawnYaw);
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] セーブする状態を集められません: " + error.Message);
                return false;
            }

            return Write(data);
        }

        /// <summary>組み立て済みのセーブを書く。失敗しても例外は投げない。</summary>
        public static bool Write(SaveData data)
        {
            if (data == null)
            {
                return false;
            }

            try
            {
                SaveStore.Write(FilePath, SaveDataRules.ToJson(data));
                return true;
            }
            catch (IOException error)
            {
                Debug.LogError("[KCD] セーブに失敗しました: " + error.Message);
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                Debug.LogError("[KCD] セーブ先に書き込めません: " + error.Message);
                return false;
            }
            catch (PlayerPrefsException error)
            {
                // Web 版は PlayerPrefs（ブラウザのストレージ）に書く。容量を超えるとここに来る。
                Debug.LogError("[KCD] セーブをブラウザに保存できません: " + error.Message);
                return false;
            }
        }

        /// <summary>
        /// セーブを読み込んで現在のシーン（キャンパス）に適用する。F9 とポーズの「ロード」。
        /// 読めなければ何も変えずに知らせる。
        /// </summary>
        public static bool Load()
        {
            SaveData data = Read();
            if (data == null)
            {
                HUD.Instance?.ShowToast(L.Get("ui.hud.load_failed", "セーブを読み込めませんでした"));
                return false;
            }

            _pending = null;

            // 読む前に頼まれていた自動セーブは、読み込んだあとの状態には当てはまらないので捨てる。
            AutoSave.Cancel();
            ApplyToManager(data);
            ApplyToScene(data);
            HUD.Instance?.ShowToast(L.Get("ui.hud.loaded", "ロードしました"));
            return true;
        }

        /// <summary>
        /// タイトルの「つづきから」。キャンパスを読み込む前に、シーンに依らない分（キャラ・時刻・日数・
        /// クエスト・一日の記録）を入れる。体の見た目（PlayerAppearance.Awake）と時計（DayNightCycle.Start）は
        /// キャンパスの読み込みでこれを拾う。位置と屋内は <see cref="ApplyPending"/> で当てる。
        /// 読めなければ何も変えずに false。
        /// </summary>
        public static bool PrepareContinue()
        {
            SaveData data = Read();
            if (data == null)
            {
                return false;
            }

            ApplyToManager(data);
            _pending = data;
            return true;
        }

        /// <summary>「つづきから」の残り（位置・向き・屋内）を当てる。CampusDirector が最初のフレームで呼ぶ。</summary>
        public static void ApplyPending()
        {
            SaveData data = _pending;
            _pending = null;
            if (data != null)
            {
                ApplyToScene(data);
            }
        }

        /// <summary>「はじめから」で、読みかけの「つづきから」を捨てる。</summary>
        public static void DiscardPending()
        {
            _pending = null;
        }

        /// <summary>セーブファイルを読むだけ。無い・読めない・壊れているときは null（どれも例外は出さない）。</summary>
        public static SaveData Read()
        {
            string json;
            try
            {
                if (!HasSave)
                {
                    return null;
                }

                json = SaveStore.Read(FilePath);
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] セーブを読み込めません: " + error.Message);
                return null;
            }

            SaveData data = SaveDataRules.Parse(json);
            if (data == null)
            {
                Debug.LogWarning("[KCD] セーブの形式が壊れているので、無いものとして扱います。");
            }

            return data;
        }

        /// <summary>シーンに依らない分を GameManager と DayStats に入れる。</summary>
        private static void ApplyToManager(SaveData data)
        {
            GameManager manager = GameManager.Instance;
            manager.SelectedCharacterId = data.CharacterId;
            manager.GameTimeHours = data.TimeHours;

            // DayNightCycle.Start は初めてキャンパスに入るとき朝から始め、GameTimeHours を見ない。
            // セーブはキャンパスに入ったあとのものなので入場済みにして、ロードした時刻を引き継がせる（#38）。
            manager.HasEnteredCampus = true;
            manager.DayNumber = data.DayNumber;

            DayStats.Restore(data.Buildings, data.Collected, data.PhotoSpots, data.Talked);
            manager.Quests?.Restore(data.Quests);

            // 読み込んだ進行で取れている称号は、ロードの時点で黙って獲得済みにする（トーストを並べない）。
            manager.SyncAchievementsQuietly();
        }

        /// <summary>見た目・時計・位置・屋内を今のシーンに当てる。</summary>
        private static void ApplyToScene(SaveData data)
        {
            // キャンパスの中からのロード（F9 / ポーズ）では PlayerAppearance.Awake はとっくに終わっている。
            // 体を今ここで入れ替える (#6)。
            var appearance = UnityEngine.Object.FindAnyObjectByType<PlayerAppearance>();
            if (appearance != null)
            {
                appearance.Apply(data.CharacterId);
            }

            // DayNightCycle は自分の Hours を毎フレーム GameTimeHours へ書き戻すので、GameTimeHours だけ変えても
            // 次のフレームで元の時刻に戻る（#38）。太陽と環境光もその場でロードした時刻に合わせる。
            // チャイムは AudioManager.ShouldChime が大きな時刻の飛びでは鳴らさない。
            var cycle = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.SetHours(data.TimeHours);
            }

            Place(data);
            QuestObjectiveLocator.Invalidate();
        }

        /// <summary>プレイヤーを立たせ、出入り係の「中にいるか」をそろえる。</summary>
        private static void Place(SaveData data)
        {
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            InteriorLoader loader = InteriorLoader.Instance;
            bool known = loader != null && data.IsInside && loader.HasInterior(data.InteriorId);

            switch (SaveDataRules.Placement(data, known))
            {
                case SavePlacement.Inside:
                    loader.RestoreInside(data.InteriorId, data.ReturnPosition, data.ReturnYaw);
                    Teleport(player, data.PlayerPosition, data.PlayerYaw);
                    break;

                case SavePlacement.ReturnPoint:
                    loader?.RestoreOutside();
                    Teleport(player, data.ReturnPosition, data.ReturnYaw);
                    break;

                case SavePlacement.Outside:
                    loader?.RestoreOutside();
                    Teleport(player, data.PlayerPosition, data.PlayerYaw);
                    break;

                default:
                    // 位置の使えないセーブ（版番号の無い古いセーブを建物の中で書いたもの）。
                    // タイトルからならシーンのスポーンのまま。キャンパスで建物の中から読んだなら、入口の手前へ出す。
                    if (loader != null && loader.IsInside)
                    {
                        Teleport(player, loader.ReturnPosition, loader.ReturnYaw);
                    }

                    loader?.RestoreOutside();
                    break;
            }
        }

        private static void Teleport(PlayerController player, Vector3 position, float yaw)
        {
            if (player == null)
            {
                return;
            }

            player.Teleport(position, yaw);
            Physics.SyncTransforms();
            CameraRig.SnapBehind(player.transform);
        }
    }
}
