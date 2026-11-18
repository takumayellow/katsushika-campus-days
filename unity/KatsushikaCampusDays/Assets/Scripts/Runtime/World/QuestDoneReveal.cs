using UnityEngine;

namespace KCD
{
    /// <summary>
    /// クエストを終えたら <see cref="Target"/> を出す (#389)。7F の白板の、自分が書き足した WORLD の行に使う。
    /// 新しいゲームやセーブの読み込みで QuestSystem が差し替わっても追えるよう、毎フレーム今の状態を見る
    /// （HashSet を 1 回引くだけ）。
    /// </summary>
    public sealed class QuestDoneReveal : MonoBehaviour
    {
        public string QuestId = string.Empty;
        public GameObject Target;

        private bool _shown;

        private void Start()
        {
            Apply(Done());
        }

        private void Update()
        {
            bool done = Done();
            if (done != _shown)
            {
                Apply(done);
            }
        }

        private bool Done()
        {
            QuestSystem quests = GameManager.Instance != null ? GameManager.Instance.Quests : null;
            return quests != null && quests.IsCompleted(QuestId);
        }

        private void Apply(bool done)
        {
            _shown = done;
            if (Target != null)
            {
                Target.SetActive(done);
            }
        }
    }
}
