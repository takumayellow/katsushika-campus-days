using UnityEngine;

namespace KCD
{
    /// <summary>屋内の出口。FBX の exit_&lt;id&gt; Empty の位置に置かれ、踏むと外へ戻る。</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class InteriorExit : MonoBehaviour
    {
        [SerializeField] private string _buildingId = string.Empty;

        /// <summary>この出口が属する建物。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            // AddComponent した直後にも Reset() は呼ばれる。すでに入っている大きさは巻き戻さない（#54）。
            if (VisitZone.ShouldApplyDefaultSize(box.size))
            {
                box.size = new Vector3(2.5f, 2.5f, 1.5f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (ShouldExit(loader, _buildingId))
            {
                loader.Exit();
            }
        }

        /// <summary>
        /// この出口を踏んだら外へ出すか。今いる建物の出口のときだけ true。
        /// 外にいるとき・別の建物の出口のときは何もしない（外にいるときの Exit はもともと何もしない）。
        /// </summary>
        public static bool ShouldExit(InteriorLoader loader, string buildingId)
        {
            return loader != null && loader.IsInside && loader.CurrentId == buildingId;
        }
    }
}
