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
    /// ファイルは初めて使うときに読み、置き場が変わったら読み直す。無いときは空として扱う。
    /// 読めなかったファイル（壊れている・新しい版・読み込みに失敗）は、記録する前にもう一度読み直す。
    /// それでも読めないとき、新しい版のものと読み込みに失敗したものは上書きせず、見たエンドはこのプレイの間だけ覚える。
    /// 壊れているものは中身を <see cref="BackupFileName"/> に残してから書き直す。
    /// </summary>
    public static class EndingProgress
    {
        /// <summary>記録のファイル名。置き場は <see cref="SaveSystem.PathOf"/>。</summary>
        public const string FileName = "kcd_endings.json";

        /// <summary>壊れていたファイルの中身を残すファイル名。置き場は記録と同じ。</summary>
        public const string BackupFileName = FileName + ".bak";

        /// <summary>ファイルの形の版。読めるのはこの版だけ。</summary>
        private const int FormatVersion = 1;

        /// <summary>ファイルを読んだ結果。Broken・Newer・Failed が「読めなかった」。</summary>
        private enum FileState
        {
            /// <summary>この版で読めた。</summary>
            Valid,

            /// <summary>ファイルが無い。</summary>
            Missing,

            /// <summary>JSON として読めない、または形が違う。</summary>
            Broken,

            /// <summary>新しい版のゲームが書いたもの。</summary>
            Newer,

            /// <summary>ファイルを開けない・読めない。</summary>
            Failed
        }

        /// <summary>ファイルの形。JsonUtility が読み書きする。</summary>
        [Serializable]
        private sealed class FileShape
        {
            public int version;
            public List<string> seen = new List<string>();
        }

        /// <summary>版だけを読む。新しい版は seen の形が違うかもしれないので、<see cref="FileShape"/> より先にこれで見る。</summary>
        [Serializable]
        private sealed class VersionOnly
        {
            public int version;
        }

        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        private static bool _loaded;

        /// <summary>読んだときの <see cref="SaveSystem.DirectoryOverride"/>。変わったら別の置き場なので読み直す。</summary>
        private static string _loadedFolder;

        private static int _version;

        /// <summary>置き場のファイルを最後に読んだ（または書いた）結果。上書きしてよいかはこれで決める。</summary>
        private static FileState _fileState = FileState.Missing;

        /// <summary>メモリにある記録を、まだファイルに書けていない。</summary>
        private static bool _unsaved;

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
        /// エンドを見た。同じ id は 1 回だけ数え、初めてのときだけ true を返す。
        /// 書けなくてもメモリには残し、例外は投げない（このプレイの間は称号が取れる）。
        /// ファイルに書けていないときは、同じ id でもう一度呼ぶと書き直す（戻り値は false のまま）。
        /// </summary>
        public static bool Note(string endingId)
        {
            if (string.IsNullOrEmpty(endingId))
            {
                return false;
            }

            EnsureLoaded();
            bool added = _seen.Add(endingId);
            if (!added && !_unsaved)
            {
                return false;
            }

            if (added)
            {
                _version++;
            }

            Write();
            return added;
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
            return Inspect(json, out List<string> ids) == FileState.Valid ? ids : null;
        }

        /// <summary>ファイルに書く文字列。id は並びを安定させる。</summary>
        public static string ToJson(IEnumerable<string> endingIds)
        {
            var ids = new List<string>(endingIds);
            ids.Sort(StringComparer.Ordinal);
            return JsonUtility.ToJson(new FileShape { version = FormatVersion, seen = ids });
        }

        /// <summary>ドメインを読み直さない再生でも、前回の再生で読んだ記録を持ち越さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            _loadedFolder = null;
            _seen.Clear();
            _fileState = FileState.Missing;
            _unsaved = false;

            // _version は戻さない。増える一方の数として比べられるので、戻すと前の値と重なりうる。
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
            _unsaved = false;

            _fileState = Load(SaveSystem.PathOf(FileName), out List<string> ids, out _);
            _seen.UnionWith(ids);
            _version++;

            if (_fileState == FileState.Broken)
            {
                Debug.LogWarning("[KCD] 見たエンドの記録の形式が壊れているので、無いものとして扱います。");
            }
            else if (_fileState == FileState.Newer)
            {
                Debug.LogWarning("[KCD] 見たエンドの記録は新しい版のゲームが書いたものなので、読めません。");
            }
        }

        /// <summary>
        /// 置き場のファイルを読む。ids は読めた id（読めなかったときは空）、text は読んだ文字列（読めなかったときは null）。
        /// 例外は出さず、読み込みに失敗したときは Failed を返してエラーを 1 行出す。
        /// </summary>
        private static FileState Load(string path, out List<string> ids, out string text)
        {
            ids = new List<string>();
            text = null;
            try
            {
                if (!SaveStore.Exists(path))
                {
                    return FileState.Missing;
                }

                text = SaveStore.Read(path);
            }
            catch (Exception error)
            {
                Debug.LogError("[KCD] 見たエンドの記録を読み込めません: " + error.Message);
                return FileState.Failed;
            }

            FileState state = Inspect(text, out List<string> read);
            if (read != null)
            {
                ids = read;
            }

            return state;
        }

        /// <summary>文字列を見て、読めるか・壊れているか・新しい版かを分ける。id は Valid のときだけ入る（それ以外は null）。</summary>
        private static FileState Inspect(string json, out List<string> ids)
        {
            ids = null;
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
            {
                return FileState.Broken;
            }

            FileShape shape;
            try
            {
                VersionOnly head = JsonUtility.FromJson<VersionOnly>(json);
                if (head != null && head.version > FormatVersion)
                {
                    return FileState.Newer;
                }

                shape = JsonUtility.FromJson<FileShape>(json);
            }
            catch (Exception)
            {
                return FileState.Broken;
            }

            if (shape == null || shape.version != FormatVersion)
            {
                return FileState.Broken;
            }

            ids = new List<string>();
            if (shape.seen == null)
            {
                return FileState.Valid;
            }

            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in shape.seen)
            {
                if (!string.IsNullOrEmpty(id) && added.Add(id))
                {
                    ids.Add(id);
                }
            }

            return FileState.Valid;
        }

        /// <summary>
        /// 置き場のファイルを上書きしてよいか。前に読めなかったファイルは、書く前にもう一度読み直す。
        /// 読めるようになっていたら中身を取り込んで書く。壊れたままなら <see cref="BackupFileName"/> に残してから書く。
        /// 新しい版と読み込みに失敗したものは、上書きしない。
        /// </summary>
        private static bool CanOverwrite(string path)
        {
            if (_fileState == FileState.Valid || _fileState == FileState.Missing)
            {
                return true;
            }

            _fileState = Load(path, out List<string> ids, out string text);
            if (_fileState == FileState.Valid)
            {
                int known = _seen.Count;
                _seen.UnionWith(ids);
                if (_seen.Count != known)
                {
                    _version++;
                }

                return true;
            }

            if (_fileState == FileState.Missing)
            {
                return true;
            }

            if (_fileState == FileState.Broken)
            {
                KeepBrokenCopy(text);
                return true;
            }

            if (_fileState == FileState.Newer)
            {
                Debug.LogWarning("[KCD] 見たエンドの記録は新しい版のゲームが書いたものなので、上書きしません。このプレイの間だけ覚えます。");
            }

            // Failed のエラーは Load が出している。
            return false;
        }

        /// <summary>壊れていたファイルの中身を <see cref="BackupFileName"/> に残す。空のときは残すものが無いので、前の控えを潰さない。</summary>
        private static void KeepBrokenCopy(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            SaveStore.Write(SaveSystem.PathOf(BackupFileName), text);
            Debug.LogWarning("[KCD] 壊れていた見たエンドの記録を " + BackupFileName + " に残して、書き直します。");
        }

        private static void Write()
        {
            _unsaved = true;
            string path = SaveSystem.PathOf(FileName);
            try
            {
                if (!CanOverwrite(path))
                {
                    return;
                }

                SaveStore.Write(path, ToJson(_seen));
                _fileState = FileState.Valid;
                _unsaved = false;
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
