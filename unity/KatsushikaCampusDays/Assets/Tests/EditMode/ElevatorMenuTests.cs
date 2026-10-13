using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// エレベーター (#275) の行き先の画面・乗り場・階ごとの明かり。EditMode では Update が走らないので、
    /// フレーム番号を明示した <see cref="ElevatorMenu.FrameInput"/> を <see cref="ElevatorMenu.Tick"/> に渡して進める。
    /// InteriorLoader が無いので、乗り場は建物の中にいる扱いになり、階の移動（GoTo）は断られる。
    /// </summary>
    public sealed class ElevatorMenuTests
    {
        private static readonly string[] FloorNames = { "1F", "3F", "4F", "5F", "6F" };

        private readonly List<GameObject> _created = new List<GameObject>();
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
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
            Time.timeScale = _timeScale;
            KCDInput.ClearAllBlocks();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private ElevatorPanel.Stop[] MakeStops()
        {
            var stops = new ElevatorPanel.Stop[FloorNames.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                Transform landing = Make("Landing_" + FloorNames[i]).transform;
                landing.position = new Vector3(1.4f, i == 0 ? 0f : 5.6f + 4f * i, 18.5f);
                stops[i] = new ElevatorPanel.Stop
                {
                    Floor = FloorNames[i],
                    CaptionJa = "階" + i,
                    CaptionEn = "Floor" + i,
                    Landing = landing
                };
            }

            return stops;
        }

        private ElevatorPanel MakePanel(string floor, ElevatorPanel.Stop[] stops)
        {
            ElevatorPanel panel = Make("Elevator_" + floor).AddComponent<ElevatorPanel>();
            panel.Floor = floor;
            panel.Stops = stops;
            return panel;
        }

        private ElevatorMenu MakeMenu()
        {
            ElevatorMenu menu = Make("ElevatorForTest").AddComponent<ElevatorMenu>();
            GameObject root = Make("ElevatorRoot");
            root.SetActive(false);
            menu.Bind(root, null);
            return menu;
        }

        [Test]
        public void RowOf_PutsUpperFloorsOnTop_AndCancelAtTheBottom()
        {
            Assert.AreEqual(4, ElevatorMenu.RowOf(0, 5));
            Assert.AreEqual(0, ElevatorMenu.RowOf(4, 5));
            Assert.AreEqual(5, ElevatorMenu.RowOf(5, 5));
        }

        [Test]
        public void Step_MovesInScreenOrder_AndWrapsAround()
        {
            Assert.AreEqual(1, ElevatorMenu.Step(0, -1, 5), "1F の上は 3F");
            Assert.AreEqual(5, ElevatorMenu.Step(0, 1, 5), "1F の下は「やめる」");
            Assert.AreEqual(4, ElevatorMenu.Step(5, 1, 5), "「やめる」の下は先頭の 6F");
            Assert.AreEqual(5, ElevatorMenu.Step(4, -1, 5), "6F の上は末尾の「やめる」");
            Assert.AreEqual(2, ElevatorMenu.Step(3, 1, 5), "5F の下は 4F");
        }

        [Test]
        public void Compose_ListsFloorsTopDown_ThenCancel()
        {
            ElevatorPanel.Stop[] stops = MakeStops();
            string text = ElevatorMenu.Compose(stops, 1, 1, false);
            int previous = -1;
            for (int i = stops.Length - 1; i >= 0; i--)
            {
                int at = text.IndexOf(stops[i].Floor, System.StringComparison.Ordinal);
                Assert.Greater(at, previous, stops[i].Floor + " の行が上の階より上にある");
                previous = at;
            }

            int marker = text.IndexOf("▶", System.StringComparison.Ordinal);
            Assert.AreEqual(1, text.Split('▶').Length - 1, "▶ は選んでいる 1 行だけ");
            Assert.Greater(marker, text.IndexOf("4F", System.StringComparison.Ordinal), "▶ が 3F の行に無い");
            Assert.Less(marker, text.IndexOf("3F", System.StringComparison.Ordinal), "▶ が 3F の行に無い");
            Assert.IsFalse(text.Contains("<link"), "キーで遊んでいるときに叩く印を付けている");
        }

        [Test]
        public void Compose_WithLinks_TagsEveryRow_AndDropsTheKeyHint()
        {
            ElevatorPanel.Stop[] stops = MakeStops();
            string links = ElevatorMenu.Compose(stops, 0, 0, true);
            string keys = ElevatorMenu.Compose(stops, 0, 0, false);
            for (int i = 0; i <= stops.Length; i++)
            {
                StringAssert.Contains("<link=\"choice:" + i + "\">", links);
            }

            Assert.Less(links.Split('\n').Length, keys.Split('\n').Length, "指のときにキーの案内が残っている");
        }

        [Test]
        public void Open_StartsAtThisFloor_AndIgnoresTheOpeningFrame()
        {
            ElevatorPanel.Stop[] stops = MakeStops();
            ElevatorPanel panel = MakePanel("4F", stops);
            ElevatorMenu menu = MakeMenu();

            menu.Open(panel);
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(2, menu.Index, "カーソルが今の階から始まらない");
            Assert.AreEqual(0f, Time.timeScale, "開いている間に時間が止まらない");
            Assert.IsTrue(KCDInput.GameplayBlocked, "開いている間に操作が封鎖されない");

            menu.Tick(new ElevatorMenu.FrameInput(Time.frameCount, submit: true, cancel: true));
            Assert.IsTrue(menu.IsOpen, "開いたフレームの Enter / Esc を拾っている");
        }

        [Test]
        public void Cancel_ClosesAndRestoresTimeAndInput()
        {
            ElevatorPanel panel = MakePanel("1F", MakeStops());
            ElevatorMenu menu = MakeMenu();
            menu.Open(panel);

            menu.Tick(new ElevatorMenu.FrameInput(Time.frameCount + 1, cancel: true));
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void TappingTheCancelRow_OnlyCloses()
        {
            ElevatorPanel.Stop[] stops = MakeStops();
            ElevatorMenu menu = MakeMenu();
            menu.Open(MakePanel("1F", stops));

            menu.Tick(new ElevatorMenu.FrameInput(Time.frameCount + 1, tap: stops.Length));
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void Open_IsRefused_WhileBlocked_OrWithASingleStop()
        {
            ElevatorMenu menu = MakeMenu();
            ElevatorPanel.Stop[] stops = MakeStops();
            var owner = new object();
            KCDInput.Block(owner);
            menu.Open(MakePanel("1F", stops));
            Assert.IsFalse(menu.IsOpen, "ほかの画面の封鎖中に開いた");
            KCDInput.Unblock(owner);

            menu.Open(MakePanel("1F", new[] { stops[0] }));
            Assert.IsFalse(menu.IsOpen, "行き先の無い乗り場で開いた");
        }

        [Test]
        public void GoTo_RefusesThisFloor_OutOfRange_AndMissingLandings()
        {
            ElevatorPanel.Stop[] stops = MakeStops();
            stops[3].Landing = null;
            ElevatorPanel panel = MakePanel("3F", stops);

            Assert.AreEqual(1, panel.CurrentIndex);
            Assert.IsFalse(panel.GoTo(1), "今の階へ移ろうとした");
            Assert.IsFalse(panel.GoTo(-1));
            Assert.IsFalse(panel.GoTo(stops.Length), "「やめる」で移ろうとした");
            Assert.IsFalse(panel.GoTo(3), "乗り場の無い階へ移ろうとした");
            Assert.IsFalse(panel.GoTo(4), "建物の出入り係が無いのに移ろうとした");
        }

        [Test]
        public void FloorOf_UsesTheLastBottomBelowTheHeight()
        {
            float[] bottoms = { -0.5f, 9.1f, 13.1f };
            Assert.AreEqual(0, InteriorFloorLights.FloorOf(bottoms, -3f), "いちばん下より下は 1 階");
            Assert.AreEqual(0, InteriorFloorLights.FloorOf(bottoms, 3.2f));
            Assert.AreEqual(1, InteriorFloorLights.FloorOf(bottoms, 9.6f));
            Assert.AreEqual(2, InteriorFloorLights.FloorOf(bottoms, 30f));
        }

        [Test]
        public void Apply_LightsOnlyThatFloor()
        {
            GameObject group = Make("Lights");
            var lights = new Light[4];
            float[] heights = { 3.0f, 3.2f, 12.4f, 16.4f };
            for (int i = 0; i < lights.Length; i++)
            {
                GameObject go = Make("Lamp" + i);
                go.transform.SetParent(group.transform, false);
                go.transform.position = new Vector3(0f, heights[i], 0f);
                lights[i] = go.AddComponent<Light>();
            }

            InteriorFloorLights floors = group.AddComponent<InteriorFloorLights>();
            floors.Configure("research2", new[] { "1F", "3F", "4F" }, new[] { -0.5f, 9.1f, 13.1f }, lights);

            floors.Apply(1);
            Assert.AreEqual(1, floors.ActiveFloor);
            CollectionAssert.AreEqual(new[] { false, false, true, false }, Enabled(lights));

            floors.Apply(0);
            CollectionAssert.AreEqual(new[] { true, true, false, false }, Enabled(lights));
        }

        private static bool[] Enabled(Light[] lights)
        {
            var enabled = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                enabled[i] = lights[i].enabled;
            }

            return enabled;
        }
    }
}
