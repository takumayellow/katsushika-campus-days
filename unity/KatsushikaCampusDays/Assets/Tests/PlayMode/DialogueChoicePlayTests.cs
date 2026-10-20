using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 指で選択肢を叩く経路 (#187)。キャンパスの会話の枠と DialogueSystem をつないだまま、
    /// 叩いた結果が会話に届くところまでを見る。
    /// </summary>
    public sealed class DialogueChoicePlayTests : DialogueChoicePlayTestBase
    {
        /// <summary>
        /// リンクを読み取った結果のうち「選択肢でない」を会話に渡しても、会話は閉じない。
        /// 空白を叩いたときも、形の崩れたリンクを叩いたときも、この値が出る。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_TappingWhatIsNotAChoiceKeepsTheChoicesOpen()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return OpenChoices(portrait: false);
            DialogueSystem dialogue = DialogueSystem.Instance;

            yield return TapAreaPress();
            int notAChoice = TmpLinkTap.ChoiceIn("choice:x", dialogue.Choices.Count);

            Assert.IsFalse(dialogue.TapChoice(notAChoice), "選択肢でない叩きを、選択肢として受け取った");
            Assert.IsTrue(dialogue.IsPlaying, "選択肢でないところを叩いたら会話が閉じた");
            Assert.IsTrue(dialogue.IsChoosing, "選択肢でないところを叩いたら選択肢が消えた");
            AssertNoErrors("キャンパス");
        }
    }
}
