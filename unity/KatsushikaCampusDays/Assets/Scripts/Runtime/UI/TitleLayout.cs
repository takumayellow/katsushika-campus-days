using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトル画面の文字の置き方とカメラを、画面の向きと入力の種類に合わせる (#187)。
    /// PC（横長・マウスとキー）は組んだときのまま。指で遊ぶ横長の画面では UI が 1.5 倍に描かれるので
    /// （<see cref="TouchCanvasScale"/>）、題名を上へ、案内を下へ寄せ、その間に 3 人が収まるまでカメラを引く。
    /// 縦長の画面では 3 人を横に並べると幅に収まらないので、選んでいる子を真ん中に置き、
    /// 両隣を画面の端からのぞかせる（<see cref="CharacterSelect.SetCarousel"/>）。
    /// 文字の枠の位置と大きさはここの表が唯一の出どころで、シーンを組むときも PC の表を使う。
    /// </summary>
    public sealed class TitleLayout : MonoBehaviour
    {
        /// <summary>画面の種類。</summary>
        public enum Shape
        {
            Desktop,
            LandscapeTouch,
            Portrait
        }

        /// <summary>置き方を変える文字の枠。題名と副題は上端から、残りは下端から測る。</summary>
        public enum Part
        {
            Title,
            Subtitle,
            Prompt,
            Name,
            Tagline,
            Hint
        }

        /// <summary>1 つの枠の置き方。y はアンカーの辺からの距離（上端から測る枠は負）。</summary>
        public readonly struct Slot
        {
            public readonly Vector2 Position;
            public readonly Vector2 Size;
            public readonly float FontSize;
            public readonly float MinSize;

            public Slot(float y, float width, float height, float fontSize, float minSize)
            {
                Position = new Vector2(0f, y);
                Size = new Vector2(width, height);
                FontSize = fontSize;
                MinSize = minSize;
            }

            /// <summary>アンカーの辺から、枠の遠い方の辺までの距離。</summary>
            public float Reach => Mathf.Abs(Position.y) + Size.y;
        }

        /// <summary>舞台（選んでいる子と台座）の高さ。台座の楕円が下に、髪が上にはみ出す分を含める。</summary>
        public const float StageHeight = 1.9f;

        /// <summary>舞台の真ん中の高さ。</summary>
        public const float StageCenterY = 0.85f;

        /// <summary>横長の画面での舞台の半幅。端の子が選ばれて手前に出たときの台座の外側まで。</summary>
        public const float RowHalfWidth = 2.5f;

        /// <summary>縦長の画面での舞台の半幅。真ん中の子の台座まで。</summary>
        public const float CarouselHalfWidth = 0.6f;

        /// <summary>縦長の画面での縦の画角。舞台を幅で決めるので、広げて端をゆがませる必要はない。</summary>
        public const float PortraitFieldOfView = 60f;

        /// <summary>縦長の画面で、両隣の子の真ん中を置く横の位置（画面の端が 1）。</summary>
        public const float NeighbourOffset = 0.85f;

        /// <summary>舞台の高さが画面の高さに占める割合の上限の半分。広い画面で子が大きくなりすぎない。</summary>
        public const float MaxHalfHeight = 0.5f;

        /// <summary>舞台の幅を収める割合（画面の端が 1）。</summary>
        public const float WidthLimit = 0.94f;

        /// <summary>文字の枠と舞台の間に空ける幅（Canvas の単位）。</summary>
        public const float Gap = 8f;

        /// <summary>カメラが新しい位置へ寄る速さ。</summary>
        private const float Follow = 6f;

        private static readonly Slot[][] Slots =
        {
            // Desktop。1920×1080 で組んだ元の置き方。
            new[]
            {
                new Slot(-120f, 1400f, 160f, 96f, 48f),
                new Slot(-280f, 1400f, 60f, 34f, 24f),
                new Slot(150f, 1200f, 64f, 36f, 24f),
                new Slot(236f, 900f, 80f, 56f, 36f),
                new Slot(168f, 1200f, 60f, 32f, 20f),
                new Slot(92f, 1200f, 56f, 30f, 20f)
            },
            // LandscapeTouch。横持ちのスマホで Canvas はおよそ 1410×650。
            // 案内は「タップではじめる」とクレジット・設定の 2 行が入る高さにする。
            new[]
            {
                new Slot(-20f, 1400f, 100f, 68f, 40f),
                new Slot(-122f, 1400f, 52f, 28f, 20f),
                new Slot(36f, 1200f, 120f, 40f, 24f),
                new Slot(182f, 900f, 84f, 48f, 32f),
                new Slot(122f, 1200f, 56f, 26f, 18f),
                new Slot(70f, 1200f, 50f, 24f, 18f)
            },
            // Portrait。縦持ちのスマホで Canvas は幅 800、高さはおよそ 1420〜1730。
            new[]
            {
                new Slot(-150f, 1400f, 130f, 80f, 44f),
                new Slot(-282f, 1400f, 56f, 34f, 22f),
                new Slot(160f, 1200f, 150f, 52f, 30f),
                new Slot(258f, 900f, 96f, 56f, 36f),
                new Slot(160f, 1200f, 90f, 30f, 20f),
                new Slot(90f, 1200f, 64f, 30f, 20f)
            }
        };

        [SerializeField] private Camera _camera;
        [SerializeField] private CharacterSelect _select;
        [SerializeField] private GameObject _selectRoot;
        [SerializeField] private RectTransform[] _parts = new RectTransform[0];
        [SerializeField] private TMP_Text[] _labels = new TMP_Text[0];

        private RectTransform _canvasRect;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private float _homeFieldOfView;
        private Shape _shape;
        private bool _placed;

        /// <summary>画面の向きと入力の種類から決まる画面の種類。縦長なら入力に関わらず縦長の置き方。</summary>
        public static Shape ShapeFor(bool portrait, bool touch)
        {
            return portrait ? Shape.Portrait : touch ? Shape.LandscapeTouch : Shape.Desktop;
        }

        /// <summary>shape の画面での part の枠の置き方。</summary>
        public static Slot SlotFor(Shape shape, Part part)
        {
            return Slots[(int)shape][(int)part];
        }

        /// <summary>
        /// 高さ height・半幅 halfWidth の舞台を、縦の画面位置 bottom〜top（画面の下端 -1、上端 1）の帯と、
        /// 横 ±widthLimit に収めるカメラの距離。帯の半分の高さは maxHalfHeight で止める。
        /// </summary>
        public static float FrameDistance(float height, float halfWidth, float pitch, float verticalFieldOfView,
            float aspect, float top, float bottom, float widthLimit, float maxHalfHeight)
        {
            float tan = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            float half = Mathf.Clamp((top - bottom) * 0.5f, 0.1f, maxHalfHeight);
            float byHeight = height * 0.5f * Mathf.Cos(pitch * Mathf.Deg2Rad) / (tan * half);
            float byWidth = aspect > 0f ? halfWidth / (tan * aspect * widthLimit) : 0f;
            return Mathf.Max(byHeight, byWidth);
        }

        /// <summary>
        /// 下へ pitch 度傾けたカメラを、center が距離 distance・縦の画面位置 centerY（下端 -1、上端 1）に
        /// 映るように置く位置。
        /// </summary>
        public static Vector3 FramePosition(Vector3 center, float pitch, float verticalFieldOfView,
            float distance, float centerY)
        {
            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            float tan = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            return center - rotation * Vector3.forward * distance -
                   rotation * Vector3.up * (centerY * distance * tan);
        }

        /// <summary>縦長の画面で、距離 depth にいる両隣の子を横の画面位置 ±offset に置く間隔。</summary>
        public static float NeighbourSpacing(float depth, float verticalFieldOfView, float aspect, float offset)
        {
            return offset * depth * Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
        }

        /// <summary>
        /// 文字の枠の間に空く、舞台を置ける帯（下端 -1、上端 1）。selecting なら題名は隠れているので上は画面の端まで、
        /// 下は名前の枠の上まで。タイトルでは副題の下から案内の上まで。canvasHeight は Canvas の高さ。
        /// </summary>
        public static Vector2 StageBand(Shape shape, bool selecting, float canvasHeight)
        {
            float topReach = selecting ? 0f : SlotFor(shape, Part.Subtitle).Reach;
            float bottomReach = SlotFor(shape, selecting ? Part.Name : Part.Prompt).Reach;
            float top = 1f - 2f * (topReach + Gap) / canvasHeight;
            float bottom = -1f + 2f * (bottomReach + Gap) / canvasHeight;
            return new Vector2(bottom, top);
        }

        /// <summary>SceneBuilder から差し込む。parts と labels は <see cref="Part"/> の順。</summary>
        public void Bind(Camera camera, CharacterSelect select, GameObject selectRoot,
            RectTransform[] parts, TMP_Text[] labels)
        {
            _camera = camera;
            _select = select;
            _selectRoot = selectRoot;
            _parts = parts ?? new RectTransform[0];
            _labels = labels ?? new TMP_Text[0];
        }

        private void Awake()
        {
            _canvasRect = (RectTransform)transform;
            if (_camera != null)
            {
                _homePosition = _camera.transform.position;
                _homeRotation = _camera.transform.rotation;
                _homeFieldOfView = _camera.fieldOfView;
            }
        }

        private void LateUpdate()
        {
            Shape shape = ShapeFor(ScreenShape.IsPortrait, InputMode.IsTouch);
            bool snap = !_placed || shape != _shape;
            if (snap)
            {
                ApplySlots(shape);
                _shape = shape;
                _placed = true;
            }

            FrameCamera(shape, snap);
        }

        private void ApplySlots(Shape shape)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                RectTransform rect = _parts[i];
                if (rect == null || i >= Slots[(int)shape].Length)
                {
                    continue;
                }

                Slot slot = SlotFor(shape, (Part)i);
                rect.anchoredPosition = slot.Position;
                rect.sizeDelta = slot.Size;
                FitInCanvas fit = rect.GetComponent<FitInCanvas>();
                if (fit != null)
                {
                    fit.SetSize(slot.Size);
                }

                TMP_Text label = i < _labels.Length ? _labels[i] : null;
                if (label != null)
                {
                    label.fontSizeMax = slot.FontSize;
                    label.fontSizeMin = slot.MinSize;
                }
            }
        }

        private void FrameCamera(Shape shape, bool snap)
        {
            if (_camera == null)
            {
                return;
            }

            Vector3 position = _homePosition;
            float fieldOfView = _homeFieldOfView;
            float spacing = 0f;
            float canvasHeight = _canvasRect.rect.height;

            if (shape != Shape.Desktop && canvasHeight > 0f)
            {
                bool selecting = _selectRoot != null && _selectRoot.activeSelf;
                Vector2 band = StageBand(shape, selecting, canvasHeight);
                bool portrait = shape == Shape.Portrait;
                fieldOfView = portrait ? PortraitFieldOfView : _homeFieldOfView;
                float pitch = _homeRotation.eulerAngles.x;
                float aspect = _camera.aspect;
                float distance = FrameDistance(StageHeight, portrait ? CarouselHalfWidth : RowHalfWidth, pitch,
                    fieldOfView, aspect, band.y, band.x, WidthLimit, MaxHalfHeight);
                float centerY = (band.x + band.y) * 0.5f;
                position = FramePosition(new Vector3(0f, StageCenterY, 0f), pitch, fieldOfView, distance, centerY);

                if (portrait && _select != null)
                {
                    spacing = NeighbourSpacing(distance + _select.IdleDepth, fieldOfView, aspect, NeighbourOffset);
                }
            }

            float blend = snap ? 1f : 1f - Mathf.Exp(-Follow * Time.unscaledDeltaTime);
            Transform cameraTransform = _camera.transform;
            cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(cameraTransform.position, position, blend), _homeRotation);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fieldOfView, blend);

            if (_select != null)
            {
                _select.SetCarousel(spacing);
            }
        }
    }
}
