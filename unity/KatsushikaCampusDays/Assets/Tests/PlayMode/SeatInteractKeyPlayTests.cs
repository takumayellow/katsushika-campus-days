using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 座っているときの E が、立ち上がりと前にいる人への話しかけの両方に使われない (#97)。
    /// PlayerController と InteractionPrompt の Update の順は決まっていないので、両方の順で見る。
    /// InteractionPrompt が先なら座っている間の判定で、PlayerController が先なら立ったフレームの判定で止まる。
    /// Awake で CharacterController を掴む本物の PlayerController を使うので PlayMode で見る。
    /// </summary>
    public sealed class SeatInteractKeyPlayTests
    {
        [UnityTest]
        public IEnumerator StandingUpWithE_DoesNotAlsoInteract()
        {
            bool lockedBefore = KCDInput.MovementLocked;
            var host = new GameObject("SeatInteractKeyPlayTests", typeof(CharacterController));
            var anchor = new GameObject("Anchor");
            try
            {
                PlayerController player = host.AddComponent<PlayerController>();
                Assert.IsFalse(InteractionPrompt.SeatUsesInteractKey(player), "座る前から E を座席に取られる");

                player.SitAt(anchor.transform);
                Assert.IsTrue(player.IsSitting);
                Assert.IsFalse(Interacts(player), "座っている間の E で前の人に話しかける（InteractionPrompt が先に走る順）");

                yield return null;
                Assert.IsFalse(Interacts(player), "座ったままの次のフレームで話しかける");

                player.StandUp();
                Assert.IsFalse(player.IsSitting);
                Assert.IsFalse(KCDInput.MovementLocked, "立ったのに移動の止めが残っている");
                Assert.IsFalse(Interacts(player), "立ち上がった E で同じフレームに話しかける（PlayerController が先に走る順）");

                yield return null;
                Assert.IsTrue(Interacts(player), "立ったあとの E で調べられない");
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(anchor);
                KCDInput.MovementLocked = lockedBefore;
            }
        }

        /// <summary>前に話せる相手がいて、封鎖もモーダルも無いところで E を押したら調べるか。</summary>
        private static bool Interacts(PlayerController player)
        {
            return InteractionPrompt.ShouldInteract(true, true, false, false, InteractionPrompt.SeatUsesInteractKey(player));
        }
    }
}
