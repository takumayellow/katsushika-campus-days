using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトルのキャラクター選択。3 人が回転台に立ち、←→ で切り替え、Enter で決定。
    /// 選ばれていない子はひと回り小さく、後ろに下がって薄くなる。
    /// </summary>
    public sealed class CharacterSelect : MonoBehaviour
    {
        private static readonly string[] Taglines =
        {
            "工学部情報工学科 2 年。明るくて好奇心旺盛。",
            "東京物理学校を出た数学教師。真っ直ぐで喧嘩っ早い。",
            "明治・大正の女学生。おっとり上品。"
        };

        [SerializeField] private Transform[] _stands = new Transform[0];
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _taglineLabel;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private float _turnSpeed = 8f;
        [SerializeField] private float _selectedScale = 1f;
        [SerializeField] private float _idleScale = 0.78f;
        [SerializeField] private float _idleDepth = 1.6f;

        private int _index;
        private bool _active;

        /// <summary>SceneBuilder から差し込む。stands は左から順の 3 体。</summary>
        public void Bind(Transform[] stands, TMP_Text nameLabel, TMP_Text taglineLabel, TMP_Text hintLabel)
        {
            _stands = stands ?? new Transform[0];
            _nameLabel = nameLabel;
            _taglineLabel = taglineLabel;
            _hintLabel = hintLabel;
        }

        private void Start()
        {
            _index = Mathf.Max(0, System.Array.IndexOf(
                GameManager.PlayableCharacterIds, GameManager.Instance.SelectedCharacterId));

            L.LocaleChanged += OnLocaleChanged;
            OnLocaleChanged();
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            if (_hintLabel != null)
            {
                _hintLabel.text = L.Get("ui.select.hint", "← → で選択　Enter で決定");
            }

            Refresh();
        }

        /// <summary>選択を受け付けるかどうか。タイトル表示中は止めておく。</summary>
        public void SetActiveSelection(bool active)
        {
            _active = active;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            AnimateStands();

            if (!_active)
            {
                return;
            }

            int step = KCDInput.MenuHorizontal;
            int count = Mathf.Min(_stands.Length, GameManager.PlayableCharacterIds.Length);
            if (step != 0 && count > 0)
            {
                _index = (_index + step + count) % count;
                AudioManager.Instance?.PlayUi("ui_move");
                Refresh();
            }

            if (KCDInput.SubmitPressed)
            {
                Confirm();
            }
        }

        private void AnimateStands()
        {
            float deltaTime = Time.unscaledDeltaTime;

            for (int i = 0; i < _stands.Length; i++)
            {
                Transform stand = _stands[i];
                if (stand == null)
                {
                    continue;
                }

                bool selected = i == _index;

                // 選ばれている子だけがゆっくり回る。
                if (selected)
                {
                    stand.Rotate(Vector3.up, 16f * deltaTime, Space.Self);
                }
                else
                {
                    stand.localRotation = Quaternion.Slerp(
                        stand.localRotation, Quaternion.identity, _turnSpeed * deltaTime);
                }

                float targetScale = selected ? _selectedScale : _idleScale;
                stand.localScale = Vector3.Lerp(
                    stand.localScale, Vector3.one * targetScale, _turnSpeed * deltaTime);

                Vector3 position = stand.localPosition;
                float targetZ = selected ? 0f : _idleDepth;
                position.z = Mathf.Lerp(position.z, targetZ, _turnSpeed * deltaTime);
                stand.localPosition = position;
            }
        }

        private void Refresh()
        {
            if (_index < 0 || _index >= GameManager.PlayableCharacterIds.Length)
            {
                return;
            }

            string id = GameManager.PlayableCharacterIds[_index];
            if (_nameLabel != null)
            {
                _nameLabel.text = L.Get("ui.select." + id + ".name", GameManager.PlayableCharacterNames[_index]);
            }

            if (_taglineLabel != null && _index < Taglines.Length)
            {
                _taglineLabel.text = L.Get("ui.select." + id + ".desc", Taglines[_index]);
            }
        }

        private void Confirm()
        {
            if (_index < 0 || _index >= GameManager.PlayableCharacterIds.Length)
            {
                return;
            }

            _active = false;
            AudioManager.Instance?.PlayUi("ui_confirm");
            GameManager.Instance.SelectedCharacterId = GameManager.PlayableCharacterIds[_index];
            GameManager.Instance.EnterCampus();
        }
    }
}
