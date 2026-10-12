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

        /// <summary>入った所（spawn）から出口の範囲の手前の縁まで空ける水平の距離（m）。プレイヤーの半径 + 余裕。</summary>
        public const float SpawnGap = 0.8f;

        /// <summary>出口の印（exit_&lt;id&gt;）から外側へ、範囲を伸ばす長さ（m）。</summary>
        public const float OutwardReach = 1.5f;

        /// <summary>出口の範囲の幅と高さ（m）。</summary>
        public const float BoxWidth = 3f;
        public const float BoxHeight = 2.6f;

        /// <summary>
        /// 出口の範囲（向き付きの箱）。入った所から出口の印へ向かう向きに箱を向け、手前の縁を入った所から
        /// SpawnGap だけ離す。入った所が範囲の中にあると、少し戻るだけで外へ出される（#212）。
        /// 外側の縁は印から OutwardReach の先。center は印の高さ + 1.2 m、yaw は Y 軸まわりの度。
        /// </summary>
        public static void BoxFor(Vector3 exit, Vector3 spawn, out Vector3 center, out float yaw, out Vector3 size)
        {
            Vector3 toExit = exit - spawn;
            toExit.y = 0f;
            float distance = toExit.magnitude;
            Vector3 direction = distance > 1e-4f ? toExit / distance : Vector3.forward;
            float near = SpawnGap;
            float far = distance + OutwardReach;
            float depth = far - near;
            Vector3 middle = spawn + direction * (near + depth * 0.5f);
            center = new Vector3(middle.x, exit.y + 1.2f, middle.z);
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            size = new Vector3(BoxWidth, BoxHeight, depth);
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
