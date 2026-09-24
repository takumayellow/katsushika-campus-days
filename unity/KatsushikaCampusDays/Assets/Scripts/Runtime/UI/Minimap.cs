using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 右上の北固定ミニマップ。Blender で描いた俯瞰図（minimap.png）を円形マスクの中で
    /// プレイヤー中心にずらして見せる。矢印がプレイヤー、点が NPC、輪が追跡中クエストの目的地。
    /// 建物の中にいるあいだは入口を中心にして建物名を出す。
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        [SerializeField] private RectTransform _map;
        [SerializeField] private RectTransform _markerRoot;
        [SerializeField] private RectTransform _playerArrow;
        [SerializeField] private TMP_Text _caption;
        [SerializeField] private Sprite _dotSprite;
        [SerializeField] private Sprite _ringSprite;
        [SerializeField] private Transform _target;

        [SerializeField] private Vector2 _worldCenter;
        [SerializeField] private float _halfExtent = 175f;
        [SerializeField] private float _viewRadiusMeters = 60f;
        [SerializeField] private float _viewRadiusPixels = 142f;
        [SerializeField] private Color _npcColor = new Color(1f, 0.74f, 0.25f, 1f);
        [SerializeField] private Color _objectiveColor = new Color(0.31f, 0.82f, 0.85f, 1f);

        private readonly List<Image> _dots = new List<Image>();
        private NPCTalker[] _npcs = new NPCTalker[0];
        private float _npcRefreshedAt = -10f;
        private Image _objective;
        private bool _indoor;
        private Vector3 _indoorAnchor;

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
        public void Bind(RectTransform map, RectTransform markerRoot, RectTransform playerArrow, TMP_Text caption,
            Sprite dotSprite, Sprite ringSprite)
        {
            _map = map;
            _markerRoot = markerRoot;
            _playerArrow = playerArrow;
            _caption = caption;
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

        /// <summary>建物に入った。地図は入口を中心に固定し、見出しを出す。</summary>
        public void SetIndoor(string caption, Vector3 entranceWorld)
        {
            _indoor = true;
            _indoorAnchor = entranceWorld;
            if (_caption != null)
            {
                _caption.text = caption;
                _caption.gameObject.SetActive(!string.IsNullOrEmpty(caption));
            }
        }

        /// <summary>外に出た。</summary>
        public void ClearIndoor()
        {
            _indoor = false;
            if (_caption != null)
            {
                _caption.gameObject.SetActive(false);
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

            if (_caption != null)
            {
                _caption.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_target == null || _map == null)
            {
                return;
            }

            Vector3 focus = _indoor ? _indoorAnchor : _target.position;
            Vector2 uv = new Vector2(
                (focus.x - _worldCenter.x) / (2f * _halfExtent) + 0.5f,
                (focus.z - _worldCenter.y) / (2f * _halfExtent) + 0.5f);
            _map.anchoredPosition = -(uv - new Vector2(0.5f, 0.5f)) * _map.sizeDelta.x;

            if (_playerArrow != null)
            {
                _playerArrow.localRotation = Quaternion.Euler(0f, 0f, -_target.eulerAngles.y);
                _playerArrow.gameObject.SetActive(!_indoor);
            }

            UpdateNpcDots(focus);
            UpdateObjective(focus);
        }

        private void UpdateNpcDots(Vector3 focus)
        {
            if (Time.unscaledTime - _npcRefreshedAt > 1.5f)
            {
                _npcRefreshedAt = Time.unscaledTime;
                _npcs = FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude);
            }

            int used = 0;
            for (int i = 0; i < _npcs.Length; i++)
            {
                NPCTalker npc = _npcs[i];
                if (npc == null || !npc.isActiveAndEnabled || !OnMap(npc.transform.position))
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
                dot.gameObject.SetActive(true);
            }

            for (int i = used; i < _dots.Count; i++)
            {
                _dots[i].gameObject.SetActive(false);
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

            if (step == null || !QuestObjectiveLocator.TryLocate(step, focus, out Vector3 position) || !OnMap(position))
            {
                _objective.gameObject.SetActive(false);
                return;
            }

            Vector2 offset = Offset(position, focus);
            float limit = _viewRadiusPixels - 16f;
            if (offset.magnitude > limit)
            {
                offset = offset.normalized * limit;
            }

            float pulse = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 4f);
            _objective.rectTransform.anchoredPosition = offset;
            _objective.rectTransform.localScale = Vector3.one * pulse;
            _objective.gameObject.SetActive(true);
        }

        /// <summary>地図の範囲内か。屋内モデルは遠くに置いてあるので、これで自然と外れる。</summary>
        private bool OnMap(Vector3 world)
        {
            return Mathf.Abs(world.x - _worldCenter.x) <= _halfExtent
                && Mathf.Abs(world.z - _worldCenter.y) <= _halfExtent;
        }

        private Vector2 Offset(Vector3 world, Vector3 focus)
        {
            Vector3 delta = world - focus;
            return new Vector2(delta.x, delta.z) * PixelsPerMeter;
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
