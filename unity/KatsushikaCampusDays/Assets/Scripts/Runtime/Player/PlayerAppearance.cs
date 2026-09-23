using UnityEngine;

namespace KCD
{
    /// <summary>
    /// プレイヤーに焼き込んである体（Body_mirai / Body_botchan / Body_madonna）の中から、
    /// 選ばれたキャラクターの 1 体だけを出す。
    ///
    /// 体はシーンを焼く時点で全員分作ってある（ActorFactory.CreatePlayer）。
    /// 実行時に差し替えるのはここだけの仕事で、NPC やタイトルの回転台には関係しない。
    ///
    /// 実行順 -200 が肝。PlayerAnimatorDriver.Awake（既定の 0）は
    /// GetComponentInChildren&lt;Animator&gt;() / GetComponentInChildren&lt;SitPose&gt;() で体を探すが、
    /// 引数なしの GetComponentInChildren は非アクティブな子を拾わない。
    /// だからこちらが先に走って余分な体を SetActive(false) にしておけば、
    /// ドライバは何も知らないまま選ばれた体の Animator と SitPose を掴む。
    ///
    /// ただし実行順だけに賭けない。Apply は毎回 PlayerAnimatorDriver.RebindAnimator() も呼ぶので、
    /// 万一ドライバが先に走って消える体の Animator を掴んでも、そのあとで掴み直させられる。
    /// （掴み直しは冪等で、先に走ったときは _animator が埋まるだけ。ドライバの Awake は
    /// 「_animator が null なら探す」なので二度手間にもならない。）
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerAppearance : MonoBehaviour
    {
        [SerializeField] private string[] _ids = new string[0];
        [SerializeField] private GameObject[] _bodies = new GameObject[0];

        /// <summary>いま出している体の id。まだ何も出していなければ空文字。</summary>
        public string ActiveCharacterId { get; private set; } = string.Empty;

        /// <summary>SceneBuilder から差し込む。ids と bodies は同じ順に並べる。</summary>
        public void Bind(string[] ids, GameObject[] bodies)
        {
            _ids = ids ?? new string[0];
            _bodies = bodies ?? new GameObject[0];
        }

        private void Awake()
        {
            // 体を差し込まれていないときは GameManager を起こさない（焼き忘れ・テスト時の保険）。
            if (_bodies != null && _bodies.Length > 0)
            {
                Apply(GameManager.Instance.SelectedCharacterId);
            }
        }

        /// <summary>
        /// 選んだ id の体だけを出し、PlayerAnimatorDriver に体を掴み直させる。
        /// 起動時（Awake）とセーブのロード時（SaveSystem.Load）の両方から呼ぶ。
        /// </summary>
        public void Apply(string characterId)
        {
            int index = ResolveIndex(_ids, _bodies, characterId);

            if (_bodies != null)
            {
                for (int i = 0; i < _bodies.Length; i++)
                {
                    GameObject body = _bodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    body.SetActive(i == index);
                }
            }

            ActiveCharacterId = index >= 0 && _ids != null && index < _ids.Length
                ? _ids[index]
                : string.Empty;

            // ドライバが消えた体の Animator を掴んだままだと、歩いても何も動かない。
            // 実行順に関係なく毎回掴み直させる（ドライバがまだ Awake していなくても安全に動く）。
            PlayerAnimatorDriver driver = GetComponent<PlayerAnimatorDriver>();
            if (driver != null)
            {
                driver.RebindAnimator();
            }
        }

        /// <summary>
        /// id に対応する体の添字を返す。
        /// 知らない id のときは最初に使える体を返す（主人公が消えるよりは違う顔で出す方がまし）。
        /// 体が 1 つも無ければ -1。
        /// </summary>
        public static int ResolveIndex(string[] ids, GameObject[] bodies, string characterId)
        {
            if (bodies == null || bodies.Length == 0)
            {
                return -1;
            }

            if (ids != null && !string.IsNullOrEmpty(characterId))
            {
                int count = Mathf.Min(ids.Length, bodies.Length);
                for (int i = 0; i < count; i++)
                {
                    if (ids[i] == characterId && bodies[i] != null)
                    {
                        return i;
                    }
                }
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
