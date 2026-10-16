using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// クエスト欄の目標文の最後の 1〜2 文字だけが次の行に落ちないこと (#187)。
    /// 縦持ちのスマホで「・理科大通りの正門からキャンパスに入」「る」と割れていた。
    /// Campus.unity のステップ欄（幅・余白・フォント）をそのまま使って測る。
    /// </summary>
    public sealed class OrphanLineFitTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        private Scene _scene;
        private TMP_Text _step;
        private float _size;
        private bool _autoSizing;
        private float _sizeMax;
        private float _fontSize;

        [OneTimeSetUp]
        public void OpenScene()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            QuestTrackerView view = _scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<QuestTrackerView>(true)).FirstOrDefault();
            Assert.IsNotNull(view, "QuestTrackerView が無い（KCD/シーンを組み直す）");
            _step = new SerializedObject(view).FindProperty("_stepLabel").objectReferenceValue as TMP_Text;
            Assert.IsNotNull(_step, "ステップ欄が差し込まれていない");
            _autoSizing = _step.enableAutoSizing;
            _sizeMax = _step.fontSizeMax;
            _fontSize = _step.fontSize;
            _size = _autoSizing ? _step.fontSizeMax : _step.fontSize;
        }

        [OneTimeTearDown]
        public void CloseScene()
        {
            EditorSceneManager.CloseScene(_scene, true);
        }

        [TearDown]
        public void RestoreSize()
        {
            _step.fontSizeMax = _sizeMax;
            _step.fontSize = _fontSize;
        }

        private void Show(string text)
        {
            _step.text = text;
            OrphanLineFit.Apply(_step, _size);
        }

        [Test]
        public void LastCharacterMovesBackToTheFirstLine()
        {
            // 元の大きさでは「る」だけが 2 行目に落ちる（落ちないなら、この測り方が欄に合っていない）。
            _step.text = "・理科大通りの正門からキャンパスに入る";
            _step.ForceMeshUpdate(true, true);
            Assert.IsTrue(OrphanLineFit.HasOrphan(_step), "元の大きさで 1 文字が落ちない");

            Show("・理科大通りの正門からキャンパスに入る");
            Assert.AreEqual(1, _step.textInfo.lineCount);
            Assert.GreaterOrEqual(_step.fontSize, _size * OrphanLineFit.MinScale);
        }

        [Test]
        public void ShortTextKeepsItsSize()
        {
            Show("・食堂へ行く");
            Assert.AreEqual(_size, _step.fontSize, 1e-3f);
        }

        [Test]
        public void TextThatNeedsTwoLinesKeepsItsSize()
        {
            // 2 行目に十分な文字があるなら、小さくしない。
            Show("・図書館で本を借りてから、講義棟の二階にいる先輩のところへ持っていく");
            Assert.AreEqual(2, _step.textInfo.lineCount);
            Assert.AreEqual(_size, _step.fontSize, 1e-3f);
        }

        [Test]
        public void SecondLineAfterABreakIsNotAnOrphan()
        {
            // 改行で始まる 2 行目（残り秒など）は、短くても取り残された行ではない。
            Show("・体育館へ行く\n<size=80%>残り 9 秒</size>");
            Assert.IsFalse(OrphanLineFit.HasOrphan(_step));
            Assert.AreEqual(_size, _step.fontSize, 1e-3f);
        }

        [Test]
        public void NoQuestStepLeavesAnOrphan()
        {
            var problems = new List<string>();
            string root = Path.Combine(Application.dataPath, "Data", "Quests");
            foreach (string file in Directory.GetFiles(root, "*.json"))
            {
                var node = MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                QuestData quest = QuestData.FromJson(node);
                foreach (QuestStep step in quest.Steps)
                {
                    foreach (string text in new[] { step.Text, step.TextEn })
                    {
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        string line = "・" + text + (step.Count > 1 ? "　(0/" + step.Count + ")" : string.Empty);
                        Show(line);
                        if (OrphanLineFit.HasOrphan(_step))
                        {
                            problems.Add(quest.Id + "/" + step.Id + ": " + line);
                        }
                    }
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
