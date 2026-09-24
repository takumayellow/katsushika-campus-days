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
                progress.States.Add(_completed.Contains(quest.Id) ? 2 : _active.Contains(quest.Id) ? 1 : 0);
            }

            return progress;
        }

        /// <summary>セーブから進行状況を復元する。</summary>
        public void Restore(QuestProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            _active.Clear();
            _completed.Clear();

            // 計時はセーブに載せない。読み込んだら数える前に戻す。
            for (int i = 0; i < _all.Count; i++)
            {
                for (int s = 0; s < _all[i].Steps.Count; s++)
                {
                    _all[i].Steps[s].Timer.Reset();
                }
            }

            for (int i = 0; i < progress.QuestIds.Count && i < progress.StepsDone.Count && i < progress.States.Count; i++)
            {
                QuestData quest = Find(progress.QuestIds[i]);
                if (quest == null)
                {
                    continue;
                }

                for (int s = 0; s < quest.Steps.Count; s++)
                {
                    QuestStep step = quest.Steps[s];
                    step.Completed = s < progress.StepsDone[i];
                    step.Progress = step.Completed ? step.Count : 0;
                }

                if (progress.States[i] == 1) { _active.Add(quest.Id); }
                else if (progress.States[i] == 2) { _completed.Add(quest.Id); }
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
    }

    /// <summary>セーブファイルに載せるクエスト進行。JsonUtility で直列化する。</summary>
    [Serializable]
    public sealed class QuestProgress
    {
        public List<string> QuestIds = new List<string>();
        public List<int> StepsDone = new List<int>();

        /// <summary>0 = 未受注、1 = 受注中、2 = 完了。</summary>
        public List<int> States = new List<int>();
    }
}
