using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 写真スポットの記念写真の構図と、ポラロイドの書き込み (#170)。
    /// シーンで実際に撮れて、キャラが写るかは PhotoSpotSelfiePlayTests が見る。
    /// </summary>
    public sealed class SelfieCompositionTests
    {
        private static CollectibleCatalog DataCatalog()
        {
            string path = Path.Combine(Application.dataPath, "Data", "Collectibles", "collectibles.json");
            return CollectibleCatalog.Parse(File.ReadAllText(path));
        }

        [Test]
        public void Read_WithoutNode_UsesDefaults()
        {
            SelfieComposition composition = SelfieComposition.Read(null);

            Assert.AreEqual(SelfieComposition.DefaultDistance, composition.Distance);
            Assert.IsNaN(composition.CameraHeight, "書かなければ背丈から決める");
            Assert.IsNaN(composition.AimHeight, "書かなければ背丈から決める");
            Assert.AreEqual(SelfieComposition.DefaultAimSide, composition.AimSide);
            Assert.AreEqual(SelfieComposition.DefaultFieldOfView, composition.FieldOfView);
            Assert.AreEqual(0f, composition.StandAngle);
        }

        [Test]
        public void Read_TakesValuesInRange_AndIgnoresOutOfRange()
        {
            var node = new Dictionary<string, object>
            {
                { "distance", 4.5 },
                { "cam_height", 1.6 },
                { "aim_height", 99.0 },
                { "aim_side", -0.8 },
                { "fov", 5.0 },
                { "stand_angle", 60.0 },
            };

            SelfieComposition composition = SelfieComposition.Read(node);

            Assert.AreEqual(4.5f, composition.Distance, 1e-5f);
            Assert.AreEqual(1.6f, composition.CameraHeight, 1e-5f);
            Assert.IsNaN(composition.AimHeight, "範囲外は既定（背丈から決める）のまま");
            Assert.AreEqual(-0.8f, composition.AimSide, 1e-5f);
            Assert.AreEqual(SelfieComposition.DefaultFieldOfView, composition.FieldOfView, "範囲外は既定値のまま");
            Assert.AreEqual(60f, composition.StandAngle, 1e-5f);
        }

        [Test]
        public void Heights_FollowTheSubject_UnlessWritten()
        {
            var composition = new SelfieComposition();
            Assert.AreEqual(1.15f * SelfieComposition.CameraHeightRatio, composition.CameraHeightFor(1.15f), 1e-5f);
            Assert.AreEqual(1.15f * SelfieComposition.AimRatio, composition.AimHeightFor(1.15f), 1e-5f);
            Assert.AreEqual(SelfieComposition.FallbackHeight * SelfieComposition.AimRatio, composition.AimHeightFor(0f), 1e-5f,
                "背丈が測れなければ既定の背丈");
            Assert.AreEqual(SelfieComposition.FallbackHeight * SelfieComposition.AimRatio, composition.AimHeightFor(float.NaN), 1e-5f);

            var written = new SelfieComposition { CameraHeight = 1.7f, AimHeight = 0.5f };
            Assert.AreEqual(1.7f, written.CameraHeightFor(1.15f), 1e-5f);
            Assert.AreEqual(0.5f, written.AimHeightFor(1.15f), 1e-5f);
        }

        [Test]
        public void ADefaultShot_FramesAChibiAndATallSubjectFromTheShinsToTheHead()
        {
            var composition = new SelfieComposition();
            var tripod = Vector3.zero;
            var feet = new Vector3(0f, 0f, composition.Distance);
            float tanY = Mathf.Tan(composition.FieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (float height in new[] { 1.22f, 1.28f, 1.6f })
            {
                composition.CameraPose(tripod, feet, height, 0f, out Vector3 position, out Quaternion rotation);
                Vector3 head = Quaternion.Inverse(rotation) * (feet + Vector3.up * height - position);
                Vector3 shin = Quaternion.Inverse(rotation) * (feet + Vector3.up * (height * 0.2f) - position);
                float headY = 0.5f + 0.5f * head.y / (head.z * tanY);
                float shinY = 0.5f + 0.5f * shin.y / (shin.z * tanY);
                Assert.That(headY, Is.InRange(0.5f, 0.9f), "背丈 " + height + " の頭（上に景色が入る）");
                Assert.That(shinY, Is.InRange(0.03f, 0.4f), "背丈 " + height + " のすね");
            }
        }

        [Test]
        public void StandPoint_IsDistanceAlongLookDir_AtTheTripodHeight()
        {
            var composition = new SelfieComposition { Distance = 3f };
            var tripod = new Vector3(10f, 2f, -5f);

            Vector3 stand = composition.StandPoint(tripod, new Vector2(0f, 2f));
            Assert.That(Vector3.Distance(stand, new Vector3(10f, 2f, -2f)), Is.LessThan(1e-4f), stand.ToString());

            Vector3 diagonal = composition.StandPoint(tripod, new Vector2(1f, 1f));
            Assert.AreEqual(3f, Vector3.Distance(diagonal, tripod), 1e-4f);
            Assert.AreEqual(tripod.y, diagonal.y, 1e-5f);

            Assert.AreEqual(tripod, composition.StandPoint(tripod, Vector2.zero), "向きが無ければ三脚の位置");
        }

        [Test]
        public void CameraPose_SitsOnTheTripod_AndAimsAtTheSubjectsChest()
        {
            var composition = new SelfieComposition();
            var tripod = new Vector3(0f, 1f, 0f);
            var feet = new Vector3(0f, 1f, 3f);

            composition.CameraPose(tripod, feet, 1.2f, 0f, out Vector3 position, out Quaternion rotation);

            var expected = new Vector3(0f, 1f + composition.CameraHeightFor(1.2f), SelfieComposition.LensForward);
            Assert.That(Vector3.Distance(position, expected), Is.LessThan(1e-4f), position.ToString());

            Vector3 aim = (feet + Vector3.up * composition.AimHeightFor(1.2f) - position).normalized;
            Assert.That(Vector3.Dot(rotation * Vector3.forward, aim), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void CameraPose_AimSide_PutsTheSubjectToTheLeftForPositiveValues()
        {
            var composition = new SelfieComposition { AimSide = 1f };
            var tripod = Vector3.zero;
            var feet = new Vector3(0f, 0f, 3f);

            composition.CameraPose(tripod, feet, 1.2f, 0f, out Vector3 position, out Quaternion rotation);

            // 狙いが右（+x）へずれるので、キャラ（x = 0）はカメラから見て左。
            Vector3 local = Quaternion.Inverse(rotation) * (feet + Vector3.up * composition.AimHeightFor(1.2f) - position);
            Assert.That(local.x, Is.LessThan(0f));
        }

        [Test]
        public void BackOff_MovesTheCameraAwayFromASubjectThatStoppedShort()
        {
            var tripod = Vector3.zero;
            var near = new Vector3(0f, 0f, 0.7f);
            float needed = SelfieComposition.BackOffNeeded(tripod, near);
            Assert.AreEqual(SelfieComposition.MinShotDistance - (0.7f - SelfieComposition.LensForward), needed, 1e-4f);
            Assert.AreEqual(0f, SelfieComposition.BackOffNeeded(tripod, new Vector3(0f, 0f, 3f)), "立ち位置まで行けたら引かない");

            new SelfieComposition().CameraPose(tripod, near, 1.2f, needed, out Vector3 position, out Quaternion _);
            Assert.AreEqual(SelfieComposition.MinShotDistance, near.z - position.z, 1e-4f);
        }

        [Test]
        public void TurnedStandPoint_ShiftsTheAimTowardTheView()
        {
            // look_dir は +z。立ち位置を 90 度回すと +x に立ち、カメラは +x を向く。景色（+z）はカメラの左。
            var composition = new SelfieComposition();
            var tripod = Vector3.zero;
            Vector3 feet = composition.StandPoint(tripod, new Vector2(0f, 1f), 90f);
            Assert.That(Vector3.Distance(feet, new Vector3(composition.Distance, 0f, 0f)), Is.LessThan(1e-4f), feet.ToString());

            composition.CameraPose(tripod, feet, 1.2f, 0f, 90f, out Vector3 position, out Quaternion rotation);
            Vector3 forward = rotation * Vector3.forward;
            Assert.That(forward.z, Is.GreaterThan(0.1f), "狙いが景色（+z）のほうへずれる: " + forward);

            Vector3 subject = Quaternion.Inverse(rotation) * (feet + Vector3.up * 0.6f - position);
            Assert.That(subject.x, Is.GreaterThan(0f).And.LessThan(subject.z * 0.8f), "キャラは画面の右寄り、画角の中");
        }

        [Test]
        public void StandAngle_TurnsTheStandPointClockwiseSeenFromAbove()
        {
            var composition = new SelfieComposition { Distance = 2f, StandAngle = 90f };
            Vector3 stand = composition.StandPoint(Vector3.zero, new Vector2(0f, 1f));
            Assert.That(Vector3.Distance(stand, new Vector3(2f, 0f, 0f)), Is.LessThan(1e-4f), stand.ToString());
        }

        [Test]
        public void FacingYaw_TurnsTheSubjectTowardTheTripod()
        {
            Assert.AreEqual(180f, Mathf.Abs(SelfieComposition.FacingYaw(new Vector3(0f, 0f, 3f), Vector3.zero, 0f)), 1e-3f);
            Assert.AreEqual(-90f, SelfieComposition.FacingYaw(new Vector3(3f, 0f, 0f), Vector3.zero, 0f), 1e-3f);
            Assert.AreEqual(42f, SelfieComposition.FacingYaw(Vector3.zero, Vector3.zero, 42f), "重なっていれば今の向き");
        }

        [Test]
        public void EverySpotInTheData_HasAUsableComposition()
        {
            foreach (CatalogPhotoSpot spot in DataCatalog().PhotoSpots)
            {
                Assert.IsNotNull(spot.Selfie, spot.Id);
                Assert.That(spot.Selfie.Distance, Is.InRange(1.5f, 8f), spot.Id);
                Assert.That(spot.Selfie.FieldOfView, Is.InRange(20f, 75f), spot.Id);
            }
        }

        [Test]
        public void Stamp_WritesTheDayAndTheClock()
        {
            Assert.AreEqual("3日目  14:05", PhotoCutIn.Stamp(3, 14f + 5f / 60f + 1e-4f, false));
            Assert.AreEqual("Day 3  14:05", PhotoCutIn.Stamp(3, 14f + 5f / 60f + 1e-4f, true));
            Assert.AreEqual("1日目  01:30", PhotoCutIn.Stamp(1, 25.5f, false), "24 時を越えたら翌日の時刻");
            Assert.AreEqual("1日目  09:00", PhotoCutIn.Stamp(1, 9f, false));
        }
    }
}
