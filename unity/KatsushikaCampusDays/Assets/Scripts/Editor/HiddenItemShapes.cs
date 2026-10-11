using UnityEngine;
using static KCD.Editor.PartMesh;

namespace KCD.Editor
{
    /// <summary>
    /// 隠しアイテム 20 個の模型 (#167)。名前どおりの形にして、どれも一番長い所が 0.2〜0.4 m に収まる大きさにする
    /// （どんぐりやチョークを実物の 2〜3 倍にしても 10 cm に満たず、芝の上で見つからないため）。
    /// 原点は <see cref="PartMesh.Build"/> が外接箱の中心に寄せるので、ここでは置きやすい所を原点にしてよい。
    /// </summary>
    internal static class HiddenItemShapes
    {
        public const string Silver = "item_metal_silver";
        public const string Brass = "item_metal_brass";
        public const string Gold = "item_metal_gold";
        public const string AcornShell = "item_acorn";
        public const string AcornCap = "item_acorn_cap";
        public const string Paper = "item_paper";
        public const string Red = "item_red";
        public const string Navy = "item_navy";
        public const string Glass = "glass_item";
        public const string MarbleGlass = "glass_marble";
        public const string Green = "tus_green";
        public const string White = "white";
        public const string Black = "bike_frame";
        public const string Blue = "vending_blue";
        public const string Wood = "wood";
        public const string Yellow = "flower_yellow";
        public const string Orange = "flower_orange";
        public const string Pink = "flower_pink";
        public const string LeafGreen = "flower_leaf";

        /// <summary>正面（-Z）を向いた板・円盤にする回転（Y 軸を Z 軸へ倒す）。</summary>
        private static readonly Vector3 FaceZ = new Vector3(90f, 0f, 0f);

        /// <summary>Y 軸を X 軸へ倒す回転（横に寝た円柱）。</summary>
        private static readonly Vector3 AlongX = new Vector3(0f, 0f, 90f);

        /// <summary>正門のキーホルダー: 金属の輪と、理科大グリーンの札。</summary>
        public static PartMesh Keyring()
        {
            return new PartMesh()
                .Torus(Silver, 0.065f, 0.011f, 24, 8, At(new Vector3(0f, 0.12f, 0f), FaceZ))
                .Torus(Silver, 0.022f, 0.007f, 12, 6, At(new Vector3(0f, 0.035f, 0f), new Vector3(90f, 90f, 0f)))
                .Box(Green, new Vector3(0.13f, 0.17f, 0.024f), At(0f, -0.075f, 0f))
                .Box(White, new Vector3(0.13f, 0.03f, 0.028f), At(0f, -0.05f, 0f));
        }

        /// <summary>石畳のピンバッジ: 銀の縁の緑の丸いバッジに白い星、裏に留め針。</summary>
        public static PartMesh PinBadge()
        {
            return new PartMesh()
                .Cylinder(Green, 0.12f, 0.024f, 32, At(Vector3.zero, FaceZ))
                .Torus(Silver, 0.12f, 0.012f, 32, 6, At(Vector3.zero, FaceZ))
                .Star(White, 5, 0.065f, 0.027f, 0.006f, At(0f, 0f, -0.014f))
                .Box(Silver, new Vector3(0.02f, 0.14f, 0.012f), At(0f, 0f, 0.018f));
        }

        /// <summary>ベンチの下の記念コイン: 縁の立った真鍮の硬貨、真ん中に金の浮き彫り。</summary>
        public static PartMesh Coin()
        {
            return new PartMesh()
                .Cylinder(Brass, 0.115f, 0.022f, 36, At(Vector3.zero, FaceZ))
                .Torus(Brass, 0.112f, 0.008f, 36, 6, At(Vector3.zero, FaceZ))
                .Cylinder(Gold, 0.05f, 0.03f, 24, At(Vector3.zero, FaceZ));
        }

