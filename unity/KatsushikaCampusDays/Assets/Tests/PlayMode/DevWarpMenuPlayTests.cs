using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 開発者用のワープメニュー (#242)。F2 で開くと時間が止まり、キーボードだけで屋外の 1 か所と屋内の 1 棟へ移動でき、
    /// 屋内にいるときに屋外を選ぶと先に外へ出ること。一覧がシーンのゾーン・入口・屋内・NPC と同じ数であること。
    /// 開いたメニューで選んでいる行が黄色の文字で枠の中に見えていることは、文字のメッシュでいつも見る。
    /// 描画のデバイスがあるときは、メニューを撮った画素にもその黄色が出ていることを見る。
    /// 環境変数 KCD_DEV_MENU_SHOT にフォルダを渡すと、撮ったメニューを PNG で書き出す。
    /// </summary>
    public sealed class DevWarpMenuPlayTests : SceneTestBase
    {
        private const float WaitSeconds = 12f;

        private const string ShotEnvironmentVariable = "KCD_DEV_MENU_SHOT";

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Menu_MovesToAnOutdoorPlaceAndABuilding_AndComesBackOutFirst()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            DevWarpMenu menu = DevWarpMenu.Instance;
            Assert.IsNotNull(menu, "エディタなのにワープメニューが無い");
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            InteriorLoader loader = InteriorLoader.Instance;
            Assert.IsNotNull(player);
            Assert.IsNotNull(loader);

            // 開く。時間が止まり、ゲームの入力が封じられる。
            menu.Tick(new DevWarpMenu.FrameInput(toggle: true));
            Assert.IsTrue(menu.IsOpen, "F2 で開かない");
            Assert.AreEqual(0f, Time.timeScale, "開いても時間が止まらない");
            Assert.IsTrue(KCDInput.GameplayBlocked, "開いてもゲームの入力が封じられない");

            // 一覧がシーンと同じ数。
            DevWarpCatalog catalog = menu.Catalog;
            Assert.AreEqual(UniqueCount(UnityEngine.Object.FindObjectsByType<VisitZone>(),
                                z => z.PlaceId != null && z.PlaceId.StartsWith(DevWarpCatalog.InteriorPlacePrefix, StringComparison.Ordinal)
                                    ? null
                                    : z.PlaceId) +
                            UniqueCount(UnityEngine.Object.FindObjectsByType<EntranceTrigger>(), e => e.BuildingId),
                catalog.Tab((int)DevWarpKind.Place).Count, "場所の数がシーンのゾーン + 入口と合わない");
            Assert.AreEqual(loader.Entries.Count, catalog.Tab((int)DevWarpKind.Interior).Count,
                "建物の中の数が InteriorLoader の登録と合わない");
            Assert.Greater(catalog.Tab((int)DevWarpKind.Npc).Count, 0, "人の行き先が 1 件も無い");
            Assert.AreEqual(DevWarpCatalog.Times.Length, catalog.Tab((int)DevWarpKind.Time).Count);

            Debug.Log("[KCD] ワープ先の数: 場所 " + catalog.Tab((int)DevWarpKind.Place).Count + " / 建物の中 " +
                      catalog.Tab((int)DevWarpKind.Interior).Count + " / 人 " + catalog.Tab((int)DevWarpKind.Npc).Count +
                      " / 時刻 " + catalog.Tab((int)DevWarpKind.Time).Count + " / 計 " + catalog.Count);

            yield return null;
            AssertSelectedRowIsShown(menu);
            if (CanRender)
            {
                yield return CaptureMenu(menu);
            }
            else
            {
                Debug.Log("[KCD] 描画のデバイスが無い（-nographics）ので、メニューを撮った画素は見ない");
            }

            // 屋外: 最初の入口前へ。
            DevWarpTarget door = catalog.Tab((int)DevWarpKind.Place)[FindIndex(catalog, DevWarpKind.Place, "door_")];
            Select(menu, DevWarpKind.Place, door.Id);
            menu.Tick(new DevWarpMenu.FrameInput(submit: true));
            Assert.IsFalse(menu.IsOpen, "移動を選んでも閉じない");
            Assert.AreEqual(1f, Time.timeScale, "閉じても時間が戻らない");
            yield return WaitUntilIdle(menu);

            Assert.IsFalse(loader.IsInside);
            Assert.Less(Vector2.Distance(Flat(player.transform.position), Flat(door.Position)), 1.5f,
                "入口前 " + door.Position.ToString("F2") + " へ移動していない。いま " + player.transform.position.ToString("F2"));

            // 屋内: 最初の建物へ。
            DevWarpTarget interior = catalog.Tab((int)DevWarpKind.Interior)[0];
            menu.Tick(new DevWarpMenu.FrameInput(toggle: true));
            Select(menu, DevWarpKind.Interior, interior.Id);
            menu.Tick(new DevWarpMenu.FrameInput(submit: true));
            yield return WaitUntilIdle(menu);
            Assert.IsTrue(loader.IsInside, interior.Id + " の中に入れていない");
            Assert.AreEqual(interior.Id, loader.CurrentId);

            // 屋内から屋外を選ぶと、先に外へ出てから動く。
            DevWarpTarget zone = catalog.Tab((int)DevWarpKind.Place)[0];
            menu.Tick(new DevWarpMenu.FrameInput(toggle: true));
            Select(menu, DevWarpKind.Place, zone.Id);
            menu.Tick(new DevWarpMenu.FrameInput(submit: true));
            yield return WaitUntilIdle(menu);
            Assert.IsFalse(loader.IsInside, "屋内から屋外を選んでも外へ出ない");
            Assert.Less(Vector2.Distance(Flat(player.transform.position), Flat(zone.Position)), 1.5f,
                "場所 " + zone.Id + " へ移動していない。いま " + player.transform.position.ToString("F2"));

            // 時刻。
            DayNightCycle cycle = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle);
            menu.Tick(new DevWarpMenu.FrameInput(toggle: true));
            Select(menu, DevWarpKind.Time, "night");
            menu.Tick(new DevWarpMenu.FrameInput(submit: true));
            yield return WaitUntilIdle(menu);
            Assert.AreEqual(21f, cycle.Hours, 0.05f, "夜を選んでも時刻が変わらない");

            AssertNoErrors("ワープメニュー");
        }

        private static int UniqueCount<T>(T[] items, Func<T, string> id) where T : UnityEngine.Object
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (T item in items)
            {
                string key = id(item);
                if (!string.IsNullOrEmpty(key))
                {
                    seen.Add(key);
                }
            }

            return seen.Count;
        }

        private static int FindIndex(DevWarpCatalog catalog, DevWarpKind kind, string prefix)
        {
            var tab = catalog.Tab((int)kind);
            for (int i = 0; i < tab.Count; i++)
            {
                if (tab[i].Id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            Assert.Fail(prefix + " で始まる行き先が無い");
            return -1;
        }

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>キーボードと同じ入力（左右で見出し、上下で行）だけで id の行を選ぶ。</summary>
        private static void Select(DevWarpMenu menu, DevWarpKind kind, string id)
        {
            for (int guard = 0; menu.Tab != (int)kind && guard < DevWarpCatalog.TabCount; guard++)
            {
                menu.Tick(new DevWarpMenu.FrameInput(horizontal: 1));
            }

            Assert.AreEqual((int)kind, menu.Tab);
            var rows = menu.Catalog.Tab(menu.Tab);
            for (int guard = 0; rows[menu.Index].Id != id && guard <= rows.Count; guard++)
            {
                menu.Tick(new DevWarpMenu.FrameInput(vertical: 1));
            }

            Assert.AreEqual(id, rows[menu.Index].Id, "行き先 " + id + " を選べない");
        }

        private static IEnumerator WaitUntilIdle(DevWarpMenu menu)
        {
            float until = Time.realtimeSinceStartup + WaitSeconds;
            yield return null;
            while (menu.IsTraveling && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.IsFalse(menu.IsTraveling, "移動が " + WaitSeconds + " 秒で終わらない");
            yield return PlayModeScenes.Advance(5, 0.2f);
        }

        /// <summary>選んでいる行の文字の色（#FFD54A）か。文字のメッシュの色と、撮った画素の色の両方をこれで見る。</summary>
        private static bool IsSelectionYellow(Color32 color) => color.r > 200 && color.g > 170 && color.b < 120;

        /// <summary>
        /// 開いたメニューで、選んでいる行（「▶ 」と行き先の名前）が黄色の文字で、枠の中と画面の中に置かれているか。
        /// TMP が組んだ文字の情報（色と位置）で見るので、描画のデバイスが無くても確かめられる。
        /// オーバーレイのキャンバスはカメラ無しで画面の座標に直す。
        /// </summary>
        private static void AssertSelectedRowIsShown(DevWarpMenu menu)
        {
            const float slackPx = 0.5f;
            Canvas canvas = menu.Canvas;
            Assert.IsNotNull(canvas, "ワープメニューの Canvas が無い");
            Assert.IsTrue(canvas.isActiveAndEnabled, "開いてもワープメニューの Canvas が出ていない");
            var panel = canvas.transform.Find("Panel") as RectTransform;
            Assert.IsNotNull(panel, "ワープメニューの枠 (Panel) が無い");
            Assert.IsTrue(panel.gameObject.activeInHierarchy, "開いてもワープメニューの枠が出ていない");
            Transform bodyTransform = panel.Find("Body");
            Assert.IsNotNull(bodyTransform, "ワープメニューの文面 (Body) が無い");
            TMP_Text body = bodyTransform.GetComponent<TMP_Text>();
            Assert.IsNotNull(body, "ワープメニューの文面に TMP_Text が無い");
            Assert.IsTrue(body.isActiveAndEnabled, "ワープメニューの文面が出ていない");

            Canvas.ForceUpdateCanvases();
            body.ForceMeshUpdate();

            string row = "▶ " + menu.Catalog.Tab(menu.Tab)[menu.Index].Label;
            string parsed = body.GetParsedText();
            int start = parsed.IndexOf(row, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "選んでいる行「" + row + "」が文面に出ていない。出ている文面:\n" + parsed);

            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            TMP_CharacterInfo[] characters = body.textInfo.characterInfo;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = start; i < start + row.Length; i++)
            {
                TMP_CharacterInfo character = characters[i];
                if (!character.isVisible)
                {
                    continue;
                }

                Assert.IsTrue(IsSelectionYellow(character.color),
                    "選んでいる行の「" + character.character + "」が黄色でない: " + character.color);
                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, body.transform.TransformPoint(character.bottomLeft));
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, body.transform.TransformPoint(character.topRight));
                min = Vector2.Min(min, Vector2.Min(bottomLeft, topRight));
                max = Vector2.Max(max, Vector2.Max(bottomLeft, topRight));
            }

            Assert.LessOrEqual(min.x, max.x, "選んでいる行「" + row + "」に見える文字が無い");

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Vector2 panelMin = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 panelMax = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            string where = "選んでいる行の四角 " + min.ToString("F1") + "-" + max.ToString("F1");
            Assert.IsTrue(min.x >= panelMin.x - slackPx && min.y >= panelMin.y - slackPx &&
                          max.x <= panelMax.x + slackPx && max.y <= panelMax.y + slackPx,
                where + " が枠 " + panelMin.ToString("F1") + "-" + panelMax.ToString("F1") + " からはみ出している");
            Assert.IsTrue(min.x >= -slackPx && min.y >= -slackPx &&
                          max.x <= Screen.width + slackPx && max.y <= Screen.height + slackPx,
                where + " が画面 " + Screen.width + "x" + Screen.height + " からはみ出している");
        }

        /// <summary>
        /// 開いているメニューをカメラ経由で RenderTexture に描き、選んでいる行の黄色が画素に出ているかを見る。
        /// 環境変数 KCD_DEV_MENU_SHOT にフォルダがあれば、撮った絵を PNG にして置く。描画のデバイスがあるときだけ呼ぶ。
        /// </summary>
        private static IEnumerator CaptureMenu(DevWarpMenu menu)
        {
            string folder = Environment.GetEnvironmentVariable(ShotEnvironmentVariable);
            Canvas canvas = menu.Canvas;
            Camera source = Camera.main;
            Assert.IsNotNull(source, "メニューを撮るカメラが無い");

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("DevMenuShotCamera");
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            Color32[] pixels;
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.CopyFrom(source);
                cam.targetTexture = rt;
                cam.enabled = false;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                yield return null;

                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                pixels = tex.GetPixels32();
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "dev-warp-menu.png"), tex.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(go);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }

            // メニューの暗い地の上に黄色い選択行が描かれていれば、その色の画素がある。
            bool hasYellow = false;
            for (int i = 0; i < pixels.Length && !hasYellow; i++)
            {
                hasYellow = IsSelectionYellow(pixels[i]);
            }

            Assert.IsTrue(hasYellow, "撮ったメニューに選択行の色が無い（描けていない）");
        }
    }
}
