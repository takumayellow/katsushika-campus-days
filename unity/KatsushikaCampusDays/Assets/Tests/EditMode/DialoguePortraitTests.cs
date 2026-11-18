using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>会話ウィンドウの話し手の顔 (#11)。行ごとに誰の顔を出すかと、顔を撮るカメラの置き方。</summary>
    public sealed class DialoguePortraitTests
    {
        private const string Json =
            "{\"id\":\"kaname\",\"speaker\":\"中川 かなめ\",\"topics\":[{\"id\":\"t\",\"lines\":[" +
            "\"いらっしゃい。\"," +
            "{\"speaker\":\"自分\",\"text\":\"日替わりを。\"}," +
            "{\"speaker\":\"\",\"text\":\"……\"}," +
            "{\"speaker\":\"店長\",\"text\":\"かなめ、牛乳！\"}," +
            "{\"text\":\"メモが挟まっている。\",\"written\":true}" +
            "]}]}";

        private const string CharactersFolder = "Assets/Models/Characters";

        /// <summary>枠に入っているか確かめる顔の部品（材質の名前の末尾）。</summary>
        private static readonly string[] FaceParts = { "_eye_white", "_eye_l", "_eye_r", "_eye_rim", "_brow" };

        /// <summary>白目の真ん中が来てよい高さ（NDC）。みらいは 0.38 ほど。下なら顔が小さく、上なら目が枠の上端に寄る。</summary>
        private const float EyeLow = 0.2f;

        private const float EyeHigh = 0.7f;

        private static List<DialogueLine> Lines()
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(Json) as Dictionary<string, object>);
            return data.Topics[0].Lines;
        }

        [Test]
        public void ThePersonYouTalkToAndThePlayerShowTheirFaces()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.Npc, DialoguePortrait.SubjectOf(lines[0], "kaname"));
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[1], "kaname"), "「自分」の行");
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[2], "kaname"), "話者が空の行");
        }

        [Test]
        public void OthersAndWrittenTextHaveNoFace()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[3], "kaname"), "その場にいない人の名前");
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[4], "kaname"), "紙に書かれた文");
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(null, "kaname"));
        }

        [Test]
        public void WithoutAPersonToTalkToOnlyThePlayerHasAFace()
        {
            List<DialogueLine> lines = Lines();

            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[0], string.Empty), "メモなど NPC の決まらない会話");
            Assert.AreEqual(DialoguePortrait.Subject.Player, DialoguePortrait.SubjectOf(lines[1], string.Empty));
            Assert.AreEqual(DialoguePortrait.Subject.None, DialoguePortrait.SubjectOf(lines[0], "inari"), "別の人の会話データ");
        }

        [Test]
        public void TheCameraFacesTheHeadFromInFront()
        {
            var head = new Vector3(1f, 1.5f, 2f);
            Pose pose = DialoguePortrait.CameraPose(head, Vector3.forward, 0.8f, 0f, 0.05f);

            Assert.That(Vector3.Distance(pose.position, head + new Vector3(0f, 0.05f, 0.8f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(pose.rotation * Vector3.forward, Vector3.back), Is.LessThan(0.01f));
        }

        [Test]
        public void TheCameraStaysLevelWhenTheBodyLeans()
        {
            Pose pose = DialoguePortrait.CameraPose(Vector3.zero, new Vector3(0f, -0.5f, 1f), 0.8f, 20f, 0.05f);

            Assert.That(pose.position.y, Is.EqualTo(0.05f).Within(1e-4f), "前かがみでも頭と同じ高さから撮る");
            Assert.That(new Vector2(pose.position.x, pose.position.z).magnitude, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(pose.position.x, Is.GreaterThan(0f), "正面から少し横へ回す");
        }

        [Test]
        public void HigherEyesAreFramedWider()
        {
            DialoguePortrait.Framing(DialoguePortrait.ReferenceEyeHeight, out float lift, out float fieldOfView);
            Assert.That(lift, Is.EqualTo(0.05f).Within(1e-5f), "みらいと同じ目の高さなら Head ボーンの 5 cm 上から");
            Assert.That(fieldOfView, Is.EqualTo(26f).Within(1e-3f), "縦 26 度で撮る");

            DialoguePortrait.Framing(0f, out float unknownLift, out float unknownFieldOfView);
            Assert.That(unknownLift, Is.EqualTo(lift).Within(1e-6f), "目の高さを測れなかった人は、みらいと同じ撮り方");
            Assert.That(unknownFieldOfView, Is.EqualTo(fieldOfView).Within(1e-4f));

            DialoguePortrait.Framing(2f * DialoguePortrait.ReferenceEyeHeight, out float twiceLift, out float twiceFieldOfView);
            Assert.That(twiceLift, Is.EqualTo(2f * lift).Within(1e-5f), "目が 2 倍の高さなら 2 倍持ち上げる");
            Assert.That(HalfTan(twiceFieldOfView), Is.EqualTo(2f * HalfTan(fieldOfView)).Within(1e-4f), "画角の tan も 2 倍");
        }

        /// <summary>
        /// キャラの FBX を 1 体ずつ置き、ゲームと同じ撮り方（目の高さを測る → 持ち上げ量と画角を決める → 正面から 20 度回した
        /// 0.8 m 先から撮る）で、白目・瞳・目のふち・眉が枠に入り、目が枠の上半分に来ることを確かめる。頭の大きさや顔の作りを
        /// 変えたときに、顔枠から目がはみ出すのを捕まえる。
        /// </summary>
        [Test]
        public void EveryCharactersEyesAndBrowsFitInTheFrame()
        {
            var bad = new List<string>();
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx"))
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path + " を読めない");
                GameObject body = UnityEngine.Object.Instantiate(model);
                try
                {
                    body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    bad.AddRange(FramingProblems(path, body));
                    checkedCount++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(body);
                }
            }

            Assert.Greater(checkedCount, 0, CharactersFolder + " にキャラの FBX が無い");
            Assert.IsEmpty(bad, "会話の顔枠で、目か眉が枠からはみ出す:\n" + string.Join("\n", bad));
        }

        /// <summary>置いた体をゲームと同じ撮り方で撮ったときの問題。顔の部品ごとに、写る範囲（NDC）を調べる。</summary>
        private static List<string> FramingProblems(string path, GameObject body)
        {
            Animator animator = body.GetComponentInChildren<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Assert.IsNotNull(head, path + " に Head ボーンが無い");

            float eyes = DialoguePortrait.MeasureEyeHeight(animator.gameObject, head);
            Assert.Greater(eyes, 0f, path + " の目の高さを測れない");
            DialoguePortrait.Framing(eyes, out float lift, out float fieldOfView);
            Pose camera = DialoguePortrait.CameraPose(
                head.position, animator.transform.forward, DialoguePortrait.Distance, DialoguePortrait.Yaw, lift);
            Quaternion toCamera = Quaternion.Inverse(camera.rotation);
            float halfTan = HalfTan(fieldOfView);
            string who = path + "（目の高さ " + eyes.ToString("0.000") + " m）";

            var problems = new List<string>();
            bool hasEyeWhite = false;
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer.sharedMesh;
                if (!renderer.enabled || mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }

                Vector3[] vertices = mesh.vertices;
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length && slot < mesh.subMeshCount; slot++)
                {
                    string part = PartOf(materials[slot]);
                    if (part == null)
                    {
                        continue;
                    }

                    var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                    var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                    float bottom = float.PositiveInfinity;
                    float top = float.NegativeInfinity;
                    foreach (int index in mesh.GetIndices(slot))
                    {
                        // 置いたままの体は骨が bind pose にあるので、頂点はメッシュの座標から移すだけでよい
                        // （下で、測った目の高さと突き合わせて確かめる）。
                        Vector3 world = renderer.transform.TransformPoint(vertices[index]);
                        Vector3 local = toCamera * (world - camera.position);
                        Vector2 ndc = new Vector2(local.x, local.y) / (local.z * halfTan);
                        min = Vector2.Min(min, ndc);
                        max = Vector2.Max(max, ndc);
                        bottom = Mathf.Min(bottom, world.y);
                        top = Mathf.Max(top, world.y);
                    }

                    if (min.x < -1f || min.y < -1f || max.x > 1f || max.y > 1f)
                    {
                        problems.Add(who + " " + part + ": x " + min.x.ToString("0.00") + "〜" + max.x.ToString("0.00")
                            + "・y " + min.y.ToString("0.00") + "〜" + max.y.ToString("0.00"));
                    }

                    if (part != "_eye_white")
                    {
                        continue;
                    }

                    hasEyeWhite = true;
                    float middle = (min.y + max.y) * 0.5f;
                    if (middle < EyeLow || middle > EyeHigh)
                    {
                        problems.Add(who + " 白目の真ん中が y " + middle.ToString("0.00") + "（" + EyeLow + "〜" + EyeHigh + " に来てほしい）");
                    }

                    float fromVertices = (bottom + top) * 0.5f - head.position.y;
                    if (Mathf.Abs(fromVertices - eyes) > 0.001f)
                    {
                        problems.Add(who + " 頂点から見た目の高さは " + fromVertices.ToString("0.000") + " m");
                    }
                }
            }

            if (!hasEyeWhite)
            {
                problems.Add(who + " 白目が無い");
            }

            return problems;
        }

        /// <summary>材質の名前から顔の部品（FaceParts のどれか）を返す。顔の部品でなければ null。</summary>
        private static string PartOf(Material material)
        {
            if (material == null)
            {
                return null;
            }

            foreach (string part in FaceParts)
            {
                if (material.name.EndsWith(part, System.StringComparison.Ordinal))
                {
                    return part;
                }
            }

            return null;
        }

        private static float HalfTan(float fieldOfView)
        {
            return Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