        /// <summary>水盤のガラス玉: 透ける玉の中に、橙と白のねじれた芯。</summary>
        public static PartMesh Marble()
        {
            return new PartMesh()
                .Ellipsoid(MarbleGlass, new Vector3(0.12f, 0.12f, 0.12f), 12, 20, At(0f, 0f, 0f))
                .Ellipsoid(Orange, new Vector3(0.075f, 0.02f, 0.05f), 6, 12, At(Vector3.zero, new Vector3(0f, 0f, 35f)))
                .Ellipsoid(White, new Vector3(0.05f, 0.015f, 0.07f), 6, 12, At(Vector3.zero, new Vector3(20f, 60f, -30f)));
        }

        /// <summary>みらい公園のどんぐり: 先の尖った実に、うろこの段のある帽子と軸。</summary>
        public static PartMesh Acorn()
        {
            return new PartMesh()
                .Lathe(AcornShell, new[]
                {
                    new Vector2(0f, -0.13f), new Vector2(0.012f, -0.125f), new Vector2(0.035f, -0.105f),
                    new Vector2(0.058f, -0.07f), new Vector2(0.074f, -0.03f), new Vector2(0.082f, 0.01f),
                    new Vector2(0.08f, 0.04f), new Vector2(0.065f, 0.06f), new Vector2(0f, 0.065f),
                }, 20, At(0f, 0f, 0f))
                .Lathe(AcornCap, new[]
                {
                    new Vector2(0f, 0.035f), new Vector2(0.088f, 0.022f), new Vector2(0.088f, 0.022f),
                    new Vector2(0.095f, 0.04f), new Vector2(0.09f, 0.065f), new Vector2(0.07f, 0.085f),
                    new Vector2(0.04f, 0.095f), new Vector2(0f, 0.098f),
                }, 20, At(0f, 0f, 0f))
                .Torus(AcornCap, 0.094f, 0.006f, 20, 6, At(0f, 0.05f, 0f))
                .Torus(AcornCap, 0.082f, 0.006f, 20, 6, At(0f, 0.072f, 0f))
                .Cylinder(AcornCap, 0.012f, 0.04f, 8, At(0f, 0.115f, 0f));
        }

        /// <summary>理科大グリーンのフライングディスク: 縁の垂れた円盤に白い線。回って両面が見えるよう傾ける。</summary>
        public static PartMesh FlyingDisc()
        {
            Matrix4x4 tilt = Matrix4x4.Rotate(Quaternion.Euler(20f, 0f, 0f));
            return new PartMesh()
                .Lathe(Green, new[]
                {
                    new Vector2(0f, -0.004f), new Vector2(0.14f, -0.006f), new Vector2(0.16f, -0.022f),
                    new Vector2(0.175f, -0.024f), new Vector2(0.18f, -0.01f), new Vector2(0.175f, 0.008f),
                    new Vector2(0.15f, 0.018f), new Vector2(0.08f, 0.026f), new Vector2(0f, 0.028f),
                }, 36, tilt)
                .Torus(White, 0.1f, 0.004f, 36, 4, tilt * At(0f, 0.024f, 0f));
        }

        /// <summary>稲荷の小さな鈴: 金の玉に帯と底の切れ目、上に輪と赤い紐。</summary>
        public static PartMesh ShrineBell()
        {
            return new PartMesh()
                .Ellipsoid(Gold, new Vector3(0.1f, 0.1f, 0.1f), 12, 20, At(0f, 0f, 0f))
                .Torus(Brass, 0.1f, 0.008f, 24, 6, At(0f, 0f, 0f))
                .Box(Black, new Vector3(0.09f, 0.016f, 0.03f), At(0f, -0.094f, 0f))
                .Torus(Brass, 0.025f, 0.007f, 12, 6, At(new Vector3(0f, 0.12f, 0f), FaceZ))
                .Cylinder(Red, 0.008f, 0.1f, 8, At(0f, 0.19f, 0f))
                .Ellipsoid(Red, new Vector3(0.02f, 0.02f, 0.02f), 6, 10, At(0f, 0.24f, 0f));
        }

