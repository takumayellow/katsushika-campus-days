using System;
using System.IO;
using UnityEngine;

namespace KCD
{
    /// <summary>セーブファイルの中身。JsonUtility で直列化する。</summary>
    [Serializable]
    public sealed class SaveData
    {
        public string CharacterId = "mirai";
        public float TimeHours = 8.5f;
        public float PlayerX;
        public float PlayerY;
        public float PlayerZ;
        public float PlayerYaw;
        public QuestProgress Quests = new QuestProgress();
    }

    /// <summary>
    /// 1 スロットだけの素朴なセーブ。Application.persistentDataPath に JSON で置く。
    /// F5 でセーブ、F9 でロード。ポーズメニューからも呼ぶ。
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "kcd_save.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>セーブが存在するか。</summary>
        public static bool HasSave => SaveStore.Exists(FilePath);

        /// <summary>現在の状態を書き出す。失敗しても例外は投げない。</summary>
        public static bool Save()
        {
            try
            {
                GameManager manager = GameManager.Instance;
                var data = new SaveData
                {
                    CharacterId = manager.SelectedCharacterId,
                    TimeHours = manager.GameTimeHours,
                    Quests = manager.Quests != null ? manager.Quests.Capture() : new QuestProgress()
                };

                var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                if (player != null)
                {
                    Vector3 position = player.transform.position;
                    data.PlayerX = position.x;
                    data.PlayerY = position.y;
                    data.PlayerZ = position.z;
                    data.PlayerYaw = player.transform.eulerAngles.y;
                }

                SaveStore.Write(FilePath, JsonUtility.ToJson(data, true));
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
        }

        /// <summary>セーブを読み込んで現在のシーンに適用する。</summary>
        public static bool Load()
        {
            SaveData data = Read();
            if (data == null)
            {
                return false;
            }

            GameManager manager = GameManager.Instance;
            manager.SelectedCharacterId = data.CharacterId;
            manager.GameTimeHours = data.TimeHours;
            manager.Quests?.Restore(data.Quests);

            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                player.Teleport(new Vector3(data.PlayerX, data.PlayerY, data.PlayerZ), data.PlayerYaw);
                CameraRig.SnapBehind(player.transform);
            }

            HUD.Instance?.ShowToast(L.Get("ui.hud.loaded", "ロードしました"));
            return true;
        }

        /// <summary>セーブファイルを読むだけ。壊れていれば null。</summary>
        public static SaveData Read()
        {
            if (!HasSave)
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<SaveData>(SaveStore.Read(FilePath));
            }
            catch (IOException error)
            {
                Debug.LogError("[KCD] セーブを読み込めません: " + error.Message);
                return null;
            }
            catch (ArgumentException error)
            {
                Debug.LogError("[KCD] セーブの形式が壊れています: " + error.Message);
                return null;
            }
        }
    }
}
