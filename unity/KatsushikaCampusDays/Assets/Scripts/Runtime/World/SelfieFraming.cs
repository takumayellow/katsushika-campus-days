using UnityEngine;

namespace KCD
{
    /// <summary>記念写真を撮った構図。テストが、撮ったときと同じ構図でキャラが写っているかを確かめる。</summary>
    public readonly struct SelfiePose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float FieldOfView;

        /// <summary>構図に使ったキャラの背丈（m）。</summary>
        public readonly float SubjectHeight;

        /// <summary>キャラが近すぎてカメラを三脚から後ろへ引いた量（m）。</summary>
        public readonly float BackOff;

        /// <summary>立ち位置を look_dir からさらに回した角度（度）。正面に立てたなら 0。</summary>
        public readonly float Turn;

        public SelfiePose(Vector3 position, Quaternion rotation, float fieldOfView, float subjectHeight, float backOff,
            float turn)
        {
            Position = position;
            Rotation = rotation;
            FieldOfView = fieldOfView;
            SubjectHeight = subjectHeight;
            BackOff = backOff;
            Turn = turn;
        }
    }

    /// <summary>
    /// 記念写真の構図を、シーンに合わせて決める (#170)。look_dir の先に立てなければ（手すり・壁・吹き抜け）
    /// 立ち位置を横へ回し、撮る瞬間はキャラの背丈を測って、三脚に近すぎればカメラを後ろへ引く
    /// （後ろの壁にはめり込ませない）。構図そのものの計算は <see cref="SelfieComposition"/>。
    /// </summary>
    public static class SelfieFraming
    {
        /// <summary>カメラを引くとき、後ろの壁からこれだけ手前で止める（m）。</summary>
        private const float WallClearance = 0.3f;

        /// <summary>後ろの壁を探る球の半径（m）。カメラの近クリップより太くして、壁の縁をかすめないように。</summary>
        private const float ProbeRadius = 0.15f;

        /// <summary>立ち位置までの道を探る球の半径と高さ（m）。キャラの胴の太さで、手すりの高さを通す。</summary>
        private const float PathRadius = 0.25f;

        private const float PathHeight = 0.5f;

        /// <summary>立ち位置の床が三脚の床からこれ以上ずれていたら（段の下・吹き抜け）立てないとみなす（m）。</summary>
        private const float FloorTolerance = 0.5f;

        /// <summary>立ち位置を探す順（look_dir からの角度、度）。正面がふさがっていれば少しずつ横へ。</summary>
        private static readonly float[] Turns = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f };

        /// <summary>
        /// 立ち位置を look_dir からさらに何度回すか。三脚から立ち位置までを胴の太さの球で探り、
        /// ふさがっているか床が無ければ次の角度を試す。どこも駄目なら 0（正面へ走って止まった所で撮る）。
        /// ignore（三脚とプレイヤー）の当たり判定は数えない。
        /// </summary>
        public static float ChooseTurn(SelfieComposition composition, Vector3 tripod, Vector2 lookDir, params Transform[] ignore)
        {
            foreach (float turn in Turns)
            {
                if (CanStand(tripod, composition.StandPoint(tripod, lookDir, turn), ignore))
                {
                    return turn;
                }
            }

            return 0f;
        }

        /// <summary>三脚 tripod から、足もと feet に立つ subject を撮る構図。subject が null なら既定の背丈で組む。</summary>
        /// turn は ChooseTurn で回した角度（度）。
        public static SelfiePose Aim(SelfieComposition composition, Vector3 tripod, Vector3 feet, float turn, Transform subject)
        {
            float height = SelfieComposition.UsableHeight(MeasureSubject(subject, feet.y));
            float backOff = Room(composition, tripod, feet, height, SelfieComposition.BackOffNeeded(tripod, feet));
            composition.CameraPose(tripod, feet, height, backOff, composition.StandAngle + turn,
                out Vector3 position, out Quaternion rotation);
            return new SelfiePose(position, rotation, composition.FieldOfView, height, backOff, turn);
        }

        /// <summary>
        /// 肩越しカメラが壁際で寄りすぎてプレイヤーの体を隠していたら（CameraNearHider）、撮る前に出す。
        /// 撮るのは同じフレームの中なので、次のフレームで肩越しカメラがまた要るなら隠し直す。
        /// </summary>
        public static void RevealSubject()
        {
#if KCD_CINEMACHINE
            CinemachineCameraGuard guard = Object.FindAnyObjectByType<CinemachineCameraGuard>();
            if (guard != null)
            {
                guard.ShowPlayer();
            }
#endif
        }

        private static bool CanStand(Vector3 tripod, Vector3 standPoint, Transform[] ignore)
        {
            Vector3 from = tripod + Vector3.up * PathHeight;
            Vector3 path = standPoint + Vector3.up * PathHeight - from;
            float length = path.magnitude;
            if (length < 1e-3f)
            {
                return false;
            }

            foreach (RaycastHit hit in Physics.SphereCastAll(from, PathRadius, path / length, length, ~0,
                         QueryTriggerInteraction.Ignore))
            {
                if (!Ignored(hit.collider.transform, ignore))
                {
                    return false;
                }
            }

            const float probe = 1.5f;
            foreach (RaycastHit hit in Physics.RaycastAll(standPoint + Vector3.up * probe, Vector3.down, probe + FloorTolerance,
                         ~0, QueryTriggerInteraction.Ignore))
            {
                if (!Ignored(hit.collider.transform, ignore) && Mathf.Abs(hit.point.y - tripod.y) <= FloorTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Ignored(Transform hit, Transform[] ignore)
        {
            foreach (Transform root in ignore)
            {
                if (root != null && hit.IsChildOf(root))
                {
                    return true;
                }
            }

            return false;
        }

        private static float MeasureSubject(Transform subject, float groundY)
        {
            if (subject == null)
            {
                return 0f;
            }

            PlayerAppearance appearance = subject.GetComponent<PlayerAppearance>();
            if (appearance != null && appearance.BodyHeight > 0f)
            {
                return appearance.BodyHeight;
            }

            return PlayerAppearance.MeasureHeight(subject.gameObject, groundY);
        }

        /// <summary>カメラを needed だけ後ろへ引けるか。後ろに壁があれば、その手前までの量。</summary>
        private static float Room(SelfieComposition composition, Vector3 tripod, Vector3 feet, float height, float needed)
        {
            if (needed <= 0f)
            {
                return 0f;
            }

            Vector3 forward = SelfieComposition.Toward(tripod, feet);
            Vector3 lens = tripod + Vector3.up * composition.CameraHeightFor(height) + forward * SelfieComposition.LensForward;
            if (Physics.SphereCast(lens, ProbeRadius, -forward, out RaycastHit hit, needed + WallClearance,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Clamp(hit.distance - WallClearance, 0f, needed);
            }

            return needed;
        }
    }
}
