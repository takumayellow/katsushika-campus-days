using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ゲームが読むのは Assets/Resources/KCD の複製で、書き直すのは Assets/Data の原本 (#67)。
    /// 複製は DataBundler.SyncAll（SceneBuilder.BuildAll の最初）が作るので、原本だけ直して
    /// シーンを作り直さないと、テストは原本を見て通り、ゲームは古い複製を読む。
    /// </summary>
    public sealed class DataBundleTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");
        private static string BundleRoot => Path.Combine(Application.dataPath, "Resources", "KCD");

        private static SortedSet<string> JsonNames(string folder)
        {
            var names = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(folder, "*.json"))
            {
                names.Add(Path.GetFileName(path));
            }

            return names;
        }

        /// <summary>改行コードの違い（チェックアウトの設定）だけでは食い違いにしない。</summary>
        private static string ReadNormalized(string path)
        {
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        [Test]
        public void EveryDataFolderIsCopiedToResourcesUnchanged()
        {
            string[] folders = Directory.GetDirectories(DataRoot);
            Assert.Greater(folders.Length, 0, DataRoot + " にフォルダが無い");

            var problems = new List<string>();
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                string copy = Path.Combine(BundleRoot, name);
                if (!Directory.Exists(copy))
                {
                    problems.Add(name + "/ が Resources/KCD に無い（新しいフォルダなら DataBundler.Folders に足す）");
                    continue;
                }

                SortedSet<string> sources = JsonNames(folder);
                SortedSet<string> copies = JsonNames(copy);
                foreach (string file in sources)
                {
                    if (!copies.Contains(file))
                    {
                        problems.Add(name + "/" + file + " が複製されていない");
                    }
                    else if (ReadNormalized(Path.Combine(folder, file)) != ReadNormalized(Path.Combine(copy, file)))
                    {
                        problems.Add(name + "/" + file + " の中身が原本と違う");
                    }
                }

                foreach (string file in copies)
                {
                    if (!sources.Contains(file))
                    {
                        problems.Add(name + "/" + file + " は原本に無い（消し忘れの古い複製）");
                    }
                }
            }

            Assert.IsEmpty(problems,
                "Assets/Data と Assets/Resources/KCD が食い違っている。SceneBuilder.BuildAll（DataBundler.SyncAll）を回し直すこと:\n  " +
                string.Join("\n  ", problems));
        }
    }
}
