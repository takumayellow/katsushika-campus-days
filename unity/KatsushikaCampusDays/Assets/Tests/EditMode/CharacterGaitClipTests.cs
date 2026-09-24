using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 取り込んだキャラクター FBX の Walk クリップを実際にサンプルして、
    /// 頭が横に振れていない（くねくねしていない）ことを見張る（#43）。
    ///
    /// 直す前の Walk は頭が 7.1 cm 横に振れ、頭のロールが 8 度つき、
    /// 接地した足が毎秒 4.3 m 滑っていた。ここでは Unity 側に取り込んだあとの姿を測る。
    /// FBX やアバターがまだ無い環境（CI の素の clone など）では Ignore で抜ける。
    /// </summary>
    public sealed class CharacterGaitClipTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";
        private const int Samples = 32;

        /// <summary>頭の横振れの上限（m）。実測 0.006、直す前は 0.071。</summary>
        private const float HeadSwayLimit = 0.02f;

        /// <summary>頭のヨーの上限（度 p-p）。実測 1.1、直す前は 5.4。</summary>
        private const float HeadYawLimit = 4.0f;

        [Test]
        public void WalkClip_KeepsTheHeadSteadyOverTheStride()
        {
            string path = FindCharacterModel();
            if (path == null)
            {
                Assert.Ignore(CharactersFolder + " に FBX がまだ無い");
            }

            AnimationClip clip = FindClip(path, "walk");
            if (clip == null || clip.length <= 0f)
            {
                Assert.Ignore(path + " に Walk クリップが無い");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Assert.Ignore(path + " を読めない");
            }

            GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = go.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid
                    || !animator.isHuman)
                {
                    Assert.Ignore("Humanoid アバターがまだ作られていない");
                }

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (head == null || hips == null || foot == null)
                {
                    Assert.Ignore("Head / Hips / LeftFoot がアバターに無い");
                }

                var headX = new Span(); var headZ = new Span(); var headYaw = new Span();
                var hipsY = new Span(); var hipsYaw = new Span();
                var footX = new Span(); var footZ = new Span(); var footY = new Span();
                // 基準は骨ごとに取る。頭の基準で骨盤を測ると、リグのボーン軸の向き次第で
                // 差が ±180 度をまたぎ、DeltaAngle が折り返して p-p が 360 度に化ける。
                float headYawBase = float.NaN;
                float hipsYawBase = float.NaN;

                for (int i = 0; i < Samples; i++)
                {
                    clip.SampleAnimation(go, clip.length * i / Samples);
                    Vector3 h = head.position;
                    Vector3 f = foot.position;
                    headX.Add(h.x); headZ.Add(h.z);
                    hipsY.Add(hips.position.y);
                    footX.Add(f.x); footZ.Add(f.z); footY.Add(f.y);
                    if (float.IsNaN(headYawBase))
                    {
                        headYawBase = head.eulerAngles.y;
                        hipsYawBase = hips.eulerAngles.y;
                    }

                    headYaw.Add(Mathf.DeltaAngle(headYawBase, head.eulerAngles.y));
                    hipsYaw.Add(Mathf.DeltaAngle(hipsYawBase, hips.eulerAngles.y));
                }

                float stride = Mathf.Max(footX.Pp, footZ.Pp);
                if (stride < 0.05f && hipsY.Pp < 0.002f)
                {
                    Assert.Ignore("SampleAnimation でクリップが再生されていない（姿勢が動かない）");
                }

                // 足がどちらの軸へ振れているかで前後方向を決め、頭はその直交方向の振れを見る。
                bool forwardIsZ = footZ.Pp >= footX.Pp;
                float sway = forwardIsZ ? headX.Pp : headZ.Pp;

                Assert.Greater(stride, 0.10f, "足がほとんど振れていない: " + clip.name);
                Assert.LessOrEqual(sway, HeadSwayLimit,
                    "頭が横に振れすぎ（くねくね）: " + sway.ToString("0.000") + " m / " + path);
                Assert.LessOrEqual(headYaw.Pp, HeadYawLimit,
                    "頭のヨーが大きすぎ: " + headYaw.Pp.ToString("0.0") + " 度");
                Assert.Greater(hipsYaw.Pp, 1.5f, "骨盤のひねりが入っていない（ルートモーションに抜けた疑い）");
                Assert.Less(hipsYaw.Pp, 20f, "骨盤のひねりが大きすぎ");
                Assert.Greater(footY.Min, -0.03f, "足が床にめり込んでいる: " + footY.Min.ToString("0.000") + " m");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>最小・最大だけ覚えておく入れ物。</summary>
        private sealed class Span
        {
            private float _min = float.MaxValue;
            private float _max = float.MinValue;

            public float Min => _min;

            public float Pp => _max - _min;

            public void Add(float value)
            {
                _min = Mathf.Min(_min, value);
                _max = Mathf.Max(_max, value);
            }
        }

        private static string FindCharacterModel()
        {
            if (!AssetDatabase.IsValidFolder(CharactersFolder))
            {
                return null;
            }

            string first = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (path.Contains("/mirai/"))
                {
                    return path;
                }

                first ??= path;
            }

            return first;
        }

        private static AnimationClip FindClip(string path, string key)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip
                    && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)
                    && clip.name.ToLowerInvariant().Contains(key))
                {
                    return clip;
                }
            }

            return null;
        }
    }
}
