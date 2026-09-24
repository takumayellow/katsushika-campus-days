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

        /// <summary>選択を受け付け始めたフレーム。そのフレームの入力は捨てる（#6）。</summary>
        private int _activatedFrame = KCDInput.NoFrame;

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
                _hintLabel.text = L.Get(
                    "ui.select.hint", "← → でえらぶ　Enter で決定　（パッドは十字キーと A ボタン）");
            }

            Refresh();
        }

        /// <summary>選択を受け付けるかどうか。タイトル表示中は止めておく。</summary>
        public void SetActiveSelection(bool active)
        {
            _active = active;
            _activatedFrame = active ? Time.frameCount : KCDInput.NoFrame;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            AnimateStands();

            // タイトルで押した Enter を同じフレームでこちらも拾うと、その場で既定のキャラで決定してしまい、
            // 選択画面が 1 フレームも操作できない（「キャラ選択が出ない」の正体, #6）。
            if (!_active || KCDInput.IgnoresInput(_activatedFrame))
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
                // 左右に人がいることが名前だけでは分からないので、矢印と「2 / 3」を添える（#6）。
                string name = L.Get("ui.select." + id + ".name", GameManager.PlayableCharacterNames[_index]);
                _nameLabel.text = "<color=#FFD98A>◀</color>　" + name + "　<color=#FFD98A>▶</color>" +
                                  "<size=45%>　" + (_index + 1) + " / " +
                                  GameManager.PlayableCharacterIds.Length + "</size>";
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

            // ここが「はじめから」の唯一の入口（「つづきから」は F9 でキャラ選択を通らない）。
            // 前の周回の時刻・日付・探索の記録・クエストをここで捨てる (#53)。
            // タイトルではなく決定の瞬間に呼ぶので、あとから「キャラ選択を Esc で取りやめる」を足しても、
            // 取りやめた時点では進行がまだ消えていない。
            GameManager.Instance.BeginNewGame();
            GameManager.Instance.SelectedCharacterId = GameManager.PlayableCharacterIds[_index];
            GameManager.Instance.EnterCampus();
        }
    }
}
