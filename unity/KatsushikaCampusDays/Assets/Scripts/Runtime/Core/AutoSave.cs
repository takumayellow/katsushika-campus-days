namespace KCD
{
    /// <summary>
    /// 自動セーブ (#61)。クエストを達成したとき、建物に出入りしたとき、「もう一日歩く」で翌朝に戻ったときに頼まれる。
    ///
    /// 頼まれてもその場では書かない。会話・出入りや落下復帰の暗転・ポーズ・写真モード・リザルトの最中
    /// （どれも KCDInput の封鎖を掛けている）と裏エンドの最中、20 時を過ぎてリザルトを待っている間は待ち、
    /// 封鎖が外れた最初のフレームで 1 回だけ書く。
    /// 暗転の途中はプレイヤーを動かす前後で位置と「建物の中か」が食い違っていることがあり、会話の途中は
    /// 進行が書きかけのことがあるため。続けて頼まれても 1 回にまとめる。
    ///
    /// 書く係は <see cref="GameManager"/> の Update（シーンをまたいで生きている）。キャンパスの外では書かず、
    /// シーンを切り替えるときは頼まれていた分を捨てる（<see cref="Cancel"/>）。
    /// 成功してもトーストは出さない（建物に入ったときの案内などを上書きしてしまう）。失敗は続けて失敗している間は 1 回だけ知らせる。
    /// 一日の終わりに「タイトルへ」を選んだときはシーンが消えるので、ここを通さず
    /// <see cref="SaveSystem.SaveAtSpawn"/> でその場で書く。
    /// </summary>
    public static class AutoSave
    {
        private static bool _pending;
        private static bool _lastFailed;

        /// <summary>書くのを待っているか。</summary>
        public static bool Pending => _pending;

        /// <summary>次に書ける最初のフレームで書くよう頼む。</summary>
        public static void Request()
        {
            _pending = true;
        }

        /// <summary>頼まれていた分を捨てる。シーンの切り替え・ロード・はじめからで呼ぶ。</summary>
        public static void Cancel()
        {
            _pending = false;
        }

        /// <summary>クエストを達成したら頼むようにする。GameManager が進行を作るときに呼ぶ。同じ進行を 2 回渡しても 1 回しか頼まない。</summary>
        public static void Watch(QuestSystem quests)
        {
            if (quests == null)
            {
                return;
            }

            quests.QuestCompleted -= OnQuestCompleted;
            quests.QuestCompleted += OnQuestCompleted;
        }

        private static void OnQuestCompleted(QuestData quest)
        {
            Request();
        }

        // ---- 純関数（Unity を起動せずにテストする）----

        /// <summary>
        /// いま書くか。頼まれていて、キャンパスにいて、操作の封鎖も裏エンドも無く、一日の終わりを待っていないときだけ。
        /// 裏エンドは閉じるときに封鎖をまとめて外すので、封鎖とは別に見る。
        /// 一日の終わり（<see cref="DayEndEvaluator.IsDayEndPending"/>）はリザルトが出るまで待つ。封鎖中に 24 時をまたいだ夜に
        /// 書くと、読み直したときにその夜が終わらない。リザルトのあとは「もう一日歩く」の翌朝か「タイトルへ」の
        /// <see cref="SaveSystem.SaveAtSpawn"/> が書く。
        /// </summary>
        public static bool ShouldWrite(bool pending, bool onCampus, bool blocked, bool dormEndingShowing, bool dayEndPending)
        {
            return pending && onCampus && !blocked && !dormEndingShowing && !dayEndPending;
        }

        /// <summary>失敗を知らせるか。続けて失敗している間は最初の 1 回だけ。</summary>
        public static bool ShouldReportFailure(bool saved, bool failedBefore)
        {
            return !saved && !failedBefore;
        }

        /// <summary>毎フレーム呼ぶ。書ける状態なら書く。</summary>
        public static void Tick(bool onCampus)
        {
            if (!ShouldWrite(_pending, onCampus, KCDInput.GameplayBlocked, DormEnding.IsAnyShowing, DayEndEvaluator.IsDayEndPending))
            {
                return;
            }

            _pending = false;
            bool saved = SaveSystem.Save();
            if (ShouldReportFailure(saved, _lastFailed))
            {
                HUD.Instance?.ShowToast(L.Get("ui.hud.save_failed", "セーブできませんでした"));
            }

            _lastFailed = !saved;
        }
    }
}
