using UnityEngine;

namespace KCD
{
    /// <summary>
    /// ピアノの椅子に座ったあとの演奏。ピアノの曲を鳴らして BGM を絞り、両手を鍵盤の上で軽く上下させる。
    /// 立つ（SittingChanged(false)）とどの時点でも止まる。曲が終わったら座ったまま手を膝へ戻す。
    /// SeatInteractable.Performance に繋ぐと、その椅子の操作が「ピアノを弾く」になる。
    /// </summary>
    public sealed class PianoPerformance : MonoBehaviour
    {
        public const string DefaultClipId = "piano_library";

        /// <summary>立ったときに座面の前から出る距離 (m)。負 = 後ろ（鍵盤から離れる側）へ下がる。</summary>
        public const float StandForward = -0.8f;

        [SerializeField] private Transform _keys;
        [SerializeField] private string _clipId = DefaultClipId;

        private PlayerController _player;
        private SitPose _pose;
        private float _startTime;

        /// <summary>手を置く鍵盤の手前の縁（ワールド座標）。</summary>
        public Transform Keys
        {
            get => _keys;
            set => _keys = value;
        }

        public string ClipId
        {
            get => _clipId;
            set => _clipId = value;
        }

        public bool IsPlaying { get; private set; }

        /// <summary>座った直後に呼ぶ。曲が無ければ座るだけ。</summary>
        public void Begin(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            Detach();
            _player = player;
            _player.SittingChanged += OnSittingChanged;
            _pose = player.GetComponentInChildren<SitPose>();

            AudioManager audio = AudioManager.Instance;
            if (audio == null || !audio.PlayPiano(_clipId))
            {
                return;
            }

            IsPlaying = true;
            _startTime = Time.time;
        }

        /// <summary>演奏を止めて手を膝へ戻す。</summary>
        public void Stop()
        {
            if (IsPlaying)
            {
                AudioManager.Instance?.StopPiano();
            }

            IsPlaying = false;
            if (_pose != null)
            {
                _pose.ReleaseKeyboardHands();
            }

            Detach();
        }

        private void OnSittingChanged(bool sitting)
        {
            if (!sitting)
            {
                Stop();
            }
        }

        private void Detach()
        {
            if (_player != null)
            {
                _player.SittingChanged -= OnSittingChanged;
                _player = null;
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                return;
            }

            AudioManager audio = AudioManager.Instance;
            if (audio == null || !audio.IsPianoPlaying)
            {
                // 曲が終わった。座ったまま手を戻す。
                IsPlaying = false;
                if (_pose != null)
                {
                    _pose.ReleaseKeyboardHands();
                }

                Detach();
                return;
            }

            if (_pose == null || _keys == null)
            {
                return;
            }

            Pose(Time.time - _startTime);
        }

        /// <summary>
        /// t 秒目の両手。左右に離して鍵盤の縁に置き、ゆっくり左右へ流しながら、指の代わりに手首を交互に上下させる。
        /// </summary>
        private void Pose(float t)
        {
            Vector3 right = _pose.transform.right;
            Vector3 centre = _keys.position + Vector3.up * 0.03f;
            float sweep = Mathf.Sin(t * 0.7f) * 0.10f;
            _pose.SetKeyboardHands(
                centre + right * (-0.20f - sweep) + Vector3.up * Bob(t, 0f),
                centre + right * (0.20f + sweep) + Vector3.up * Bob(t, 2.1f));
        }

        /// <summary>手首の持ち上がり (m)。0 から 0.05 の間で、phase をずらすと左右が交互になる。</summary>
        private static float Bob(float t, float phase)
        {
            return Mathf.Max(0f, Mathf.Sin(t * 2f * Mathf.PI * 1.9f + phase)) * 0.05f;
        }
    }
}
