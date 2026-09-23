using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Tab で開く一覧。受注中・完了済みをまとめて 1 枚のテキストに流し込む。
    /// </summary>
    public sealed class QuestLogView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _bodyLabel;

        private readonly StringBuilder _builder = new StringBuilder(1024);
        private QuestSystem _quests;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>Esc でログを閉じたフレーム。同じ Esc でポーズメニューが開かないよう PauseMenu が見る。</summary>
        public int ClosedByMenuFrame { get; private set; } = -1;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text body)
        {
            _root = root;
            _bodyLabel = body;
        }

        private void Start()
        {
            _quests = GameManager.Instance.Quests;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void Update()
        {
            // DormEnding.IsAnyShowing: 裏エンド (#41) の最中は Tab でログを開かせない。
            if (ResultScreen.IsAnyOpen || DormEnding.IsAnyShowing)
            {
                return;
            }

            if (KCDInput.QuestLogPressed)
            {
                Toggle();
            }
            else if (IsOpen && KCDInput.MenuPressed)
            {
                SetOpen(false);
                ClosedByMenuFrame = Time.frameCount;
            }
        }

        /// <summary>開閉を切り替える。</summary>
        public void Toggle() => SetOpen(!IsOpen);

        /// <summary>開閉する。開いている間はゲームプレイ入力を止める。</summary>
        public void SetOpen(bool open)
        {
            if (_root == null)
            {
                return;
            }

            if (open)
            {
                Rebuild();
            }

            if (open != _root.activeSelf)
            {
                AudioManager.Instance?.PlayUi(open ? "ui_open" : "ui_close");
            }

            _root.SetActive(open);
            KCDInput.GameplayBlocked = open;
        }

        private void Rebuild()
        {
            if (_bodyLabel == null || _quests == null)
            {
                return;
            }

            _builder.Length = 0;
            int shown = 0;

            for (int i = 0; i < _quests.All.Count; i++)
            {
                QuestData quest = _quests.All[i];
                bool active = _quests.IsActive(quest.Id);
                bool completed = _quests.IsCompleted(quest.Id);
                if (!active && !completed)
                {
                    continue;
                }

                shown++;
                _builder.Append(completed ? "<color=#8FD48F>" : "<color=#FFE6A8>");
                _builder.Append(completed
                    ? L.Get("ui.questlog.completed", "[達成] ")
                    : L.Get("ui.questlog.active", "[受注中] "));
                _builder.Append(quest.Title);
                _builder.Append("</color>\n");

                if (!string.IsNullOrEmpty(quest.Summary))
                {
                    _builder.Append("  ").Append(quest.Summary).Append('\n');
                }

                for (int s = 0; s < quest.Steps.Count; s++)
                {
                    QuestStep step = quest.Steps[s];
                    _builder.Append(step.Completed ? "  <s>" : "  ");
                    _builder.Append("・").Append(step.Text);
                    _builder.Append(step.Completed ? "</s>\n" : "\n");
                }

                _builder.Append('\n');
            }

            if (shown == 0)
            {
                _builder.Append(L.Get("ui.questlog.empty", "まだ受けているクエストはない。\nキャンパスを歩いて、誰かに話しかけてみよう。"));
            }

            _bodyLabel.text = _builder.ToString();
        }
    }
}
