using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 見たエンドの記録 (#41)。称号 ending_seen（<see cref="AchievementBook"/>）が読む。
    /// 今あるのは裏エンド「寮でぐーたら」（<see cref="DormRoute.EndingId"/>）だけ。
    ///
    /// セーブの 1 枠（<see cref="SaveSystem"/>）には載せず、同じ置き場の別ファイル kcd_endings.json に置く。
    /// エンドのあとは必ずタイトルへ戻って通常の保存の機会が無いのと、寮の屋内にいる状態で最後のセーブを
    /// 上書きしないため。置き場の扱いはセーブと同じ（<see cref="SaveStore"/>、<see cref="SaveSystem.DirectoryOverride"/>）。
    ///
    /// 「はじめから」でも消さない。プレイごとの記録ではなく、この端末で見たエンドの記録。
    /// ファイルは初めて使うときに読み、置き場が変わったら読み直す。無い・壊れている・知らない版のときは空として扱う。
    /// </summary>
    public static class EndingProgress
    {
        /// <summary>記録のファイル名。置き場は <see cref="SaveSystem.PathOf"/>。</summary>
        public const string FileName = "kcd_endings.json";

        /// <summary>ファイルの形の版。読めるのはこの版だけ。</summary>
        private const int FormatVersion = 1;

        /// <summary>ファイルの形。JsonUtility が読み書きする。</summary>
        [Serializable]
        private sealed class FileShape
        {
            public int version;
            public List<string> seen = new List<string>();
        }

        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        private static bool _loaded;

        /// <summary>読んだときの <see cref="SaveSystem.DirectoryOverride"/>。変わったら別の置き場なので読み直す。</summary>
        private static string _loadedFolder;

        private static int _version;

        /// <summary>
        /// 記録が変わるたび（エンドを見たとき、ファイルを読み込んだとき）に 1 つ進む数。
        /// 称号の判定（GameManager）は、これが変わったフレームだけ数え直す。<see cref="DayStats.Version"/> と同じ役目。
        /// </summary>
        public static int Version
        {
            get
            {
                EnsureLoaded();
                return _version;
            }
        }

        /// <summary>見たエンドの id。</summary>
        public static IReadOnlyCollection<string> SeenIds
        {
            get
            {
                EnsureLoaded();
                return _seen;
            }
        }

        /// <summary>そのエンドを見たことがあるか。</summary>
        public static bool Has(string endingId)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(endingId) && _seen.Contains(endingId);
        }

        /// <summary>
        /// エンドを見た。同じ id は 1 回だけ数え、初めてのときだけファイルに書く。初めてなら true。
        /// 書けなくてもメモリには残し、例外は投げない（このプレイの間は称号が取れる）。
        /// </summary>
        public static bool Note(string endingId)
        {
            if (string.IsNullOrEmpty(endingId))
            {
                return false;
            }

            EnsureLoaded();
            if (!_seen.Add(endingId))
            {
                return false;
            }

            _version++;
            Write();
            return true;
        }

        /// <summary>ファイルから読み直す。置き場を差し替えたあとや、ファイルを消したあとにテストが使う。</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        /// <summary>
        /// ファイルの中身から見たエンドの id を取り出す。順番は保ち、空の id と重複は落とす。
        /// 空・JSON でない・壊れている・知らない版のときは null（例外は出さない）。
        /// </summary>
        public static List<string> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
            {
                return null;
            }

            FileShape shape;
            try
            {
                shape = JsonUtility.FromJson<FileShape>(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (shape == null || shape.version != FormatVersion)
            {
                return null;
            }

            var ids = new List<string>();
            if (shape.seen == null)
            {
                return ids;
            }

            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in shape.seen)
            {
                if (!string.IsNullOrEmpty(id) && added.Add(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>ファイルに書く文字列。id は並びを安定させる。</summary>
        public static string ToJson(IEnumerable<string> endingIds)
        {
            var ids = new List<string>(endingIds);
            ids.Sort(StringComparer.Ordinal);
            return JsonUtility.ToJson(new FileShape { version = FormatVersion, seen = ids });
        }

        /// <summary>
        /// 毎フレーム呼ばれても軽いよう、読むのは初めてと置き場が変わったときだけ。
        /// 置き場が変わるのはテストが <see cref="SaveSystem.DirectoryOverride"/> を差し替えたときで、
        /// 前の置き場の記録が残らないようにする。
        /// </summary>
        private static void EnsureLoaded()
        {
            string folder = SaveSystem.DirectoryOverride;
            if (_loaded && string.Equals(_loadedFolder, folder, StringComparison.Ordinal))
            {
                return;
            }

            _loaded = true;
            _loadedFolder = folder;
            _seen.Clear();
            _seen.UnionWith(Read());
            _version++;
        }

        private static List<string> Read()
        {
            string path = SaveSystem.PathOf(FileName);
            try
            {
                if (!SaveStore.Exists(path))
                {
                    return new List<string>();
                }

                List<string> ids = Parse(SaveStore.Read(path));
                if (ids == null)
                {
                    Debug.LogWarning("[KCD] 見たエンドの記録の形式が壊れているので、無いものとして扱います。");
                    return new List<string>();
                }

                return ids;
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] 見たエンドの記録を読み込めません: " + error.Message);
                return new List<string>();
            }
        }

        private static void Write()
        {
            try
            {
                SaveStore.Write(SaveSystem.PathOf(FileName), ToJson(_seen));
            }
            catch (IOException error)
            {
                Debug.LogError("[KCD] 見たエンドを記録できませんでした: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                Debug.LogError("[KCD] 見たエンドの記録先に書き込めません: " + error.Message);
            }
            catch (PlayerPrefsException error)
            {
                // Web 版は PlayerPrefs（ブラウザのストレージ）に書く。容量を超えるとここに来る。
                Debug.LogError("[KCD] 見たエンドをブラウザに保存できません: " + error.Message);
            }
        }
    }
}
