using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// タイトル画面。3 人がターンテーブルに並び、左右で選んで Enter で決める。
    /// </summary>
    public static class TitleStage
    {
        private static readonly float[] StandX = { -1.95f, 0f, 1.95f };

        /// <summary>タイトルシーンの中身を root の下に組む。</summary>
        public static void Build(Transform root)
        {
            Camera camera = BuildCamera(root);
            BuildLighting(root);

            Transform[] stands = BuildStands(root);
            BuildUI(root, stands, camera);
            AudioFactory.Place(root);
            PostProcessFactory.Place(root, false);
        }

        /// <summary>
        /// 横長の PC 画面に合わせて置く。指で遊ぶ画面と縦長の画面では TitleLayout が画角と位置を決め直すので、
        /// PortraitFieldOfView は付けない（付けると縦長の画面で画角を取り合う）。
        /// </summary>
        private static Camera BuildCamera(Transform root)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(
                new Vector3(0f, 1.45f, -4.4f), Quaternion.Euler(6f, 0f, 0f));

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.13f, 0.2f, 1f);
            go.AddComponent<AudioListener>();
            PostProcessFactory.EnableOnCamera(camera);
            return camera;
        }

        /// <summary>
        /// キー光は斜め上の前方から当てて頬と鼻筋に陰影を作り、逆側の後方から弱いリム光で輪郭を抜く。
        /// 正面から平らに当てると 2 階調が出ずのっぺりする。
        /// </summary>
        private static void BuildLighting(Transform root)
        {
            var key = new GameObject("KeyLight");
            key.transform.SetParent(root, false);
            key.transform.rotation = Quaternion.Euler(48f, 32f, 0f);

            Light keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.97f, 0.92f);
            keyLight.intensity = 1.35f;
            keyLight.shadows = LightShadows.Soft;

            var fill = new GameObject("FillLight");
            fill.transform.SetParent(root, false);
            fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.62f, 0.76f, 1f);
            fillLight.intensity = 0.22f;
            fillLight.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.31f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.20f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.11f);
            RenderSettings.fog = false;
        }

        /// <summary>選択できる 3 人。CharacterSelect が位置・大きさ・回転を動かす。</summary>
        private static Transform[] BuildStands(Transform root)
        {
            var group = new GameObject("Stands");
            group.transform.SetParent(root, false);

            var stands = new Transform[GameManager.PlayableCharacterIds.Length];

            for (int i = 0; i < stands.Length; i++)
            {
                string characterId = GameManager.PlayableCharacterIds[i];
                var stand = new GameObject("Stand_" + characterId);
                stand.transform.SetParent(group.transform, false);
                stand.transform.localPosition = new Vector3(
                    i < StandX.Length ? StandX[i] : i * 1.95f, 0f, 1.6f);

                // FBX の正面は +Z（Campus では進行方向 = root の +Z と一致する）。
                // タイトルカメラは -Z 側から +Z を見るので 180° 回して顔をカメラへ向ける。
                // Animator の付いた Body 自身の回転は Humanoid が毎フレーム書き戻すので、
                // 台座と Body の間に向き専用の親（Facing）を挟んでそちらを回す。
                // 台座（stand）の回転は CharacterSelect が毎フレーム上書きするので、ここでは触らない。
                var facing = new GameObject("Facing");
                facing.transform.SetParent(stand.transform, false);
                facing.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                ActorFactory.CreateBody(characterId, facing.transform);
                BuildPedestal(stand.transform);
                stands[i] = stand.transform;
            }

            return stands;
        }

        /// <summary>足元の円盤。浮いて見えないようにする。</summary>
        private static void BuildPedestal(Transform parent)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Pedestal";
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            disc.transform.localScale = new Vector3(1.1f, 0.04f, 1.1f);

            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.EnsureCampus("stone_light");
            // stone_light には敷石の写真が貼られる。円柱の UV は面ごとに 0..1 なので、実寸にそろえる。
            CampusSurfaces.UseTopDownUv(disc.GetComponent<MeshFilter>(), disc.transform.localScale);
        }

        /// <summary>タイトル表示と選択画面。どちらも同じ Canvas に置き、TitleMenu が切り替える。</summary>
        private static void BuildUI(Transform root, Transform[] stands, Camera camera)
        {
            Canvas canvas = UIFactory.CreateCanvas(root, "TitleCanvas", 0);
            UIFactory.EnsureEventSystem(root);

            var canvasRect = (RectTransform)canvas.transform;
            GameObject host = canvas.gameObject;

            var top = new Vector2(0.5f, 1f);
            var bottom = new Vector2(0.5f, 0f);

            RectTransform titleRoot = UIFactory.Stretch(canvasRect, "TitleRoot");
            RectTransform titleBox = Box(titleRoot, "Title", TitleLayout.Part.Title, top);
            TMP_Text title = Text(titleBox, "葛飾キャンパスデイズ", TitleLayout.Part.Title);
            title.fontStyle = FontStyles.Bold;

            RectTransform subtitleBox = Box(titleRoot, "Subtitle", TitleLayout.Part.Subtitle, top);
            TMP_Text subtitle = Text(subtitleBox, "東京理科大学 葛飾キャンパス 探索記", TitleLayout.Part.Subtitle);

            RectTransform promptBox = Box(titleRoot, "Prompt", TitleLayout.Part.Prompt, bottom);
            TMP_Text prompt = Text(promptBox, "Enter ではじめる", TitleLayout.Part.Prompt);

            RectTransform selectRoot = UIFactory.Stretch(canvasRect, "SelectRoot");
            RectTransform nameBox = Box(selectRoot, "Name", TitleLayout.Part.Name, bottom);
            TMP_Text nameLabel = Text(nameBox, string.Empty, TitleLayout.Part.Name);
            nameLabel.fontStyle = FontStyles.Bold;

            RectTransform taglineBox = Box(selectRoot, "Tagline", TitleLayout.Part.Tagline, bottom);
            TMP_Text tagline = Text(taglineBox, string.Empty, TitleLayout.Part.Tagline);

            RectTransform hintBox = Box(selectRoot, "Hint", TitleLayout.Part.Hint, bottom);
            TMP_Text hint = Text(hintBox, string.Empty, TitleLayout.Part.Hint);

            CharacterSelect select = host.AddComponent<CharacterSelect>();
            select.Bind(stands, nameLabel, tagline, hint);
            select.BindTap(UIFactory.AddLinkTapArea(selectRoot, nameLabel));

            TitleMenu menu = host.AddComponent<TitleMenu>();
            menu.Bind(titleRoot.gameObject, selectRoot.gameObject, prompt, select);
            menu.BindTap(UIFactory.AddLinkTapArea(titleRoot, prompt));

            MapAttributionLabel attribution = BuildMapAttribution(canvasRect);

            SettingsView settings = UIFactory.BuildSettings(host, canvasRect);
            CreditsView credits = UIFactory.BuildCredits(host, canvasRect);
            attribution.Avoid(UIFactory.Child(canvasRect, "Settings"), UIFactory.Child(canvasRect, "Credits"));
            menu.BindExtras(settings, credits);
            menu.BindHeadings(title, subtitle);

            host.AddComponent<TitleLayout>().Bind(camera, select, selectRoot.gameObject,
                new[] { titleBox, subtitleBox, promptBox, nameBox, taglineBox, hintBox },
                new[] { title, subtitle, prompt, nameLabel, tagline, hint });
        }

        /// <summary>文字の枠を 1 つ。位置と大きさは TitleLayout の PC の表から取る。</summary>
        private static RectTransform Box(RectTransform parent, string name, TitleLayout.Part part, Vector2 anchor)
        {
            TitleLayout.Slot slot = TitleLayout.SlotFor(TitleLayout.Shape.Desktop, part);
            return UIFactory.Rect(parent, name, anchor, anchor, slot.Position, slot.Size);
        }

        /// <summary>枠いっぱいの文字。幅が足りなければ TitleLayout の表の下限まで小さくする。</summary>
        private static TMP_Text Text(RectTransform box, string text, TitleLayout.Part part)
        {
            TitleLayout.Slot slot = TitleLayout.SlotFor(TitleLayout.Shape.Desktop, part);
            TMP_Text label = UIFactory.Label(box, "Label", text, slot.FontSize, TextAlignmentOptions.Center);
            UIFactory.ReflowLabel(box, label, slot.MinSize);
            return label;
        }

        /// <summary>
        /// 地図データの帰属表示（ODbL）を右下に小さく出す (#20)。TitleRoot と SelectRoot の外に置くので、
        /// タイトルと主人公選びのどちらでも見える。設定とクレジットの枠はこの後に作るのでそちらが上に描かれ、
        /// 開いた枠が文字にかかる間は MapAttributionLabel が文字を隠す (#187)。
        /// 下端から 12〜68 px に収め、いちばん下にある主人公選びの操作説明（下端から 92 px より上）と重ならない。
        /// </summary>
        private static MapAttributionLabel BuildMapAttribution(RectTransform canvasRect)
        {
            RectTransform box = UIFactory.Rect(canvasRect, "MapAttribution", new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-24f, 12f), new Vector2(900f, 56f));
            TMP_Text label = UIFactory.Label(
                box, "Label", MapAttribution.DefaultJa, 22f, TextAlignmentOptions.BottomRight);
            Color ink = label.color;
            label.color = new Color(ink.r, ink.g, ink.b, 0.8f);
            UIFactory.ReflowLabel(box, label, 16f);
            MapAttributionLabel attribution = box.gameObject.AddComponent<MapAttributionLabel>();
            attribution.Bind(label);
            return attribution;
        }
    }
}
