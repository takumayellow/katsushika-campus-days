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
            box.size = new Vector3(2.5f, 2.5f, 1.5f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null && loader.CurrentId == _buildingId)
            {
                loader.Exit();
            }
        }
    }
}
