using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 建物の出入り（InteriorLoader / InteriorExit）の状態の移り変わり。
    /// セーブからの屋内復帰 (#61) と外へ戻す道、入れないときに操作を封鎖したまま残さないこと (#40)。
    /// EditMode では Awake が走らないので Instance も暗転の板も無く、コルーチン（暗転つきの移動）も通さない。
    /// </summary>
    public sealed class InteriorLoaderTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            KCDInput.ClearAllBlocks();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
            KCDInput.ClearAllBlocks();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private InteriorLoader MakeLoader(params string[] ids)
        {
            InteriorLoader loader = Make("InteriorLoader").AddComponent<InteriorLoader>();
            foreach (string id in ids)
            {
                loader.Register(new InteriorLoader.Entry
                {
                    Id = id,
                    DisplayName = id,
                    Spawn = Make("spawn_" + id).transform,
                    EntranceWorld = new Vector3(10f, 0f, 20f)
                });
            }

            return loader;
        }

        // ---- 登録 ----

        [Test]
        public void HasInterior_OnlyForRegisteredBuildings()
        {
            InteriorLoader loader = MakeLoader("library");

            Assert.IsTrue(loader.HasInterior("library"));
            Assert.IsFalse(loader.HasInterior("gym"), "登録していない建物に屋内があることになっている");
            Assert.IsFalse(loader.HasInterior(string.Empty));
            Assert.IsFalse(loader.HasInterior(null));
        }

        [Test]
        public void NewLoader_StartsOutside()
        {
            InteriorLoader loader = MakeLoader("library");

            Assert.IsFalse(loader.IsInside);
            Assert.AreEqual(string.Empty, loader.CurrentId);
            Assert.Less(loader.LastExitAt, -100f, "始めから「出た直後」扱いだと入口が閉じたまま");
        }

        // ---- セーブからの復帰 ----

        [Test]
        public void RestoreInside_UnknownBuilding_ReturnsFalseAndStaysOutside()
        {
            InteriorLoader loader = MakeLoader("library");

            Assert.IsFalse(loader.RestoreInside("gym", Vector3.one, 90f));
            Assert.IsFalse(loader.IsInside, "屋内の無い建物の中にいることになっている");
        }

        [Test]
        public void RestoreInside_KnownBuilding_GoesInsideAndRemembersTheWayOut()
        {
            InteriorLoader loader = MakeLoader("library", "gym");
            var back = new Vector3(12f, 0.5f, -30f);

            Assert.IsTrue(loader.RestoreInside("gym", back, 135f));

            Assert.IsTrue(loader.IsInside);
            Assert.AreEqual("gym", loader.CurrentId);
            Assert.AreEqual(back, loader.ReturnPosition, "出たときに立つ位置を覚えていない");
            Assert.AreEqual(135f, loader.ReturnYaw, 0.0001f);
            Assert.IsFalse(KCDInput.GameplayBlocked, "屋内に戻しただけで操作が止まっている");
        }

        [Test]
        public void RestoreInside_WhileInsideAnother_MovesToTheNewBuilding()
        {
            InteriorLoader loader = MakeLoader("library", "gym");
            loader.RestoreInside("library", Vector3.zero, 0f);

            Assert.IsTrue(loader.RestoreInside("gym", new Vector3(1f, 0f, 2f), 45f));

            Assert.AreEqual("gym", loader.CurrentId);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), loader.ReturnPosition, "前の建物の戻り先が残っている");
        }

        [Test]
        public void RestoreOutside_FromInside_LeavesAndClosesTheEntranceForAWhile()
        {
            InteriorLoader loader = MakeLoader("library");
            loader.RestoreInside("library", Vector3.zero, 0f);

            loader.RestoreOutside();

            Assert.IsFalse(loader.IsInside, "外に立たせたのに屋内扱いのまま（照明・ミニマップが屋内のまま残る）");
            Assert.AreEqual(string.Empty, loader.CurrentId);
            Assert.AreEqual(Time.time, loader.LastExitAt, "出た直後の入口の再発火を抑えていない");
        }

        [Test]
        public void RestoreOutside_WhenAlreadyOutside_StillClosesTheEntranceForAWhile()
        {
            // 出た直後の自動セーブは入口の 2 m 手前に立っている。読んだとたんに入口を踏み直さないこと。
            InteriorLoader loader = MakeLoader("library");

            loader.RestoreOutside();

            Assert.IsFalse(loader.IsInside);
            Assert.AreEqual(Time.time, loader.LastExitAt);
        }

        // ---- 入る・出るの受け付け ----

        [Test]
        public void Enter_UnknownBuilding_DoesNothing()
        {
            InteriorLoader loader = MakeLoader("library");

            loader.Enter("gym");

            Assert.IsFalse(loader.IsInside);
            Assert.IsFalse(KCDInput.GameplayBlocked);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Enter_WithoutSpawn_WarnsAndLeavesTheControlsFree()
        {
            InteriorLoader loader = Make("InteriorLoader").AddComponent<InteriorLoader>();
            loader.Register(new InteriorLoader.Entry { Id = "lab", DisplayName = "lab", Spawn = null });
            LogAssert.Expect(LogType.Warning, new Regex("屋内へ入れません: lab"));

            loader.Enter("lab");

            Assert.IsFalse(loader.IsInside);
            Assert.IsFalse(KCDInput.GameplayBlocked, "入れなかったのに操作を封鎖している（進行不能）");
            Assert.IsFalse(KCDInput.IsBlockedBy(loader));
        }

        [Test]
        public void Enter_WhileInside_DoesNothing()
        {
            InteriorLoader loader = MakeLoader("library", "gym");
            loader.RestoreInside("library", Vector3.zero, 0f);

            loader.Enter("gym");

            Assert.AreEqual("library", loader.CurrentId, "屋内から別の建物へ入り直している");
            Assert.IsFalse(KCDInput.GameplayBlocked);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Exit_WhileOutside_DoesNothing()
        {
            InteriorLoader loader = MakeLoader("library");

            loader.Exit();

            Assert.IsFalse(loader.IsInside);
            Assert.Less(loader.LastExitAt, -100f, "外にいるのに「出た」ことになっている");
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        // ---- 出口（InteriorExit）----

        [Test]
        public void ShouldExit_WithoutALoader_IsFalse()
        {
            Assert.IsFalse(InteriorExit.ShouldExit(null, "library"));
        }

        [Test]
        public void ShouldExit_OnlyTheExitOfTheBuildingYouAreIn()
        {
            InteriorLoader loader = MakeLoader("library", "gym");
            loader.RestoreInside("library", Vector3.zero, 0f);

            Assert.IsTrue(InteriorExit.ShouldExit(loader, "library"));
            Assert.IsFalse(InteriorExit.ShouldExit(loader, "gym"), "別の建物の出口で外へ出ている");
            Assert.IsFalse(InteriorExit.ShouldExit(loader, string.Empty));
        }

        [Test]
        public void ShouldExit_Outside_IsFalseEvenForAnExitWithoutABuilding()
        {
            // 建物の id が空のままの出口を外で踏んでも、外へ出す処理は走らせない。
            InteriorLoader loader = MakeLoader("library");

            Assert.IsFalse(InteriorExit.ShouldExit(loader, string.Empty));
            Assert.IsFalse(InteriorExit.ShouldExit(loader, "library"));
        }
    }
}
