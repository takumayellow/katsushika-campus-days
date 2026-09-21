using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>フォルダ作成・ログ出力など、バッチ実行の土台になる小物。</summary>
    public static class EditorPaths
    {
        public const string CampusFbx = "Assets/Models/Campus/campus.fbx";
        public const string TreesFbx = "Assets/Models/Campus/trees.fbx";
        public const string TreesJson = "Assets/Models/Campus/trees.json";
        public const string CampusJson = "Assets/Data/campus.json";
        public const string CharactersFolder = "Assets/Models/Characters";
        public const string ScenesFolder = "Assets/Scenes";
        public const string CampusScene = "Assets/Scenes/Campus.unity";
        public const string TitleScene = "Assets/Scenes/Title.unity";
        public const string GeneratedFolder = "Assets/Generated";

        /// <summary>"Assets/A/B" のような相対パスのフォルダを、無ければ順に作る。</summary>
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>バッチのログへ 1 行出す。Editor 専用なので Debug は使わない。</summary>
        public static void Report(string message)
        {
            Console.WriteLine("[KCD] " + message);
        }

        /// <summary>検証ログ（unity/verify.log）へ追記する。</summary>
        public static void AppendVerify(string message)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify.log"));
                string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(path, stamp + "  " + message + Environment.NewLine);
            }
            catch (IOException error)
            {
                Console.WriteLine("[KCD] verify.log へ書けません: " + error.Message);
            }
        }

        /// <summary>コマンドライン引数 -name value を読む。無ければ fallback。</summary>
        public static string ReadArgument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        /// <summary>プロジェクト直下（Assets の親）からの絶対パス。</summary>
        public static string ProjectRelative(string relative)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
        }
    }
}
