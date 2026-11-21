using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KCD
{
    /// <summary>
    /// 通しプレイの階の移動。屋内で目的地が別の階にあれば、同じ階の階段・エレベーター（<see cref="ElevatorPanel"/>）へ歩き、
    /// 行き先を選ぶ画面（<see cref="ElevatorMenu"/>）で目的地の階を選ぶ。格子の経路は段差 0.4 m までしか上らないので、
    /// 階をまたぐ目的地には歩いて届かない。
    /// </summary>
    public sealed partial class PlaythroughDirector
    {
        /// <summary>目的地とプレイヤーの高さの差がこれより大きければ別の階とみなす（m）。階の高さは 4 m。</summary>
        private const float FloorGap = 2.5f;

        /// <summary>乗り場の行き先がこれより遠ければ別の建物の階（m）。屋内は互いに遠く離して建ててある。</summary>
        private const float SameBuildingSpan = 150f;

        /// <summary>話さずに調べる物の目的地は、その物の判定の位置。選ばれた相手がこれより離れていれば別の物（m）。</summary>
        private const float ExaminedTalkerSpan = 2f;

        /// <summary>高さが一番近い乗り場から、これ以内の高さの乗り場までを同じ階とみなす（m）。</summary>
        private const float PanelFloorSlack = 1f;

        private const string FloorKey = "floor:";

        /// <summary>選ぶ階（乗り場の <see cref="ElevatorPanel.Stops"/> の番号）。選ぶものが無ければ -1。</summary>
        private int _floorTarget = -1;

        /// <summary>
        /// 行き先を選ぶ画面が開いていれば、目的地の階までカーソルを動かして決める。選ぶ階が無ければ閉じる。
        /// 画面が開いている間は <see cref="KCDInput.GameplayBlocked"/> なので、その Enter より先に呼ぶ。開いていれば true。
        /// </summary>
        private bool TickElevatorMenu()
        {
            ElevatorMenu menu = ElevatorMenu.Instance;
            if (menu == null || !menu.IsOpen)
            {
                return false;
            }

            Player()?.ClearAutopilot();
            _progressAt = _clock;
            if (!Due(0.4f))
            {
                return true;
            }

            if (_floorTarget < 0)
            {
                Press(Key.Escape);
            }
            else if (menu.Index > _floorTarget)
            {
                // 上の階ほど画面の上に並ぶ。下を押すと 1 つ下の階へ。
                Press(Key.DownArrow);
            }
            else if (menu.Index < _floorTarget)
            {
                Press(Key.UpArrow);
            }
            else
            {
                Debug.Log("[KCD] playthrough: 階を移る " + _lastGoalKey + " → " + _floorTarget + " t=" + Clock());
                Press(Key.Enter);
                _floorTarget = -1;
            }

            return true;
        }

        /// <summary>
        /// 屋内で目的地が別の階にあれば、目的地を今の階の乗り場の扉に置き換える。置き換えたら true。
        /// 目的地の階に止まる乗り場が今の階に無ければ何もしない（そのまま歩き、届かなければワープ）。
        /// </summary>
        private bool TryFloorGoal(Vector3 here, ref Vector3 goal, ref string key, ref bool interact)
        {
            _floorTarget = -1;
            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null || !loader.IsInside || Mathf.Abs(goal.y - here.y) <= FloorGap)
            {
                return false;
            }

            if (loader.IsBusy)
            {
                return false;
            }

            // 乗り場の CanInteract は、プレイヤーが乗り場の高さから 2 m 以内のときだけ true になる。階段教室の
            // 底のように同じ階でも床が下がっている所では外れるので、ここでは使わない。高さが一番近い乗り場の階を
            // 今の階とみなし、その階の乗り場から選ぶ（着けば CanInteract になる）。
            // 屋内は全部同じ場面に並べてあるので、隣の建物の乗り場も見つかる。今いる屋内の乗り場だけを使う。
            ElevatorPanel[] panels = FindObjectsByType<ElevatorPanel>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(panel => InInterior(panel.transform, loader.CurrentId))
                .ToArray();
            float floorGap = float.MaxValue;
            foreach (ElevatorPanel panel in panels)
            {
                if (panel.enabled && panel.Stops.Length > 1)
                {
                    floorGap = Mathf.Min(floorGap, Mathf.Abs(panel.transform.position.y - here.y));
                }
            }

            ElevatorPanel best = null;
            int bestStop = -1;
            float bestDistance = float.MaxValue;
            foreach (ElevatorPanel panel in panels)
            {
                if (!panel.enabled || panel.Stops.Length <= 1
                    || Mathf.Abs(panel.transform.position.y - here.y) > floorGap + PanelFloorSlack)
                {
                    continue;
                }

                int stop = StopNear(panel, goal);
                if (stop < 0 || stop == panel.CurrentIndex)
                {
                    continue;
                }

                float distance = Flat(panel.transform.position - here).magnitude;
                if (distance < bestDistance)
                {
                    best = panel;
                    bestStop = stop;
                    bestDistance = distance;
                }
            }

            if (best == null)
            {
                return false;
            }

            goal = best.AimPoint(here);
            key = FloorKey + loader.CurrentId + ":" + best.Stops[bestStop].Floor;
            interact = true;
            _floorTarget = bestStop;
            return true;
        }

        /// <summary>t が屋内 id の根（<see cref="QuestObjectiveLocator.InteriorPrefix"/> + id）の下にあるか。</summary>
        private static bool InInterior(Transform t, string id)
        {
            string root = QuestObjectiveLocator.InteriorPrefix + id;
            for (Transform at = t; at != null; at = at.parent)
            {
                if (at.name == root)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>乗り場の行き先のうち、目的地と同じ建物で高さが最も近い階。<see cref="FloorGap"/> 以内に無ければ -1。</summary>
        private static int StopNear(ElevatorPanel panel, Vector3 goal)
        {
            ElevatorPanel.Stop[] stops = panel.Stops;
            int best = -1;
            float bestGap = FloorGap;
            for (int i = 0; i < stops.Length; i++)
            {
                Transform landing = stops[i].Landing;
                if (landing == null || Flat(landing.position - goal).magnitude > SameBuildingSpan)
                {
                    continue;
                }

                float gap = Mathf.Abs(landing.position.y - goal.y);
                if (gap < bestGap)
                {
                    best = i;
                    bestGap = gap;
                }
            }

            return best;
        }

        /// <summary>
        /// 前にある物が、目的のキーで E を押してよい相手か。
        /// 外から屋内の相手と話す・屋内の物を取るときは、案内が建物の入口を指す。そのときは入口で E を押して入る。
        /// </summary>
        private bool IsRightTarget(Interactable current, Vector3 goal, string key)
        {
            if (current == null)
            {
                return false;
            }

            if (key == "dorm_entrance")
            {
                return current is DormEntrance;
            }

            if (key.StartsWith(PhotoKey, StringComparison.Ordinal))
            {
                return current is PhotoSpot tripod && key.StartsWith(PhotoKey + tripod.SpotId + "@", StringComparison.Ordinal);
            }

            if (key.StartsWith(FloorKey, StringComparison.Ordinal))
            {
                return current is ElevatorPanel;
            }

            bool door = current is EntranceTrigger entrance && Flat(entrance.transform.position - goal).magnitude < 6f;
            if (key.StartsWith(nameof(QuestStepKind.Enter) + ":", StringComparison.Ordinal))
            {
                return door;
            }

            if (key.StartsWith(nameof(QuestStepKind.Collect) + ":", StringComparison.Ordinal))
            {
                // 拾う物のそばの椅子が選ばれているときに押すと、座って動けなくなる。拾う物と、外から屋内の物を
                // 目指すときの入口にだけ押す。
                return current is CollectableItem || door;
            }

            string npcId = NpcIdOf(key);
            if (npcId == null)
            {
                // 話さずに調べる物（306 の PC など）は、そばの人（TA）と取り違えない。話し相手が選ばれていたら、
                // 目的地のすぐそばの相手（会話で調べる物そのもの）のときだけ押す。
                NPCTalker person = current as NPCTalker ?? current.GetComponentInParent<NPCTalker>();
                return person == null || Flat(person.transform.position - goal).magnitude < ExaminedTalkerSpan;
            }

            if ((current is NPCTalker talker && talker.NpcId == npcId) || current.GetComponentInParent<NPCTalker>()?.NpcId == npcId)
            {
                return true;
            }

            // 相手がこの場に居ない（屋内にいて今は読み込まれていない）なら、案内の指す入口から入る。
            return door && !TryTalkerPosition(key, goal, out _);
        }

        private static string Height(Vector3 at)
        {
            return at.y.ToString("F1", CultureInfo.InvariantCulture);
        }
    }
}
