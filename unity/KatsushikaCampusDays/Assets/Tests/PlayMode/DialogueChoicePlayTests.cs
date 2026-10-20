using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 指で選択肢を叩く経路 (#187)。キャンパスの会話の枠と DialogueSystem をつないだまま、
    /// 叩いた結果が会話に届くところまでを見る。
    /// </summary>
    public sealed class DialogueChoicePlayTests : SceneTestBase
    {
        private const string DialogueId = "sora";
        private const string TopicId = "t_choice";

        [TearDown]
        public void ReleaseForcedMode()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            TouchInput.Clear();
        }

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

        /// <summary>
        /// キャンパスを指・横持ちで読み、選択肢が 3 つの話題を出して選択肢を見せるところまで進める。
        /// 指は「選択肢を出したあとに置いた指」でないと選ばないので、その最初の叩きは <see cref="TapAreaPress"/> で入れる。
        /// </summary>
        private IEnumerator OpenChoices(bool portrait)
        {
            InputMode.Force(InputKind.Touch);
            ScreenShape.Force(portrait);

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);

            DialogueSystem dialogue = DialogueSystem.Instance;
            Assert.IsNotNull(dialogue, "キャンパスに会話の仕組みが無い");
            Assert.IsTrue(dialogue.PlayTopic(DialogueId, TopicId), "選択肢のある話題を出せない（会話中か、話題が無い）");
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsPlaying);

            dialogue.Advance();
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsTrue(dialogue.IsChoosing, "話題の最後の行のあとに選択肢が出ない");
            Assert.AreEqual(3, dialogue.Choices.Count, "選択肢の数が 3 つでない");
            Assert.IsTrue(InputMode.IsTouch, "指のはずがマウスの扱いになっている");
        }

        /// <summary>
        /// 会話の枠の叩く板（親が Dialogue の TmpLinkTap）の仮想のボタンを押す。DialogueSystem は
        /// 選択肢を出した次のフレーム以降の最初の叩きを見て、そこから選択肢を受け付ける。
        /// </summary>
        private static IEnumerator TapAreaPress()
        {
            OnScreenButton button = DialogueTapButton();
            Assert.IsNotNull(button, "会話の枠の叩く板が無い");
            Assert.IsTrue(button.isActiveAndEnabled, "指で遊んでいるのに会話の叩く板が動いていない");

            PointerEventData finger = Finger(5, ScreenPointOf((RectTransform)button.transform));
            button.OnPointerDown(finger);
            yield return null;
            button.OnPointerUp(finger);
            yield return PlayModeScenes.Advance(2, 0f);
        }

        private static OnScreenButton DialogueTapButton()
        {
            foreach (TmpLinkTap tap in Object.FindObjectsByType<TmpLinkTap>(FindObjectsInactive.Include))
            {
                Transform parent = tap.transform.parent;
                if (parent != null && parent.name == "Dialogue")
                {
                    return tap.GetComponent<OnScreenButton>();
                }
            }

            return null;
        }

        private static PointerEventData Finger(int id, Vector2 position)
        {
            return new PointerEventData(EventSystem.current) { pointerId = id, position = position };
        }

        /// <summary>rect の中心の画面の座標。オーバーレイのキャンバスはカメラ無しで変換する。</summary>
        private static Vector2 ScreenPointOf(RectTransform rect)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
    }
}
