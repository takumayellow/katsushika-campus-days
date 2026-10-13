using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 体育館の屋内（#115）を歩いて確かめる。入った所から床を 0.25 m ごとにたどり（プレイヤーのカプセルが
    /// 入れ、段差は stepOffset 以内）、行き先と座る場所と NPC の位置まで歩いて行けること、壁の外へ抜け出せる
    /// 所が無いことを見る。
    /// </summary>
    public sealed class GymInteriorPlayTests : SceneTestBase
    {
        private const string BuildingId = "gym";

        /// <summary>歩ける所を探す格子の間隔（m）。壁の厚み・カプセルの半径より細かい。</summary>
        private const float Cell = 0.25f;

        /// <summary>行き先の位置から、歩ける格子までの許容（m、水平）。訪問ゾーンの半幅は 2 m。</summary>
        private const float PoiReach = 1.5f;

        /// <summary>座る場所の位置から、歩ける格子までの許容（m、水平）。</summary>
        private const float SeatReach = 1.0f;

        /// <summary>NPC の位置から、歩ける格子までの許容（m、水平）。窓口の奥の職員は、カウンターの手前から話しかけられる。</summary>
        private const float NpcReach = 3.5f;

        /// <summary>壁の外へ出たとみなす距離（m）。外形の内側の縁から。</summary>
        private const float LeakMargin = 0.3f;

        /// <summary>探す範囲を外形の外へ広げる量（m）。壁の外へ抜けていれば見つかる。</summary>
        private const float SearchMargin = 3f;

        [Serializable]
        private sealed class EnvelopeJson
        {
            public float x0;
            public float x1;
            public float y_face;
            public float y_back;
        }

        [Serializable]
        private sealed class InteriorJson
        {
            public EnvelopeJson envelope;
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Gym_EveryDestinationIsWalkable_AndTheWallsHold()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            DayNightCycle cycle = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                UnityEngine.Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Enter(BuildingId);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "体育館の中へ入れていない");

            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + BuildingId);
            Assert.IsNotNull(root, "屋内の入れ物が無い");
            EnvelopeJson env = ReadEnvelope();

            CharacterController body = player.GetComponent<CharacterController>();
            Vector3 start = player.transform.position;
            var walk = new Walker(root.transform, env, body, body.stepOffset);

            // 自分の当たり判定と、屋内のほかの人の当たり判定をはずして探す。
            body.enabled = false;
            walk.Flood(start);
            body.enabled = true;

            Assert.That(walk.Reached.Count, Is.GreaterThan(2000),
                "入った所から歩ける所が少なすぎる: " + walk.Reached.Count + " 格子（入った所 " + start.ToString("F2") + "）");
            Assert.AreEqual(0, walk.Leaks.Count,
                "壁の外へ抜け出せる所がある（" + walk.Leaks.Count + " 格子、例 " +
                string.Join(" / ", walk.Leaks.Take(5).Select(p => p.ToString("F2"))) + "）");

            var unreachable = new List<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                float reach;
                if (t.name.StartsWith("poi_" + BuildingId + "_"))
                {
                    reach = PoiReach;
                }
                else if (t.name.StartsWith("npc_" + BuildingId + "_"))
                {
                    reach = NpcReach;
                }
                else if (t.name.StartsWith("seat_" + BuildingId + "_") && t.name.Length == ("seat_" + BuildingId + "_00").Length)
                {
                    reach = SeatReach;
                }
                else
                {
                    continue;
                }

                if (!walk.IsNear(t.position, reach))
                {
                    unreachable.Add(t.name + " " + t.position.ToString("F2"));
                }
            }

            Assert.IsEmpty(unreachable, "歩いて行けない行き先・座る場所・NPC:\n" + string.Join("\n", unreachable));
            Assert.That(walk.Reached.Any(p => p.y > root.transform.position.y + 4.0f),
                "階段を上った先（2F）に歩いて行けない");
            AssertNoErrors("体育館を歩く");
        }

        private static EnvelopeJson ReadEnvelope()
        {
            string path = Path.Combine(Application.dataPath, "Models", "Interiors", BuildingId + ".json");
            var json = JsonUtility.FromJson<InteriorJson>(File.ReadAllText(path));
            Assert.IsNotNull(json?.envelope, "外形が " + path + " に無い");
            return json.envelope;
        }

        /// <summary>床をたどって歩ける格子を集める。1F と 2F のように上下に重なる床は別の格子として数える。</summary>
        private sealed class Walker
        {
            private const float Radius = 0.29f;
            private const float Height = 1.7f;
            private const float DropLimit = 0.45f;

            private readonly Transform _root;
            private readonly EnvelopeJson _env;
            private readonly float _step;

            // 格子（x, z）と階層（床の高さを 1 m で丸めた値）から、その床の高さへ。
            private readonly Dictionary<Vector3Int, float> _floor = new Dictionary<Vector3Int, float>();

            public Walker(Transform root, EnvelopeJson env, CharacterController body, float step)
            {
                _root = root;
                _env = env;
                _step = step;
            }

            public IReadOnlyCollection<Vector3> Reached =>
                _floor.Select(kv => new Vector3(kv.Key.x * Cell, kv.Value, kv.Key.y * Cell)).ToList();

            public List<Vector3> Leaks { get; } = new List<Vector3>();

            public void Flood(Vector3 start)
            {
                var queue = new Queue<Vector3Int>();
                Vector3Int first = Key(start.x, start.z, start.y);
                _floor[first] = start.y;
                queue.Enqueue(first);
                var offsets = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
                while (queue.Count > 0)
                {
                    Vector3Int at = queue.Dequeue();
                    float y = _floor[at];
                    foreach (Vector2Int d in offsets)
                    {
                        var world = new Vector3((at.x + d.x) * Cell, 0f, (at.y + d.y) * Cell);
                        if (!OnSearchArea(world) || !TryStand(world, y, out float floorY))
                        {
                            continue;
                        }

                        Vector3Int next = Key(world.x, world.z, floorY);
                        if (_floor.ContainsKey(next))
                        {
                            continue;
                        }

                        _floor[next] = floorY;
                        if (!InsideEnvelope(new Vector3(world.x, floorY, world.z), -LeakMargin))
                        {
                            Leaks.Add(new Vector3(world.x, floorY, world.z));
                        }

                        queue.Enqueue(next);
                    }
                }
            }

            public bool IsNear(Vector3 target, float reach)
            {
                foreach (KeyValuePair<Vector3Int, float> kv in _floor)
                {
                    var cell = new Vector3(kv.Key.x * Cell, kv.Value, kv.Key.y * Cell);
                    Vector2 flat = new Vector2(cell.x - target.x, cell.z - target.z);
                    if (flat.magnitude <= reach && Mathf.Abs(kv.Value - target.y) <= 1.5f)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static Vector3Int Key(float x, float z, float y)
            {
                return new Vector3Int(Mathf.RoundToInt(x / Cell), Mathf.RoundToInt(z / Cell), Mathf.RoundToInt(y));
            }

            private bool TryStand(Vector3 xz, float fromY, out float floorY)
            {
                floorY = 0f;
                Vector3 top = new Vector3(xz.x, fromY + _step + 0.05f, xz.z);
                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, _step + DropLimit + 0.05f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                floorY = hit.point.y;
                Vector3 bottom = new Vector3(xz.x, floorY + _step + 0.02f + Radius, xz.z);
                Vector3 upper = new Vector3(xz.x, floorY + Height - Radius, xz.z);
                return !Physics.CheckCapsule(bottom, upper, Radius, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
            }

            private bool OnSearchArea(Vector3 world)
            {
                return InsideEnvelope(world, -SearchMargin);
            }

            /// <summary>外形（屋内のローカルの x と奥行き）の内側 margin の位置か。負の margin は外へ広げる。</summary>
            private bool InsideEnvelope(Vector3 world, float margin)
            {
                // 屋内の入れ物は 180 度回して置いてある。外形は Blender の座標（x と奥行き）で書いてあるので、
                // ローカル座標の符号を反転して照らす。
                Vector3 local = _root.InverseTransformPoint(world);
                float bx = -local.x;
                float by = -local.z;
                return bx >= _env.x0 + margin && bx <= _env.x1 - margin
                    && by >= _env.y_face + margin && by <= _env.y_back - margin;
            }
        }
    }
}
