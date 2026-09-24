using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// Campus シーンに、13 クエストのどのステップの対象も、隠しアイテム 20 個も、写真スポット 6 箇所も
    /// 置かれていることを守る (#65)。
    ///
    /// 以前は collectibles.json の一覧だけがあってシーンに何も無く、サブクエ「写真散歩」の ps_* と
    /// 「実験ノート」のゴーグル c_lab_goggles に行き先が無かった。結果画面の「隠しアイテム」「写真」も
    /// 0 から動かなかった。クエストを会話と報告だけでなぞるテスト（QuestAchievabilityTests）では
    /// この詰みは見えないので、シーンを開いて確かめる。
    ///
    /// シーンは SceneBuilder（KCD/シーンを組み直す）が作る。置き方を変えたら組み直してから走らせる。
    /// </summary>
    public sealed class CampusQuestTargetsTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>置き場所（屋外の (x, z) か屋内の poi）から、置いた物がどれだけ離れてよいか [m]。
        /// 写真スポットと重なる物は三脚から横へ 0.9 m ずらす（CollectibleStage.SideStep）。</summary>
        private const float MaxPlaceOffset = 2f;

        /// <summary>宝石の中心と、その真下の床との隙間 [m]。CollectibleStage.ItemLift（0.45 m）の前後。</summary>
        private const float MinItemGap = 0.2f;

        private const float MaxItemGap = 0.8f;

        /// <summary>屋外の物の高さの上限 [m]。これより上は庇や屋根の上で、手が届かず下から見えない。</summary>
        private const float MaxOutdoorY = 3f;

        /// <summary>三脚の足もとと床の差の許容 [m]。</summary>
        private const float TripodFloorTolerance = 0.1f;

        /// <summary>隠しアイテムと三脚の水平距離の下限 [m]。近いと E の対象を取り合う。</summary>
        private const float MinItemToTripod = 0.8f;

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private Scene _scene;
        private readonly List<VisitZone> _zones = new List<VisitZone>();
        private readonly List<EntranceTrigger> _entrances = new List<EntranceTrigger>();
        private readonly List<CollectableItem> _items = new List<CollectableItem>();
        private readonly List<NPCTalker> _npcs = new List<NPCTalker>();
        private readonly List<PhotoSpot> _tripods = new List<PhotoSpot>();
        private readonly List<PropInteractable> _props = new List<PropInteractable>();
        private readonly List<Transform> _interiors = new List<Transform>();
        private CollectibleCatalog _catalog;
        private int _solidMask;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _catalog = CollectibleCatalog.Parse(File.ReadAllText(Path.Combine(DataRoot, "Collectibles", "collectibles.json")));
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                _zones.AddRange(go.GetComponentsInChildren<VisitZone>(true));
                _entrances.AddRange(go.GetComponentsInChildren<EntranceTrigger>(true));
                _items.AddRange(go.GetComponentsInChildren<CollectableItem>(true));
                _npcs.AddRange(go.GetComponentsInChildren<NPCTalker>(true));
                _tripods.AddRange(go.GetComponentsInChildren<PhotoSpot>(true));
                _props.AddRange(go.GetComponentsInChildren<PropInteractable>(true));
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (QuestObjectiveLocator.InteriorIdFromName(t.name) != null)
                    {
                        _interiors.Add(t);
                    }
                }
            }

            // 床を探すレイは、拾い物・三脚・人には当てない。
            _solidMask = ~LayerMask.GetMask("Interactable", "NPC", "Player");
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        private static List<QuestData> LoadQuests()
        {
            var quests = new List<QuestData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(ReadObject(file));
                Assert.IsNotNull(quest, file);
                quests.Add(quest);
            }

            return quests;
        }

        private static HashSet<string> DialogueFlags()
        {
            var flags = new HashSet<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Dialogue"), "*.json"))
            {
                DialogueData data = DialogueData.FromJson(ReadObject(file));
                Assert.IsNotNull(data, file);
                foreach (DialogueTopic topic in data.Topics)
                {
                    if (!string.IsNullOrEmpty(topic.SetsFlag))
                    {
                        flags.Add(topic.SetsFlag);
                    }
                }
            }

            return flags;
        }

        private static Dictionary<string, object> Totals()
        {
            Dictionary<string, object> totals = MiniJson.GetObject(ReadObject(Path.Combine(DataRoot, "Ending", "result.json")), "totals");
            Assert.IsNotNull(totals, "result.json に totals が無い");
            return totals;
        }

        private List<CollectableItem> ItemsWithId(string id)
        {
            return _items.FindAll(i => i.ItemId == id);
        }

        private Transform FindPoi(string building, string poi)
        {
            foreach (Transform interior in _interiors)
            {
                if (QuestObjectiveLocator.InteriorIdFromName(interior.name) != building)
                {
                    continue;
                }

                foreach (Transform t in interior.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == poi)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        [Test]
        public void EveryQuestStep_HasItsTargetInTheScene()
        {
            Assert.IsTrue(_scene.IsValid(), ScenePath + " を開けない");
            List<QuestData> quests = LoadQuests();
            Assert.AreEqual(MiniJson.GetInt(Totals(), "quests", -1), quests.Count, "クエストの数が result.json の totals と合わない");

            var missing = new List<string>();
            foreach (QuestData quest in quests)
            {
                foreach (QuestStep step in quest.Steps)
                {
                    string where = quest.Id + "/" + step.Id + " (" + step.Kind + " " + step.Target + ")";
                    switch (step.Kind)
                    {
                        case QuestStepKind.Visit:
                            VisitZone zone = _zones.Find(z => z.PlaceId == step.Target);
                            if (zone == null)
                            {
                                missing.Add(where + ": VisitZone が無い");
                            }
                            else if (!zone.GetComponent<BoxCollider>().isTrigger)
                            {
                                missing.Add(where + ": VisitZone の箱がトリガーになっていない");
                            }

                            break;
                        case QuestStepKind.Enter:
                            if (!_entrances.Exists(e => e.BuildingId == step.Target))
                            {
                                missing.Add(where + ": EntranceTrigger が無い");
                            }

                            break;
                        case QuestStepKind.Collect:
                            int count = ItemsWithId(step.Target).Count;
                            if (count < step.Count)
                            {
                                missing.Add(where + ": CollectableItem が " + count + " 個（" + step.Count + " 個要る）");
                            }

                            break;
                        case QuestStepKind.Talk:
                            if (!_npcs.Exists(n => n.NpcId == step.Target))
                            {
                                missing.Add(where + ": NPCTalker が無い");
                            }

                            break;
                        default:
                            // flag は、シーンの物（PropInteractable）を調べるか、会話（setsFlag）で立つ。
                            if (!_props.Exists(p => p.FlagId == step.Target) && !DialogueFlags().Contains(step.Target))
                            {
                                missing.Add(where + ": フラグを立てる物（PropInteractable）も会話（setsFlag）も無い");
                            }

                            break;
                    }
                }
            }

            Assert.IsEmpty(missing, "シーンに対象が無いステップ:\n" + string.Join("\n", missing));
        }

        [Test]
        public void HiddenItems_AreAllPlaced_AndResultCanCountToTheTotal()
        {
            var ids = new List<string>();
            var bad = new List<string>();
            foreach (CatalogItem item in _catalog.Items)
            {
                if (!item.IsHidden)
                {
                    continue;
                }

                int count = ItemsWithId(item.Id).Count;
                if (count != 1)
                {
                    bad.Add(item.Id + ": シーンに " + count + " 個（1 個のはず）");
                    continue;
                }

                ids.Add(item.Id);
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));

            // 結果画面は DayStats の記録を一覧で数える。シーンの物を全部拾えば totals に届くこと。
            Assert.AreEqual(MiniJson.GetInt(Totals(), "collectibles", -1), _catalog.CountHidden(ids),
                "シーンの隠しアイテムを全部拾っても result.json の totals に届かない");
        }

        [Test]
        public void HiddenItems_SitOnAFloorWhereThePlayerCanReachThem()
        {
            int interactable = LayerMask.NameToLayer("Interactable");
            var bad = new List<string>();
            foreach (CatalogItem item in _catalog.Items)
            {
                if (!item.IsHidden)
                {
                    continue;
                }

                List<CollectableItem> placed = ItemsWithId(item.Id);
                if (placed.Count == 0)
                {
                    bad.Add(item.Id + ": シーンに無い");
                    continue;
                }

                CollectableItem found = placed[0];
                Vector3 p = found.transform.position;
                string owner = QuestObjectiveLocator.InteriorOwnerOf(found.transform);

                if (found.gameObject.layer != interactable)
                {
                    bad.Add(item.Id + ": Interactable レイヤーに無い（E の対象にならない）");
                }

                Collider trigger = found.GetComponent<Collider>();
                if (trigger == null || !trigger.isTrigger)
                {
                    bad.Add(item.Id + ": トリガーの当たり判定が無い");
                }

                if (item.Place.IsOutdoor)
                {
                    var expected = new Vector3(item.Place.XZ.x, p.y, item.Place.XZ.y);
                    if (owner != null)
                    {
                        bad.Add(item.Id + ": 屋外の物が Interior_" + owner + " の下にある");
                    }

                    if (Horizontal(p, expected) > MaxPlaceOffset || p.y < -0.5f || p.y > MaxOutdoorY)
                    {
                        bad.Add(string.Format("{0}: 位置 {1} / 一覧の (x, z) = {2}", item.Id, p, item.Place.XZ));
                    }
                }
                else
                {
                    Transform poi = FindPoi(item.Place.Building, item.Place.Poi);
                    if (owner != item.Place.Building)
                    {
                        bad.Add(item.Id + ": Interior_" + item.Place.Building + " の下に無い（" + owner + "）");
                    }

                    if (poi == null)
                    {
                        bad.Add(item.Id + ": " + item.Place.Building + " に " + item.Place.Poi + " が無い");
                    }
                    else if (Horizontal(p, poi.position) > MaxPlaceOffset || p.y < poi.position.y - 0.1f
                             || p.y > poi.position.y + 1f)
                    {
                        bad.Add(string.Format("{0}: 位置 {1} / {2} {3}", item.Id, p, item.Place.Poi, poi.position));
                    }

                    if (!_entrances.Exists(e => e.BuildingId == item.Place.Building))
                    {
                        bad.Add(item.Id + ": " + item.Place.Building + " の入口（EntranceTrigger）が無い");
                    }
                }

                // 宝石は床の少し上に浮かぶ。床が無い（吹き抜けの上）・浮きすぎ・壁や家具にめり込み、を拾う。
                if (!Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 1.5f, _solidMask,
                        QueryTriggerInteraction.Ignore))
                {
                    bad.Add(string.Format("{0}: 真下 1.5 m に床が無い（位置 {1}）", item.Id, p));
                }
                else
                {
                    float gap = p.y - hit.point.y;
                    if (gap < MinItemGap || gap > MaxItemGap || hit.normal.y < 0.5f)
                    {
                        bad.Add(string.Format("{0}: 床 {1}（{2}）との隙間 {3:0.00} m", item.Id, hit.collider.name,
                            hit.point, gap));
                    }
                }

                if (Physics.CheckSphere(p, 0.12f, _solidMask, QueryTriggerInteraction.Ignore))
                {
                    bad.Add(string.Format("{0}: 何かにめり込んでいる（位置 {1}）", item.Id, p));
                }

                foreach (PhotoSpot tripod in _tripods)
                {
                    if (QuestObjectiveLocator.InteriorOwnerOf(tripod.transform) == owner
                        && Horizontal(p, tripod.transform.position) < MinItemToTripod)
                    {
                        bad.Add(item.Id + ": 三脚 " + tripod.SpotId + " に近すぎて E の対象を取り合う");
                    }
                }
            }

            Assert.IsEmpty(bad, bad.Count + " 件:\n" + string.Join("\n", bad));
        }

        [Test]
        public void PhotoSpots_HaveAZoneAndATripod_AndResultCanCountToTheTotal()
        {
            int interactable = LayerMask.NameToLayer("Interactable");
            var ids = new List<string>();
            var bad = new List<string>();
            foreach (CatalogPhotoSpot spot in _catalog.PhotoSpots)
            {
                List<VisitZone> zones = _zones.FindAll(z => z.PlaceId == spot.Id);
                List<PhotoSpot> tripods = _tripods.FindAll(t => t.SpotId == spot.Id);
                if (zones.Count != 1 || tripods.Count != 1)
                {
                    bad.Add(spot.Id + ": VisitZone が " + zones.Count + " 個、三脚が " + tripods.Count + " 個（1 つずつのはず）");
                    continue;
                }

                PhotoSpot tripod = tripods[0];
                Vector3 foot = tripod.transform.position;
                ids.Add(spot.Id);

                if (tripod.gameObject.layer != interactable)
                {
                    bad.Add(spot.Id + ": 三脚が Interactable レイヤーに無い");
                }

                Collider trigger = tripod.GetComponent<Collider>();
                if (trigger == null || !trigger.isTrigger)
                {
                    bad.Add(spot.Id + ": 三脚にトリガーの当たり判定が無い");
                }

                if (tripod.InteractionRange < 2f)
                {
                    bad.Add(spot.Id + ": 三脚の E の距離が " + tripod.InteractionRange + " m と短い");
                }

                if (Vector2.Distance(tripod.LookDir, spot.LookDir) > 1e-3f)
                {
                    bad.Add(spot.Id + ": 三脚の向き " + tripod.LookDir + " が一覧の look_dir " + spot.LookDir + " と違う");
                }

                // 三脚のそばに立った人の体（足もと + 1 m）が visit の箱に入る。
                BoxCollider box = zones[0].GetComponent<BoxCollider>();
                Vector3 local = box.transform.InverseTransformPoint(foot + Vector3.up) - box.center;
                if (!box.isTrigger || Mathf.Abs(local.x) > box.size.x * 0.5f || Mathf.Abs(local.y) > box.size.y * 0.5f
                    || Mathf.Abs(local.z) > box.size.z * 0.5f)
                {
                    bad.Add(spot.Id + ": 三脚のそばに立っても visit の箱に入らない");
                }

                string owner = QuestObjectiveLocator.InteriorOwnerOf(tripod.transform);
                if (spot.Place.IsIndoor ? owner != spot.Place.Building : owner != null)
                {
                    bad.Add(spot.Id + ": 置いた建物が違う（" + owner + "）");
                }

                if (!Physics.Raycast(foot + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1f, _solidMask,
                        QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(foot.y - hit.point.y) > TripodFloorTolerance)
                {
                    bad.Add(string.Format("{0}: 三脚の足もと {1} が床に着いていない", spot.Id, foot));
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            Assert.AreEqual(MiniJson.GetInt(Totals(), "photos", -1), _catalog.CountPhotoSpots(ids),
                "シーンの写真スポットを全部撮っても result.json の totals に届かない");
        }
    }
}
