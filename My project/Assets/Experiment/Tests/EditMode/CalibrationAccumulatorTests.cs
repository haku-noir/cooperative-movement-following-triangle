using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §5.1 のキャリブレーション。
    ///
    /// ここで出る L（体格指標）は §5.2 のスケール係数と §6.1 の誤差正規化の分母を兼ねる。
    /// 誤れば全条件の誤差値が同じ比率で歪むため、実験結果は一見もっともらしいまま無効になる。
    /// </summary>
    public class CalibrationAccumulatorTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void StaticPose_YieldsExactArmLength()
        {
            var accumulator = new CalibrationAccumulator();

            for (int i = 0; i < 90; i++)
            {
                accumulator.Add(SyntheticSamples.StaticPose(i / 90.0, armLength: 0.42f));
            }

            var result = accumulator.Build(0.15f);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.neckToLeftHandMeters, Is.EqualTo(0.42f).Within(Tolerance));
            Assert.That(result.neckToRightHandMeters, Is.EqualTo(0.42f).Within(Tolerance));
            Assert.That(result.characteristicLength, Is.EqualTo(0.42f).Within(Tolerance));
            Assert.That(result.characteristicLengthStdDev, Is.LessThan(1e-4f), "静止姿勢なのでばらつきは 0");
            Assert.That(result.sampleCount, Is.EqualTo(90));
        }

        /// <summary>
        /// 眼高 h は CenterEye のワールド Y。トラッキング原点が Floor Level である
        /// 前提に乗っている（§1, §5.1-1）。この前提は XrRuntimeConfig が実行時に検証する。
        /// </summary>
        [Test]
        public void EyeHeight_IsWorldYOfCenterEye()
        {
            var accumulator = new CalibrationAccumulator();
            for (int i = 0; i < 10; i++) accumulator.Add(SyntheticSamples.StaticPose(i / 90.0));

            var result = accumulator.Build(0.15f);

            Assert.That(result.eyeHeightMeters, Is.EqualTo(SyntheticSamples.HeadRest.y).Within(Tolerance));
        }

        [Test]
        public void AsymmetricArms_AveragesLeftAndRight()
        {
            var accumulator = new CalibrationAccumulator();
            Vector3 neck = VertexMath.NeckVertex(SyntheticSamples.HeadRest, 0.15f);

            for (int i = 0; i < 30; i++)
            {
                accumulator.Add(new BodyTriangleSample
                {
                    timestampSeconds = i / 90.0,
                    headPosition = SyntheticSamples.HeadRest,
                    headRotation = Quaternion.identity,
                    v0 = neck,
                    v1 = neck + new Vector3(-0.30f, 0f, 0f),
                    v2 = neck + new Vector3(0.50f, 0f, 0f),
                });
            }

            var result = accumulator.Build(0.15f);

            Assert.That(result.neckToLeftHandMeters, Is.EqualTo(0.30f).Within(Tolerance));
            Assert.That(result.neckToRightHandMeters, Is.EqualTo(0.50f).Within(Tolerance));
            Assert.That(result.characteristicLength, Is.EqualTo(0.40f).Within(Tolerance));
        }

        /// <summary>
        /// 保持が不安定だと標準偏差が立つ。実験者がキャリブレーションをやり直す
        /// 判断材料になるので、0 でないことを確認しておく。
        /// </summary>
        [Test]
        public void UnstableHold_ReportsNonZeroStdDev()
        {
            var accumulator = new CalibrationAccumulator();
            Vector3 neck = VertexMath.NeckVertex(SyntheticSamples.HeadRest, 0.15f);

            for (int i = 0; i < 60; i++)
            {
                float wobble = 0.02f * Mathf.Sin(i * 0.5f);
                accumulator.Add(new BodyTriangleSample
                {
                    timestampSeconds = i / 90.0,
                    headPosition = SyntheticSamples.HeadRest,
                    headRotation = Quaternion.identity,
                    v0 = neck,
                    v1 = neck + new Vector3(-(0.40f + wobble), 0f, 0f),
                    v2 = neck + new Vector3(0.40f + wobble, 0f, 0f),
                });
            }

            var result = accumulator.Build(0.15f);

            Assert.That(result.characteristicLengthStdDev, Is.GreaterThan(0.005f));
            Assert.That(result.characteristicLength, Is.EqualTo(0.40f).Within(0.005f));
        }

        [Test]
        public void EmptyAccumulator_ProducesInvalidCalibration()
        {
            var result = new CalibrationAccumulator().Build(0.15f);

            Assert.That(result.IsValid, Is.False);
        }

        /// <summary>
        /// 仕様書 §5.2 のスケール係数 s = L_B / L_A。
        /// 実行時の自由度にしてはならない値なので、算出は純関数として固定する。
        /// </summary>
        [Test]
        public void BodyScaleFactor_IsRatioOfCharacteristicLengths()
        {
            var performerA = new PerformerCalibration
            {
                eyeHeightMeters = 1.60f, characteristicLength = 0.40f, sampleCount = 10,
            };
            var participantB = new PerformerCalibration
            {
                eyeHeightMeters = 1.75f, characteristicLength = 0.50f, sampleCount = 10,
            };

            float scale = CalibrationAccumulator.BodyScaleFactor(performerA, participantB);

            Assert.That(scale, Is.EqualTo(1.25f).Within(Tolerance));
        }

        [Test]
        public void BodyScaleFactor_FallsBackToOneWhenCalibrationMissing()
        {
            var valid = new PerformerCalibration
            {
                eyeHeightMeters = 1.6f, characteristicLength = 0.4f, sampleCount = 1,
            };

            Assert.That(CalibrationAccumulator.BodyScaleFactor(null, valid), Is.EqualTo(1f));
            Assert.That(CalibrationAccumulator.BodyScaleFactor(valid, null), Is.EqualTo(1f));
            Assert.That(
                CalibrationAccumulator.BodyScaleFactor(new PerformerCalibration(), valid),
                Is.EqualTo(1f));
        }
    }
}
