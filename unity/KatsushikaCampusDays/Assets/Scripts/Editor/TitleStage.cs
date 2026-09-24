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
            BuildCamera(root);
            BuildLighting(root);

            Transform[] stands = BuildStands(root);
            BuildUI(root, stands);
            AudioFactory.Place(root);
            PostProcessFactory.Place(root, false);
        }

        private static void BuildCamera(Transform root)
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
        private static void BuildUI(Transform root, Transform[] stands)
        {
            Canvas canvas = UIFactory.CreateCanvas(root, "TitleCanvas", 0);
            UIFactory.EnsureEventSystem(root);

            var canvasRect = (RectTransform)canvas.transform;
            GameObject host = canvas.gameObject;

            RectTransform titleRoot = UIFactory.Stretch(canvasRect, "TitleRoot");
            RectTransform titleBox = UIFactory.Rect(titleRoot, "Title", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1400f, 160f));
            TMP_Text title = UIFactory.Label(
                titleBox, "Label", "葛飾キャンパスデイズ", 96f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            RectTransform subtitleBox = UIFactory.Rect(titleRoot, "Subtitle", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -280f), new Vector2(1400f, 60f));
            TMP_Text subtitle = UIFactory.Label(
                subtitleBox, "Label", "東京理科大学 葛飾キャンパス 探索記", 34f, TextAlignmentOptions.Center);

            RectTransform promptBox = UIFactory.Rect(titleRoot, "Prompt", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1200f, 64f));
            TMP_Text prompt = UIFactory.Label(
                promptBox, "Label", "Enter ではじめる", 36f, TextAlignmentOptions.Center);

            RectTransform selectRoot = UIFactory.Stretch(canvasRect, "SelectRoot");
            RectTransform nameBox = UIFactory.Rect(selectRoot, "Name", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(900f, 80f));
            TMP_Text nameLabel = UIFactory.Label(
                nameBox, "Label", string.Empty, 56f, TextAlignmentOptions.Center);
            nameLabel.fontStyle = FontStyles.Bold;

            RectTransform taglineBox = UIFactory.Rect(selectRoot, "Tagline", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 168f), new Vector2(1200f, 60f));
            TMP_Text tagline = UIFactory.Label(
                taglineBox, "Label", string.Empty, 32f, TextAlignmentOptions.Center);

            RectTransform hintBox = UIFactory.Rect(selectRoot, "Hint", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1200f, 56f));
            TMP_Text hint = UIFactory.Label(
                hintBox, "Label", string.Empty, 30f, TextAlignmentOptions.Center);

            CharacterSelect select = host.AddComponent<CharacterSelect>();
            select.Bind(stands, nameLabel, tagline, hint);

            TitleMenu menu = host.AddComponent<TitleMenu>();
            menu.Bind(titleRoot.gameObject, selectRoot.gameObject, prompt, select);

            SettingsView settings = UIFactory.BuildSettings(host, canvasRect);
            CreditsView credits = UIFactory.BuildCredits(host, canvasRect);
            menu.BindExtras(settings, credits);
            menu.BindHeadings(title, subtitle);
        }
    }
}
