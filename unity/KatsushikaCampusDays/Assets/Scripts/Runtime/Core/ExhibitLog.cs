using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 展示モードの遊ばれ方の記録 (#466)。1 回の「はじめから」〜タイトルへ戻るまでを 1 人分として、
    /// いつ始めて・どのキャラで・どこまで進んで・なぜ終わったか（放置・タイトルへ・終了）を 1 行の JSON で足していく。
    /// セーブを消したことも同じ行に残す。来場者の個人の情報は持たない（「誰」は通し番号とキャラだけ）。
    ///
    /// 置き場はセーブと同じフォルダ（展示モードでは persistentDataPath/exhibit）の <see cref="FileName"/>。
    /// 書けなくても遊べるので、失敗は Player.log に出すだけにする。同じ内容を Player.log にも出す。
    /// </summary>
    public static class ExhibitLog
    {
        /// <summary>記録のファイル名。1 行 1 件の JSON。</summary>
        public const string FileName = "kcd_exhibit_log.jsonl";

        private static Entry _session;
        private static bool _saveResetPending;
        private static float _startedRealtime;
        private static int _count;

        /// <summary>1 件分。JsonUtility で書くので public のフィールドにする。</summary>
        [Serializable]
        public sealed class Entry
        {
            public string evt = string.Empty;
            public string session = string.Empty;
            public string started = string.Empty;
            public string at = string.Empty;
            public string version = string.Empty;
            public string character = string.Empty;
            public bool saveReset;
            public string reason = string.Empty;
            public int playSeconds;
            public int day;
            public string clock = string.Empty;
            public int questsDone;
            public int questsTotal;
            public int buildings;
            public int collected;
            public int photoSpots;
            public int talked;
            public string ending = string.Empty;
        }

        /// <summary>遊んでいる人がいるか（「はじめから」のあと、まだ終わりを書いていない）。</summary>
        public static bool InSession => _session != null;

        /// <summary>1 行にする。改行を含まない JSON。</summary>
        public static string FormatLine(Entry entry)
        {
            return JsonUtility.ToJson(entry);
        }

        /// <summary>時刻の書き方（ローカル時刻と UTC からのずれ）。</summary>
        public static string Stamp(DateTimeOffset time)
        {
            return time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        /// <summary>ゲーム内の時刻（時間）を HH:mm にする。</summary>
        public static string Clock(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours))
            {
                return string.Empty;
            }

            int minutes = Mathf.FloorToInt(Mathf.Repeat(hours, 24f) * 60f);
            return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>「はじめから」の決定でセーブを消せたか。次の <see cref="StartSession"/> の行に載せる。</summary>
        public static void NoteSaveReset(bool reset)
        {
            _saveResetPending = reset;
        }

        /// <summary>
        /// 新しい人が「はじめから」で始めた（キャラを決めたあと）。前の人の終わりを書いていなければ先に書く（理由は new_game）。
        /// </summary>
        public static void StartSession(string characterId)
        {
            if (!ExhibitMode.Enabled)
            {
                return;
            }

            EndSession("new_game");
            _count++;
            DateTimeOffset now = DateTimeOffset.Now;
            _session = new Entry
            {
                session = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                          _count.ToString(CultureInfo.InvariantCulture),
                started = Stamp(now),
                version = Application.version,
                character = characterId ?? string.Empty,
                saveReset = _saveResetPending
            };
            _saveResetPending = false;
            _startedRealtime = Time.realtimeSinceStartup;

            Append(SafeSnapshot("start", string.Empty));
        }

        /// <summary>エンドを見た。</summary>
        public static void Ending(string endingId)
        {
            if (!ExhibitMode.Enabled || _session == null)
            {
                return;
            }

            _session.ending = endingId ?? string.Empty;
            Append(SafeSnapshot("ending", string.Empty));
        }

        /// <summary>
        /// 遊んでいた人が終わった。reason は idle（放置でタイトルへ）・title（自分でタイトルへ）・quit（アプリ終了）・new_game。
        /// 遊んでいる人がいなければ何もしない（タイトルで終了したときなど）。
        /// </summary>
        public static void EndSession(string reason)
        {
            if (!ExhibitMode.Enabled || _session == null)
            {
                return;
            }

            Entry end = SafeSnapshot("end", reason);
            _session = null;
            Append(end);
        }

        /// <summary>アプリを起動した（展示モードのとき 1 回）。</summary>
        public static void Launched()
        {
            if (!ExhibitMode.Enabled)
            {
                return;
            }

            Append(new Entry { evt = "launch", at = Stamp(DateTimeOffset.Now), version = Application.version });
        }

        /// <summary>
        /// 記録のせいでタイトルへ戻れなくなると展示が止まるので、進み具合を読めなかったら読めた分だけで書く。
        /// </summary>
        private static Entry SafeSnapshot(string evt, string reason)
        {
            try
            {
                return Snapshot(evt, reason);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KCD] exhibit log snapshot failed: " + Kind(e));
                Entry e2 = new Entry
                {
                    evt = evt,
                    reason = reason ?? string.Empty,
                    at = Stamp(DateTimeOffset.Now)
                };
                if (_session != null)
                {
                    e2.session = _session.session;
                    e2.started = _session.started;
                    e2.character = _session.character;
                }

                return e2;
            }
        }

        private static Entry Snapshot(string evt, string reason)
        {
            Entry e = JsonUtility.FromJson<Entry>(JsonUtility.ToJson(_session));
            e.evt = evt;
            e.reason = reason ?? string.Empty;
            e.at = Stamp(DateTimeOffset.Now);
            e.playSeconds = Mathf.Max(0, Mathf.RoundToInt(Time.realtimeSinceStartup - _startedRealtime));

            GameManager gm = GameManager.HasInstance ? GameManager.Instance : null;
            if (gm != null)
            {
                e.character = gm.SelectedCharacterId ?? e.character;
                e.day = gm.DayNumber;
                e.clock = Clock(gm.GameTimeHours);
                if (gm.Quests != null)
                {
                    e.questsTotal = gm.Quests.All.Count;
                    e.questsDone = gm.Quests.All.Count(q => q != null && gm.Quests.IsCompleted(q.Id));
                }
            }

            e.buildings = DayStats.BuildingCount;
            e.collected = DayStats.CollectedCount;
            e.photoSpots = DayStats.PhotoSpotCount;
            e.talked = DayStats.TalkedIds.Count;
            return e;
        }

        private static void Append(Entry entry)
        {
            try
            {
                string line = FormatLine(entry);
                Debug.Log("[KCD] exhibit-log " + line);
                File.AppendAllText(SaveSystem.PathOf(FileName), line + "\n");
            }
            catch (Exception e)
            {
                // 書けなくても遊べる。どの例外でも呼び出し元（タイトルへ戻る処理など）へ投げない。
                Debug.LogWarning("[KCD] exhibit log write failed: " + Kind(e));
            }
        }

        /// <summary>
        /// 例外の種類を短く書く（DirectoryNotFoundException → DirectoryNotFound）。記録の失敗は遊ぶのを止めないので、
        /// ログの点検（"…Exception" を含む行を落ちた証拠に数える）に例外として拾わせない。
        /// </summary>
        public static string Kind(Exception e)
        {
            string name = e == null ? string.Empty : e.GetType().Name;
            const string Suffix = "Exception";
            return name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - Suffix.Length)
                : name;
        }

        /// <summary>テスト用。遊んでいる人と通し番号を捨てる。</summary>
        public static void ResetForTests()
        {
            _session = null;
            _count = 0;
            _saveResetPending = false;
        }
    }
}
