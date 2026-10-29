using System;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// KCD/Toon のマテリアルのうち、描いても画に出ないパスを切る (#15)。
    /// シェーダ（KCD_Toon.shader）は変えず、マテリアルの側でパスを無効にする（.mat の disabledShaderPasses）。
    ///
    /// 輪郭のパス（SRPDefaultUnlit）は、法線の向きへ _OutlineWidth だけ押し出した殻の裏面を輪郭の色で描く。
    /// 幅が 0 だと殻は元の面に重なって線にならず、描画コールだけが 1 回ずつ増える。
    ///
    /// - 木（幹と葉）: 幅 0 なので輪郭を切る。葉の塊の内側は、それまで輪郭の殻（裏面）が暗い色で塞いでいたので、
    ///   葉は両面で描く（_Cull = 0）。シェーダは裏面の法線を裏返して照らすので、塊に入ったカメラには葉の緑が見える。
    /// - 顔の細部（<see cref="FaceSlots"/>）: 幅 0 のものは輪郭を切る。どれも顔の表面すれすれに貼った薄い板か細い帯で、
    ///   落とす影は顔の面とほとんど重なるので、影のパス（ShadowCaster）も切る。幅のあるもの（線を付けた眉・まつ毛）は輪郭を残す。
    ///
    /// MaterialLibrary が .mat を作る・合わせ直すときにこの規則を通す。モブの .mat（MobStage）は、元の .mat で切ってあるパスを写す。
    /// </summary>
    public static class ToonPasses
    {
        /// <summary>輪郭（反転ハル）のパスの LightMode。</summary>
        public const string OutlinePass = "SRPDefaultUnlit";

        /// <summary>影のマップに描くパスの LightMode。</summary>
        public const string ShadowPass = "ShadowCaster";

        /// <summary>モブの遠い段の .mat の名前の末尾（MobStage）。</summary>
        public const string FarSuffix = "_lod1";

        /// <summary>顔の細部の部品名。.mat の名前は「キャラ_部品」（モブの遠い段は末尾に _lod1）。</summary>
        public static readonly string[] FaceSlots = { "brow", "eye_l", "eye_r", "eye_rim", "eye_white", "lash" };

        private static readonly string[] Folders =
        {
            MaterialLibrary.CampusFolder,
            MaterialLibrary.CharacterFolder,
            MobStage.MobMaterialFolder,
        };

        private const string OutlineWidth = "_OutlineWidth";
        private const string Cull = "_Cull";

        /// <summary>木の幹と葉の .mat か（MaterialLibrary.EnsureCampus の foliage と同じ名前）。</summary>
        public static bool IsTree(string name)
        {
            return name == "trunk" || name.StartsWith("leaf", StringComparison.Ordinal);
        }

        /// <summary>顔の細部の .mat か。「madonna_boots_brown」のように部品名で終わらないものは含めない。</summary>
        public static bool IsFacePart(string name)
        {
            if (name.EndsWith(FarSuffix, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - FarSuffix.Length);
            }

            foreach (string slot in FaceSlots)
            {
                if (name == slot || name.EndsWith("_" + slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 規則に合わせてパスを切る。name は .mat の名前（拡張子なし）か部品名。
        /// 変えたら true を返し、アセットなら dirty にする。KCD/Toon でない（_OutlineWidth を持たない）ものは触らない。
        /// </summary>
        public static bool Apply(Material material, string name)
        {
            if (material == null || string.IsNullOrEmpty(name) || !material.HasProperty(OutlineWidth))
            {
                return false;
            }

            bool tree = IsTree(name);
            bool face = IsFacePart(name);
            if (!tree && !face)
            {
                return false;
            }

            bool changed = false;
            if (material.GetFloat(OutlineWidth) <= 0f)
            {
                changed |= Disable(material, OutlinePass);
            }

            if (face)
            {
                changed |= Disable(material, ShadowPass);
            }

            if (tree && name.StartsWith("leaf", StringComparison.Ordinal) && material.HasProperty(Cull)
                && material.GetFloat(Cull) != 0f)
            {
                material.SetFloat(Cull, 0f);
                changed = true;
            }

            if (changed && EditorUtility.IsPersistent(material))
            {
                EditorUtility.SetDirty(material);
            }

            return changed;
        }

        /// <summary>
        /// キャンパス・キャラクター・モブの .mat をすべて規則に合わせて保存する。
        /// batchmode からは -executeMethod KCD.Editor.ToonPasses.ApplyAll で呼ぶ。
        /// </summary>
        [MenuItem("KCD/トゥーンの不要なパスを切る")]
        public static void ApplyAll()
        {
            int seen = 0;
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", Folders))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null)
                {
                    continue;
                }

                seen++;
                if (Apply(material, material.name))
                {
                    changed++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ToonPasses] マテリアル " + seen + " 枚のうち " + changed + " 枚のパスを切りました");
        }

        private static bool Disable(Material material, string pass)
        {
            if (!material.GetShaderPassEnabled(pass))
            {
                return false;
            }

            material.SetShaderPassEnabled(pass, false);
            return true;
        }
    }
}
