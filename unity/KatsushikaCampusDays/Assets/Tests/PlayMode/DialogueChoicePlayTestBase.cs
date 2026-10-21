using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;

namespace KCD.Tests
{
    /// <summary>
    /// 指で選択肢を出す会話の PlayMode テストの土台 (#187)。キャンパスを指の扱いで読み込み、選択肢が 3 つある話題を
    /// 選択肢が見えるところまで進める。会話の枠と DialogueSystem は、シーンに組んだままつないで使う。
    /// </summary>
    public abstract class DialogueChoicePlayTestBase : SceneTestBase
    {
        protected const string DialogueId = "sora";
        protected const string TopicId = "t_choice";

        [TearDown]
        public void ReleaseForcedMode()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            TouchInput.Clear();
        }

        /// <summary>
        /// キャンパスを指で読み、選択肢が 3 つの話題を出して選択肢を見せるところまで進める。
        /// 指は「選択肢を出したあとに置いた指」でないと選ばないので、その最初の叩きは <see cref="TapAreaPress"/> で入れる。
        /// portrait は読み込む前に決める（選択肢を最初に描くときの画面の向き）。
        /// </summary>
        protected IEnumerator OpenChoices(bool portrait)
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
        protected static IEnumerator TapAreaPress()
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

        /// <summary>会話の枠の叩く板（親が Dialogue の TmpLinkTap）。選択肢を叩いた結果はここから会話へ届く。</summary>
        protected static TmpLinkTap DialogueTap()
        {
            foreach (TmpLinkTap tap in Object.FindObjectsByType<TmpLinkTap>(FindObjectsInactive.Include))
            {
                Transform parent = tap.transform.parent;
                if (parent != null && parent.name == "Dialogue")
                {
                    return tap;
                }
            }

            return null;
        }

        protected static OnScreenButton DialogueTapButton()
        {
            TmpLinkTap tap = DialogueTap();
            return tap != null ? tap.GetComponent<OnScreenButton>() : null;
        }

        protected static PointerEventData Finger(int id, Vector2 position)
        {
            return new PointerEventData(EventSystem.current) { pointerId = id, position = position };
        }

        /// <summary>rect の中心の画面の座標。オーバーレイのキャンバスはカメラ無しで変換する。</summary>
        protected static Vector2 ScreenPointOf(RectTransform rect)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
    }
}
