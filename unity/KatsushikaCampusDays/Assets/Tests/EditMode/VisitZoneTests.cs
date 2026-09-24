using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// クエストの visit 判定の箱が、指定どおりの大きさでシーンに焼けていることを守る (#54)。
    ///
    /// VisitZone.Reset() / EntranceTrigger.Reset() は当たり判定の大きさを無条件に既定値へ代入していた。
    /// Reset() は Inspector のリセットだけでなく、エディタで AddComponent した瞬間にも呼ばれる。
    /// CampusProps.Zone は「箱を足す → 大きさを入れる → VisitZone を足す」の順だったので、
    /// 最後の行で大きさが 10x6x10 に巻き戻り、シーンの Zone_* 46 個すべてが既定値になっていた。
    /// 水盤ゾーンは 58x8x16 のはずが 10x6x10 で、位置の y は size.y=8 前提のまま焼かれていたため
    /// 箱の下端が地面から 1.018 m 浮き、プレイヤーの足元が判定の外に出て
    /// 「18:00 以降に水盤の前へ行く」(q_sunset) が達成できなかった。
    ///
    /// 見た目には何も出ない不具合なので、シーンの YAML を直接読んで CampusProps.cs の指定と突き合わせる。
    /// EditMode のアセンブリからは Editor のクラスを呼べないため、ソースの数値を正として取り出す。
    /// </summary>
    public sealed class VisitZoneTests
    {
        /// <summary>箱の下端がこれより高いと、プレイヤーの足元が判定から外れているとみなす（m）。</summary>
        private const float MaxBoxBottomY = 0.6f;

        private static string ReadAsset(params string[] parts)
        {
            string path = Path.Combine(Application.dataPath, Path.Combine(parts));
            Assert.IsTrue(File.Exists(path), path + " が無い");
            return File.ReadAllText(path);
        }

        private static float F(string literal)
        {
            return float.Parse(literal.TrimEnd('f'), CultureInfo.InvariantCulture);
        }

        // --- Reset() のガード ---------------------------------------------------------------

        [Test]
        public void 手つかずの箱にだけ既定の大きさを入れる()
        {
            Assert.IsTrue(VisitZone.ShouldApplyDefaultSize(Vector3.one), "1x1x1 は Unity が作った直後の箱");
        }

        [Test]
        public void すでに大きさが入っている箱は巻き戻さない()
        {
            Assert.IsFalse(VisitZone.ShouldApplyDefaultSize(new Vector3(58f, 8f, 16f)), "水盤ゾーン");
            Assert.IsFalse(VisitZone.ShouldApplyDefaultSize(new Vector3(4f, 3f, 4f)), "屋内 POI");
            Assert.IsFalse(VisitZone.ShouldApplyDefaultSize(VisitZone.DefaultSize), "既定値そのもの");
        }

        // --- シーンに焼かれた箱 -------------------------------------------------------------

        /// <summary>CampusProps.PlaceZones が指定している placeId → 大きさ。</summary>
        private static Dictionary<string, Vector3> DeclaredCampusZones()
        {
            string source = ReadAsset("Scripts", "Editor", "CampusProps.cs");
            var declared = new Dictionary<string, Vector3>();
            var pattern = new Regex(
                @"Zone\(parent,\s*""(?<id>[a-z0-9_]+)"",\s*""[^""]*"",\s*Local\([^)]*\),\s*"
                + @"new Vector3\((?<x>[-\d.f]+),\s*(?<y>[-\d.f]+),\s*(?<z>[-\d.f]+)\)\)");

            foreach (Match m in pattern.Matches(source))
            {
                declared[m.Groups["id"].Value] =
                    new Vector3(F(m.Groups["x"].Value), F(m.Groups["y"].Value), F(m.Groups["z"].Value));
            }

            Assert.GreaterOrEqual(declared.Count, 4, "CampusProps.PlaceZones を読めていない");
            return declared;
        }

        /// <summary>Campus.unity から「GameObject の名前 → BoxCollider の大きさ / Transform の位置」を取り出す。</summary>
        private static void ReadScene(out Dictionary<string, Vector3> sizes, out Dictionary<string, Vector3> positions)
        {
            string scene = ReadAsset("Scenes", "Campus.unity");
            var nameOf = new Dictionary<string, string>();

            foreach (Match m in Regex.Matches(
                scene, @"--- !u!1 &(?<fid>\d+)\nGameObject:(?<body>.*?)(?=\n--- |\z)", RegexOptions.Singleline))
            {
                Match name = Regex.Match(m.Groups["body"].Value, @"m_Name: (?<n>.*)");
                if (name.Success)
                {
                    nameOf[m.Groups["fid"].Value] = name.Groups["n"].Value.Trim();
                }
            }

            sizes = new Dictionary<string, Vector3>();
            positions = new Dictionary<string, Vector3>();
            CollectVectors(scene, @"--- !u!65 &\d+\nBoxCollider:", "m_Size", nameOf, sizes);
            CollectVectors(scene, @"--- !u!4 &\d+\nTransform:", "m_LocalPosition", nameOf, positions);
        }

        private static void CollectVectors(
            string scene, string header, string field, Dictionary<string, string> nameOf,
            Dictionary<string, Vector3> into)
        {
            foreach (Match m in Regex.Matches(
                scene, header + @"(?<body>.*?)(?=\n--- |\z)", RegexOptions.Singleline))
            {
                string body = m.Groups["body"].Value;
                Match owner = Regex.Match(body, @"m_GameObject: \{fileID: (?<fid>\d+)\}");
                Match vector = Regex.Match(
                    body, field + @": \{x: (?<x>[-\d.e]+), y: (?<y>[-\d.e]+), z: (?<z>[-\d.e]+)\}");
                if (!owner.Success || !vector.Success || !nameOf.TryGetValue(owner.Groups["fid"].Value, out string n))
                {
                    continue;
                }

                into[n] = new Vector3(
                    float.Parse(vector.Groups["x"].Value, CultureInfo.InvariantCulture),
                    float.Parse(vector.Groups["y"].Value, CultureInfo.InvariantCulture),
                    float.Parse(vector.Groups["z"].Value, CultureInfo.InvariantCulture));
            }
        }

        [Test]
        public void キャンパスのゾーンは指定どおりの大きさで焼かれている()
        {
            ReadScene(out Dictionary<string, Vector3> sizes, out _);

            foreach (KeyValuePair<string, Vector3> pair in DeclaredCampusZones())
            {
                string go = "Zone_" + pair.Key;
                Assert.IsTrue(sizes.ContainsKey(go), go + " がシーンに無い。SceneBuilder.BuildAll を実行する");
                Assert.AreEqual(
                    pair.Value, sizes[go],
                    go + " の当たり判定が CampusProps の指定と違う（Reset() に巻き戻されていないか）");
            }
        }

        [Test]
        public void ゾーンの箱の下端が地面にある()
        {
            ReadScene(out Dictionary<string, Vector3> sizes, out Dictionary<string, Vector3> positions);

            foreach (string id in DeclaredCampusZones().Keys)
            {
                string go = "Zone_" + id;
                float bottom = positions[go].y - sizes[go].y * 0.5f;
                Assert.Less(
                    bottom, MaxBoxBottomY,
                    go + " の箱の下端が " + bottom.ToString("0.000") + " m に浮いている。"
                    + "プレイヤーの足元が判定の外に出る");
            }
        }

        [Test]
        public void 屋内のクエスト地点は四角い小さな箱になっている()
        {
            ReadScene(out Dictionary<string, Vector3> sizes, out _);
            var expected = new Vector3(4f, 3f, 4f);
            int count = 0;

            foreach (KeyValuePair<string, Vector3> pair in sizes)
            {
                if (!pair.Key.StartsWith("Zone_poi_"))
                {
                    continue;
                }

                count++;
                Assert.AreEqual(expected, pair.Value, pair.Key + " が InteriorStage.AddPoiZones の指定と違う");
            }

            Assert.Greater(count, 30, "屋内の POI ゾーンが焼かれていない");
        }

        [Test]
        public void 温室の入口は開口に合わせて狭くなっている()
        {
            ReadScene(out Dictionary<string, Vector3> sizes, out _);
            Assert.IsTrue(sizes.ContainsKey("Entrance_greenhouse"), "Entrance_greenhouse がシーンに無い");
            Assert.Less(
                sizes["Entrance_greenhouse"].x, EntranceTrigger.BoxSize.x,
                "温室の入口が汎用の幅に巻き戻っている（開口は 1.8 m しかない）");
        }
    }
}
