using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 本編とサブクエストを終えたあと（追うものも次の行き先も無いあいだ）の道しるべ (#410)。
    /// 宝探しを受けられるようになったら（<see cref="HiddenNote.ShouldHint"/>）依頼主のいなり先輩へ案内し、
    /// それまでは、まだ拾っていない隠しアイテムのうちメモの番号が最も小さいものの手がかりと、
    /// まだ撮っていない写真スポットの名前を出す。追跡枠（<see cref="QuestTrackerView"/>）と
    /// ミニマップの輪（<see cref="Minimap"/>）が同じ判定を使う。
    /// </summary>
    public static class ExploreGuide
    {
        /// <summary>宝探し（q_secret_spring_hunt）を始めてくれる NPC。</summary>
        public const string HuntGiver = "inari";

        /// <summary>
        /// 宝探しの依頼主へ案内するときのステップ（いなり先輩に話しかける）。案内しないなら null。
        /// 隠しクエストなので「次の行き先」の依頼主の一覧（<see cref="QuestSystem.OfferedQuest"/>）には載らない。
        /// </summary>
        public static QuestStep HuntStep(int hiddenCount, QuestSystem quests)
        {
            if (!HiddenNote.ShouldHint(hiddenCount, quests))
            {
                return null;
            }

            return new QuestStep { Kind = QuestStepKind.Talk, Target = HuntGiver };
        }

        /// <summary>
        /// 次に探す隠しアイテム。まだ拾っていないもののうち、メモの番号（note_no）が最も小さいもの。
        /// 番号が同じなら一覧の順。拾い終えていれば null。
        /// </summary>
        public static CatalogItem NextHidden(IReadOnlyList<CatalogItem> items, Func<string, bool> collected)
        {
            if (items == null || collected == null)
            {
                return null;
            }

            CatalogItem best = null;
            for (int i = 0; i < items.Count; i++)
            {
                CatalogItem item = items[i];
                if (item == null || !item.IsHidden || collected(item.Id))
                {
                    continue;
                }

                if (best == null || item.NoteNo < best.NoteNo)
                {
                    best = item;
                }
            }

            return best;
        }

        /// <summary>次に撮る写真スポット。まだ撮っていないもののうち一覧で最初のもの。撮り終えていれば null。</summary>
        public static CatalogPhotoSpot NextPhotoSpot(IReadOnlyList<CatalogPhotoSpot> spots, Func<string, bool> taken)
        {
            if (spots == null || taken == null)
            {
                return null;
            }

            for (int i = 0; i < spots.Count; i++)
            {
                CatalogPhotoSpot spot = spots[i];
                if (spot != null && !taken(spot.Id))
                {
                    return spot;
                }
            }

            return null;
        }

        /// <summary>
        /// 探索の枠の 2 行目に出す文。隠しアイテムが残っていてその手がかりがあれば手がかり、
        /// そうでなければ次の写真スポットの名前。どちらも無ければ空文字。
        /// </summary>
        public static string ClueText(CatalogItem nextHidden, CatalogPhotoSpot nextSpot)
        {
            if (nextHidden != null)
            {
                string hint = L.Pick(nextHidden.HintJa, nextHidden.HintEn);
                if (!string.IsNullOrEmpty(hint))
                {
                    return L.Format("ui.hud.explore_clue", hint);
                }
            }

            if (nextSpot != null)
            {
                string name = L.Pick(nextSpot.NameJa, nextSpot.NameEn);
                if (!string.IsNullOrEmpty(name))
                {
                    return L.Format("ui.hud.photo_spot", name);
                }
            }

            return string.Empty;
        }
    }
}
