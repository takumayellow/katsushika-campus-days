using System.Globalization;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 設定画面で変えるカメラの感度と上下の向き (#6)。PlayerPrefs に文字列で覚える。
    /// 感度は 1〜10 の段で、真ん中の 5 がシーンに置いた感度そのまま。上下の反転はシーンの向きを逆にするかどうか。
    /// 毎フレーム読むので、一度読んだ値を覚えておき、書いたときに入れ替える。
    /// </summary>
    public static class CameraPrefs
    {
        public const string SensitivityPrefKey = "KCD.Camera.Sensitivity";
        public const string InvertPrefKey = "KCD.Camera.InvertY";

        public const int MinLevel = 1;
        public const int MaxLevel = 10;
        public const int DefaultLevel = 5;

        private static bool _loaded;
        private static int _level = DefaultLevel;
        private static bool _invertY;

        /// <summary>感度の段（1〜10）。</summary>
        public static int SensitivityLevel
        {
            get
            {
                Load();
                return _level;
            }
            set
            {
                Load();
                _level = ClampLevel(value);
                if (!ExhibitMode.Enabled)
                {
                    PlayerPrefs.SetString(SensitivityPrefKey, _level.ToString(CultureInfo.InvariantCulture));
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>上下の向きをシーンの既定と逆にするか。</summary>
        public static bool InvertY
        {
            get
            {
                Load();
                return _invertY;
            }
            set
            {
                Load();
                _invertY = value;
                if (!ExhibitMode.Enabled)
                {
                    PlayerPrefs.SetString(InvertPrefKey, value ? "1" : "0");
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>シーンに置いた感度に掛ける倍率。</summary>
        public static float SensitivityScale => Scale(SensitivityLevel);

        /// <summary>上下の視点の入力に掛ける符号（反転なら -1）。</summary>
        public static float VerticalSign => InvertY ? -1f : 1f;

        /// <summary>段から倍率へ。5 で 1 倍、1 で 0.2 倍、10 で 2 倍。</summary>
        public static float Scale(int level)
        {
            return ClampLevel(level) / (float)DefaultLevel;
        }

        /// <summary>段を 1 つ動かす。端で止まる。</summary>
        public static int StepLevel(int level, int direction)
        {
            return ClampLevel(level + System.Math.Sign(direction));
        }

        public static int ClampLevel(int level)
        {
            return Mathf.Clamp(level, MinLevel, MaxLevel);
        }

        /// <summary>保存した文字列を段に読む。読めなければ既定の段。</summary>
        public static int ParseLevel(string saved)
        {
            return int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                ? ClampLevel(level)
                : DefaultLevel;
        }

        /// <summary>次に読むときに PlayerPrefs から読み直す。テストで保存を差し替えたあとに使う。</summary>
        public static void Reload()
        {
            _loaded = false;
        }

        /// <summary>
        /// 展示モード (#466) で次の人のために既定（感度 5・反転なし）へ戻す。保存はしない。
        /// 展示モードでは保存された値も読まない（変えても保存しないので、普段の設定だけが残っている）。
        /// </summary>
        public static void ResetForNextVisitor()
        {
            _loaded = true;
            _level = DefaultLevel;
            _invertY = false;
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            if (ExhibitMode.Enabled)
            {
                ResetForNextVisitor();
                return;
            }

            _loaded = true;
            _level = ParseLevel(PlayerPrefs.GetString(SensitivityPrefKey, string.Empty));
            _invertY = PlayerPrefs.GetString(InvertPrefKey, "0") == "1";
        }
    }
}
