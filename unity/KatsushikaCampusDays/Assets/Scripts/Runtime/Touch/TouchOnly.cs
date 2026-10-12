using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 指で遊んでいる間だけ target を出す (#187)。会話の枠を叩いて進める受け口のように、
    /// 仮想のゲームパッドに書く部品を PC で動かさないために使う。target は既定で止めておく。
    /// </summary>
    public sealed class TouchOnly : MonoBehaviour
    {
        [SerializeField] private GameObject _target;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject target)
        {
            _target = target;
        }

        private void OnEnable()
        {
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        /// <summary>
        /// 隠れるときに target も止めておく。止めずにおくと、次に出たとき target が先に動き出し、
        /// マウスで遊んでいても仮想のゲームパッドが一瞬できる。
        /// </summary>
        private void OnDisable()
        {
            if (_target != null)
            {
                _target.SetActive(false);
            }
        }

        private void Sync()
        {
            bool touch = InputMode.IsTouch;
            if (_target != null && _target.activeSelf != touch)
            {
                _target.SetActive(touch);
            }
        }
    }
}
