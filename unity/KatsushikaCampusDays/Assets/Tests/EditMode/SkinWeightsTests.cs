using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// どの品質レベルでも 1 頂点 4 ボーンで肌を変形させることを守る (#49)。
    ///
    /// WebGL は Mobile（品質レベル 0）で動き、そこだけ skinWeights が 2 だった。
    /// スカートの前中央は Hips ＋ 左右 UpperLeg の 3 本が要る場所で、
    /// 上位 2 本に切ると x=0 をまたいで「残る脚」が左右で入れ替わる。
    /// 走ると左右の脚は逆位相なので、隣り合う頂点が反対向きに動いて布が裂ける。
    /// 実測で 3 本目のウェイトは最大 0.327 あり、丸め残りでは済まない大きさだった。
    ///
    /// 直すたびに「Web だけ形が違う」に戻らないよう、設定ファイルを直接見張る。
    /// </summary>
    public sealed class SkinWeightsTests
    {
        /// <summary>スカート前中央に必要な本数（Hips ＋ 左右の UpperLeg）。</summary>
        private const int Required = 4;

        [Test]
        public void すべての品質レベルが一頂点四ボーンで変形する()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "ProjectSettings", "QualitySettings.asset"));
            Assert.IsTrue(File.Exists(path), path + " が無い");

            string yaml = File.ReadAllText(path);
            // name と skinWeights は品質レベルごとに同じ順で並ぶ。名前で報告したいので両方拾う。
            MatchCollection names = Regex.Matches(yaml, @"^\s{4}name:\s*(?<v>.+?)\s*$", RegexOptions.Multiline);
            MatchCollection weights = Regex.Matches(yaml, @"^\s{4}skinWeights:\s*(?<v>\d+)\s*$", RegexOptions.Multiline);

            Assert.Greater(names.Count, 0, "品質レベルを読めていない");
            Assert.AreEqual(names.Count, weights.Count, "name と skinWeights の数が合わない。読み方が壊れている");

            var thin = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                int n = int.Parse(weights[i].Groups["v"].Value);
                if (n < Required)
                {
                    thin.Add(string.Format("{0}: skinWeights {1}", names[i].Groups["v"].Value, n));
                }
            }

            Assert.IsEmpty(
                thin,
                "この品質レベルではスカート前中央のウェイトが落ちて布が裂ける:\n" + string.Join("\n", thin));
        }
    }
}
