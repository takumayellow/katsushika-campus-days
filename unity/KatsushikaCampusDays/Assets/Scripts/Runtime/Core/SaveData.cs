using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// セーブファイルの中身。JsonUtility で直列化する。
    /// JsonUtility は JSON に無い項目を初期値のまま残すので、あとから足した項目の初期値は
    /// 「その項目を持たない古いセーブを読んだときの値」になる。
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>今の版。2 で日数・屋内・一日の記録（DayStats）・位置の有無を足した (#61)。</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// 版番号を持たない最初の形（キャラ・時刻・位置・クエストだけ）。
        /// その JSON には SaveVersion が無く 0 と読めるので、0 と 1 をこの版として扱う。
        /// </summary>
        public const int LegacyVersion = 1;

        /// <summary>版番号。初期値を 0 にしておくのが肝で、ここを変えると古いセーブを見分けられなくなる。</summary>
        public int SaveVersion;

        public string CharacterId = "mirai";
        public float TimeHours = DayRestart.DayStartHour;

        /// <summary>何日目か。</summary>
        public int DayNumber = DayRestart.FirstDay;

        /// <summary>位置を使ってよいか。false ならシーンに置かれたスポーン（正門側）から始める。</summary>
        public bool HasPosition;

        public float PlayerX;
        public float PlayerY;
        public float PlayerZ;
        public float PlayerYaw;

        /// <summary>いた建物の id。外なら空。</summary>
        public string InteriorId = string.Empty;

        /// <summary>建物から出たときに立つ位置と向き。InteriorLoader が入るときに覚えたもの。</summary>
        public float ReturnX;
        public float ReturnY;
        public float ReturnZ;
        public float ReturnYaw;

        public QuestProgress Quests = new QuestProgress();

        /// <summary>入った建物（DayStats）。</summary>
        public List<string> Buildings = new List<string>();

        /// <summary>拾った物（DayStats）。</summary>
        public List<string> Collected = new List<string>();

        /// <summary>撮った写真スポット（DayStats）。</summary>
        public List<string> PhotoSpots = new List<string>();

        public Vector3 PlayerPosition => new Vector3(PlayerX, PlayerY, PlayerZ);

        public Vector3 ReturnPosition => new Vector3(ReturnX, ReturnY, ReturnZ);

        /// <summary>建物の中でセーブしたか。</summary>
        public bool IsInside => !string.IsNullOrEmpty(InteriorId);
    }

    /// <summary>読み込んだセーブをどこに立たせるか。</summary>
    public enum SavePlacement
    {
        /// <summary>動かさない。シーンに置かれたスポーンのまま（位置が使えないセーブ）。</summary>
        Stay,

        /// <summary>外のセーブした位置へ。</summary>
        Outside,

        /// <summary>建物の中のセーブした位置へ。出入り係にもその建物の中にいると知らせる。</summary>
        Inside,

        /// <summary>建物の中でセーブしたが、その建物の屋内が見つからない。入ったときの戻り先（入口の手前）へ。</summary>
        ReturnPoint
    }

    /// <summary>
    /// セーブの読み書きのうち、シーンに触らない部分。壊れた入力でも例外を出さずに既定値へ寄せる (#61)。
    /// EditMode テストから直接呼ぶ。
    /// </summary>
    public static class SaveDataRules
    {
        /// <summary>ファイルに書く文字列。</summary>
        public static string ToJson(SaveData data)
        {
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// 文字列からセーブを読む。空・壊れた JSON・オブジェクトでない JSON は例外を出さずに null（セーブ無しと同じ扱い）。
        /// 読めたものは <see cref="Sanitize"/> を通して返す。
        /// </summary>
        public static SaveData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
            {
                return null;
            }

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception)
            {
                // JsonUtility は書式の崩れを ArgumentException で返すが、型の食い違いなどで別の例外になることもある。
                // どれでも「読めないセーブ」として扱い、呼び出し側で既定の始め方に回す。
                return null;
            }

            return Sanitize(data);
        }

        /// <summary>
        /// 読み込んだ値を今の版の、使ってよい値に寄せた写しを返す（元は変えない）。
        /// NaN・無限大の時刻は朝、未知のキャラは mirai、1 未満の日数は 1 日目、null のリストは空。
        /// 版番号の無い古いセーブは、キャンパスの範囲の外（建物の中でセーブしたもの）なら位置を使わない。
        /// </summary>
        public static SaveData Sanitize(SaveData source)
        {
            if (source == null)
            {
                return null;
            }

            bool legacy = source.SaveVersion <= SaveData.LegacyVersion;
            var data = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                CharacterId = PlayableOrDefault(source.CharacterId),
                TimeHours = SanitizeHours(source.TimeHours),
                DayNumber = Mathf.Max(DayRestart.FirstDay, source.DayNumber),
                PlayerX = source.PlayerX,
                PlayerY = source.PlayerY,
                PlayerZ = source.PlayerZ,
                PlayerYaw = SanitizeYaw(source.PlayerYaw),
                Quests = SanitizeQuests(source.Quests),
                Buildings = CleanIds(source.Buildings),
                Collected = CleanIds(source.Collected),
                PhotoSpots = CleanIds(source.PhotoSpots)
            };

            bool finitePosition = IsFinite(source.PlayerX) && IsFinite(source.PlayerY) && IsFinite(source.PlayerZ);
            bool inside = !legacy && !string.IsNullOrEmpty(source.InteriorId);
            bool finiteReturn = IsFinite(source.ReturnX) && IsFinite(source.ReturnY) && IsFinite(source.ReturnZ);

            if (inside && finiteReturn)
            {
                data.InteriorId = source.InteriorId;
                data.ReturnX = source.ReturnX;
                data.ReturnY = source.ReturnY;
                data.ReturnZ = source.ReturnZ;
                data.ReturnYaw = SanitizeYaw(source.ReturnYaw);
                data.HasPosition = source.HasPosition && finitePosition;
                return data;
            }

            // 外にいた。屋内なのに戻り先が壊れていたら、屋内の位置も外では使えないのでスポーンから始める。
            // 古い版は位置の有無を持たない（キャンパスでしかセーブできなかったので常に位置がある）。
            bool claimed = legacy || (source.HasPosition && !inside);
            data.HasPosition = claimed && finitePosition &&
                               WorldBounds.IsWithin(data.PlayerPosition, WorldBounds.DefaultArea);
            return data;
        }

        /// <summary>
        /// 立たせ方を決める。屋内のセーブは、その建物の屋内がこのシーンにあり位置も使えるときだけ中へ戻す。
        /// </summary>
        public static SavePlacement Placement(SaveData data, bool interiorKnown)
        {
            if (data == null)
            {
                return SavePlacement.Stay;
            }

            if (data.IsInside)
            {
                return interiorKnown && data.HasPosition ? SavePlacement.Inside : SavePlacement.ReturnPoint;
            }

            return data.HasPosition ? SavePlacement.Outside : SavePlacement.Stay;
        }

        /// <summary>
        /// 位置だけ差し替えた写し。外のその位置に立っていることにする（屋内は外す）。
        /// 一日の終わりに「タイトルへ」を選んだとき、翌朝のスポーンに立たせて書くのに使う。
        /// </summary>
        public static SaveData StandingAt(SaveData data, Vector3 position, float yaw)
        {
            SaveData copy = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
            copy.HasPosition = true;
            copy.PlayerX = position.x;
            copy.PlayerY = position.y;
            copy.PlayerZ = position.z;
            copy.PlayerYaw = SanitizeYaw(yaw);
            copy.InteriorId = string.Empty;
            copy.ReturnX = 0f;
            copy.ReturnY = 0f;
            copy.ReturnZ = 0f;
            copy.ReturnYaw = 0f;
            return copy;
        }

        /// <summary>0 以上 24 未満の時刻。NaN・無限大は朝。</summary>
        public static float SanitizeHours(float hours)
        {
            return IsFinite(hours) ? Mathf.Repeat(hours, 24f) : DayRestart.DayStartHour;
        }

        /// <summary>遊べるキャラの id ならそのまま、それ以外は mirai。</summary>
        public static string PlayableOrDefault(string characterId)
        {
            return Array.IndexOf(GameManager.PlayableCharacterIds, characterId) >= 0 ? characterId : "mirai";
        }

        private static float SanitizeYaw(float yaw)
        {
            return IsFinite(yaw) ? Mathf.Repeat(yaw, 360f) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static List<string> CleanIds(List<string> ids)
        {
            var result = new List<string>();
            if (ids == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!string.IsNullOrEmpty(id) && seen.Add(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        /// <summary>3 本の列の長さをそろえ、空の id を落とし、負の StepsDone と未知の状態を 0 にする。</summary>
        private static QuestProgress SanitizeQuests(QuestProgress source)
        {
            var result = new QuestProgress();
            int rows = QuestProgress.RowCount(source);
            for (int i = 0; i < rows; i++)
            {
                string id = source.QuestIds[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                int state = source.States[i];
                result.QuestIds.Add(id);
                result.StepsDone.Add(Mathf.Max(0, source.StepsDone[i]));
                result.States.Add(state >= QuestProgress.StateNotStarted && state <= QuestProgress.StateCompleted
                    ? state
                    : QuestProgress.StateNotStarted);
            }

            return result;
        }
    }
}
