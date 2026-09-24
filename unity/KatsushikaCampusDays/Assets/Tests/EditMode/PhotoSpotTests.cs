using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 写真スポットの三脚と、隠しアイテム・写真スポットのトースト、屋内の物の持ち主の引き方 (#65)。
    /// シーンに置けているかは CampusQuestTargetsTests が見る。
    /// </summary>
    public sealed class PhotoSpotTests
    {
        private static CollectibleCatalog DataCatalog()
        {
            string path = Path.Combine(Application.dataPath, "Data", "Collectibles", "collectibles.json");
            return CollectibleCatalog.Parse(File.ReadAllText(path));
        }

        [Test]
        public void TryYaw_TurnsLookDirIntoDegreesAroundY()
        {
            // CollectibleStage が三脚のレンズ（+Z）を向け、PhotoSpot.Interact がプレイヤーを向ける角度。
            Assert.IsTrue(PhotoSpot.TryYaw(new Vector2(0f, 1f), out float north));
            Assert.AreEqual(0f, north, 1e-4f, "+z が 0 度");

            Assert.IsTrue(PhotoSpot.TryYaw(new Vector2(1f, 0f), out float east));
            Assert.AreEqual(90f, east, 1e-4f, "+x が 90 度");

            Assert.IsTrue(PhotoSpot.TryYaw(new Vector2(-2f, 0f), out float west));
            Assert.AreEqual(-90f, west, 1e-4f, "長さは関係ない");

            Vector3 forward = Quaternion.Euler(0f, east, 0f) * Vector3.forward;
            Assert.AreEqual(1f, forward.x, 1e-4f, "Euler(0, yaw, 0) の +Z が look_dir を向く");
        }

        [Test]
        public void TryYaw_ZeroLookDirKeepsTheCurrentFacing()
        {
            Assert.IsFalse(PhotoSpot.TryYaw(Vector2.zero, out float yaw));
            Assert.AreEqual(0f, yaw);
        }

        [Test]
        public void EveryCatalogSpot_HasALookDirThatTurnsThePlayer()
        {
            foreach (CatalogPhotoSpot spot in DataCatalog().PhotoSpots)
            {
                Assert.IsTrue(PhotoSpot.TryYaw(spot.LookDir, out _), spot.Id + " の look_dir が 0 で、撮る向きが決まらない");
            }
        }

        [Test]
        public void PhotoToast_NamesTheSpotInsteadOfTheId()
        {
            CatalogPhotoSpot spot = DataCatalog().FindPhotoSpot("ps_mall_view");
            Assert.IsNotNull(spot);

            // ランタイムの一覧（Resources の写し）で名前を引く。ja / en の辞書は collectibles.json と同じ値。
            string label = PhotoSystem.LabelFor("ps_mall_view", "kcd_20260924_120000.png");
            Assert.AreEqual(L.Pick(spot.NameJa, spot.NameEn), label);
            Assert.AreNotEqual("ps_mall_view", label);
        }

        [Test]
        public void PhotoToast_FreeShotsShowTheFileAndUnknownSpotsTheId()
        {
            Assert.AreEqual("kcd_20260924_120000.png", PhotoSystem.LabelFor(null, "kcd_20260924_120000.png"));
            Assert.AreEqual("kcd_20260924_120000.png", PhotoSystem.LabelFor(string.Empty, "kcd_20260924_120000.png"));
            Assert.AreEqual(string.Empty, PhotoSystem.LabelFor(null, null));
            Assert.AreEqual("ps_not_in_catalog", PhotoSystem.LabelFor("ps_not_in_catalog", "kcd.png"));
        }

        [Test]
        public void VisitToast_AnnouncesPhotoSpotsAndNamesOtherPlaces()
        {
            CatalogPhotoSpot spot = DataCatalog().FindPhotoSpot("ps_pond_library");
            Assert.IsNotNull(spot);
            string name = L.Pick(spot.NameJa, spot.NameEn);

            string label = VisitZone.PlaceLabel("ps_pond_library", spot.NameJa);
            Assert.AreEqual(L.Format("ui.hud.photo_spot", name), label);
            StringAssert.Contains(name, label);
            Assert.AreNotEqual(name, label, "写真スポットだと分かる文言が付いていない");

            Assert.AreEqual(L.Get("ui.place.mall_bench", "モールのベンチ"), VisitZone.PlaceLabel("mall_bench", "モールのベンチ"));
            Assert.AreEqual("どこかの場所", VisitZone.PlaceLabel("place_not_in_dictionary", "どこかの場所"));
        }

        [Test]
        public void PickupToast_HiddenItemsAreFoundAndQuestPickupsArePickedUp()
        {
            CatalogItem keyring = DataCatalog().FindItem("c_gate_keyring");
            Assert.IsNotNull(keyring);
            Assert.AreEqual(L.Format("ui.hud.collectible_found", L.Pick(keyring.NameJa, keyring.NameEn)),
                CollectableItem.ToastFor("c_gate_keyring", keyring.NameJa));

            // 葉はクエストの拾い物。一覧に無いので「◯◯を拾った」。
            Assert.AreEqual(L.Format("ui.hud.picked_up", L.Get("ui.item.leaf", "理科大グリーンの葉")),
                CollectableItem.ToastFor("leaf", "理科大グリーンの葉"));

            // クエストの報酬（source = quest）は結果画面の隠しアイテムに数えないので、隠しアイテムの文言にはしない。
            CatalogItem charm = DataCatalog().FindItem("c_sora_charm");
            Assert.IsNotNull(charm);
            Assert.IsFalse(charm.IsHidden);
            Assert.AreEqual(L.Format("ui.hud.picked_up", L.Get("ui.item.c_sora_charm", charm.NameJa)),
                CollectableItem.ToastFor("c_sora_charm", charm.NameJa));
        }

        [Test]
        public void InteriorOwner_WalksUpToTheInteriorRoot()
        {
            var interior = new GameObject("Interior_lab1");
            var room = new GameObject("room");
            var item = new GameObject("Item_c_lab_goggles");
            var outdoor = new GameObject("Collectibles");
            try
            {
                room.transform.SetParent(interior.transform, false);
                item.transform.SetParent(room.transform, false);

                Assert.AreEqual("lab1", QuestObjectiveLocator.InteriorOwnerOf(item.transform));
                Assert.AreEqual("lab1", QuestObjectiveLocator.InteriorOwnerOf(interior.transform));
                Assert.IsNull(QuestObjectiveLocator.InteriorOwnerOf(outdoor.transform));
                Assert.IsNull(QuestObjectiveLocator.InteriorOwnerOf(null));
            }
            finally
            {
                Object.DestroyImmediate(interior);
                Object.DestroyImmediate(outdoor);
            }
        }
    }
}
