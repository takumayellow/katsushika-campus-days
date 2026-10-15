using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 屋内のエスカレーターの 1 区間。下の端 (原点) から上の端 (<see cref="_top"/>) へ（下りの区間は上の端から下の端へ）、
    /// 乗っているプレイヤーを運び、段の部品を 1 段ぶんずつ繰り返し動かして流れて見せる。当たり判定は 2 点を結ぶ斜めの板
    /// （<see cref="AddRamp"/> が子に作る BoxCollider）で、段の部品には当たり判定を付けない。
    /// </summary>
    public sealed class Escalator : MonoBehaviour
    {
        /// <summary>段の線に沿った速さ（m/s）。実物のエスカレーターの定格 30 m/min に合わせる。</summary>
        public const float DefaultSpeed = 0.5f;

        /// <summary>運ぶ範囲の幅の半分（m）。列の幅 1.8 m から両側の腹板 0.14 m を除いた段の幅の半分。</summary>
        public const float DefaultHalfWidth = 0.76f;

        /// <summary>足が段の線からこれより離れていたら乗っていないとみなす（m）。</summary>
        public const float VerticalTolerance = 0.45f;

        /// <summary>降り口の端（上りは上の端、下りは下の端）を越えてからも運ぶ長さ（m）。降り口で止まって段に押し戻されないよう、平場へ送り出す。</summary>
        public const float RunOff = 0.6f;

        /// <summary>当たり判定の板の厚み（m）。</summary>
        public const float RampThickness = 0.1f;

        private static readonly List<Escalator> Active = new List<Escalator>();

        [SerializeField] private Vector3 _top;
        [SerializeField] private float _speed = DefaultSpeed;
        [SerializeField] private float _halfWidth = DefaultHalfWidth;
        [SerializeField] private int _stepCount = 1;
        [SerializeField] private Transform _steps;
        [SerializeField] private Vector3 _stepsHome;
        [SerializeField] private bool _down;

        /// <summary>
        /// InteriorEscalatorStage から。top は上の端（この Transform のローカル）、stepCount は段の数、
        /// steps は段の部品（無ければ null）、down は上の端から下の端へ運ぶ下りの区間か。
        /// </summary>
        public void Configure(Vector3 top, int stepCount, Transform steps, float speed = DefaultSpeed,
            float halfWidth = DefaultHalfWidth, bool down = false)
        {
            _down = down;
            _top = top;
            _stepCount = Mathf.Max(1, stepCount);
            _steps = steps;
            _stepsHome = steps != null ? steps.localPosition : Vector3.zero;
            _speed = speed;
            _halfWidth = halfWidth;
        }

        /// <summary>
        /// 下の端から上の端までの斜めの板（BoxCollider）を子に作る。上の面が 2 点を結ぶ線に乗り、幅は運ぶ範囲と同じ。
        /// layer が負なら変えない。作った板を返す。
        /// </summary>
        public BoxCollider AddRamp(int layer)
        {
            var ramp = new GameObject("Ramp");
            ramp.transform.SetParent(transform, false);
            Vector3 axis = transform.TransformVector(_top);
            Quaternion tilt = Quaternion.LookRotation(axis.normalized, Vector3.up);
            Vector3 up = tilt * Vector3.up;
            ramp.transform.SetPositionAndRotation(
                Bottom + axis * 0.5f - up * (RampThickness * 0.5f), tilt);
            BoxCollider box = ramp.AddComponent<BoxCollider>();
            box.size = new Vector3(_halfWidth * 2f, RampThickness, axis.magnitude);
            if (layer >= 0)
            {
                ramp.layer = layer;
            }

            return box;
        }

        /// <summary>下の端（ワールド）。</summary>
        public Vector3 Bottom => transform.position;

        /// <summary>上の端（ワールド）。</summary>
        public Vector3 Top => transform.TransformPoint(_top);

        /// <summary>段の数。</summary>
        public int StepCount => _stepCount;

        /// <summary>段の部品（無ければ null）。</summary>
        public Transform Steps => _steps;

        /// <summary>上の端から下の端へ運ぶ下りの区間か。</summary>
        public bool Down => _down;

        private void OnEnable()
        {
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        private void OnDisable()
        {
            Active.Remove(this);
            if (_steps != null)
            {
                _steps.localPosition = _stepsHome;
            }
        }

        private void Update()
        {
            if (_steps == null)
            {
                return;
            }

            float phase = StepPhase(Time.time, _speed, (_top / _stepCount).magnitude);
            if (_down)
            {
                phase = 1f - phase;
            }

            Vector3 step = _steps.parent != null
                ? _steps.parent.InverseTransformVector(transform.TransformVector(_top / _stepCount))
                : transform.TransformVector(_top / _stepCount);
            _steps.localPosition = _stepsHome + step * phase;
        }

        /// <summary>
        /// 段がいま 1 段のうちどこまで進んだか（0 以上 1 未満）。stepLength は 1 段ぶんの段の線の長さ（m）。
        /// </summary>
        public static float StepPhase(float time, float speed, float stepLength)
        {
            if (stepLength <= 0f || speed <= 0f)
            {
                return 0f;
            }

            return Mathf.Repeat(time * speed / stepLength, 1f);
        }

        /// <summary>
        /// 足の位置 feet を、bottom と top を結ぶ区間がいま運ぶ水平の速度（m/s）。乗っていなければ zero。
        /// 上りは bottom→top、down なら top→bottom の向きに運ぶ。段の線を水平に投影した長さの中（降り口の端の先は
        /// <see cref="RunOff"/> まで）、幅の半分 halfWidth の中、段の線の高さから <see cref="VerticalTolerance"/> の中に
        /// いるときだけ運ぶ。
        /// </summary>
        public static Vector3 CarryVelocity(Vector3 bottom, Vector3 top, float halfWidth, float speed, Vector3 feet,
            bool down = false)
        {
            return CarryVelocity(bottom, top, halfWidth, speed, feet, down, out _);
        }

        /// <summary><see cref="CarryVelocity(Vector3, Vector3, float, float, Vector3, bool)"/> と同じ。gap は足と段の線の高さの差（m）。</summary>
        private static Vector3 CarryVelocity(Vector3 bottom, Vector3 top, float halfWidth, float speed, Vector3 feet,
            bool down, out float gap)
        {
            gap = float.PositiveInfinity;
            Vector3 axis = top - bottom;
            Vector3 flat = new Vector3(axis.x, 0f, axis.z);
            float run = flat.magnitude;
            if (run < 0.01f || axis.magnitude < 0.01f)
            {
                return Vector3.zero;
            }

            Vector3 along = flat / run;
            Vector3 rel = feet - bottom;
            float t = Vector3.Dot(new Vector3(rel.x, 0f, rel.z), along);
            if (t < (down ? -RunOff : 0f) || t > run + (down ? 0f : RunOff))
            {
                return Vector3.zero;
            }

            Vector3 side = Vector3.Cross(Vector3.up, along);
            if (Mathf.Abs(Vector3.Dot(rel, side)) > halfWidth)
            {
                return Vector3.zero;
            }

            float lineY = Mathf.Lerp(bottom.y, top.y, Mathf.Clamp01(t / run));
            if (Mathf.Abs(feet.y - lineY) > VerticalTolerance)
            {
                return Vector3.zero;
            }

            gap = Mathf.Abs(feet.y - lineY);
            // 段の線に沿った速さ speed の水平の成分。高さは斜めの板の上を歩くのと同じく CharacterController が拾う。
            return along * ((down ? -speed : speed) * run / axis.magnitude);
        }

        /// <summary>
        /// 足の位置 feet を、いま有効なエスカレーターが運ぶ水平の速度。乗っていなければ zero。
        /// 2 区間の範囲が重なるところ（降り口の先と次の区間の乗り口など）では、段の線の高さが足にいちばん近い区間で決める。
        /// </summary>
        public static Vector3 CarryAt(Vector3 feet)
        {
            Vector3 best = Vector3.zero;
            float bestGap = float.PositiveInfinity;
            for (int i = 0; i < Active.Count; i++)
            {
                Escalator e = Active[i];
                if (e == null)
                {
                    continue;
                }

                Vector3 v = CarryVelocity(e.Bottom, e.Top, e._halfWidth, e._speed, feet, e._down, out float gap);
                if (gap < bestGap)
                {
                    best = v;
                    bestGap = gap;
                }
            }

            return best;
        }
    }
}
