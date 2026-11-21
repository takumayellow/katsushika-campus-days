using System;
using System.Globalization;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 通しプレイの格子の道探し。NavMesh で道が見つからないときに、周りを格子に区切って調べた道を使う。
    /// </summary>
    public sealed partial class PlaythroughDirector
    {
        /// <summary>
        /// 格子の道を解き、目的地の手前で終わる道は、終わりから目的地の方へ壁が無いときだけ使う。目的地の升に届かない
        /// とき（扉を人が塞いでいたなど）は届いた中で一番近い升で道が終わり、それが壁の向こうだと、そこへ歩いても
        /// 調べられないまま戻れなくなる。
        /// </summary>
        private static bool SolveClear(GridPath.Grid grid, Vector3 start, Vector3 goal, float startSnap,
            Func<Vector3, bool> seen, out Vector3[] corners)
        {
            if (!GridPath.Solve(grid, start, goal, startSnap, GoalSnap, seen, out corners))
            {
                return false;
            }

            Vector3 last = corners[corners.Length - 1];
            if (EndsShortBehindWall(last, goal))
            {
                corners = Array.Empty<Vector3>();
                return false;
            }

            return true;
        }

        /// <summary>道の終わり last が目的地 goal から ShortEnd m より遠く、その間を机より高い所で壁が遮るか。</summary>
        public static bool EndsShortBehindWall(Vector3 last, Vector3 goal)
        {
            Vector2 gap = new Vector2(goal.x - last.x, goal.z - last.z);
            if (gap.magnitude <= ShortEnd)
            {
                return false;
            }

            Vector3 eye = last + Vector3.up * SightHeight;
            Vector3 toward = new Vector3(goal.x, eye.y, goal.z);
            return Physics.Linecast(eye, toward, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>格子で start から goal までの道を探す。プレイヤー自身に当たらないよう、調べる間だけ体を切る。</summary>
        private bool TryGrid(PlayerController player, Vector3 start, Vector3 goal, out Vector3[] corners)
        {
            CharacterController body = player.GetComponent<CharacterController>();
            float radius = body != null ? body.radius + 0.05f : 0.35f;
            float height = body != null ? body.height : 1.7f;
            float step = body != null ? body.stepOffset : 0.4f;
            bool bodyWasEnabled = body != null && body.enabled;
            if (bodyWasEnabled)
            {
                body.enabled = false;
            }

            // 寄せる升は、体の高さから壁に遮られずに見える升に限る（壁と机の隅では、一番近い升が壁の向こうになる）。
            Vector3 eye = start + Vector3.up * SightHeight;
            Func<Vector3, bool> seen = cell => !Physics.Linecast(eye, cell + Vector3.up * SightHeight,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            GridPath.Grid grid;
            bool found;
            try
            {
                // 出発点は近くの升にだけ寄せる。遠くへ寄せると、机と椅子に囲まれた所から家具の向こうへ道を引き、
                // 体が椅子に当たったまま進まない。
                grid = GridPath.Sample(start, goal, radius, height, step, Physics.DefaultRaycastLayers);
                found = SolveClear(grid, start, goal, StartSnap, seen, out corners);
                if (!found && radius > ThinRadius)
                {
                    // 席に着いた後（机と椅子の間）や座席の間の通路は、体の幅で調べると升が塞がって出られない。
                    // 細い体で調べ直す（体は横へ滑って通り抜ける）。
                    GridPath.Grid thin = GridPath.Sample(start, goal, ThinRadius, height, step, Physics.DefaultRaycastLayers);
                    found = SolveClear(thin, start, goal, StartSnap, seen, out corners);
                    if (found)
                    {
                        grid = thin;
                    }
                }

                if (!found)
                {
                    // 出発点が床の縁などで升から外れているときは、遠くまで寄せて探す。
                    found = SolveClear(grid, start, goal, GoalSnap, seen, out corners);
                }

                if (!found && Mathf.Abs(goal.y - start.y) > FloorGap)
                {
                    // 別の階の目的地は、上がる階段が出発点と目的地のどちらからも遠いことがある（図書館の 2F 西側へは
                    // 東の吹き抜けの段を回って上がる）。範囲を広げて階段まで入れて探し直す。
                    GridPath.Grid wide = GridPath.Sample(start, goal, radius, height, step, Physics.DefaultRaycastLayers, WideMargin);
                    found = SolveClear(wide, start, goal, StartSnap, seen, out corners);
                    grid = wide;
                }
            }
            finally
            {
                if (bodyWasEnabled)
                {
                    body.enabled = true;
                }
            }

            _gridSearches++;
            if (found && _gridMissed && _gridRecoveries < GridRecoveryLimit)
            {
                // 道を探している間（「無い」が続いた間）は進めないので、見つかってから歩く時間を数え直す。
                _gridRecoveries++;
                _progressAt = _clock;
            }

            _gridMissed = !found;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[KCD] playthrough: 格子で道を探す {0} 升 {1}x{2} ({3:F2}m) → {4} 角 {5} t={6} 道 {7}",
                _lastGoalKey, grid.Width, grid.Depth, grid.Cell, found ? "見つかった" : "無い", corners.Length, Clock(),
                DescribeRoute(start, corners)));
            bool sameGoal = _hasLastGrid && (goal - _lastGridGoal).sqrMagnitude < 1f;
            RememberGrid(grid, start, goal);
            // 目的が行き来しても、同じ目的で道が無い格子は 1 枚だけ書く。
            if (!found && !sameGoal && _noneDumped.Add(_lastGoalKey ?? string.Empty))
            {
                DumpLastGrid("none");
            }

            return found;
        }

        /// <summary>出発点と最初の数個の角を (x,z) で並べる。詰まった所の道をログから追えるように。</summary>
        private static string DescribeRoute(Vector3 start, Vector3[] corners)
        {
            var text = new System.Text.StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture, "({0:F1},{1:F1})", start.x, start.z);
            int shown = Mathf.Min(corners.Length, RouteLogCorners);
            for (int i = 0; i < shown; i++)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, "→({0:F1},{1:F1})", corners[i].x, corners[i].z);
            }

            if (corners.Length > shown)
            {
                text.Append("→…");
            }

            return text.ToString();
        }
    }
}
