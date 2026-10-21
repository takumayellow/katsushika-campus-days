using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 指で出した選択肢を、シーンに組んだ会話の枠の配線のまま見る (#187)。
    /// DialogueSystem が選択肢を出し、DialogueView が本文の欄に並べ、TmpLinkTap が叩きを返す経路をそのまま使う。
    /// 文字の大きさと行の間そのものは EditMode の DialogueChoiceLayoutTests が測っているので、ここでは配線を見る。
    /// </summary>
    public sealed class DialogueChoiceViewPlayTests : DialogueChoicePlayTestBase
    {
        /// <summary>選択肢を最初に描くときの間隔が、横持ちの値になる。</summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_ChoicesAreSpacedForLandscapeWhenFirstDrawn()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: false);

            AssertTouchChoices(DialogueBody(), portrait: false);
            AssertNoErrors("キャンパス");
        }

        /// <summary>選択肢を最初に描くときの間隔が、縦持ちの値になる。</summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_ChoicesAreSpacedForPortraitWhenFirstDrawn()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: true);

            AssertTouchChoices(DialogueBody(), portrait: true);
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 指で「やめる」を叩くと、どの選択肢も選ばずに会話が閉じる。叩いた結果が叩く板から会話の枠を通って
        /// DialogueSystem まで届くところを見る。キーの Esc と同じく、答えた話題にも好感度にも数えない。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_TappingCancelClosesTheDialogueWithoutAnsweringTheTopic()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: false);
            DialogueSystem dialogue = DialogueSystem.Instance;
            TMP_Text body = DialogueBody();
            string topicKey = DialogueId + "/" + TopicId;
            Assert.IsFalse(NpcAffinity.HasAnswered(topicKey), "新しい遊びなのに、この話題に答えたことになっている");
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue), "選択肢を出している間は操作を封鎖しているはず");
            int affinity = NpcAffinity.Get(DialogueId);
            int finished = 0;
            dialogue.Finished += () => finished++;

            // 選択肢を出したあとに置いた指がまだ無いので、「やめる」を受け取らない。
            Assert.IsFalse(dialogue.TapClose(), "指を置く前なのに「やめる」で会話が閉じた");
            Assert.IsTrue(dialogue.IsChoosing);
            yield return TapAreaPress();

            // 選択肢でも「やめる」でもない空白を叩いても閉じない。
            DialogueTap().OnPointerClick(Finger(7, new Vector2(2f, Screen.height - 2f)));
            yield return PlayModeScenes.Advance(2, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "空白を叩いたら選択肢が消えた");
            Assert.AreEqual(0, finished, "空白を叩いたら会話が閉じた");

            Vector2 cancel = LinkCenter(body, DialogueView.CloseLinkId(dialogue.Choices.Count));
            DialogueTap().OnPointerClick(Finger(7, cancel));
            yield return PlayModeScenes.Advance(2, 0f);

            Assert.IsFalse(dialogue.IsPlaying, "「やめる」を叩いても会話が閉じない（叩きが会話に届いていないか、TapClose の配線が切れている）");
            Assert.IsFalse(dialogue.IsChoosing, "「やめる」を叩いても選択肢が残っている");
            Assert.AreEqual(1, finished, "会話が閉じた知らせが 1 回でない");
            Assert.IsFalse(KCDInput.IsBlockedBy(dialogue), "会話が閉じたのに操作の封鎖が残っている");
            Assert.IsFalse(DialogueTap().transform.parent.gameObject.activeSelf, "会話が閉じたのに枠が残っている");
            Assert.IsFalse(NpcAffinity.HasAnswered(topicKey), "「やめる」を答えた話題として数えた");
            Assert.AreEqual(affinity, NpcAffinity.Get(DialogueId), "「やめる」で好感度が動いた");
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 選択肢を出したまま画面を縦と横に回すと、選択肢を向きに合う間隔で描き直す。
        /// 会話は閉じず、選んでいる行も動かない。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_RotatingTheScreenWhileChoosingRedrawsTheChoices()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: false);
            DialogueSystem dialogue = DialogueSystem.Instance;
            TMP_Text body = DialogueBody();
            int index = dialogue.ChoiceIndex;
            AssertTouchChoices(body, portrait: false);

            ScreenShape.Force(true);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "画面を縦に回したら選択肢が閉じた");
            Assert.AreEqual(index, dialogue.ChoiceIndex, "画面を縦に回したら選んでいる行が動いた");
            AssertTouchChoices(body, portrait: true);

            ScreenShape.Force(false);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "画面を横に戻したら選択肢が閉じた");
            Assert.AreEqual(index, dialogue.ChoiceIndex, "画面を横に戻したら選んでいる行が動いた");
            AssertTouchChoices(body, portrait: false);
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 選択肢を出したまま指とマウスの扱いを切り替えると、選択肢を描き直す。
        /// マウスには「やめる」も行の間も無く、キーの案内が付き、指に戻すとそれが消える。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_SwitchingTheInputWhileChoosingRedrawsTheChoices()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: false);
            DialogueSystem dialogue = DialogueSystem.Instance;
            TMP_Text body = DialogueBody();
            AssertTouchChoices(body, portrait: false);

            InputMode.Force(InputKind.Pointer);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "マウスの扱いに切り替えたら選択肢が閉じた");
            AssertPointerChoices(body);

            InputMode.Force(InputKind.Touch);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "指の扱いに戻したら選択肢が閉じた");
            AssertTouchChoices(body, portrait: false);
            AssertNoErrors("キャンパス");
        }

        /// <summary>会話の枠の本文の欄。選択肢はここに並ぶ。</summary>
        private static TMP_Text DialogueBody()
        {
            TmpLinkTap tap = DialogueTap();
            Assert.IsNotNull(tap, "会話の枠の叩く板が無い");
            Transform body = tap.transform.parent.Find("BodyArea/Body");
            Assert.IsNotNull(body, "会話の枠に本文の欄（BodyArea/Body）が無い");
            TMP_Text label = body.GetComponent<TMP_Text>();
            Assert.IsNotNull(label, "本文の欄に TMP_Text が無い");
            return label;
        }

        /// <summary>指の選択肢が、画面の向きに合う間隔で、最後の「やめる」まで並んでいる。</summary>
        private static void AssertTouchChoices(TMP_Text body, bool portrait)
        {
            string shape = portrait ? "縦持ち" : "横持ち";
            float gap = portrait ? DialogueView.PortraitChoiceGap : DialogueView.LandscapeChoiceGap;
            Assert.AreEqual(gap, body.paragraphSpacing * body.fontSize / 100f, 0.01f, shape + "の選択肢の間隔が合わない");

            int choices = DialogueSystem.Instance.Choices.Count;
            body.ForceMeshUpdate();
            TMP_TextInfo info = body.textInfo;
            Assert.AreEqual(choices + 1, info.linkCount, shape + "の選択肢と「やめる」の数が合わない");
            Assert.AreEqual(DialogueView.CloseLinkId(choices), info.linkInfo[choices].GetLinkID(), shape + "の最後が「やめる」でない");
        }

        /// <summary>マウスの選択肢は行の間を空けず、「やめる」を置かずにキーの案内を添える。</summary>
        private static void AssertPointerChoices(TMP_Text body)
        {
            Assert.AreEqual(0f, body.paragraphSpacing, 0.01f, "マウスなのに選択肢の間が空いている");

            int choices = DialogueSystem.Instance.Choices.Count;
            body.ForceMeshUpdate();
            Assert.AreEqual(choices, body.textInfo.linkCount, "マウスなのに「やめる」が並んでいる");
            StringAssert.Contains("Enter", body.text, "マウスなのにキーの案内が無い");
        }

        /// <summary>
        /// linkId のリンクを囲む四角の中心の画面の座標。TmpLinkTap が叩きを探すのと同じく、見える文字の位置から求める。
        /// オーバーレイのキャンバスはカメラ無しで変換する。
        /// </summary>
        private static Vector2 LinkCenter(TMP_Text body, string linkId)
        {
            body.ForceMeshUpdate();
            TMP_TextInfo info = body.textInfo;
            Canvas canvas = body.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (int i = 0; i < info.linkCount; i++)
            {
                TMP_LinkInfo link = info.linkInfo[i];
                if (link.GetLinkID() != linkId)
                {
                    continue;
                }

                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                int end = Mathf.Min(link.linkTextfirstCharacterIndex + link.linkTextLength, info.characterCount);
                for (int c = link.linkTextfirstCharacterIndex; c < end; c++)
                {
                    TMP_CharacterInfo character = info.characterInfo[c];
                    if (!character.isVisible)
                    {
                        continue;
                    }

                    Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, body.transform.TransformPoint(character.bottomLeft));
                    Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, body.transform.TransformPoint(character.topRight));
                    min = Vector2.Min(min, Vector2.Min(bottomLeft, topRight));
                    max = Vector2.Max(max, Vector2.Max(bottomLeft, topRight));
                }

                if (min.x <= max.x)
                {
                    return (min + max) / 2f;
                }
            }

            Assert.Fail("リンク " + linkId + " の見える文字が無い");
            return Vector2.zero;
        }
    }
}
