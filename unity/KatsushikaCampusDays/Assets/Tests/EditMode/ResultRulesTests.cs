using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 一日の終わりのリザルト（Ending/result.json と DayEndEvaluator）の決まりごと (#67)。
    /// ランクは min_percent の高い順に見て最初に届いたもの、総数はデータの実数と同じ。
    /// </summary>
    public sealed class ResultRulesTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");
        private static string ResultPath => Path.Combine(DataRoot, "Ending", "result.json");

        /// <summary>わざと並びを崩した ranks。読み込みで高い順に並べ直せているかを見る。</summary>
        private const string UnsortedRanksJson =
            "{\"ranks\":[" +
            "{\"rank\":\"B\",\"min_percent\":45}," +
            "{\"rank\":\"S\",\"min_percent\":90}," +
            "{\"rank\":\"C\",\"min_percent\":0}," +
            "{\"rank\":\"A\",\"min_percent\":70}]}";

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        /// <summary>
        /// 使い捨ての DayEndEvaluator に決まりを読ませて渡す。EditMode では Awake が走らないので、
        /// 暗幕もスポーンも作られず、ランクの計算だけを見られる。
        /// </summary>
        private static void WithEvaluator(string json, Action<DayEndEvaluator> body)
        {
            var go = new GameObject("DayEndEvaluatorForTest");
            try
            {
                DayEndEvaluator evaluator = go.AddComponent<DayEndEvaluator>();
                evaluator.LoadRules(json);
                body(evaluator);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---- ランクの境目 ----

        [Test]
        public void RankFor_TakesTheHighestRankReached()
        {
            WithEvaluator(UnsortedRanksJson, evaluator =>
            {
                Assert.AreEqual("S", evaluator.RankFor(100));
                Assert.AreEqual("S", evaluator.RankFor(90));
                Assert.AreEqual("A", evaluator.RankFor(89));
                Assert.AreEqual("A", evaluator.RankFor(70));
                Assert.AreEqual("B", evaluator.RankFor(69));
                Assert.AreEqual("B", evaluator.RankFor(45));
                Assert.AreEqual("C", evaluator.RankFor(44));
                Assert.AreEqual("C", evaluator.RankFor(0));
            });
        }

        [Test]
        public void RankFor_BelowEveryRank_FallsBackToC()
        {
            WithEvaluator("{\"ranks\":[{\"rank\":\"S\",\"min_percent\":90}]}", evaluator =>
            {
                Assert.AreEqual("S", evaluator.RankFor(95));
                Assert.AreEqual("C", evaluator.RankFor(50), "どのランクにも届かないときは ResultData の既定");
            });
        }

        [Test]
        public void RankFor_UsesTheBoundariesWrittenInResultJson()
        {
            // 実データの境目ちょうどで上のランク、1 点下で次のランクになること。
            List<object> ranks = MiniJson.GetArray(ReadObject(ResultPath), "ranks");
            var entries = new List<KeyValuePair<int, string>>();
            foreach (object item in ranks)
            {
                var node = item as Dictionary<string, object>;
                Assert.IsNotNull(node, "ranks の要素がオブジェクトでない");
                entries.Add(new KeyValuePair<int, string>(
                    Mathf.CeilToInt(MiniJson.GetFloat(node, "min_percent", -1f)), MiniJson.GetString(node, "rank")));
            }

            entries.Sort((a, b) => b.Key.CompareTo(a.Key));
            Assert.Greater(entries.Count, 1, "ランクが 1 つ以下");

            WithEvaluator(File.ReadAllText(ResultPath), evaluator =>
            {
                Assert.AreEqual(entries[0].Value, evaluator.RankFor(100), "100% で最上位にならない");
                for (int i = 0; i < entries.Count; i++)
                {
                    int boundary = entries[i].Key;
                    Assert.AreEqual(entries[i].Value, evaluator.RankFor(boundary), boundary + "% ちょうど");
                    if (i + 1 < entries.Count && boundary > 0)
                    {
                        Assert.AreEqual(entries[i + 1].Value, evaluator.RankFor(boundary - 1), (boundary - 1) + "%");
                    }
                }
            });
        }

        // ---- result.json とデータの突き合わせ ----

        [Test]
        public void ResultJson_EveryPercentHasARankWithText()
        {
            Dictionary<string, object> root = ReadObject(ResultPath);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasZero = false;
            foreach (object item in MiniJson.GetArray(root, "ranks"))
            {
                var node = (Dictionary<string, object>)item;
                string rank = MiniJson.GetString(node, "rank");
                Assert.IsTrue(seen.Add(rank), "ランク " + rank + " が重複している");
                hasZero |= MiniJson.GetFloat(node, "min_percent", -1f) <= 0f;
                foreach (string key in new[] { "title_ja", "title_en", "comment_ja", "comment_en" })
                {
                    Assert.IsFalse(string.IsNullOrEmpty(MiniJson.GetString(node, key)), rank + " の " + key + " が空");
                }
            }

            Assert.IsTrue(hasZero, "min_percent 0 のランクが無いと、低い達成率で題名と講評が空になる");
        }

        [Test]
        public void ResultJson_WeightsAddUpToOne()
        {
            Dictionary<string, object> weights = MiniJson.GetObject(ReadObject(ResultPath), "weights");
            Assert.IsNotNull(weights, "result.json に weights が無い");

            float sum = 0f;
            foreach (string key in new[] { "quests", "collectibles", "photos", "buildings" })
            {
                Assert.IsTrue(weights.ContainsKey(key), "weights." + key + " が無い");
                sum += MiniJson.GetFloat(weights, key);
            }

            Assert.AreEqual(1f, sum, 1e-4f, "重みの合計が 1 でないと、全部達成しても 100% にならない");
        }

        [Test]
        public void ResultJson_TotalsMatchTheData()
        {
            Dictionary<string, object> totals = MiniJson.GetObject(ReadObject(ResultPath), "totals");
            Assert.IsNotNull(totals, "result.json に totals が無い");

            int quests = Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json").Length;
            Assert.AreEqual(quests, MiniJson.GetInt(totals, "quests"),
                "totals.quests が Data/Quests のクエスト数と違う");

            Dictionary<string, object> collectibles =
                ReadObject(Path.Combine(DataRoot, "Collectibles", "collectibles.json"));

            // 集める対象は隠しアイテム（source = hidden）。クエストの報酬（source = quest）は数えない。
            int hidden = 0;
            foreach (object item in MiniJson.GetArray(collectibles, "collectibles"))
            {
                if (item is Dictionary<string, object> node && MiniJson.GetString(node, "source") == "hidden")
                {
                    hidden++;
                }
            }

            Assert.AreEqual(hidden, MiniJson.GetInt(totals, "collectibles"),
                "totals.collectibles が collectibles.json の隠しアイテム数と違う");
            Assert.AreEqual(MiniJson.GetArray(collectibles, "photo_spots").Count, MiniJson.GetInt(totals, "photos"),
                "totals.photos が collectibles.json の写真スポット数と違う");
        }
    }
}
