using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using TMPro;
using UnityEditor;

namespace KCD.Editor
{
    /// <summary>
    /// TMP Essential Resources（設定・既定フォント・シェーダ）をプロジェクトへ置く。
    /// TMP_PackageResourceImporter.ImportResources は取り込みを遅延させるので、
    /// -quit を付けたバッチでは最後まで走らない。ここでは unitypackage
    /// （gzip + tar）をそのまま展開し、.meta ごと置いて GUID 参照を保つ。
    /// </summary>
    public static class TmpEssentials
    {
        public const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private const string PackageFile =
            "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        private const int BlockSize = 512;

        /// <summary>TMP Settings が無ければ展開する。使える状態なら true。</summary>
        public static bool Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath) != null)
            {
                return true;
            }

            string archive = Path.GetFullPath(PackageFile);
            if (!File.Exists(archive))
            {
                EditorPaths.Report("TMP Essential Resources が見つかりません: " + archive);
                return false;
            }

            int written = Extract(archive, Directory.GetParent(UnityEngine.Application.dataPath).FullName);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            EditorPaths.Report("TMP Essential Resources を展開しました: " + written + " 件");

            return AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath) != null;
        }

        /// <summary>unitypackage の中身を projectRoot 配下へ書き出す。</summary>
        private static int Extract(string archive, string projectRoot)
        {
            var assets = new Dictionary<string, byte[]>();
            var metas = new Dictionary<string, byte[]>();
            var paths = new Dictionary<string, string>();

            foreach (KeyValuePair<string, byte[]> entry in ReadTar(Decompress(archive)))
            {
                int slash = entry.Key.IndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }

                string id = entry.Key.Substring(0, slash);
                string kind = entry.Key.Substring(slash + 1);

                if (kind == "asset")
                {
                    assets[id] = entry.Value;
                }
                else if (kind == "asset.meta")
                {
                    metas[id] = entry.Value;
                }
                else if (kind == "pathname")
                {
                    paths[id] = Encoding.UTF8.GetString(entry.Value).Split('\n')[0].Trim();
                }
            }

            int written = 0;
            foreach (KeyValuePair<string, string> pair in paths)
            {
                if (string.IsNullOrEmpty(pair.Value) || !pair.Value.StartsWith("Assets/"))
                {
                    continue;
                }

                string target = Path.Combine(projectRoot, pair.Value.Replace('/', Path.DirectorySeparatorChar));

                if (assets.TryGetValue(pair.Key, out byte[] body))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, body);
                }
                else
                {
                    Directory.CreateDirectory(target);
                }

                if (metas.TryGetValue(pair.Key, out byte[] meta))
                {
                    File.WriteAllBytes(target + ".meta", meta);
                }

                written++;
            }

            return written;
        }

        private static byte[] Decompress(string archive)
        {
            using FileStream file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gzip.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>tar を頭から読む。unitypackage は普通のファイルエントリだけで出来ている。</summary>
        private static IEnumerable<KeyValuePair<string, byte[]>> ReadTar(byte[] tar)
        {
            int offset = 0;

            while (offset + BlockSize <= tar.Length)
            {
                string name = Text(tar, offset, 100);
                if (string.IsNullOrEmpty(name))
                {
                    yield break;
                }

                string prefix = Text(tar, offset + 345, 155);
                if (!string.IsNullOrEmpty(prefix))
                {
                    name = prefix + "/" + name;
                }

                long size = Octal(tar, offset + 124, 12);
                char type = (char)tar[offset + 156];
                offset += BlockSize;

                if (type == '0' || type == '\0')
                {
                    var body = new byte[size];
                    System.Array.Copy(tar, offset, body, 0, size);
                    yield return new KeyValuePair<string, byte[]>(name.Replace('\\', '/'), body);
                }

                offset += (int)((size + BlockSize - 1) / BlockSize) * BlockSize;
            }
        }

        private static string Text(byte[] tar, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && end < tar.Length && tar[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(tar, offset, end - offset).Trim();
        }

        private static long Octal(byte[] tar, int offset, int length)
        {
            long value = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte c = tar[i];
                if (c < (byte)'0' || c > (byte)'7')
                {
                    continue;
                }

                value = (value * 8) + (c - '0');
            }

            return value;
        }
    }
}
