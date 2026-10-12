using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトルのキャラクター選択。3 人が回転台に立ち、←→ で切り替え、Enter で決定。
    /// 選ばれていない子はひと回り小さく、後ろに下がって薄くなる。
    /// 指で遊んでいるときは、名前の両脇の ◀ ▶ を叩いて切り替え、名前を叩いて決定 (#187)。
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
        [SerializeField] private TmpLinkTap _tap;

        /// <summary>名前の行の、叩ける部分の id。</summary>
        public const string PrevLink = "prev";

        /// <summary>名前そのもの。叩くと決定。</summary>
        public const string PickLink = "pick";

        /// <summary>右の ▶。</summary>
        public const string NextLink = "next";

        private int _index;
        private bool _active;

        /// <summary>選択を受け付け始めたフレーム。そのフレームの入力は捨てる（#6）。</summary>
        private int _activatedFrame = KCDInput.NoFrame;

        /// <summary>案内を指向けに書いているか。</summary>
        private bool _hintForTouch;

        /// <summary>SceneBuilder から差し込む。stands は左から順の 3 体。</summary>
        public void Bind(Transform[] stands, TMP_Text nameLabel, TMP_Text taglineLabel, TMP_Text hintLabel)
        {
            _stands = stands ?? new Transform[0];
            _nameLabel = nameLabel;
            _taglineLabel = taglineLabel;
            _hintLabel = hintLabel;
        }

        /// <summary>名前の行を叩く受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void OnEnable()
        {
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        private void OnDisable()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }
        }

        private void Start()
        {
            _index = StartIndex(GameManager.Instance.SelectedCharacterId);

            L.LocaleChanged += OnLocaleChanged;
            OnLocaleChanged();
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            _hintForTouch = InputMode.IsTouch;
            if (_hintLabel != null)
            {
                _hintLabel.text = _hintForTouch
                    ? L.Pick("◀ ▶ でえらぶ　名前をタップで決定", "Tap ◀ ▶ to choose   Tap the name to start")
                    : L.Get("ui.select.hint", "← → でえらぶ　Enter で決定　（パッドは十字キーと A ボタン）");
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
            if (InputMode.IsTouch != _hintForTouch)
            {
                OnLocaleChanged();
            }

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
                _index = Step(_index, step, count);
                AudioManager.Instance?.PlayUi("ui_move");
                Refresh();
            }

            if (KCDInput.SubmitPressed)
            {
                Confirm();
            }
        }

        /// <summary>名前の行の id を ←→ の向きに直す。◀ は -1、▶ は +1、それ以外は 0。</summary>
        public static int TapStep(string linkId)
        {
            return linkId == PrevLink ? -1 : linkId == NextLink ? 1 : 0;
        }

        private void OnTapped(string linkId)
        {
            // マウスのクリックでは選ばない（タイトルと同じ）。キーボードの人は ←→ と Enter で選ぶ。
            if (!_active || !InputMode.IsTouch || KCDInput.IgnoresInput(_activatedFrame))
            {
                return;
            }

            if (linkId == PickLink)
            {
                Confirm();
                return;
            }

            int step = TapStep(linkId);
            int count = Mathf.Min(_stands.Length, GameManager.PlayableCharacterIds.Length);
            if (step != 0 && count > 0)
            {
                _index = Step(_index, step, count);
                AudioManager.Instance?.PlayUi("ui_move");
                Refresh();
            }
        }

        /// <summary>選択画面を開いたときに選んでおく子。いまのキャラの位置、知らない id なら先頭。</summary>
        public static int StartIndex(string selectedCharacterId)
        {
            return Mathf.Max(0, System.Array.IndexOf(GameManager.PlayableCharacterIds, selectedCharacterId));
        }

        /// <summary>←→ で 1 人ずらした先。端から先へ進むと反対の端に回り込む。</summary>
        public static int Step(int index, int step, int count)
        {
            return (index + step + count) % count;
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
                _nameLabel.text = "<link=\"" + PrevLink + "\"><color=#FFD98A>◀</color></link>　" +
                                  "<link=\"" + PickLink + "\">" + name + "</link>　" +
                                  "<link=\"" + NextLink + "\"><color=#FFD98A>▶</color></link>" +
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
