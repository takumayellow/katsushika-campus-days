using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 写真スポットの記念写真の構図 (#170)。三脚から look_dir の向きへ Distance だけ離れた所にキャラが立ち、
    /// 三脚のほうを向く。三脚のカメラはキャラ越しに look_dir の向きの景色を撮る。
    /// カメラの高さと狙う高さは、書かなければキャラの背丈から決める（2〜3 頭身のちびキャラも 1.6 m の人も同じ写り方）。
    /// 値は collectibles.json の photo_spots[].selfie で上書きできる。
    /// </summary>
    public sealed class SelfieComposition
    {
        /// <summary>三脚からキャラの立ち位置までの距離（m）。</summary>
        public const float DefaultDistance = 3f;

        /// <summary>カメラの高さ / 背丈。目の高さのあたりから撮ると、見下ろさずに背景の建物が上に抜ける。</summary>
        public const float CameraHeightRatio = 0.9f;

        /// <summary>
        /// 狙う高さ / 背丈。2.65 m 先を縦 50 度で撮ると、背丈 1.2 m 台（坊っちゃん 1.28 m・マドンナちゃん 1.22 m）なら
        /// 足が画面の下 1 割、頭が 6 割の高さに来て、上の 4 割に景色が入る。1.6 m なら足の裏がほぼ画面の下端に来る。
        /// </summary>
        public const float AimRatio = 0.8f;

        /// <summary>
        /// 狙いを横へずらす量（m、カメラから見て右が正）。正ならキャラは画面の左に寄り、右に景色が開く。
        /// </summary>
        public const float DefaultAimSide = 0f;

        /// <summary>縦の画角（度）。</summary>
        public const float DefaultFieldOfView = 50f;

        /// <summary>
        /// 立ち位置を look_dir から回したとき、景色のほうへ狙いをずらす量 / Distance。
        /// 横を向いて撮っても、look_dir の先の景色が画面の片側に入る。
        /// </summary>
        public const float TurnedAimShift = 0.4f;

        /// <summary>カメラを三脚の頭からキャラのほうへ出す量（m）。三脚の本体が写り込まないように。</summary>
        public const float LensForward = 0.35f;

        /// <summary>カメラとキャラがこれより近いと頭か足が切れるので、カメラを後ろへ引く（m、水平）。</summary>
        public const float MinShotDistance = 2.2f;

        /// <summary>背丈が測れなかったときに使う背丈（m）。</summary>
        public const float FallbackHeight = PlayerAppearance.ReferenceHeight;

        public float Distance = DefaultDistance;

        /// <summary>カメラの高さ（三脚の足もとから、m）。NaN なら背丈 × CameraHeightRatio。</summary>
        public float CameraHeight = float.NaN;

        /// <summary>狙う高さ（キャラの足もとから、m）。NaN なら背丈 × AimRatio。</summary>
        public float AimHeight = float.NaN;

        public float AimSide = DefaultAimSide;
        public float FieldOfView = DefaultFieldOfView;

        /// <summary>
        /// 立ち位置を look_dir から回す角度（度、上から見て時計回りが正）。手すりの向こうなど、
        /// look_dir の先に立てないスポットで横へ逃がす。カメラはそのままキャラを向くので、景色は斜めに入る。
        /// </summary>
        public float StandAngle;

        /// <summary>JSON の selfie。無い項目と範囲外の値は既定のまま。node が null なら全部既定。</summary>
        public static SelfieComposition Read(Dictionary<string, object> node)
        {
            var composition = new SelfieComposition();
            if (node == null)
            {
                return composition;
            }

            composition.Distance = Pick(node, "distance", DefaultDistance, 1.5f, 8f);
            composition.CameraHeight = Pick(node, "cam_height", float.NaN, 0.5f, 3f);
            composition.AimHeight = Pick(node, "aim_height", float.NaN, 0.3f, 2.5f);
            composition.AimSide = Pick(node, "aim_side", DefaultAimSide, -3f, 3f);
            composition.FieldOfView = Pick(node, "fov", DefaultFieldOfView, 20f, 75f);
            composition.StandAngle = Pick(node, "stand_angle", 0f, -120f, 120f);
            return composition;
        }

        /// <summary>背丈が測れていなければ（0 以下・NaN・小さすぎ）FallbackHeight。</summary>
        public static float UsableHeight(float subjectHeight)
        {
            return subjectHeight > 0.3f ? subjectHeight : FallbackHeight;
        }

        /// <summary>背丈 subjectHeight のキャラを撮るときのカメラの高さ（三脚の足もとから）。</summary>
        public float CameraHeightFor(float subjectHeight)
        {
            return float.IsNaN(CameraHeight) ? UsableHeight(subjectHeight) * CameraHeightRatio : CameraHeight;
        }

        /// <summary>背丈 subjectHeight のキャラを撮るときに狙う高さ（キャラの足もとから）。</summary>
        public float AimHeightFor(float subjectHeight)
        {
            return float.IsNaN(AimHeight) ? UsableHeight(subjectHeight) * AimRatio : AimHeight;
        }

        /// <summary>
        /// キャラの立ち位置。三脚から look_dir を StandAngle だけ回した向きへ Distance（高さは三脚と同じ）。
        /// 向きが無ければ三脚の位置。
        /// </summary>
        public Vector3 StandPoint(Vector3 tripod, Vector2 lookDir)
        {
            return StandPoint(tripod, lookDir, 0f);
        }

        /// <summary>StandPoint をさらに turn 度回した立ち位置（そこに立てないときに横へ逃がす）。</summary>
        public Vector3 StandPoint(Vector3 tripod, Vector2 lookDir, float turn)
        {
            Vector3 direction = Quaternion.Euler(0f, StandAngle + turn, 0f) * Planar(lookDir);
            return tripod + direction * Distance;
        }

        /// <summary>
        /// 狙いを横へずらす量。立ち位置を look_dir から turnFromLook 度回したときは、look_dir の側へ
        /// TurnedAimShift ぶん足す（キャラは反対側に寄り、景色が開く）。
        /// </summary>
        public float AimSideFor(float turnFromLook)
        {
            return AimSide - Mathf.Sin(turnFromLook * Mathf.Deg2Rad) * Distance * TurnedAimShift;
        }

        /// <summary>
        /// キャラが三脚に近すぎて（立ち位置まで行けなかったとき）、カメラを後ろへどれだけ引けば
        /// MinShotDistance を保てるか（m）。足りていれば 0。
        /// </summary>
        public static float BackOffNeeded(Vector3 tripod, Vector3 subjectFeet)
        {
            Vector3 toSubject = subjectFeet - tripod;
            toSubject.y = 0f;
            return Mathf.Max(0f, MinShotDistance - (toSubject.magnitude - LensForward));
        }

        /// <summary>
        /// 三脚のカメラの位置と向き。subjectFeet は実際にキャラが立った足もと（立ち位置まで行けなかったときもそこを撮る）。
        /// backOff だけカメラを三脚から後ろ（キャラと反対）へ引く。turnFromLook は立ち位置を look_dir から回した角度（度）。
        /// </summary>
        public void CameraPose(Vector3 tripod, Vector3 subjectFeet, float subjectHeight, float backOff,
            out Vector3 position, out Quaternion rotation)
        {
            CameraPose(tripod, subjectFeet, subjectHeight, backOff, StandAngle, out position, out rotation);
        }

        /// <inheritdoc cref="CameraPose(Vector3, Vector3, float, float, out Vector3, out Quaternion)"/>
        public void CameraPose(Vector3 tripod, Vector3 subjectFeet, float subjectHeight, float backOff, float turnFromLook,
            out Vector3 position, out Quaternion rotation)
        {
            Vector3 forward = Toward(tripod, subjectFeet);
            position = tripod + Vector3.up * CameraHeightFor(subjectHeight) + forward * (LensForward - Mathf.Max(0f, backOff));

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 aim = subjectFeet + Vector3.up * AimHeightFor(subjectHeight) + right * AimSideFor(turnFromLook);
            Vector3 look = aim - position;
            rotation = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.LookRotation(forward);
        }

        /// <summary>三脚からキャラへの水平の向き。重なっていれば +z。</summary>
        public static Vector3 Toward(Vector3 tripod, Vector3 subjectFeet)
        {
            Vector3 toSubject = subjectFeet - tripod;
            toSubject.y = 0f;
            return toSubject.sqrMagnitude > 1e-4f ? toSubject.normalized : Vector3.forward;
        }

        /// <summary>キャラが三脚のほうを向く角度（度、+z が 0）。重なっていれば現在の向き fallback を返す。</summary>
        public static float FacingYaw(Vector3 subjectFeet, Vector3 tripod, float fallback)
        {
            Vector3 d = tripod - subjectFeet;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : fallback;
        }

        private static Vector3 Planar(Vector2 lookDir)
        {
            var direction = new Vector3(lookDir.x, 0f, lookDir.y);
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.zero;
        }

        private static float Pick(Dictionary<string, object> node, string key, float fallback, float min, float max)
        {
            if (!node.ContainsKey(key))
            {
                return fallback;
            }

            float value = MiniJson.GetFloat(node, key, float.NaN);
            return value >= min && value <= max ? value : fallback;
        }
    }
}
