using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// PlayMode テストでキャンパスを歩く手 (#19)。プレイヤーを目的の物の前へテレポートさせ、
    /// E で調べたときと同じ Interact を呼ぶ。入口・出口・訪問ゾーンは物理のトリガーをそのまま踏む。
    /// QuestSystem へ直接は報告しないので、シーンの配線（id の食い違い・置き忘れ）が切れていれば
    /// そこで進まなくなり、<see cref="Problems"/> に理由が残る。
    /// </summary>
    public sealed class CampusDriver
    {
        /// <summary>屋内の入れ物の名前の頭（InteriorStage が "Interior_" + id で置く）。</summary>
        public const string InteriorPrefix = "Interior_";

        /// <summary>出入り・撮影・会話の明けを待つ上限（実時間の秒）。</summary>
        private const float StepTimeoutSeconds = 20f;

        /// <summary>封鎖も会話も無い状態がこれだけ続いたら落ち着いたとみなす（実時間の秒）。遅れて始まる会話を拾う。</summary>
        private const float QuietSeconds = 0.4f;

        /// <summary>会話を送る回数の上限。どの話題の行数よりも十分に多い。</summary>
        private const int MaxLines = 200;

        /// <summary>話しかける・拾う・撮るときに立つ距離（m）。E の届く距離より内側。</summary>
        private const float StandOff = 1.2f;

        /// <summary>入口の前に立つ距離（m）。歩いて入る範囲（EntranceTrigger.WalkInDepth）より外で、E は届く。</summary>
        private const float EntranceStandOff = 1.6f;

        /// <summary>訪問ゾーンに入ってから報告を待つ時間（実時間の秒）。OnTriggerStay の間引き（0.25 秒）より長い。</summary>
        private const float VisitDwellSeconds = 0.6f;

        private readonly PlayerController _player;
        private readonly InteriorLoader _loader;
        private readonly DialogueSystem _dialogue;
        private readonly FormalEnding _ending;
        private readonly List<string> _problems = new List<string>();

        public CampusDriver(PlayerController player, InteriorLoader loader, DialogueSystem dialogue, FormalEnding ending)
        {
            _player = player;
            _loader = loader;
            _dialogue = dialogue;
            _ending = ending;
        }

        /// <summary>たどり着けなかった・反応しなかった操作。テストの失敗文に添える。</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>
        /// t が置かれている建物の id（Interior_&lt;id&gt; の下なら id）。屋外なら空文字。
        /// </summary>
        public static string BuildingOf(Transform t)
        {
            for (Transform node = t; node != null; node = node.parent)
            {
                if (node.name.StartsWith(InteriorPrefix))
                {
                    return node.name.Substring(InteriorPrefix.Length);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 会話を最後まで送り、エンディングのページをめくり、暗転と封鎖が明けて静かになるまで待つ。
        /// 落とし物の小さな手記や夕日のあとの話のように、少し遅れて始まるものもここで片付ける。
        /// </summary>
        public IEnumerator Settle()
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds * 2f;
            float quietSince = -1f;
            int lines = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_dialogue != null && _dialogue.IsPlaying && lines < MaxLines)
                {
                    lines++;
                    _dialogue.Advance();
                    quietSince = -1f;
                }
                else if (FormalEnding.IsAnyShowing)
                {
                    if (_ending != null && _ending.CurrentPage >= 0)
                    {
                        _ending.AdvancePage();
                    }

                    quietSince = -1f;
                }
                else if (KCDInput.GameplayBlocked)
                {
                    quietSince = -1f;
                }
                else if (quietSince < 0f)
                {
                    quietSince = Time.realtimeSinceStartup;
                }
                else if (Time.realtimeSinceStartup - quietSince >= QuietSeconds)
                {
                    yield break;
                }

                yield return null;
            }

            _problems.Add("落ち着かない: 会話中 " + (_dialogue != null && _dialogue.IsPlaying) +
                " / エンディング " + FormalEnding.IsAnyShowing + " / 操作の封鎖 " + KCDInput.GameplayBlocked);
        }

        /// <summary>建物 id の中へ移る（空文字なら屋外へ）。もうそこにいれば何もしない。</summary>
        public IEnumerator GoTo(string buildingId)
        {
            string current = _loader != null ? _loader.CurrentId : string.Empty;
            if (current == buildingId)
            {
                yield break;
            }

            yield return Leave();
            if (!string.IsNullOrEmpty(buildingId))
            {
                yield return Enter(buildingId);
            }
        }

        /// <summary>
        /// 外から入口（玄関）の前に立ち、E で入る。中にいれば先に出る（「建物に入る」のステップは入り直さないと進まない）。
        /// 屋内の無い建物は E で入ったことになるだけなので、外に立ったまま戻る。
        /// </summary>
        public IEnumerator Enter(string buildingId)
        {
            yield return EnterThrough(buildingId);
        }

        /// <summary>
        /// 扉 doorId（玄関なら建物の id、2 つ目からの扉はその扉の id, BuildingDoors）の前に立ち、E で入る。
        /// 中にいれば先に出る。
        /// </summary>
        public IEnumerator EnterThrough(string doorId)
        {
            yield return Leave();

            if (doorId == DormRoute.Id)
            {
                yield return EnterDorm();
                yield break;
            }

            string buildingId = BuildingDoors.BuildingOf(doorId);
            EntranceTrigger entrance = FindEntrance(doorId);
            if (entrance == null)
            {
                _problems.Add("入口 Entrance_" + doorId + " がシーンに無い");
                yield break;
            }

            // 出た直後と入った直後の数秒は扉が開かない（EntranceTrigger.IsOpen）。
            yield return WaitUntil(() => entrance.CanInteract);
            if (!entrance.CanInteract)
            {
                _problems.Add(buildingId + " の入口が " + StepTimeoutSeconds + " 秒待っても開かない");
                yield break;
            }

            Vector3 outward = entrance.Outward;
            Vector3 stand = entrance.transform.position + outward * EntranceStandOff;
            _player.Teleport(stand, Yaw(-outward));
            yield return null;
            entrance.Interact(_player.gameObject);

            if (_loader != null && _loader.HasInterior(buildingId))
            {
                yield return WaitUntil(() => _loader.CurrentId == buildingId && !KCDInput.GameplayBlocked);
                if (_loader.CurrentId != buildingId)
                {
                    _problems.Add(buildingId + " に E で入っても屋内へ移らない（いま '" + _loader.CurrentId + "'）");
                }
            }

            yield return Settle();
        }

        /// <summary>寮は専用の入口（DormEntrance）。扉の前に立って E を押す。</summary>
        private IEnumerator EnterDorm()
        {
            DormEntrance door = Object.FindAnyObjectByType<DormEntrance>(FindObjectsInactive.Include);
            if (door == null)
            {
                _problems.Add("寮の入口 Entrance_" + DormRoute.Id + " がシーンに無い");
                yield break;
            }

            yield return WaitUntil(() => door.CanInteract);
            Vector3 outward = door.Outward;
            _player.Teleport(door.transform.position + outward * EntranceStandOff, Yaw(-outward));
            yield return null;
            door.Interact(_player.gameObject);
            yield return WaitUntil(() => _loader.CurrentId == DormRoute.Id && !KCDInput.GameplayBlocked);
            if (_loader.CurrentId != DormRoute.Id)
            {
                _problems.Add("寮に E で入っても屋内へ移らない（いま '" + _loader.CurrentId + "'）");
            }

            yield return Settle();
        }

        /// <summary>中にいれば、その建物の玄関の出口を踏んで外へ出る。</summary>
        public IEnumerator Leave()
        {
            yield return LeaveThrough(null);
        }

        /// <summary>
        /// 中にいれば、扉 doorId の出口（Exit_&lt;扉の id&gt;）を踏んで外へ出る。null なら今いる建物の玄関の出口。
        /// </summary>
        public IEnumerator LeaveThrough(string doorId)
        {
            if (_loader == null || !_loader.IsInside)
            {
                yield break;
            }

            string buildingId = _loader.CurrentId;
            string door = string.IsNullOrEmpty(doorId) ? buildingId : doorId;
            InteriorExit exit = FindExit(buildingId, door);
            if (exit == null)
            {
                _problems.Add("屋内 " + buildingId + " に出口 Exit_" + door + " が無い");
                yield break;
            }

            // 出口の箱は入った所から離れた奥側にあり、浮かせてある（InteriorExit.BoxFor）。手前の縁から少し入った所の床に立つ。
            BoxCollider box = exit.GetComponent<BoxCollider>();
            Vector3 stand = exit.transform.TransformPoint(new Vector3(0f, 0f, -box.size.z * 0.5f + 0.3f)) - Vector3.up * 1.15f;
            _player.Teleport(stand, _player.transform.eulerAngles.y);
            yield return WaitUntil(() => !_loader.IsInside && !KCDInput.GameplayBlocked);
            if (_loader.IsInside)
            {
                _problems.Add(buildingId + " の出口を踏んでも外へ出ない");
            }

            yield return Settle();
        }

        /// <summary>訪問ゾーンの中に立ち、トリガーの報告を待つ。</summary>
        public IEnumerator Visit(string placeId)
        {
            VisitZone zone = FindVisitZone(placeId);
            if (zone == null)
            {
                _problems.Add("訪問ゾーン Zone_" + placeId + " がシーンに無い");
                yield break;
            }

            yield return GoTo(BuildingOf(zone.transform));
            Bounds bounds = zone.GetComponent<Collider>().bounds;
            _player.Teleport(FloorUnder(bounds), _player.transform.eulerAngles.y);
            float until = Time.realtimeSinceStartup + VisitDwellSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            yield return Settle();
        }

        /// <summary>NPC の前に立って話しかけ、会話を最後まで送る。</summary>
        public IEnumerator Talk(string npcId)
        {
            NPCTalker npc = FindNpc(npcId);
            if (npc == null)
            {
                _problems.Add("話し相手 " + npcId + " がシーンに無い");
                yield break;
            }

            yield return GoTo(BuildingOf(npc.transform));
            yield return StandBefore(npc.transform);
            if (!npc.CanInteract)
            {
                _problems.Add(npcId + " に話しかけられない（無効か、会話中）");
                yield break;
            }

            npc.Interact(_player.gameObject);
            yield return Settle();
        }

        /// <summary>まだ拾っていない itemId を count 個まで拾う。</summary>
        public IEnumerator Collect(string itemId, int count)
        {
            int picked = 0;
            foreach (CollectableItem item in Object.FindObjectsByType<CollectableItem>(FindObjectsInactive.Include))
            {
                if (picked >= count)
                {
                    break;
                }

                if (item.ItemId != itemId || item.IsTaken)
                {
                    continue;
                }

                yield return GoTo(BuildingOf(item.transform));
                yield return StandBefore(item.transform);
                if (!item.CanInteract)
                {
                    _problems.Add(itemId + " (" + Path(item.transform) + ") が拾えない（無効になっている）");
                    continue;
                }

                item.Interact(_player.gameObject);
                picked++;
                yield return Settle();
            }

            if (picked < count)
            {
                _problems.Add(itemId + " があと " + (count - picked) + " 個足りない（シーンに置かれた拾える物が少ない）");
            }
        }

        /// <summary>写真スポットの三脚の前で撮り、一日の記録に載るまで待つ。</summary>
        public IEnumerator Photo(string spotId)
        {
            PhotoSpot spot = FindPhotoSpot(spotId);
            if (spot == null)
            {
                _problems.Add("写真スポット " + spotId + " の三脚がシーンに無い");
                yield break;
            }

            yield return GoTo(BuildingOf(spot.transform));
            yield return StandBefore(spot.transform);
            if (!spot.CanInteract)
            {
                _problems.Add(spotId + " で撮れない（PhotoSystem が無いか、フォトモード中）");
                yield break;
            }

            spot.Interact(_player.gameObject);
            yield return WaitUntil(() => Contains(DayStats.PhotoSpotIds, spotId));
            if (!Contains(DayStats.PhotoSpotIds, spotId))
            {
                _problems.Add(spotId + " で撮っても一日の記録に載らない（保存に失敗した?）");
            }

            yield return Settle();
        }

        /// <summary>id のものがシーンのどこにあるか（失敗文用）。見つからなければ「無い」。</summary>
        public static string Locate(QuestStepKind kind, string target)
        {
            Transform found = null;
            switch (kind)
            {
                case QuestStepKind.Visit:
                    found = FindVisitZone(target)?.transform;
                    break;
                case QuestStepKind.Talk:
                    found = FindNpc(target)?.transform;
                    break;
                case QuestStepKind.Enter:
                    found = FindEntrance(target)?.transform;
                    break;
                case QuestStepKind.Collect:
                    foreach (CollectableItem item in Object.FindObjectsByType<CollectableItem>(FindObjectsInactive.Include))
                    {
                        if (item.ItemId == target && !item.IsTaken)
                        {
                            found = item.transform;
                            break;
                        }
                    }

                    break;
            }

            return found == null ? "シーンに無い" : Path(found) + " " + found.position.ToString("F1");
        }

        private IEnumerator StandBefore(Transform target)
        {
            Vector3 toTarget = target.position - _player.transform.position;
            toTarget.y = 0f;
            Vector3 approach = toTarget.sqrMagnitude > 1e-4f ? toTarget.normalized : Vector3.forward;
            Vector3 stand = target.position - approach * StandOff;
            Bounds probe = new Bounds(stand + Vector3.up * 1.5f, new Vector3(0.2f, 3f, 0.2f));
            _player.Teleport(FloorUnder(probe), Yaw(approach));
            yield return null;
            yield return null;
        }

        /// <summary>箱の中の床の位置。箱の中心から真下へ探し、当たらなければ箱の底の少し上。</summary>
        private static Vector3 FloorUnder(Bounds bounds)
        {
            Vector3 top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, bounds.size.y + 1.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.05f;
            }

            return new Vector3(bounds.center.x, bounds.min.y + 0.3f, bounds.center.z);
        }

        private static IEnumerator WaitUntil(System.Func<bool> done)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        internal static float Yaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static bool Contains(IReadOnlyCollection<string> ids, string id)
        {
            foreach (string each in ids)
            {
                if (each == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform node = t.parent; node != null; node = node.parent)
            {
                path = node.name + "/" + path;
            }

            return path;
        }

        /// <summary>扉 doorId の入口。玄関なら扉の id は建物の id と同じ。</summary>
        private static EntranceTrigger FindEntrance(string doorId)
        {
            foreach (EntranceTrigger each in Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Include))
            {
                if (each.DoorId == doorId)
                {
                    return each;
                }
            }

            return null;
        }

        /// <summary>建物 buildingId の、扉 doorId の出口。</summary>
        private static InteriorExit FindExit(string buildingId, string doorId)
        {
            foreach (InteriorExit each in Object.FindObjectsByType<InteriorExit>(FindObjectsInactive.Include))
            {
                if (each.BuildingId == buildingId && each.DoorId == doorId)
                {
                    return each;
                }
            }

            return null;
        }

        private static VisitZone FindVisitZone(string placeId)
        {
            foreach (VisitZone each in Object.FindObjectsByType<VisitZone>(FindObjectsInactive.Include))
            {
                if (each.PlaceId == placeId)
                {
                    return each;
                }
            }

            return null;
        }

        private static NPCTalker FindNpc(string npcId)
        {
            foreach (NPCTalker each in Object.FindObjectsByType<NPCTalker>(FindObjectsInactive.Include))
            {
                if (each.NpcId == npcId)
                {
                    return each;
                }
            }

            return null;
        }

        private static PhotoSpot FindPhotoSpot(string spotId)
        {
            foreach (PhotoSpot each in Object.FindObjectsByType<PhotoSpot>(FindObjectsInactive.Include))
            {
                if (each.SpotId == spotId)
                {
                    return each;
                }
            }

            return null;
        }
    }
}
