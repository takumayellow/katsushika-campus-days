using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 「E で調べられるもの」の基底。NPC・掲示板・自販機などが継承する。
    /// 判定用のコライダーは trigger で置き、Interactable レイヤーに入れる。
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [SerializeField] private string _promptLabel = "調べる";
        [SerializeField] private float _interactionRange = 2.2f;

        /// <summary>画面に出す動詞。「話す」「調べる」など。</summary>
        public string PromptLabel
        {
            get => _promptLabel;
            set => _promptLabel = value;
        }

        /// <summary>反応する距離（m）。</summary>
        public float InteractionRange
        {
            get => _interactionRange;
            set => _interactionRange = Mathf.Max(0.2f, value);
        }

        /// <summary>プロンプトを出す位置。既定では自分の少し上。</summary>
        public virtual Vector3 PromptAnchor => transform.position + Vector3.up * 1.6f;

        /// <summary>いま調べられる状態か。会話中や条件未達なら false を返す。</summary>
        public virtual bool CanInteract => isActiveAndEnabled;

        /// <summary>E が押されたとき呼ばれる。</summary>
        public abstract void Interact(GameObject interactor);
    }
}
