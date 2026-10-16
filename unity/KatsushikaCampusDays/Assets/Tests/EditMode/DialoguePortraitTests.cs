using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>会話ウィンドウの話し手の顔 (#11)。行ごとに誰の顔を出すかと、顔を撮るカメラの置き方。</summary>
    public sealed class DialoguePortraitTests
    {
        private const string Json =
            "{\"id\":\"kaname\",\"speaker\":\"中川 かなめ\",\"topics\":[{\"id\":\"t\",\"lines\":[" +
            "\"いらっしゃい。\"," +
            "{\"speaker\":\"自分\",\"text\":\"日替わりを。\"}," +
            "{\"speaker\":\"\",\"text\":\"……\"}," +
            "{\"speaker\":\"店長\",\"text\":\"かなめ、牛乳！\"}," +
            "{\"text\":\"メモが挟まっている。\",\"written\":true}" +
            "]}]}";

        private static List<DialogueLine> Lines()
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(Json) as Dictionary<string, object>);
            return data.Topics[0].Lines;
        }

        [Test]
        public void ThePersonYouTalkToAndThePlayerShowTheirFaces()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.Npc, DialoguePortrait.SubjectOf(lines[0], "kaname"));
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[1], "kaname"), "「自分」の行");
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[2], "kaname"), "話者が空の行");
        }

        [Test]
        public void OthersAndWrittenTextHaveNoFace()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[3], "kaname"), "その場にいない人の名前");
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[4], "kaname"), "紙に書かれた文");
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(null, "kaname"));
        }

        [Test]
        public void WithoutAPersonToTalkToOnlyThePlayerHasAFace()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[0], string.Empty), "メモなど NPC の決まらない会話");
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[1], string.Empty));
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[0], "inari"), "別の人の会話データ");
        }

        [Test]
        public void TheCameraFacesTheHeadFromInFront()
        {
            var head = new Vector3(1f, 1.5f, 2f);
            Pose pose = DialoguePortrait.CameraPose(head, Vector3.forward, 0.8f, 0f);

            Assert.That(Vector3.Distance(pose.position, head + new Vector3(0f, 0.05f, 0.8f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, Vector3.back), Is.LessThan(0.01f));
        }

        [Test]
        public void TheCameraStaysLevelWhenTheBodyLeans()
        {
            Pose pose = DialoguePortrait.CameraPose(Vector3.zero, new Vector3(0f, -0.5f, 1f), 0.8f, 20f);

            Assert.That(pose.position.y, Is.EqualTo(0.05f).Within(1e-4f), "前かがみでも頭と同じ高さから撮る");
            Assert.That(new Vector2(pose.position.x, pose.position.z).magnitude, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(pose.position.x, Is.GreaterThan(0f), "正面から少し横へ回す");
        }
    }
}
