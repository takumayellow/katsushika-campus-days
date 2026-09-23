#if KCD_CINEMACHINE
using Unity.Cinemachine;
#endif
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

        /// <summary>カメラの既定値（注視点 1.47 m・軌道半径 4.2 m）が前提にしている背丈。ActorFactory.BodyHeight と同じ。</summary>
        public const float ReferenceHeight = 1.6f;

        /// <summary>注視点の高さ / 背丈。ActorFactory.CreateAim と同じ比。</summary>
        public const float AimRatio = 0.92f;

        /// <summary>背丈の違いをカメラの距離にどれだけ映すか（1 で背丈に比例、0 で固定）。</summary>
        public const float RadiusFollow = 0.7f;

        /// <summary>カメラの注視点。プレイヤー直下の CameraAim（ActorFactory.CreateAim）。</summary>
        public const string AimName = "CameraAim";

        private float _baseRadius = -1f;

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

            if (index >= 0)
            {
                FitCamera(MeasureHeight(_bodies[index], transform.position.y));
            }
        }

        /// <summary>
        /// 背丈に合わせてカメラの注視点の高さと距離を決める。
        /// カメラは 1.6 m の人に合わせて焼いてあり、背丈 1.15 m の坊っちゃん・マドンナちゃん
        /// （公式マスコットの 2.8〜3 頭身）では注視点が頭より 30 cm 以上も上に来て、
        /// 画面の下に小さく映っていた。距離は背丈に比例させきらず RadiusFollow ぶんだけ寄せる
        /// （寄せすぎると周りが見えなくなる。1.15 m で 0.80 倍、画面上の大きさは 1.6 m の 9 割）。
        /// 背丈が測れないとき（0 以下・NaN）は既定のまま。
        /// </summary>
        public static void Framing(float height, out float aimY, out float radiusScale)
        {
            if (!(height > 0.3f))
            {
                height = ReferenceHeight;
            }

            aimY = height * AimRatio;
            radiusScale = Mathf.Clamp(1f - RadiusFollow * (1f - height / ReferenceHeight), 0.6f, 1.3f);
        }

        /// <summary>体の Renderer を合わせた高さ（足元 groundY から天辺まで）。Renderer が無ければ 0。</summary>
        public static float MeasureHeight(GameObject body, float groundY)
        {
            if (body == null)
            {
                return 0f;
            }

            bool any = false;
            float top = float.NegativeInfinity;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                // 輪郭シェル（CharacterImporter が無効にしている）は体より一回り大きいので数えない。
                if (!r.enabled)
                {
                    continue;
                }

                top = Mathf.Max(top, r.bounds.max.y);
                any = true;
            }

            return any ? top - groundY : 0f;
        }

        private void FitCamera(float height)
        {
            Framing(height, out float aimY, out float radiusScale);

            Transform aim = transform.Find(AimName);
            if (aim != null)
            {
                Vector3 p = aim.localPosition;
                aim.localPosition = new Vector3(p.x, aimY, p.z);
            }

#if KCD_CINEMACHINE
            CinemachineOrbitalFollow orbital = FindAnyObjectByType<CinemachineOrbitalFollow>();
            if (orbital != null)
            {
                // ロードで Apply し直しても縮みが重ならないよう、最初に見た半径を基準にする。
                if (_baseRadius <= 0f)
                {
                    _baseRadius = orbital.Radius;
                }

                orbital.Radius = _baseRadius * radiusScale;
            }
#endif
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
