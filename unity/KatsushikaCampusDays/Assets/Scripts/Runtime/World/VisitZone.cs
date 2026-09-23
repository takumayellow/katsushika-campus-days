using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 「ここに来たら進む」場所。キャンパスモール・水盤前・公園などに置く見えない箱。
    /// 入ると QuestSystem に visit を報告する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VisitZone : MonoBehaviour
    {
        [SerializeField] private string _placeId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private bool _announce = true;

        /// <summary>Reset() が入れる既定の大きさ。まだ何も指定されていない箱にだけ入れる。</summary>
        public static readonly Vector3 DefaultSize = new Vector3(10f, 6f, 10f);

        /// <summary>留まっているあいだ、何秒おきに報告し直すか。</summary>
        private const float RepeatSeconds = 0.25f;

        private bool _reportedOnce;
        private float _nextRepeat;

        /// <summary>クエストの visit ステップの target と揃える id。</summary>
        public string PlaceId
        {
            get => _placeId;
            set => _placeId = value;
        }

        /// <summary>トーストに出す場所の名前。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>
        /// 既定の大きさを入れてよいか。Unity が箱を作った直後の 1x1x1 のときだけ入れる。
        ///
        /// Reset() は Inspector のリセットだけでなく、エディタで AddComponent した瞬間にも呼ばれる。
        /// CampusProps.Zone は「箱を足す → 大きさを入れる → VisitZone を足す」の順なので、
        /// 無条件に代入すると最後の行で大きさが 10x6x10 に巻き戻っていた。実際、シーンに焼かれた
        /// 46 ゾーンが全部 10x6x10 になっていて、水盤ゾーンは指定の 58x8x16 ではなく
        /// 10x6x10、しかも位置の y は size.y=8 前提のままなので箱の下端が地面から 1.02 m 浮き、
        /// プレイヤーの足元が判定の外に出ていた（#54）。
        /// </summary>
        public static bool ShouldApplyDefaultSize(Vector3 current)
        {
            return current == Vector3.one;
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            if (ShouldApplyDefaultSize(box.size))
            {
                box.size = DefaultSize;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (string.IsNullOrEmpty(_placeId) || !other.CompareTag("Player"))
            {
                return;
            }

            // 場所の名前を先に出す。そのあとに「◯時ごろにまた来よう」やクエスト達成が続くと読みやすい。
            if (_announce && !_reportedOnce && !string.IsNullOrEmpty(_displayName))
            {
                _reportedOnce = true;
                HUD.Instance?.ShowToast(L.Get("ui.place." + _placeId, _displayName));
            }

            // 入った瞬間。時刻の条件より早ければ「◯時ごろにまた来よう」が出る (#66)。
            GameManager.Instance.Quests?.ReportVisit(_placeId, true);
        }

        private void OnTriggerStay(Collider other)
        {
            // 時刻条件つきのステップは、入った瞬間には条件を満たしていないことがある。
            // 留まっているあいだは一定間隔で報告し直す。
            //
            // 間引きに Time.frameCount を使ってはいけない。OnTriggerStay が走るのは物理ステップ
            // （既定 50 Hz）で、frameCount は描画フレーム（vsync なら 60 Hz）。60/50 の比だと
            // 30 の倍数がいつも同じ位相に来るので、物理が走らない側の位相に当たると 1 度も
            // 報告されないまま終わる。実時間で測る（#54）。
            if (Time.time < _nextRepeat)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_placeId) && other.CompareTag("Player"))
            {
                _nextRepeat = Time.time + RepeatSeconds;
                // 留まっているあいだの報告し直し。早すぎる案内は入った瞬間の 1 回だけにする (#66)。
                GameManager.Instance.Quests?.ReportVisit(_placeId, false);
            }
        }
    }
}
