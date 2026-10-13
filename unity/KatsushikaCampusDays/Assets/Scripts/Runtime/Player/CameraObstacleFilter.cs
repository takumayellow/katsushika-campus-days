using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// カメラを止める当たり判定の選び方と、見る点からの球の掃引 (#12)。
    /// 止めるのは Ground と Building のすべてと、Default のうち木の幹（名前 <see cref="TreeTrunkName"/>）だけ。
    /// プレイヤー・NPC・Interactable とトリガーは素通りする。Default の見えない壁
    /// （堀の Keepout_BasinWall、WorldBounds、ベンチの Blocker など）も素通りで、今までどおり。
    /// 肩越しカメラ（CinemachineCameraGuard）とフォトモードの自由カメラ（PhotoFreeCamera）が同じ判定を使う。
    /// </summary>
    public static class CameraObstacleFilter
    {
        /// <summary>木の幹の当たり判定（CapsuleCollider）の名前。CampusStage が同じ名前で作る。</summary>
        public const string TreeTrunkName = "trunk";

        /// <summary>Default レイヤーの番号。</summary>
        public const int DefaultLayer = 0;

        /// <summary>法線の y がこれより小さい面を天井と見なす（下向きの面）。</summary>
        public const float CeilingNormalY = -0.5f;

        private const int MaxHits = 64;
        private const int MaxCachedColliders = 1024;

        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];

        // Default レイヤーの当たり判定が幹かどうか。name を読むたびに文字列が作られるので覚えておく。
        private static readonly Dictionary<Collider, bool> TrunkCache = new Dictionary<Collider, bool>();

        private static bool _resolved;
        private static int _groundLayer = -1;
        private static int _buildingLayer = -1;
        private static int _mask;

        /// <summary>掃引で拾うレイヤー（Default | Ground | Building）。Default の中身は <see cref="Blocks(Collider)"/> で選ぶ。</summary>
        public static int Mask
        {
            get
            {
                Resolve();
                return _mask;
            }
        }

        /// <summary>
        /// その当たり判定でカメラを止めるか。物理に触らない判定だけを出し、テストで確かめる。
        /// groundLayer と buildingLayer は存在しなければ負の値を渡す。
        /// </summary>
        public static bool Blocks(int layer, string name, bool isTrigger, int groundLayer, int buildingLayer)
        {
            if (isTrigger)
            {
                return false;
            }

            if ((groundLayer >= 0 && layer == groundLayer) || (buildingLayer >= 0 && layer == buildingLayer))
            {
                return true;
            }

            return layer == DefaultLayer && string.Equals(name, TreeTrunkName, StringComparison.Ordinal);
        }

        /// <summary>その当たり判定でカメラを止めるか（実際の Collider から）。</summary>
        public static bool Blocks(Collider collider)
        {
            if (collider == null || collider.isTrigger)
            {
                return false;
            }

            Resolve();
            int layer = collider.gameObject.layer;
            if (layer != DefaultLayer)
            {
                return Blocks(layer, null, false, _groundLayer, _buildingLayer);
            }

            if (!TrunkCache.TryGetValue(collider, out bool trunk))
            {
                if (TrunkCache.Count >= MaxCachedColliders)
                {
                    TrunkCache.Clear();
                }

                trunk = Blocks(layer, collider.name, false, _groundLayer, _buildingLayer);
                TrunkCache[collider] = trunk;
            }

            return trunk;
        }

        /// <summary>法線が下を向いている面（天井）か。</summary>
        public static bool IsCeiling(Vector3 normal)
        {
            return normal.y < CeilingNormalY;
        }

        /// <summary>
        /// origin から半径 radius の球を direction へ maxDistance だけ飛ばし、止める当たり判定までの距離を返す。
        /// 当たらなければ -1。始点で既に重なっている当たり判定は、中心から同じ向きへ線を飛ばして当たった所
        /// （から半径を引いた距離）で数え、線が当たらなければ無視する（真横の壁に触れているだけなら止めない）。
        /// </summary>
        public static float Sweep(Vector3 origin, float radius, Vector3 direction, float maxDistance, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (maxDistance <= 0f || direction.sqrMagnitude < 1e-8f)
            {
                return -1f;
            }

            direction = direction.normalized;
            int count = Physics.SphereCastNonAlloc(
                origin, radius, direction, Hits, maxDistance, Mask, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (!Blocks(hit.collider))
                {
                    continue;
                }

                float distance = hit.distance;
                Vector3 hitNormal = hit.normal;
                if (distance <= 0f)
                {
                    // 始点で重なっている。中心からの線で、カメラの向きに面があるかを確かめる。
                    if (!hit.collider.Raycast(new Ray(origin, direction), out RaycastHit ray, maxDistance + radius))
                    {
                        continue;
                    }

                    distance = Mathf.Max(0f, ray.distance - radius);
                    hitNormal = ray.normal;
                }

                if (distance < best)
                {
                    best = distance;
                    normal = hitNormal;
                }
            }

            return float.IsPositiveInfinity(best) ? -1f : best;
        }

        /// <summary>
        /// origin の真上 maxDistance までにある天井（下を向いた止める面）のうち、いちばん低い所の高さ。無ければ +∞。
        /// </summary>
        public static float CeilingAbove(Vector3 origin, float radius, float maxDistance)
        {
            if (maxDistance <= 0f)
            {
                return float.PositiveInfinity;
            }

            int count = Physics.SphereCastNonAlloc(
                origin, radius, Vector3.up, Hits, maxDistance, Mask, QueryTriggerInteraction.Ignore);

            float lowest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.distance <= 0f || !IsCeiling(hit.normal) || !Blocks(hit.collider))
                {
                    continue;
                }

                lowest = Mathf.Min(lowest, hit.point.y);
            }

            return lowest;
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _groundLayer = LayerMask.NameToLayer("Ground");
            _buildingLayer = LayerMask.NameToLayer("Building");
            int mask = 1 << DefaultLayer;
            if (_groundLayer >= 0)
            {
                mask |= 1 << _groundLayer;
            }

            if (_buildingLayer >= 0)
            {
                mask |= 1 << _buildingLayer;
            }

            _mask = mask;
            _resolved = true;
        }
    }
}
