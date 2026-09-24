using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>クエスト進行のセーブと復元。</summary>
    public sealed partial class QuestSystem
    {
        /// <summary>セーブ用に進行状況を書き出す。</summary>
        public QuestProgress Capture()
        {
            var progress = new QuestProgress();
            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                int done = 0;
                for (int s = 0; s < quest.Steps.Count; s++)
                {
                    if (quest.Steps[s].Completed)
                    {
                        done++;
                    }
                }

                progress.QuestIds.Add(quest.Id);
                progress.StepsDone.Add(done);
                progress.States.Add(_completed.Contains(quest.Id) ? QuestProgress.StateCompleted
                    : _active.Contains(quest.Id) ? QuestProgress.StateActive
                    : QuestProgress.StateNotStarted);
            }

            return progress;
        }

        /// <summary>
        /// セーブから進行状況を復元する。セーブに書かれた分だけで全体を作り直す (#61)。
        /// セーブに無いクエストは未受注に戻し、そのうち autoStart で前提を満たすものは黙って受注する
        /// （あとから足されたクエストを古いセーブで遊んでも始まるように）。
        /// 完了なのにステップが途中、受注中なのにステップが全部済み、といった食い違いは完了にそろえる。
        /// </summary>
        public void Restore(QuestProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            _active.Clear();
            _completed.Clear();

            // 前の状態を残さない。セーブに無いクエストのステップが済みのまま残ると、未受注なのに途中から始まる。
            for (int i = 0; i < _all.Count; i++)
            {
                for (int s = 0; s < _all[i].Steps.Count; s++)
                {
                    QuestStep step = _all[i].Steps[s];
                    step.Completed = false;
                    step.Progress = 0;
                }
            }

            var restored = new HashSet<string>(StringComparer.Ordinal);
            int rows = QuestProgress.RowCount(progress);
            for (int i = 0; i < rows; i++)
            {
                QuestData quest = Find(progress.QuestIds[i]);

                // 知らない id（消えたクエスト）と、同じ id の 2 行目以降は読まない。
                if (quest == null || !restored.Add(quest.Id))
                {
                    continue;
                }

                RestoreOne(quest, progress.States[i], progress.StepsDone[i]);
            }

            ActivateNewAutoStarts(restored);

            // 計時はセーブに載せない。読み込んだら数える前に戻す。
            for (int i = 0; i < _all.Count; i++)
            {
                for (int s = 0; s < _all[i].Steps.Count; s++)
                {
                    _all[i].Steps[s].Timer.Reset();
                }
            }

            // 受注中の制限時間つきステップ。依頼主がいれば、話しかけるまで数えずに待つ
            // （読み込んだ瞬間から減り始めると、どこにいても間に合わない）。依頼主がいなければここから数える。
            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (!_active.Contains(quest.Id))
                {
                    continue;
                }

                QuestStep step = quest.CurrentStep;
                if (step != null && step.IsTimed && string.IsNullOrEmpty(step.Giver))
                {
                    StartTimer(quest, step);
                }
            }

            Changed?.Invoke();
        }

        /// <summary>1 件を状態とステップ数から戻す。未知の状態は未受注。</summary>
        private void RestoreOne(QuestData quest, int state, int stepsDone)
        {
            int total = quest.Steps.Count;
            int done;
            if (state == QuestProgress.StateCompleted)
            {
                done = total;
            }
            else if (state == QuestProgress.StateActive)
            {
                done = Math.Max(0, Math.Min(stepsDone, total));
            }
            else
            {
                // 未受注のクエストは Report が進めないので、済みのステップがあるのは壊れたセーブ。
                done = 0;
            }

            for (int s = 0; s < total; s++)
            {
                QuestStep step = quest.Steps[s];
                step.Completed = s < done;
                step.Progress = step.Completed ? step.Count : 0;
            }

            if (state == QuestProgress.StateCompleted || (state == QuestProgress.StateActive && quest.IsComplete))
            {
                // 受注中のまま全部済んでいたら、達成の処理（Report の IsComplete の枝）が済んだ扱いにする。
                // 受注中のままだと CurrentStep が null で、二度と完了にならない。
                _completed.Add(quest.Id);
            }
            else if (state == QuestProgress.StateActive)
            {
                _active.Add(quest.Id);
            }
        }

        /// <summary>セーブに行の無い autoStart のクエストのうち、前提を満たすものを黙って受注する。</summary>
        private void ActivateNewAutoStarts(HashSet<string> restored)
        {
            _silent = true;
            try
            {
                for (int i = 0; i < _all.Count; i++)
                {
                    QuestData quest = _all[i];
                    if (quest.AutoStart && !restored.Contains(quest.Id) && !_completed.Contains(quest.Id) &&
                        !_active.Contains(quest.Id) && PrerequisitesMet(quest))
                    {
                        Activate(quest);
                    }
                }
            }
            finally
            {
                _silent = false;
            }
        }
    }

    /// <summary>セーブファイルに載せるクエスト進行。JsonUtility で直列化する。</summary>
    [Serializable]
    public sealed class QuestProgress
    {
        public const int StateNotStarted = 0;
        public const int StateActive = 1;
        public const int StateCompleted = 2;

        public List<string> QuestIds = new List<string>();
        public List<int> StepsDone = new List<int>();

        /// <summary>0 = 未受注、1 = 受注中、2 = 完了。</summary>
        public List<int> States = new List<int>();

        /// <summary>3 本の列がそろっている行数。どれかが null なら 0（壊れたセーブは行が無いものとして読む）。</summary>
        public static int RowCount(QuestProgress progress)
        {
            if (progress == null || progress.QuestIds == null || progress.StepsDone == null || progress.States == null)
            {
                return 0;
            }

            return Math.Min(progress.QuestIds.Count, Math.Min(progress.StepsDone.Count, progress.States.Count));
        }
    }
}
