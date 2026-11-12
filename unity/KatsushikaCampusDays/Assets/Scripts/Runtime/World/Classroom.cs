using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 講義棟の、時間割どおりに授業をしている教室 1 室（#439）。
    /// 授業中は講師と学生を出し、板書を時間とともに 1 行ずつ増やす。休み時間・昼休み・放課後は人を消して板を消す。
    /// 講師は 9 秒の周期で、板に書く → 板を指す → 学生の方へ振り返る を繰り返す。学生は座ってノートを取る。
    /// 授業中に部屋へ入ると、近くの学生が小声で話しかける（同じ授業では 1 回だけ）。
    /// 屋内の建物は InteriorLoader が丸ごと出し入れするので、建物の外にいる間はこの Update も止まる。
    /// 別の階にいるときは人を出さない（壁越しに人が透けて見えないように）。
    /// </summary>
    public sealed class Classroom : MonoBehaviour
    {
        /// <summary>部屋に入ったときの会話（Dialogue/class_entry.json）。話題は分野ごと（t_math / t_physics / t_info）。</summary>
        public const string EntryDialogueId = "class_entry";

        /// <summary>講師と学生の会話 id。</summary>
        public const string LecturerDialogueId = "class_prof";

        public const string StudentDialogueId = "class_student";

        /// <summary>授業の有無を確かめる間隔（秒）。ゲームの 1 分はこれより長い。</summary>
        private const float CheckInterval = 0.3f;

        /// <summary>プレイヤーの足もとと部屋の床の高さの差がこれを超えたら、別の階にいるとみなす。</summary>
        private const float OtherFloorHeight = 3f;

        /// <summary>講師の 1 周期（秒）と、そのうち書いている時間・指している時間。残りは振り返って話す。</summary>
        public const float GestureCycle = 9f;

        public const float WriteSeconds = 5f;
        public const float PointSeconds = 1.5f;

        /// <summary>講師が向きを変える速さ（度/秒）。</summary>
        private const float TurnDegreesPerSecond = 200f;

        /// <summary>向きの残りがこの角度（度）より小さければ、回り切ったとみなす。</summary>
        private const float FacingSettledDegrees = 1f;

        private static readonly int SitHash = Animator.StringToHash("Sit");
        private static readonly int TalkHash = Animator.StringToHash("Talk");

        private static ClassTimetable _timetable;
        private static bool _timetableLoaded;

        /// <summary>座っている学生 1 人。手はノートの上（机の天板の中心から左右へ）に置く。</summary>
        [Serializable]
        public sealed class Seat
        {
            public Animator Animator;
            public SitPose Pose;

            /// <summary>ノートの中心（天板の上、ワールド座標）。</summary>
            public Vector3 Note;

            /// <summary>座っている人の右（水平、ワールド）。</summary>
            public Vector3 Right;

            /// <summary>書く手の動きをずらす位相（ラジアン）。</summary>
            public float Phase;
        }

        /// <summary>1 つの renderer の材質の並び（Unity は入れ子の配列をシリアライズできないので包む）。</summary>
        [Serializable]
        public sealed class MaterialSet
        {
            public Material[] Materials;
        }

        /// <summary>分野ごとの講師の見た目。Renderers の並びは <see cref="_lecturerRenderers"/> と同じ。</summary>
        [Serializable]
        public sealed class LecturerLook
        {
            public string Field;
            public MaterialSet[] Renderers;
            public Vector3 BodyScale = Vector3.one;
        }

        [SerializeField] private string _roomId;
        [SerializeField] private float _floorY;

        /// <summary>部屋の中（ワールドの箱）。入ったかどうかを見る。</summary>
        [SerializeField] private Bounds _room;

        /// <summary>授業中だけ出す人（学生と講師の根元）。</summary>
        [SerializeField] private GameObject[] _people = Array.Empty<GameObject>();

        [SerializeField] private Seat[] _seats = Array.Empty<Seat>();

        [SerializeField] private Transform _lecturer;
        [SerializeField] private Transform _lecturerBody;
        [SerializeField] private Animator _lecturerAnimator;
        [SerializeField] private LecturerGesture _gesture;
        [SerializeField] private Renderer[] _lecturerRenderers = Array.Empty<Renderer>();
        [SerializeField] private LecturerLook[] _looks = Array.Empty<LecturerLook>();

        /// <summary>板書の文字。原点は板の面の上で、+Z（forward）は部屋の側。</summary>
        [SerializeField] private TextMeshPro _board;

        [SerializeField] private Transform _boardFrame;

        /// <summary>板書の枠の幅（m）。講師が指す位置に使う。</summary>
        [SerializeField] private float _boardTextWidth = 4f;

        /// <summary>学生の席の真ん中（講師が振り返って見る点）。</summary>
        [SerializeField] private Vector3 _audience;

        /// <summary>挨拶を済ませた授業（日数 × 10 + 時限）。授業は順に進むので、最後の 1 つを覚えれば足りる。</summary>
        private int _greetedSession = -1;

        private readonly StringBuilder _text = new StringBuilder(256);
        private Transform _player;
        private float _nextCheck;
        private bool _peopleShown;
        private string _shownSubject;
        private int _shownLines = -1;
        private bool _shownEnglish;
        private string _lookField;
        private ClassTimetable.Session _session;
        private bool _hasTalk;
        private bool _hasSit;
        private bool _sameFloor = true;

        public string RoomId => _roomId;
        public float FloorY => _floorY;
        public Bounds Room => _room;
        public IReadOnlyList<GameObject> People => _people;
        public IReadOnlyList<Seat> Seats => _seats;
        public Transform Lecturer => _lecturer;
        public TextMeshPro Board => _board;

        /// <summary>いま授業をしているか（最後に確かめた時点）。</summary>
        public bool InSession => _session.IsInClass;

        /// <summary>講師の 1 周期の中の動き。</summary>
        public enum LecturerMotion
        {
            None,
            Write,
            Point,
            Turn,
        }

        /// <summary>
        /// 最後のフレームで講師がしていた動き。授業中でないときと講師がいないときは None。
        /// 動きの画を撮るテストが、撮る時点の動きと名前の一致を待つために読む。
        /// </summary>
        public LecturerMotion Motion { get; private set; }

        /// <summary>
        /// 講師がいまの動きの向きまで回り切っているか（最後のフレーム）。
        /// 動きの画を撮るテストが、向き替えの途中を撮らないために読む。
        /// </summary>
        public bool LecturerFacingSettled { get; private set; }

        /// <summary>SceneBuilder（ClassroomStage）が組み立てた中身を渡す。</summary>
        public void Configure(string roomId, float floorY, Bounds room, GameObject[] people, Seat[] seats,
            Transform lecturer, Transform lecturerBody, Renderer[] lecturerRenderers, LecturerLook[] looks,
            TextMeshPro board, Transform boardFrame, float boardTextWidth, Vector3 audience)
        {
            _roomId = roomId;
            _floorY = floorY;
            _room = room;
            _people = people ?? Array.Empty<GameObject>();
            _seats = seats ?? Array.Empty<Seat>();
            _lecturer = lecturer;
            _lecturerBody = lecturerBody;
            _lecturerAnimator = lecturerBody != null ? lecturerBody.GetComponent<Animator>() : null;
            _gesture = lecturerBody != null ? lecturerBody.GetComponent<LecturerGesture>() : null;
            _lecturerRenderers = lecturerRenderers ?? Array.Empty<Renderer>();
            _looks = looks ?? Array.Empty<LecturerLook>();
            _board = board;
            _boardFrame = boardFrame;
            _boardTextWidth = boardTextWidth;
            _audience = audience;
        }

        /// <summary>PlayMode を続けて回しても、前の回に読んだ時間割を使い回さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTimetable()
        {
            _timetable = null;
            _timetableLoaded = false;
        }

        /// <summary>時間割。Resources から 1 回だけ読む。</summary>
        public static ClassTimetable Timetable
        {
            get
            {
                if (!_timetableLoaded)
                {
                    _timetable = ClassTimetable.LoadFromResources();
                    _timetableLoaded = true;
                }

                return _timetable;
            }
        }

        private void Awake()
        {
            _hasTalk = HasParameter(_lecturerAnimator, TalkHash);
            foreach (GameObject person in _people)
            {
                Animator animator = person != null ? person.GetComponentInChildren<Animator>(true) : null;
                if (animator != null)
                {
                    // 消して出し直しても、座った状態から始める。
                    animator.keepAnimatorStateOnDisable = true;
                }
            }

            _hasSit = _seats.Length > 0 && HasParameter(_seats[0].Animator, SitHash);
        }

        private void OnEnable()
        {
            L.LocaleChanged += OnLocaleChanged;
            _nextCheck = 0f;
        }

        private void OnDisable()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            _shownLines = -1;
            RefreshBoard();
        }

        private void Update()
        {
            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + CheckInterval;
                Check();
            }

            if (_peopleShown && _session.IsInClass)
            {
                AnimateStudents(Time.time);
                AnimateLecturer(Time.time, Time.deltaTime);
            }
            else
            {
                Motion = LecturerMotion.None;
                LecturerFacingSettled = false;
            }
        }

        private void Check()
        {
            GameManager game = GameManager.Instance;
            ClassTimetable timetable = Timetable;
            _session = game != null && timetable != null
                ? timetable.SessionAt(_roomId, game.DayNumber, game.GameTimeHours)
                : default;

            if (_player == null)
            {
                GameObject player = GameObject.FindWithTag("Player");
                _player = player != null ? player.transform : null;
            }

            _sameFloor = _player == null || Mathf.Abs(_player.position.y - _floorY) <= OtherFloorHeight;
            ShowPeople(_session.IsInClass && _sameFloor);
            if (_session.IsInClass)
            {
                ApplyLook(_session.Subject.Field);
            }

            RefreshBoard();
            CheckEntry(game);
        }

        private void ShowPeople(bool show)
        {
            if (show == _peopleShown)
            {
                return;
            }

            _peopleShown = show;
            foreach (GameObject person in _people)
            {
                if (person != null)
                {
                    person.SetActive(show);
                }
            }

            if (show)
            {
                SitDown();
            }
        }

        private void SitDown()
        {
            foreach (Seat seat in _seats)
            {
                if (seat.Pose != null)
                {
                    seat.Pose.Sitting = true;
                }

                if (_hasSit && seat.Animator != null)
                {
                    seat.Animator.SetBool(SitHash, true);
                }
            }
        }

        /// <summary>板書をいまの科目と行数にする。変わらなければ何もしない。</summary>
        private void RefreshBoard()
        {
            if (_board == null)
            {
                return;
            }

            string subject = _session.SubjectId;
            int lines = _session.BoardLines;
            bool english = L.IsEnglish;
            if (subject == _shownSubject && lines == _shownLines && english == _shownEnglish)
            {
                return;
            }

            if (_session.IsInClass && (subject != _shownSubject || english != _shownEnglish))
            {
                FitBoardSize(_session.Subject, english);
            }

            _shownSubject = subject;
            _shownLines = lines;
            _shownEnglish = english;
            _board.text = _session.IsInClass ? BoardText(_session.Subject, lines, english, _text) : string.Empty;
        }

        /// <summary>
        /// 板書の文字の大きさを、全部の行が出たときに枠に収まる大きさに決めて固定する。
        /// 自動の大きさのままだと、行が増えるたびに文字が縮んでしまう。
        /// </summary>
        private void FitBoardSize(ClassTimetable.Subject subject, bool english)
        {
            _board.enableAutoSizing = true;
            _board.text = BoardText(subject, subject.Lines.Count, english, _text);
            _board.ForceMeshUpdate();
            float size = _board.fontSize;
            _board.enableAutoSizing = false;
            _board.fontSize = size;
        }

        /// <summary>
        /// 板書の文字列。最初の行（題目）は少し大きく、続く行は出ている分だけ。テストから呼ぶ。
        /// </summary>
        public static string BoardText(ClassTimetable.Subject subject, int lines, bool english, StringBuilder buffer = null)
        {
            if (subject == null || lines <= 0)
            {
                return string.Empty;
            }

            StringBuilder text = buffer ?? new StringBuilder(256);
            text.Clear();
            int count = Mathf.Min(lines, subject.Lines.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    text.Append('\n');
                }

                string line = subject.Lines[i].Pick(english);
                if (i == 0)
                {
                    text.Append("<size=125%>").Append(line).Append("</size>");
                }
                else
                {
                    text.Append(line);
                }
            }

            return text.ToString();
        }

        private void ApplyLook(string field)
        {
            if (field == _lookField)
            {
                return;
            }

            _lookField = field;
            foreach (LecturerLook look in _looks)
            {
                if (look == null || look.Field != field || look.Renderers == null)
                {
                    continue;
                }

                int count = Mathf.Min(look.Renderers.Length, _lecturerRenderers.Length);
                for (int i = 0; i < count; i++)
                {
                    if (_lecturerRenderers[i] != null && look.Renderers[i] != null && look.Renderers[i].Materials != null)
                    {
                        _lecturerRenderers[i].sharedMaterials = look.Renderers[i].Materials;
                    }
                }

                if (_lecturerBody != null)
                {
                    _lecturerBody.localScale = look.BodyScale;
                }

                return;
            }
        }

        /// <summary>
        /// 授業中に部屋の中にいたら、近くの学生が小声で話しかける。1 つの授業に 1 回だけ。
        /// 休み時間から部屋にいて授業が始まったときも同じ。会話中なら終わってから出す。
        /// </summary>
        private void CheckEntry(GameManager game)
        {
            if (!_session.IsInClass || game == null || _player == null || !_sameFloor
                || !_room.Contains(_player.position))
            {
                return;
            }

            int session = game.DayNumber * 10 + _session.PeriodNumber;
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (session == _greetedSession || dialogue == null || dialogue.IsPlaying)
            {
                return;
            }

            if (dialogue.PlayTopic(EntryDialogueId, EntryTopicFor(_session.Subject.Field)))
            {
                _greetedSession = session;
            }
        }

        /// <summary>入ったときの話題がある分野。</summary>
        private static readonly string[] EntryFields = { "math", "physics", "info" };

        /// <summary>分野ごとの、入ったときの話題 id。話題の無い分野は情報の話題にする。</summary>
        public static string EntryTopicFor(string field)
        {
            return "t_" + (System.Array.IndexOf(EntryFields, field) >= 0 ? field : "info");
        }

        /// <summary>学生は右手でノートに書き、ときどき手を止める。左手はノートを押さえる。</summary>
        private void AnimateStudents(float time)
        {
            foreach (Seat seat in _seats)
            {
                if (seat.Pose == null)
                {
                    continue;
                }

                Vector3 forward = Vector3.Cross(seat.Right, Vector3.up);
                float t = time + seat.Phase;
                float writing = Mathf.Sin(t * 0.45f) > -0.6f ? 1f : 0f;
                Vector3 scribble = seat.Right * (0.035f * Mathf.Sin(t * 6.3f) * writing + 0.02f * Mathf.Sin(t * 0.9f))
                    + forward * (0.02f * Mathf.Sin(t * 2.7f) * writing)
                    + Vector3.up * (0.012f * Mathf.Max(0f, Mathf.Sin(t * 12.6f)) * writing);
                seat.Pose.SetKeyboardHands(
                    seat.Note - seat.Right * 0.16f,
                    seat.Note + seat.Right * 0.10f + scribble);
            }
        }

        /// <summary>部屋ごとに講師の動きの周期をずらす秒数。階の数字（室番号の先頭）ごとに周期の 1/3 ずつずらす。</summary>
        public static float PhaseOffset(string roomId)
        {
            int floor = string.IsNullOrEmpty(roomId) || !char.IsDigit(roomId[0]) ? 0 : roomId[0] - '0';
            return Mathf.Repeat(floor * GestureCycle / 3f, GestureCycle);
        }

        /// <summary>講師の 1 周期。書く → 指す → 振り返る。</summary>
        private void AnimateLecturer(float time, float deltaTime)
        {
            if (_lecturer == null || _boardFrame == null)
            {
                Motion = LecturerMotion.None;
                LecturerFacingSettled = false;
                return;
            }

            Vector3 toRoom = _boardFrame.forward;
            Vector3 right = Vector3.Cross(Vector3.up, -toRoom).normalized;
            float phase = Mathf.Repeat(time + PhaseOffset(_roomId), GestureCycle);
            Vector3 facing;
            bool talking = false;

            if (phase < WriteSeconds)
            {
                // 板に向かい、肩の高さで板の面に右手を当て、左から右へ少しずつ動かす。
                float stroke = Mathf.Repeat(phase, 1.25f) / 1.25f;
                Vector3 onBoard = BoardPointNear(_lecturer.position, right)
                    + right * Mathf.Lerp(-0.25f, 0.15f, stroke)
                    + Vector3.up * (0.02f * Mathf.Sin(phase * 9f));
                facing = -toRoom;
                Reach(onBoard + toRoom * 0.02f, 1f, onBoard);
                Motion = LecturerMotion.Write;
            }
            else if (phase < WriteSeconds + PointSeconds)
            {
                // 半身になって、書いた行を指す。
                Vector3 text = _boardFrame.position - right * (_boardTextWidth * 0.25f);
                facing = (toRoom - right).normalized;
                Reach(Vector3.Lerp(_lecturer.position + Vector3.up * 1.35f, text, 0.55f), 0.9f, _audience);
                talking = true;
                Motion = LecturerMotion.Point;
            }
            else
            {
                facing = toRoom;
                _gesture?.Release();
                _gesture?.LookAt(_audience);
                talking = true;
                Motion = LecturerMotion.Turn;
            }

            Quaternion goal = Quaternion.LookRotation(facing, Vector3.up);
            _lecturer.rotation = Quaternion.RotateTowards(_lecturer.rotation, goal, TurnDegreesPerSecond * deltaTime);
            LecturerFacingSettled = Quaternion.Angle(_lecturer.rotation, goal) < FacingSettledDegrees;
            if (_hasTalk && _lecturerAnimator != null)
            {
                _lecturerAnimator.SetBool(TalkHash, talking);
            }
        }

        private void Reach(Vector3 hand, float weight, Vector3 look)
        {
            if (_gesture == null)
            {
                return;
            }

            _gesture.Reach(hand, weight);
            _gesture.LookAt(look);
        }

        /// <summary>講師の立つ位置の正面の、板の面の上の肩の高さの点。</summary>
        private Vector3 BoardPointNear(Vector3 standing, Vector3 right)
        {
            Vector3 center = _boardFrame.position;
            float along = Vector3.Dot(standing - center, right);
            return center + right * (along - 0.25f) + Vector3.up * (standing.y + 1.45f - center.y);
        }

        private static bool HasParameter(Animator animator, int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == hash)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
