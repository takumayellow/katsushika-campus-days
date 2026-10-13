using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 屋内の窓の外に見えるキャンパスの遠景 (#60)。
    ///
    /// 屋内はキャンパスから遠く（x = 1200 m 以降）に置いてあるので、窓の外には本物のキャンパスが無い。
    /// そこで SceneBuilder（InteriorBackdropStage）が、その建物の位置と向きからキャンパスを 360 度撮って
    /// 1 枚のパノラマに焼き、屋内を囲む閉じたドーム（地面の円盤 + 円筒の帯 + ふた）に貼る。
    /// このコンポーネントは、今いる建物のドームだけを描き、明るさを時刻（夕方・夜）に合わせる。
    ///
    /// 撮り直し（InteriorBackdropStage.Bake）が読むので、撮ったときの位置と向きもここに残す。
    /// </summary>
    public sealed class InteriorBackdrop : MonoBehaviour
    {
        /// <summary>
        /// 窓のすぐ外の近景（舗装・芝・生垣・木）のメッシュ名の頭。kcd_interior が外周の壁の外にだけ置く (#45)。
        /// プレイヤーは届かないので当たり判定を付けず、描くだけにする (#60)。
        /// </summary>
        public const string ExteriorPrefix = "ext_";

        /// <summary>夜の色。昼の画をこの色で掛けて暗くする。</summary>
        public static readonly Color NightTint = new Color(0.18f, 0.21f, 0.34f, 1f);

        /// <summary>夜明けの色。</summary>
        public static readonly Color DawnTint = new Color(0.80f, 0.68f, 0.66f, 1f);

        /// <summary>夕方の色。DayNightCycle の日没どきの橙に寄せる。</summary>
        public static readonly Color DuskTint = new Color(1.00f, 0.68f, 0.50f, 1f);

        /// <summary>
        /// 時刻（時）と色の対応。間は線形につなぎ、24 時で 0 時へ戻る。
        /// DayNightCycle の日の出 5.5 時・日没 18.5 時をはさむように置いてある。
        /// </summary>
        private static readonly float[] TintHours = { 0f, 4.5f, 5.8f, 7.5f, 15.5f, 18.0f, 19.5f, 24f };

        private static readonly Color[] TintColors =
        {
            NightTint, NightTint, DawnTint, Color.white, Color.white, DuskTint, NightTint, NightTint,
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>どの建物の遠景か（InteriorLoader の id と同じ）。</summary>
        public string BuildingId;

        /// <summary>ドームを描く Renderer。</summary>
        public Renderer Target;

        /// <summary>撮ったときの目の位置（キャンパスのワールド座標）。</summary>
        public Vector3 CampusEye;

        /// <summary>屋内の +Z（入口から奥）がキャンパスで向いている方位（度）。</summary>
        public float YawDegrees;

        /// <summary>パノラマの下端の tan(仰角)。負の値（水平より下）。</summary>
        public float TanBottom = -0.25f;

        /// <summary>パノラマの上端の tan(仰角)。</summary>
        public float TanTop = 0.84f;

        /// <summary>
        /// 近景 ext_* が受け持つ範囲。建物の矩形の半幅（屋内のローカルの x, z）と、そこからの距離（m）。
        /// この範囲にすっぽり入るものは近景が立体で描くので、パノラマには写さない。近景が無い屋内では 0。
        /// </summary>
        public Vector2 NearHalfSize;
        public float NearRadius;

        private MaterialPropertyBlock _block;
        private Color _applied = new Color(-1f, -1f, -1f, -1f);

        /// <summary>メッシュ名が窓の外の近景か（ext_ で始まる。大文字小文字は見ない）。</summary>
        public static bool IsExteriorDressing(string objectName)
        {
            return !string.IsNullOrEmpty(objectName)
                && objectName.StartsWith(ExteriorPrefix, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// この建物の遠景を描くか。出入り係が無い（エディタで開いただけ）ときは描く。
        /// 出入り係があるときは、その建物の中にいるときだけ描く。
        /// </summary>
        public static bool VisibleFor(bool hasLoader, string currentId, string buildingId)
        {
            if (!hasLoader)
            {
                return true;
            }

            return !string.IsNullOrEmpty(currentId) && currentId == buildingId;
        }

        /// <summary>時刻（0-24 時。範囲の外は 24 で折り返す）の遠景の色。昼は白（そのまま）。</summary>
        public static Color TintAt(float hours)
        {
            float h = Mathf.Repeat(hours, 24f);
            for (int i = 1; i < TintHours.Length; i++)
            {
                if (h <= TintHours[i])
                {
                    float span = TintHours[i] - TintHours[i - 1];
                    float t = span > 1e-4f ? (h - TintHours[i - 1]) / span : 1f;
                    return Color.Lerp(TintColors[i - 1], TintColors[i], t);
                }
            }

            return TintColors[TintColors.Length - 1];
        }

        private void Start()
        {
            Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (Target == null)
            {
                return;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            bool visible = VisibleFor(loader != null, loader != null ? loader.CurrentId : null, BuildingId);
            if (Target.enabled != visible)
            {
                Target.enabled = visible;
            }

            // GameManager.Instance は無ければ作るので、再生中だけ読む。
            if (!visible || !Application.isPlaying)
            {
                return;
            }

            ApplyTint(TintAt(GameManager.Instance.GameTimeHours));
        }

        private void ApplyTint(Color tint)
        {
            if (tint == _applied)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, tint);
            Target.SetPropertyBlock(_block);
            _applied = tint;
        }
    }
}
