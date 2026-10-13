using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// スカート・袴の前と後ろの中央が腿について行く（#49）。走ると前へ出た腿がスカートの前を、
    /// 後ろへ蹴った腿が後ろを突き抜けていた。SkirtFront / SkirtBack の骨を SkirtFollow が腿の角度から回す。
    /// </summary>
    public sealed class SkirtFollowTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";
        private const string TitleScenePath = "Assets/Scenes/Title.unity";

        /// <summary>スカートか袴を着ている体（blender/tests/test_chara_skirt.py の SKIRT と HAKAMA）。</summary>
        private static readonly string[] SkirtCharacters = { "mirai", "inari", "kaname", "sora", "botchan", "madonna" };

        /// <summary>スカートも袴も着ていない体。</summary>
        private static readonly string[] TrouserCharacters = { "prof", "student_m" };

        private const float ToleranceDegrees = 0.5f;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        [Test]
        public void SwingAngle_IsPositiveForward_NegativeBackward_ZeroSideways()
        {
            Vector3 rest = Vector3.down;
            Vector3 axis = Vector3.Cross(rest, Vector3.forward).normalized;

            Vector3 ahead = Quaternion.AngleAxis(-35f, Vector3.right) * rest;
            Vector3 behind = Quaternion.AngleAxis(25f, Vector3.right) * rest;
            Vector3 aside = Quaternion.AngleAxis(30f, Vector3.forward) * rest;

            Assert.AreEqual(35f, SkirtFollow.SwingAngle(rest, ahead, axis) * Mathf.Rad2Deg, 1e-3f);
            Assert.AreEqual(-25f, SkirtFollow.SwingAngle(rest, behind, axis) * Mathf.Rad2Deg, 1e-3f);
            Assert.AreEqual(0f, SkirtFollow.SwingAngle(rest, aside, axis) * Mathf.Rad2Deg, 1e-3f);
        }

        [Test]
        public void FrontFollowsTheLegAhead_BackFollowsTheLegBehind()
        {
            // 体ごと横を向いていても、体の前（+Z）から見た向きで決まる。
            Rig rig = BuildRig(Quaternion.Euler(0f, 120f, 0f));
            rig.LeftUpper.localRotation = Quaternion.AngleAxis(-40f, Vector3.right);
            rig.RightUpper.localRotation = Quaternion.AngleAxis(30f, Vector3.right);

            rig.Follow.Apply();

            AssertPointsAlong(rig.Front, Thigh(rig.LeftUpper, rig.LeftLower), "前は前へ出た左の腿について行く");
            AssertPointsAlong(rig.Back, Thigh(rig.RightUpper, rig.RightLower), "後ろは後ろへ出た右の腿について行く");
        }

        [Test]
        public void EmptyOutlineHullBeforeTheSkin_IsSkipped()
        {
            // 輪郭線の複製（_outline）は肌と同じ骨を持つが、取り込みでメッシュを空にしてある（#70）。
            Rig rig = BuildRig(Quaternion.identity);
            var skin = rig.Follow.GetComponentInChildren<SkinnedMeshRenderer>();
            var hull = new GameObject("Skin_outline");
            hull.transform.SetParent(rig.Follow.transform, false);
            hull.transform.SetAsFirstSibling();
            var empty = new Mesh();
            _created.Add(empty);
            var hullRenderer = hull.AddComponent<SkinnedMeshRenderer>();
            hullRenderer.bones = skin.bones;
            hullRenderer.sharedMesh = empty;
            rig.LeftUpper.localRotation = Quaternion.AngleAxis(-40f, Vector3.right);

            rig.Follow.Apply();

            AssertPointsAlong(rig.Front, Thigh(rig.LeftUpper, rig.LeftLower), "空のハルを飛ばして肌の骨で回す");
        }

        [Test]
        public void BothLegsAhead_FrontTakesTheFurthest_BackStays()
        {
            Rig rig = BuildRig(Quaternion.identity);
            rig.LeftUpper.localRotation = Quaternion.AngleAxis(-20f, Vector3.right);
            rig.RightUpper.localRotation = Quaternion.AngleAxis(-70f, Vector3.right);

            rig.Follow.Apply();

            AssertPointsAlong(rig.Front, Thigh(rig.RightUpper, rig.RightLower), "前は大きく出たほうの腿に付く");
            AssertSameAsHips(rig, rig.Back, "後ろへ出た腿が無いので後ろは腰のまま");
        }

        [Test]
        public void LeaningHips_WithLegsAtRest_DoNotSwingTheSkirt()
        {
            Rig rig = BuildRig(Quaternion.identity);
            rig.Hips.localRotation = Quaternion.AngleAxis(25f, Vector3.right) * Quaternion.AngleAxis(15f, Vector3.up);

            rig.Follow.Apply();

            AssertSameAsHips(rig, rig.Front, "腰ごと傾いただけなら前は回さない");
            AssertSameAsHips(rig, rig.Back, "腰ごと傾いただけなら後ろは回さない");
        }

        [Test]
        public void LegOpenedSideways_DoesNotSwingTheSkirt()
        {
            Rig rig = BuildRig(Quaternion.identity);
            rig.LeftUpper.localRotation = Quaternion.AngleAxis(30f, Vector3.forward);

            rig.Follow.Apply();

            AssertSameAsHips(rig, rig.Front, "横へ開いただけなら前は回さない");
            AssertSameAsHips(rig, rig.Back, "横へ開いただけなら後ろは回さない");
        }

        [Test]
        public void BodyWithoutSkirtBones_IsLeftAlone()
        {
            var body = new GameObject("Body");
            _created.Add(body);
            Assert.IsFalse(SkirtFollow.HasSkirtBones(body));

            // 付けてしまっても何もしない（例外を出さない）。
            body.AddComponent<SkirtFollow>().Apply();
        }

        [Test]
        public void SkirtCharacters_HaveTheBones_OutsideTheHumanoid()
        {
            int checkedCount = 0;
            foreach (string id in SkirtCharacters)
            {
                string path = CharactersFolder + "/" + id + "/" + id + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    continue;
                }

                checkedCount++;
                Assert.IsTrue(SkirtFollow.HasSkirtBones(model), id + " の肌に SkirtFront / SkirtBack が無い（キャラクターを作り直す）");

                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                foreach (HumanBone bone in importer.humanDescription.human)
                {
                    Assert.AreNotEqual(SkirtFollow.FrontBone, bone.boneName, id + ": " + bone.humanName + " に割り当てられている");
                    Assert.AreNotEqual(SkirtFollow.BackBone, bone.boneName, id + ": " + bone.humanName + " に割り当てられている");
                }
            }

            if (checkedCount == 0)
            {
                Assert.Ignore("キャラクターの FBX がまだ無い");
            }
        }

        [Test]
        public void TrouserCharacters_AreLeftAlone()
        {
            // 骨は全員の肌に載っているが、背広とズボンの布は付いていない。
            int checkedCount = 0;
            foreach (string id in TrouserCharacters)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharactersFolder + "/" + id + "/" + id + ".fbx");
                if (model == null)
                {
                    continue;
                }

                checkedCount++;
                Assert.IsFalse(SkirtFollow.HasSkirtBones(model), id + " はスカートを履いていないのに SkirtFollow の対象になった");
            }

            if (checkedCount == 0)
            {
                Assert.Ignore("キャラクターの FBX がまだ無い");
            }
        }

        [Test]
        public void TitleStands_FollowTheSkirt()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);
            try
            {
                int bodies = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
                    {
                        if (!SkirtFollow.HasSkirtBones(animator.gameObject))
                        {
                            continue;
                        }

                        bodies++;
                        Assert.IsNotNull(animator.GetComponent<SkirtFollow>(),
                            animator.name + " に SkirtFollow が無い（SceneBuilder で組み直す）");
                    }
                }

                if (bodies == 0)
                {
                    Assert.Ignore("タイトルにスカートの骨を持つ体が無い（FBX がまだ無い）");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private struct Rig
        {
            public SkirtFollow Follow;
            public Transform Hips;
            public Transform LeftUpper;
            public Transform LeftLower;
            public Transform RightUpper;
            public Transform RightLower;
            public Transform Front;
            public Transform Back;
        }

        /// <summary>
        /// 腰・左右の腿と脛・スカートの 2 本だけの体。FBX の取り込みと同じく、肌は骨と別の向き・倍率に置く
        /// （bindposes に回転と拡大が乗っても読めることを見る）。
        /// </summary>
        private Rig BuildRig(Quaternion bodyRotation)
        {
            var body = new GameObject("Body");
            _created.Add(body);
            body.transform.rotation = bodyRotation;

            var skin = new GameObject("Skin");
            skin.transform.SetParent(body.transform, false);
            skin.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            skin.transform.localScale = Vector3.one * 100f;

            Transform hips = Bone("Hips", body.transform, new Vector3(0f, 0.8f, 0f));
            Transform leftUpper = Bone("LeftUpperLeg", hips, new Vector3(0.1f, -0.05f, 0f));
            Transform leftLower = Bone("LeftLowerLeg", leftUpper, new Vector3(0f, -0.38f, 0f));
            Transform rightUpper = Bone("RightUpperLeg", hips, new Vector3(-0.1f, -0.05f, 0f));
            Transform rightLower = Bone("RightLowerLeg", rightUpper, new Vector3(0f, -0.38f, 0f));
            Transform front = Bone(SkirtFollow.FrontBone, hips, new Vector3(0f, -0.05f, 0f));
            Transform back = Bone(SkirtFollow.BackBone, hips, new Vector3(0f, -0.05f, 0f));

            Transform[] bones = { hips, leftUpper, leftLower, rightUpper, rightLower, front, back };
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bindposes[i] = bones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
            }

            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = bindposes,
            };
            _created.Add(mesh);

            var renderer = skin.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = bones;
            renderer.sharedMesh = mesh;

            return new Rig
            {
                Follow = body.AddComponent<SkirtFollow>(),
                Hips = hips,
                LeftUpper = leftUpper,
                LeftLower = leftLower,
                RightUpper = rightUpper,
                RightLower = rightLower,
                Front = front,
                Back = back,
            };
        }

        private static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }

        private static Vector3 Thigh(Transform upper, Transform lower)
        {
            return (lower.position - upper.position).normalized;
        }

        /// <summary>スカートの骨はレストで真下を向いている（腿のレストと同じ向き）。</summary>
        private static void AssertPointsAlong(Transform skirt, Vector3 thigh, string message)
        {
            Vector3 down = skirt.rotation * Vector3.down;
            Assert.Less(Vector3.Angle(down, thigh), ToleranceDegrees, message + "（ずれ " + Vector3.Angle(down, thigh) + "°）");
        }

        private static void AssertSameAsHips(Rig rig, Transform skirt, string message)
        {
            float gap = Quaternion.Angle(skirt.rotation, rig.Hips.rotation);
            Assert.Less(gap, ToleranceDegrees, message + "（ずれ " + gap + "°）");
        }
    }
}
