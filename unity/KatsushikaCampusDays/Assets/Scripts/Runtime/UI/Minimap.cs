using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 右上の北固定ミニマップ。Blender で描いた俯瞰図（minimap.png）を円形マスクの中で
    /// プレイヤー中心にずらして見せる。矢印がプレイヤー、点が NPC、輪が追跡中クエストの目的地。
    /// いま話しかけると依頼を受けられる人の点は、桃色で少し大きく出す (#410)。
    /// 目的地の案内はここだけに出す（画面の上には出さない。#163）。
    /// 建物の中にいるあいだはキャンパスの絵を隠して建物名を出し、屋内の目的地と NPC をプレイヤーからの向きどおりに出す。
    /// 目的地が外にあるときは、その建物の出口を指す。
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        [SerializeField] private RectTransform _map;
        [SerializeField] private RectTransform _markerRoot;
        [SerializeField] private RectTransform _playerArrow;
        [SerializeField] private TMP_Text _caption;
        [SerializeField] private GameObject _captionPanel;
        [SerializeField] private TMP_Text _northLabel;
        [SerializeField] private Sprite _dotSprite;
        [SerializeField] private Sprite _ringSprite;
        [SerializeField] private Transform _target;

        [SerializeField] private Vector2 _worldCenter;
        [SerializeField] private float _halfExtent = 175f;
        [SerializeField] private float _viewRadiusMeters = 60f;
        [SerializeField] private float _viewRadiusPixels = 142f;
        [SerializeField] private Color _npcColor = new Color(1f, 0.74f, 0.25f, 1f);
        [SerializeField] private Color _objectiveColor = new Color(0.31f, 0.82f, 0.85f, 1f);
        [SerializeField] private Color _questGiverColor = new Color(1f, 0.42f, 0.78f, 1f);

        private readonly List<Image> _dots = new List<Image>();
        private NPCTalker[] _npcs = new NPCTalker[0];

        /// <summary>_npcs と同じ並びで、いま話しかけると依頼を受けられるか (#410)。</summary>
        private bool[] _offers = new bool[0];
        private float _npcRefreshedAt = -10f;
        private bool _wasTalking;
        private Image _objective;
        private bool _indoor;

        /// <summary>現在のシーンのミニマップ。</summary>
        public static Minimap Instance { get; private set; }

        /// <summary>追従対象。SceneBuilder がプレイヤーを差し込む。</summary>
        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        private float PixelsPerMeter => _viewRadiusPixels / Mathf.Max(1f, _viewRadiusMeters);

        /// <summary>SceneBuilder から差し込む。</summary>
        /// <param name="captionPanel">見出しの背景ごとの親。外では背景も消す (#181)。null なら文字だけを出し入れする。</param>
        public void Bind(RectTransform map, RectTransform markerRoot, RectTransform playerArrow, TMP_Text caption,
            Sprite dotSprite, Sprite ringSprite, GameObject captionPanel = null, TMP_Text northLabel = null)
        {
            _northLabel = northLabel;
            _map = map;
            _markerRoot = markerRoot;
            _playerArrow = playerArrow;
            _caption = caption;
            _captionPanel = captionPanel;
            _dotSprite = dotSprite;
            _ringSprite = ringSprite;
        }

        /// <summary>地図画像とワールド座標の対応。minimap.json の値をそのまま渡す。</summary>
        public void Configure(Vector2 worldCenter, float halfExtent, float viewRadiusMeters, float viewRadiusPixels)
        {
            _worldCenter = worldCenter;
            _halfExtent = Mathf.Max(1f, halfExtent);
            _viewRadiusMeters = Mathf.Max(1f, viewRadiusMeters);
            _viewRadiusPixels = Mathf.Max(1f, viewRadiusPixels);
        }

        // 見出し(建物と階の名前)と「N」の、組み立てたときの値。指か縦長の画面で文字を大きくして、戻すときに使う (#187)。
        private const float CaptionHeight = 44f;
        private const float CaptionWidth = 300f;
        private const float CaptionWidthMax = 450f;
        private const float CaptionPadding = 24f;
        private const float CaptionMinFont = 22f;
        private int _textKey = -1;
        private float _captionBaseSize;
        private float _northBaseSize;

        /// <summary>文字を今の向きの大きさにする。縦横や指とマウスの切り替えにも追従する。</summary>
        private void ApplyTextSize()
        {
            bool touch = InputMode.IsTouch;
            bool portrait = ScreenShape.IsPortrait;
            int key = HudTextScale.Key(touch, portrait);
            if (key == _textKey)
            {
                return;
            }

            if (_northLabel == null && _playerArrow != null && _playerArrow.parent != null)
            {
                Transform north = _playerArrow.parent.Find("North/Label");
                _northLabel = north != null ? north.GetComponent<TMP_Text>() : null;
            }

            if (_textKey < 0)
            {
                _captionBaseSize = _caption != null ? _caption.fontSize : 0f;
                _northBaseSize = _northLabel != null ? _northLabel.fontSize : 0f;
            }

            _textKey = key;
            if (_northLabel != null)
            {
                HudTextScale.FitRow(_northLabel, HudTextScale.Size(_northBaseSize, touch, portrait), CaptionHeight);
            }

            ApplyCaptionSize(touch, portrait);
        }

        /// <summary>
        /// 見出しの文字と枠。大きくした文字が 1 行で入るよう、枠を左へ広げる
        /// （右端は画面の端の余白に収める。左は円の外で空いている）。入らない長さは 22 まで縮める。
        /// </summary>
        private void ApplyCaptionSize(bool touch, bool portrait)
        {
            if (_caption == null)
            {
                return;
            }

            float size = HudTextScale.Size(_captionBaseSize, touch, portrait);
            _caption.fontSize = size;
            _caption.enableAutoSizing = false;
            RectTransform label = _caption.rectTransform;
            label.offsetMin = new Vector2(label.offsetMin.x, 0f);
            label.offsetMax = new Vector2(label.offsetMax.x, 0f);

            RectTransform panel = label.parent as RectTransform;
            if (panel == null)
            {
                return;
            }

            if (!HudTextScale.Shrunk(touch, portrait))
            {
                SetCaptionBox(panel, CaptionWidth, false);
                _caption.textWrappingMode = TextWrappingModes.Normal;
                return;
            }

            float width = CaptionWidth;
            if (!string.IsNullOrEmpty(_caption.text))
            {
                width = Mathf.Clamp(_caption.GetPreferredValues(_caption.text, 0f, 0f).x + CaptionPadding,
                    CaptionWidth, CaptionWidthMax);
            }

            SetCaptionBox(panel, width, true);
            _caption.enableAutoSizing = true;
            _caption.fontSizeMax = size;
            _caption.fontSizeMin = Mathf.Min(CaptionMinFont, size);
            _caption.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void SetCaptionBox(RectTransform panel, float width, bool anchorRight)
        {
            panel.sizeDelta = new Vector2(width, CaptionHeight);
            if (anchorRight)
            {
                // ミニマップの右端にそろえ、画面の端の余白 40 のうち 30 まで右へ出す。
                panel.anchorMin = new Vector2(1f, 0f);
                panel.anchorMax = new Vector2(1f, 0f);
                panel.pivot = new Vector2(1f, 1f);
                panel.anchoredPosition = new Vector2(30f, -6f);
            }
            else
            {
                panel.anchorMin = new Vector2(0.5f, 0f);
                panel.anchorMax = new Vector2(0.5f, 0f);
                panel.pivot = new Vector2(0.5f, 1f);
                panel.anchoredPosition = new Vector2(0f, -6f);
            }
        }

        /// <summary>建物に入った。キャンパスの絵を隠し、見出しを出す。</summary>
        public void SetIndoor(string caption)
        {
            _indoor = true;
            if (_map != null)
            {
                _map.gameObject.SetActive(false);
            }

            if (_caption != null)
            {
                _caption.text = caption;
                if (_textKey >= 0)
                {
                    ApplyCaptionSize(InputMode.IsTouch, ScreenShape.IsPortrait);
                }
            }

            ShowCaption(!string.IsNullOrEmpty(caption));
        }

        /// <summary>外に出た。</summary>
        public void ClearIndoor()
        {
            _indoor = false;
            if (_map != null)
            {
                _map.gameObject.SetActive(true);
            }

            ShowCaption(false);
        }

        /// <summary>見出しを背景ごと出し入れする。</summary>
        private void ShowCaption(bool visible)
        {
            GameObject panel = _captionPanel != null ? _captionPanel : _caption != null ? _caption.gameObject : null;
            if (panel != null)
            {
                panel.SetActive(visible);
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (_map == null || _markerRoot == null)
            {
                Debug.LogError("[KCD] Minimap の参照が張られていません。");
                enabled = false;
                return;
            }

            float mapPixels = 2f * _halfExtent * PixelsPerMeter;
            _map.sizeDelta = new Vector2(mapPixels, mapPixels);

            _objective = CreateMarker("Objective", _ringSprite, 26f, _objectiveColor);
            _objective.gameObject.SetActive(false);

            ShowCaption(false);
        }

        private void LateUpdate()
        {
            ApplyTextSize();

            if (_target == null || _map == null)
            {
                return;
            }

            Vector3 focus = _target.position;
            if (!_indoor)
            {
                Vector2 uv = new Vector2(
                    (focus.x - _worldCenter.x) / (2f * _halfExtent) + 0.5f,
                    (focus.z - _worldCenter.y) / (2f * _halfExtent) + 0.5f);
                _map.anchoredPosition = -(uv - new Vector2(0.5f, 0.5f)) * _map.sizeDelta.x;
            }

            if (_playerArrow != null)
            {
                _playerArrow.localRotation = Quaternion.Euler(0f, 0f, -_target.eulerAngles.y);
            }

            UpdateNpcDots(focus);
            UpdateObjective(focus);
        }

        private void OnEnable()
        {
            // 会話の間は HUD ごと隠れる。戻ったら NPC を取り直し、受けた依頼の色をすぐ戻す。
            _npcRefreshedAt = -10f;
        }

        private void UpdateNpcDots(Vector3 focus)
        {
            // 会話を閉じたときも取り直す（依頼を受けた人の色を、1.5 秒の取り直しを待たずに戻す）。
            bool talking = DialogueSystem.Instance != null && DialogueSystem.Instance.IsPlaying;
            if (_wasTalking && !talking)
            {
                _npcRefreshedAt = -10f;
            }

            _wasTalking = talking;

            if (Time.unscaledTime - _npcRefreshedAt > 1.5f)
            {
                _npcRefreshedAt = Time.unscaledTime;
                _npcs = FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude);
                RefreshOffers();
            }

            // 地図の範囲では絞らない。屋内では屋内の NPC を出したいので、プレイヤーからの距離だけで決める。
            // 屋内の模型はキャンパスから遠くに置いてあるので、外にいるときは円の外になって出ない。
            int used = 0;
            for (int i = 0; i < _npcs.Length; i++)
            {
                NPCTalker npc = _npcs[i];
                if (npc == null || !npc.isActiveAndEnabled)
                {
                    continue;
                }

                Vector2 offset = Offset(npc.transform.position, focus);
                if (offset.magnitude > _viewRadiusPixels - 6f)
                {
                    continue;
                }

                while (_dots.Count <= used)
                {
                    _dots.Add(CreateMarker("Npc", _dotSprite, 12f, _npcColor));
                }

                Image dot = _dots[used++];
                dot.rectTransform.anchoredPosition = offset;
                Paint(dot, i < _offers.Length && _offers[i]);
                dot.gameObject.SetActive(true);
            }

            for (int i = used; i < _dots.Count; i++)
            {
                _dots[i].gameObject.SetActive(false);
            }
        }

        /// <summary>いま話しかけると依頼を受けられる人か、NPC ごとに調べ直す (#410)。</summary>
        private void RefreshOffers()
        {
            if (_offers.Length != _npcs.Length)
            {
                _offers = new bool[_npcs.Length];
            }

            DialogueSystem dialogue = DialogueSystem.Instance;
            for (int i = 0; i < _npcs.Length; i++)
            {
                _offers[i] = dialogue != null && _npcs[i] != null && dialogue.OffersQuest(_npcs[i].NpcId);
            }
        }

        /// <summary>
        /// 点は NPC の並びで使い回すので、毎回その人に合わせて塗る。依頼を受けられる人は色を変え、少し大きくする（色だけに頼らない）。
        /// </summary>
        private void Paint(Image dot, bool offersQuest)
        {
            dot.color = offersQuest ? _questGiverColor : _npcColor;
            float size = offersQuest ? 16f : 12f;
            if (dot.rectTransform.sizeDelta.x != size)
            {
                dot.rectTransform.sizeDelta = new Vector2(size, size);
            }
        }

        private void UpdateObjective(Vector3 focus)
        {
            if (_objective == null)
            {
                return;
            }

            QuestData quest = GameManager.Instance.Quests?.TrackedQuest;
            QuestStep step = quest?.CurrentStep;

            string building = InteriorLoader.Instance != null ? InteriorLoader.Instance.CurrentId : null;
            if (step == null || !TryGoal(step, focus, building, out Vector3 position))
            {
                _objective.gameObject.SetActive(false);
                return;
            }

            Vector2 offset = PlaceMarker(position, focus, PixelsPerMeter, _viewRadiusPixels - 16f);
            float pulse = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 4f);
            _objective.rectTransform.anchoredPosition = offset;
            _objective.rectTransform.localScale = Vector3.one * pulse;
            _objective.gameObject.SetActive(true);
        }

        /// <summary>
        /// 輪を地図の中心からどこに置くか（地図の単位）。円の縁（limit）より外なら、向きを保って縁に寄せる。
        /// </summary>
        public static Vector2 PlaceMarker(Vector3 goal, Vector3 focus, float pixelsPerMeter, float limit)
        {
            Vector3 delta = goal - focus;
            var offset = new Vector2(delta.x, delta.z) * pixelsPerMeter;
            return offset.magnitude > limit ? offset.normalized * limit : offset;
        }

        /// <summary>
        /// 追跡中のステップの目的地。外にいるときは、キャンパスの外（屋内の模型の中）の地点は出さない。
        /// 屋内にいて目的地がキャンパスにあるなら、いまいる建物（building）の出口を指す。
        /// </summary>
        public bool TryGoal(QuestStep step, Vector3 from, string building, out Vector3 goal)
        {
            if (!QuestObjectiveLocator.TryLocate(step, from, out goal))
            {
                return false;
            }

            if (!_indoor)
            {
                return IsOnMap(goal);
            }

            if (!IsOnMap(goal))
            {
                return true;
            }

            return QuestObjectiveLocator.TryLocateExit(building, from, out goal);
        }

        /// <summary>地図の範囲内（キャンパス）か。屋内モデルは遠くに置いてあるので、これで自然と外れる。</summary>
        public bool IsOnMap(Vector3 world)
        {
            return Mathf.Abs(world.x - _worldCenter.x) <= _halfExtent
                && Mathf.Abs(world.z - _worldCenter.y) <= _halfExtent;
        }

        private Vector2 Offset(Vector3 world, Vector3 focus)
        {
            return PlaceMarker(world, focus, PixelsPerMeter, float.MaxValue);
        }

        private Image CreateMarker(string name, Sprite sprite, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(_markerRoot, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
