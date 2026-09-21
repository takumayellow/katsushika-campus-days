using System.IO;
using UnityEditor;

namespace KCD.Editor
{
    /// <summary>
    /// Assets/Data 配下の JSON を Resources へ複製する。
    /// ランタイムは QuestSystem / DialogueSystem が Resources.LoadAll&lt;TextAsset&gt; で読む。
    /// </summary>
    public static class DataBundler
    {
        public const string QuestSource = "Assets/Data/Quests";
        public const string DialogueSource = "Assets/Data/Dialogue";
        public const string QuestTarget = "Assets/Resources/KCD/Quests";
        public const string DialogueTarget = "Assets/Resources/KCD/Dialogue";
        public const string SourceRoot = "Assets/Data";
        public const string TargetRoot = "Assets/Resources/KCD";

        /// <summary>同期するフォルダ。Assets/Data/&lt;name&gt; → Assets/Resources/KCD/&lt;name&gt;。</summary>
        public static readonly string[] Folders =
        {
            "Quests", "Dialogue", "Collectibles", "Localization", "Mobs", "Ending"
        };

        /// <summary>全フォルダを同期し、コピーした本数を返す。</summary>
        public static int SyncAll()
        {
            int count = 0;
            foreach (string folder in Folders)
            {
                count += Sync(SourceRoot + "/" + folder, TargetRoot + "/" + folder);
            }

            AssetDatabase.Refresh();
            EditorPaths.Report("Resources へ同期した JSON: " + count + " 本");
            return count;
        }

        private static int Sync(string source, string target)
        {
            if (!AssetDatabase.IsValidFolder(source))
            {
                EditorPaths.Report("データフォルダが見つかりません: " + source);
                return 0;
            }

            EditorPaths.EnsureFolder(target);
            RemoveStale(source, target);

            int count = 0;
            foreach (string path in Directory.GetFiles(EditorPaths.ProjectRelative(source), "*.json"))
            {
                string name = Path.GetFileName(path);
                string destination = Path.Combine(EditorPaths.ProjectRelative(target), name);

                if (!File.Exists(destination) || !SameContent(path, destination))
                {
                    File.Copy(path, destination, true);
                    AssetDatabase.ImportAsset(target + "/" + name, ImportAssetOptions.ForceUpdate);
                }

                count++;
            }

            return count;
        }

        /// <summary>元から消えた JSON が Resources に残っていると二重定義になるので削除する。</summary>
        private static void RemoveStale(string source, string target)
        {
            string sourceDirectory = EditorPaths.ProjectRelative(source);

            foreach (string path in Directory.GetFiles(EditorPaths.ProjectRelative(target), "*.json"))
            {
                string name = Path.GetFileName(path);
                if (!File.Exists(Path.Combine(sourceDirectory, name)))
                {
                    AssetDatabase.DeleteAsset(target + "/" + name);
                }
            }
        }

        private static bool SameContent(string left, string right)
        {
            return File.ReadAllText(left) == File.ReadAllText(right);
        }
    }
}