        /// <summary>自転車のベル: 銀の椀、黒い台とレバー、ハンドルに巻く輪。</summary>
        public static PartMesh BikeBell()
        {
            return new PartMesh()
                .Lathe(Silver, new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.11f, 0f), new Vector2(0.11f, 0f),
                    new Vector2(0.108f, 0.025f), new Vector2(0.095f, 0.05f), new Vector2(0.07f, 0.07f),
                    new Vector2(0.035f, 0.08f), new Vector2(0f, 0.083f),
                }, 32, At(0f, 0f, 0f))
                .Ellipsoid(Silver, new Vector3(0.018f, 0.012f, 0.018f), 6, 10, At(0f, 0.085f, 0f))
                .Cylinder(Black, 0.05f, 0.03f, 16, At(0f, -0.015f, 0f))
                .Box(Black, new Vector3(0.09f, 0.012f, 0.02f), At(0.1f, -0.01f, 0f))
                .Ellipsoid(Red, new Vector3(0.02f, 0.012f, 0.02f), 6, 10, At(0.145f, -0.005f, 0f))
                .Torus(Black, 0.04f, 0.009f, 16, 6, At(new Vector3(0f, -0.075f, 0f), AlongX));
        }

        /// <summary>温室のたね袋: 紙の袋に、緑の帯と花の絵。</summary>
        public static PartMesh SeedPacket()
        {
            return new PartMesh()
                .Box(Paper, new Vector3(0.2f, 0.28f, 0.012f), At(0f, 0f, 0f))
                .Box(Green, new Vector3(0.2f, 0.035f, 0.014f), At(0f, 0.115f, 0f))
                .Box(LeafGreen, new Vector3(0.16f, 0.13f, 0.004f), At(0f, -0.01f, -0.007f))
                .Star(Orange, 8, 0.05f, 0.028f, 0.004f, At(0f, -0.01f, -0.0095f))
                .Star(Yellow, 8, 0.018f, 0.012f, 0.004f, At(0f, -0.01f, -0.012f));
        }

        /// <summary>体育館のホイッスル: 銀の胴と吹き口、後ろの輪に赤いひも。</summary>
        public static PartMesh Whistle()
        {
            return new PartMesh()
                .Cylinder(Silver, 0.055f, 0.075f, 24, At(Vector3.zero, AlongX))
                .Box(Silver, new Vector3(0.05f, 0.04f, 0.13f), At(0f, 0.03f, 0.09f))
                .Box(Black, new Vector3(0.035f, 0.008f, 0.025f), At(0f, 0.05f, 0.04f))
                .Torus(Silver, 0.018f, 0.005f, 12, 6, At(new Vector3(0f, 0f, -0.065f), AlongX))
                .Torus(Red, 0.075f, 0.006f, 24, 6, At(new Vector3(0f, 0f, -0.14f), AlongX));
        }

        /// <summary>閉架の栞: 紺の栞に金の線と星、上の角から赤い房。</summary>
        public static PartMesh Bookmark()
        {
            return new PartMesh()
                .Box(Navy, new Vector3(0.085f, 0.3f, 0.006f), At(0f, 0f, 0f))
                .Box(Gold, new Vector3(0.065f, 0.004f, 0.008f), At(0f, -0.1f, 0f))
                .Box(Gold, new Vector3(0.065f, 0.004f, 0.008f), At(0f, 0.1f, 0f))
                .Star(Gold, 5, 0.025f, 0.01f, 0.008f, At(0f, -0.05f, 0f))
                .Box(Red, new Vector3(0.004f, 0.068f, 0.004f), At(new Vector3(0.03f, 0.165f, 0f), new Vector3(0f, 0f, -63f)))
                .Ellipsoid(Red, new Vector3(0.014f, 0.014f, 0.014f), 6, 10, At(0.06f, 0.18f, 0f))
                .Lathe(Red, new[]
                {
                    new Vector2(0f, 0.09f), new Vector2(0.022f, 0.09f), new Vector2(0.022f, 0.09f),
                    new Vector2(0.012f, 0.165f), new Vector2(0f, 0.172f),
                }, 12, At(0.06f, 0f, 0f));
        }

        /// <summary>八角ドームの鍵: 八角の輪の持ち手、軸と 2 段の歯。すべて金。</summary>
        public static PartMesh DomeKey()
        {
            // 八角の平らな辺が上下に来るよう、輪を自分の軸まわりに 22.5 度回してから立てる。
            Matrix4x4 bow = At(new Vector3(0f, 0.13f, 0f), FaceZ) * Matrix4x4.Rotate(Quaternion.Euler(0f, 22.5f, 0f));
            return new PartMesh()
                .Torus(Gold, 0.06f, 0.014f, 8, 8, bow)
                .Cylinder(Gold, 0.02f, 0.025f, 12, At(0f, 0.06f, 0f))
                .Cylinder(Gold, 0.012f, 0.2f, 12, At(0f, -0.05f, 0f))
                .Box(Gold, new Vector3(0.05f, 0.022f, 0.012f), At(0.03f, -0.13f, 0f))
                .Box(Gold, new Vector3(0.035f, 0.018f, 0.012f), At(0.025f, -0.095f, 0f))
                .Ellipsoid(Gold, new Vector3(0.014f, 0.01f, 0.014f), 6, 10, At(0f, -0.15f, 0f));
        }

        /// <summary>共創棟のタンブラー: 緑の胴に白い帯、黒い蓋と飲み口。</summary>
        public static PartMesh Tumbler()
        {
            return new PartMesh()
                .Lathe(Green, new[]
                {
                    new Vector2(0f, -0.12f), new Vector2(0.068f, -0.12f), new Vector2(0.068f, -0.12f),
                    new Vector2(0.078f, 0.09f), new Vector2(0.078f, 0.09f), new Vector2(0f, 0.09f),
                }, 28, At(0f, 0f, 0f))
                .Lathe(White, new[] { new Vector2(0.0748f, -0.02f), new Vector2(0.0771f, 0.03f) }, 28, At(0f, 0f, 0f))
                .Cylinder(Black, 0.082f, 0.03f, 28, At(0f, 0.105f, 0f))
                .Box(Black, new Vector3(0.03f, 0.012f, 0.02f), At(0f, 0.126f, 0.05f));
        }

        /// <summary>売店のステッカー: 白い縁の丸に、緑の地と黄色い星。</summary>
        public static PartMesh Sticker()
        {
            return new PartMesh()
                .Cylinder(White, 0.13f, 0.006f, 36, At(Vector3.zero, FaceZ))
                .Cylinder(Green, 0.11f, 0.008f, 36, At(Vector3.zero, FaceZ))
                .Star(Yellow, 5, 0.075f, 0.032f, 0.01f, At(0f, 0f, 0f));
        }

        /// <summary>大ホールのチョーク: 白・黄・桃の 3 本を俵に積む。</summary>
        public static PartMesh Chalk()
        {
            return new PartMesh()
                .Cylinder(White, 0.022f, 0.22f, 12, At(new Vector3(0f, 0f, -0.024f), AlongX))
                .Cylinder(Yellow, 0.022f, 0.22f, 12, At(new Vector3(0.012f, 0f, 0.024f), AlongX))
                .Cylinder(Pink, 0.022f, 0.22f, 12, At(new Vector3(-0.012f, 0.04f, 0f), AlongX));
        }

        /// <summary>置き忘れの演習ノート: 青い表紙と背、白い紙の小口、表紙に白いラベル。立てて置く。</summary>
        public static PartMesh Notebook()
        {
            return new PartMesh()
                .Box(Blue, new Vector3(0.22f, 0.3f, 0.005f), At(0f, 0f, 0.0125f))
                .Box(Blue, new Vector3(0.22f, 0.3f, 0.005f), At(0f, 0f, -0.0125f))
                .Box(Blue, new Vector3(0.006f, 0.3f, 0.03f), At(-0.11f, 0f, 0f))
                .Box(Paper, new Vector3(0.21f, 0.29f, 0.02f), At(0.003f, 0f, 0f))
                .Box(White, new Vector3(0.13f, 0.05f, 0.002f), At(0.01f, 0.08f, -0.0155f));
        }

        /// <summary>返却口のトレーチャーム: 縁のある茶色の盆に白い茶碗と赤い汁椀、角に輪とひも。斜めに起こす。</summary>
        public static PartMesh TrayCharm()
        {
            Matrix4x4 tilt = Matrix4x4.Rotate(Quaternion.Euler(-25f, 0f, 0f));
            return new PartMesh()
                .Box(Wood, new Vector3(0.24f, 0.012f, 0.17f), tilt * At(0f, 0f, 0f))
                .Box(Wood, new Vector3(0.24f, 0.025f, 0.012f), tilt * At(0f, 0.012f, 0.079f))
                .Box(Wood, new Vector3(0.24f, 0.025f, 0.012f), tilt * At(0f, 0.012f, -0.079f))
                .Box(Wood, new Vector3(0.012f, 0.025f, 0.146f), tilt * At(0.114f, 0.012f, 0f))
                .Box(Wood, new Vector3(0.012f, 0.025f, 0.146f), tilt * At(-0.114f, 0.012f, 0f))
                .Lathe(White, Bowl(0.025f, 0.05f, 0.045f), 16, tilt * At(-0.05f, 0f, 0f))
                .Lathe(Red, Bowl(0.02f, 0.04f, 0.035f), 16, tilt * At(0.055f, 0f, 0.02f))
                .Torus(Silver, 0.022f, 0.006f, 12, 6, tilt * At(new Vector3(0.11f, 0.045f, 0.08f), FaceZ))
                .Cylinder(Red, 0.005f, 0.08f, 6, tilt * At(0.11f, 0.105f, 0.08f));
        }

        /// <summary>掲示板のマグネット: 黒い裏板に、正面へ膨らんだ赤い丸。</summary>
        public static PartMesh Magnet()
        {
            return new PartMesh()
                .Lathe(Red, new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.11f, 0f), new Vector2(0.11f, 0f),
                    new Vector2(0.11f, 0.02f), new Vector2(0.095f, 0.04f), new Vector2(0.055f, 0.05f),
                    new Vector2(0f, 0.053f),
                }, 32, At(Vector3.zero, new Vector3(-90f, 0f, 0f)))
                .Cylinder(Black, 0.105f, 0.012f, 32, At(new Vector3(0f, 0f, 0.007f), FaceZ));
        }

        /// <summary>実験用ゴーグル: 透けるレンズ 2 枚に黒い枠と鼻の橋、後ろに青いバンド。</summary>
        public static PartMesh Goggles()
        {
            var goggles = new PartMesh();
            foreach (float x in new[] { -0.07f, 0.07f })
            {
                goggles
                    .Cylinder(Glass, 0.058f, 0.03f, 24, At(new Vector3(x, 0f, 0f), FaceZ))
                    .Torus(Black, 0.06f, 0.012f, 24, 6, At(new Vector3(x, 0f, 0f), FaceZ));
            }

            return goggles
                .Box(Black, new Vector3(0.03f, 0.018f, 0.02f), At(0f, 0.01f, 0f))
                .Torus(Blue, 0.135f, 0.008f, 32, 6,
                    At(new Vector3(0f, 0f, 0.09f), Vector3.zero, new Vector3(1f, 1f, 0.5f)));
        }

        /// <summary>目盛りの消えたビーカー: 口の開いた厚みのないガラス（内側も見える）、注ぎ口、底に少しの水。</summary>
        public static PartMesh Beaker()
        {
            return new PartMesh()
                .Lathe(Glass, new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.09f, 0f),
                    new Vector2(0.09f, 0.23f), new Vector2(0.097f, 0.238f),
                }, 32, At(0f, 0f, 0f), doubleSided: true)
                .Box(Glass, new Vector3(0.03f, 0.006f, 0.03f), At(new Vector3(0.1f, 0.236f, 0f), new Vector3(0f, 0f, 20f)))
                .Cylinder(MarbleGlass, 0.085f, 0.05f, 32, At(0f, 0.03f, 0f));
        }

        /// <summary>底を閉じた椀（底の半径 foot、口の半径 rim、高さ height）。上はご飯や汁の面で閉じる。</summary>
        private static Vector2[] Bowl(float foot, float rim, float height)
        {
            return new[]
            {
                new Vector2(0f, 0.006f), new Vector2(foot, 0.006f), new Vector2(foot, 0.006f),
                new Vector2((foot + rim) * 0.5f, height * 0.55f), new Vector2(rim, height), new Vector2(rim, height),
                new Vector2(0f, height),
            };
        }
    }
}
