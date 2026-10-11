using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// Q で手を振る相手の選び方 (#13)。会話中・振り返し中・無効・破棄済みの人を選ばないこと。
    /// NPCWander は OnEnable / OnDisable で候補に出入りするので PlayMode で見る。
    /// 他のテストのシーンに残った NPC と混ざらないよう、キャンパスから離れた場所に置く。
    /// </summary>
    public sealed class NpcWaveTargetPlayTests
    {
        private static readonly Vector3 Origin = new Vector3(5000f, 0f, 5000f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _created.Clear();
        }

        [UnityTest]
        public IEnumerator FindWaveTarget_SkipsPeopleWhoCannotWaveBack()
        {
            NPCWander near = Spawn("Near", 3f);
            NPCWander far = Spawn("Far", 4f);
            yield return null;

            Assert.AreSame(near, Find(), "正面のいちばん近い人を選ばない");

            near.Paused = true;
            Assert.AreSame(far, Find(), "会話中の人に手を振る");
            near.Paused = false;

            Assert.IsTrue(near.WaveBack(Origin));
            Assert.AreSame(far, Find(), "もう振り返している人をまた選ぶ");

            far.enabled = false;
            Assert.IsNull(Find(), "無効にした人が候補に残っている");

            far.enabled = true;
            Assert.AreSame(far, Find());

            Object.Destroy(far.gameObject);
            yield return null;
            Assert.IsNull(Find(), "消した人が候補に残っている");
        }

        private static NPCWander Find()
        {
            return NPCWander.FindWaveTarget(Origin, Vector3.forward);
        }

        /// <summary>プレイヤーの正面 distance m に NPC を置く。NavMesh の無い場所なので歩かず立っている。</summary>
        private NPCWander Spawn(string name, float distance)
        {
            var go = new GameObject("NpcWaveTargetPlayTests_" + name);
            go.SetActive(false);
            go.transform.position = Origin + Vector3.forward * distance;
            go.AddComponent<NavMeshAgent>().enabled = false;
            NPCWander npc = go.AddComponent<NPCWander>();
            go.SetActive(true);
            _created.Add(go);
            return npc;
        }
    }
}
