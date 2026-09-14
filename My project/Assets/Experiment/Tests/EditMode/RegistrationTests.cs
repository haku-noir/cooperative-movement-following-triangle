using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §5.2 の体格正規化と §5.3 の位置合わせ。
    ///
    /// ここが狂うと、A の三角形が B の空間で系統的にずれた場所に置かれる。
    /// 画面上は「それらしく」見えてしまい、実機での目視では気づけない。
    /// 既知の変換を与えて、それが復元できることをテストで固定する。
    /// </summary>
    public class RegistrationTests
    {
        private const float PositionTolerance = 1e-4f;

        /// <summary>
        /// 首が原点上、両手が左右前方に開いた基準三角形。
        /// 手を z 方向にも出しているのは、真上から見て 3 点が一直線にならないようにするため。
        /// 一直線だとヨーが不定になり（§7.2 の退化）、テストの意味がなくなる。
        /// </summary>
        private static Triangle Reference(float armSpan = 0.40f, float neckHeight = 1.45f)
        {
            var neck = new Vector3(0f, neckHeight, 0f);
            return new Triangle(
                neck,
                neck + new Vector3(-armSpan, 0f, 0.20f),
                neck + new Vector3(armSpan, 0f, 0.20f));
        }

        /// <summary>
        /// 三角形の実寸からキャリブレーションを作る。
        ///
        /// 体格正規化のスケールはキャリブレーションの L から決まる（§5.2）のであって、
        /// Registration に渡す基準三角形の形から決まるのではない。両者が食い違っていると
        /// その差はそのまま残差になる。テストではこの 2 つを整合させておく。
        /// </summary>
        private static PerformerCalibration CalibrationFor(in Triangle triangle, float eyeHeight = 1.60f)
        {
            float left = Vector3.Distance(triangle.V1, triangle.V0);
            float right = Vector3.Distance(triangle.V2, triangle.V0);

            return new PerformerCalibration
            {
                eyeHeightMeters = eyeHeight,
                neckToLeftHandMeters = left,
                neckToRightHandMeters = right,
                characteristicLength = 0.5f * (left + right),
                sampleCount = 100,
            };
        }

        // ------------------------------------------------------------------
        // 基本性質
        // ------------------------------------------------------------------

        [Test]
        public void IdenticalTrianglesAndBodies_YieldIdentityTransform()
        {
            var triangle = Reference();
            var calibration = CalibrationFor(triangle);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, triangle, triangle, calibration, calibration);

            Assert.That(transform.scale, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(transform.yawDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(transform.translation.magnitude, Is.LessThan(PositionTolerance));
            Assert.That(Registration.ResidualRms(transform, triangle, triangle),
                Is.LessThan(PositionTolerance));
        }

        [Test]
        public void PureTranslation_IsRecoveredExactly()
        {
            var a = Reference();
            var offset = new Vector3(0.35f, 0.12f, -0.80f);
            var b = new Triangle(a.V0 + offset, a.V1 + offset, a.V2 + offset);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            Assert.That(transform.yawDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Vector3.Distance(transform.translation, offset), Is.LessThan(PositionTolerance));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.LessThan(PositionTolerance));
        }

        [TestCase(15f)]
        [TestCase(-40f)]
        [TestCase(90f)]
        [TestCase(179f)]
        public void PureYaw_IsRecoveredExactly(float yawDegrees)
        {
            var a = Reference();
            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            var b = new Triangle(rotation * a.V0, rotation * a.V1, rotation * a.V2);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            Assert.That(Mathf.DeltaAngle(transform.yawDegrees, yawDegrees), Is.EqualTo(0f).Within(1e-2f));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.LessThan(PositionTolerance));
        }

        [Test]
        public void YawAndTranslation_AreRecoveredTogether()
        {
            var a = Reference();
            var rotation = Quaternion.Euler(0f, 37f, 0f);
            var offset = new Vector3(-0.5f, 0.08f, 1.1f);
            var b = new Triangle(
                rotation * a.V0 + offset, rotation * a.V1 + offset, rotation * a.V2 + offset);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            Assert.That(Mathf.DeltaAngle(transform.yawDegrees, 37f), Is.EqualTo(0f).Within(1e-2f));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.LessThan(PositionTolerance));
        }

        // ------------------------------------------------------------------
        // 注意点 2：ピッチ・ロールを含めない（§5.3-3）
        // ------------------------------------------------------------------

        /// <summary>
        /// B の三角形がピッチ方向に傾いていても、Registration はヨーしか出さない。
        ///
        /// 重力方向は A と B で共有されているので、傾ける理由がない。
        /// ピッチ・ロールを許すと、A の三角形全体が重力に対して傾き、
        /// V0 の重力方向固定（§2.2）が実質的に壊れる。
        /// </summary>
        [Test]
        public void PitchInBaseline_DoesNotProducePitchInTransform()
        {
            var a = Reference();
            var pitched = Quaternion.Euler(25f, 0f, 0f);
            var b = new Triangle(pitched * a.V0, pitched * a.V1, pitched * a.V2);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            // 変換は Y 軸回転のみで構成されている。
            var euler = transform.YawRotation.eulerAngles;
            Assert.That(Mathf.DeltaAngle(euler.x, 0f), Is.EqualTo(0f).Within(1e-3f), "pitch");
            Assert.That(Mathf.DeltaAngle(euler.z, 0f), Is.EqualTo(0f).Within(1e-3f), "roll");

            // 傾きは吸収できないので残差として残る。これは正しい挙動。
            Assert.That(Registration.ResidualRms(transform, a, b), Is.GreaterThan(0.01f),
                "ピッチは吸収せず残差に出るべき");
        }

        [Test]
        public void RollInBaseline_DoesNotProduceRollInTransform()
        {
            var a = Reference();
            var rolled = Quaternion.Euler(0f, 0f, 20f);
            var b = new Triangle(rolled * a.V0, rolled * a.V1, rolled * a.V2);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            var euler = transform.YawRotation.eulerAngles;
            Assert.That(Mathf.DeltaAngle(euler.x, 0f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Mathf.DeltaAngle(euler.z, 0f), Is.EqualTo(0f).Within(1e-3f));
        }

        /// <summary>
        /// 身長差は並進で吸収され、ヨーには漏れない。
        /// ヨーの推定に Y 成分が寄与しないことの確認。
        /// </summary>
        [Test]
        public void HeightDifference_DoesNotLeakIntoYaw()
        {
            var a = Reference(neckHeight: 1.45f);
            var b = Reference(neckHeight: 1.62f);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            Assert.That(transform.yawDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(transform.translation.y, Is.EqualTo(0.17f).Within(PositionTolerance));
        }

        // ------------------------------------------------------------------
        // 注意点 3：体格正規化は固定値、中心は基準フレームの V0_A（§5.2）
        // ------------------------------------------------------------------

        [Test]
        public void BodyScale_IsRatioOfCharacteristicLengths()
        {
            var a = Reference();

            // B は A を首頂点中心に 1.25 倍しただけの体格。キャリブレーションも
            // その三角形の実寸から作るので、L の比はちょうど 1.25 になる。
            var b = Registration.ScaleAbout(a, a.V0, 1.25f);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares,
                a, b, CalibrationFor(a), CalibrationFor(b));

            Assert.That(transform.scale, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.LessThan(1e-3f),
                "体格差はスケールで吸収され、残差はほぼ 0 になるはず");
        }

        /// <summary>
        /// キャリブレーションの L と、Registration に渡す基準三角形の実寸が食い違うと、
        /// その差は残差として現れる。スケールが「基準三角形の形」ではなく
        /// 「キャリブレーション値」から決まっていることの裏返しであり、仕様書 §5.2 のとおり。
        ///
        /// 実運用では、キャリブレーションと基準姿勢が別タイミングで取られるため
        /// この食い違いは必ず少しは生じる。残差 RMS をログに残しておく理由でもある。
        /// </summary>
        [Test]
        public void ScaleComesFromCalibrationNotFromReferenceTriangleShape()
        {
            var a = Reference();
            var b = Registration.ScaleAbout(a, a.V0, 1.25f);

            // B のキャリブレーションだけ 10% 大きく申告する（測定タイミングのずれを模擬）。
            var inflated = CalibrationFor(b);
            inflated.characteristicLength *= 1.10f;

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, CalibrationFor(a), inflated);

            Assert.That(transform.scale, Is.EqualTo(1.25f * 1.10f).Within(1e-4f));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.GreaterThan(0.01f),
                "食い違いは吸収されず残差に出るべき");
        }

        /// <summary>
        /// スケールの中心は Registration 基準フレームの首頂点であり、毎フレームの V0_A ではない。
        ///
        /// この違いは基準フレームでは見えない（首頂点は不動点なので）。
        /// 差が出るのは A が並進した後のフレーム。固定点中心なら頭部並進も s 倍される。
        /// 毎フレーム中心だと並進振幅が元のまま残り、区間2（並進のみ）の
        /// 刺激強度が体格補正を受けないことになる。
        /// </summary>
        [Test]
        public void ScalePivot_IsFixedSoTranslationIsAlsoScaled()
        {
            var a = Reference();
            var b = Registration.ScaleAbout(a, a.V0, 2f);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares,
                a, b, CalibrationFor(a), CalibrationFor(b));

            Assert.That(transform.scale, Is.EqualTo(2f).Within(1e-5f));

            // 基準フレームの首頂点はスケールの不動点。ここでは剛体変換も恒等なので動かない。
            Assert.That(Vector3.Distance(transform.Apply(a.V0), b.V0), Is.LessThan(PositionTolerance));

            // 本題：A が並進したフレームでの変位。固定点中心の相似変換なので s 倍される。
            // 毎フレームの V0_A を中心にする実装だと、頭部並進は元の大きさのまま残り、
            // 区間2（並進のみ）の刺激振幅が体格補正を受けないことになる。
            var displacement = new Vector3(0.30f, 0.05f, -0.10f);
            Vector3 mapped = transform.Apply(a.V0 + displacement) - transform.Apply(a.V0);

            Assert.That(Vector3.Distance(mapped, displacement * 2f), Is.LessThan(PositionTolerance),
                "固定点中心の相似変換なら頭部並進も s 倍される");
        }

        /// <summary>
        /// 変位の拡大はヨー回転とも整合する。方向は回り、大きさは s 倍。
        /// 剛体変換と相似変換の合成順（スケールが先）が守られていることの確認。
        /// </summary>
        [Test]
        public void DisplacementIsScaledThenRotated()
        {
            var a = Reference();
            var scaled = Registration.ScaleAbout(a, a.V0, 1.5f);
            var rotation = Quaternion.Euler(0f, 60f, 0f);
            var offset = new Vector3(0.2f, 0f, -0.4f);
            var b = new Triangle(
                rotation * scaled.V0 + offset,
                rotation * scaled.V1 + offset,
                rotation * scaled.V2 + offset);

            var transform = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares,
                a, b, CalibrationFor(a), CalibrationFor(scaled));

            var displacement = new Vector3(0.30f, 0f, 0f);
            Vector3 mapped = transform.Apply(a.V0 + displacement) - transform.Apply(a.V0);
            Vector3 expected = rotation * (displacement * 1.5f);

            Assert.That(Vector3.Distance(mapped, expected), Is.LessThan(1e-3f));
        }

        // ------------------------------------------------------------------
        // 3 つの推定法
        // ------------------------------------------------------------------

        /// <summary>
        /// 剛体変換だけで完全に一致させられる場合、3 手法はすべて同じ答えを出す。
        /// 手法の差が出るのは、一致させきれない（優決定が効く）ときだけ。
        /// </summary>
        [TestCase(RegistrationMethod.ThreeVertexLeastSquares)]
        [TestCase(RegistrationMethod.NeckAnchored)]
        [TestCase(RegistrationMethod.HandsOnly)]
        public void AllMethods_AgreeWhenExactFitExists(RegistrationMethod method)
        {
            var a = Reference();
            var rotation = Quaternion.Euler(0f, 52f, 0f);
            var offset = new Vector3(0.4f, -0.1f, 0.7f);
            var b = new Triangle(
                rotation * a.V0 + offset, rotation * a.V1 + offset, rotation * a.V2 + offset);
            var calibration = CalibrationFor(a);

            var transform = Registration.Solve(method, a, b, calibration, calibration);

            Assert.That(Mathf.DeltaAngle(transform.yawDegrees, 52f), Is.EqualTo(0f).Within(1e-2f));
            Assert.That(Registration.ResidualRms(transform, a, b), Is.LessThan(1e-3f));
        }

        /// <summary>
        /// 首頂点固定法では、首の残差がちょうど 0 になる。
        /// 「首を身体の基点とみなす」という選択がそのまま結果に現れることの確認。
        /// </summary>
        [Test]
        public void NeckAnchored_LeavesZeroResidualAtNeck()
        {
            var a = Reference();

            // 手だけ非対称にずらし、剛体変換では一致させられない状況を作る。
            var b = new Triangle(
                a.V0 + new Vector3(0.10f, 0.05f, 0f),
                a.V1 + new Vector3(0.10f, 0.05f, 0.06f),
                a.V2 + new Vector3(0.10f, 0.05f, -0.03f));
            var calibration = CalibrationFor(a);

            var neckAnchored = Registration.Solve(
                RegistrationMethod.NeckAnchored, a, b, calibration, calibration);
            var leastSquares = Registration.Solve(
                RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration);

            float neckResidualAnchored = Vector3.Distance(neckAnchored.Apply(a.V0), b.V0);
            float neckResidualLeastSquares = Vector3.Distance(leastSquares.Apply(a.V0), b.V0);

            Assert.That(neckResidualAnchored, Is.LessThan(PositionTolerance),
                "首頂点固定法では首の残差は 0");
            Assert.That(neckResidualLeastSquares, Is.GreaterThan(neckResidualAnchored),
                "等重み法では首にも残差が配分される");
        }

        /// <summary>
        /// 等重み最小二乗は、3 頂点の残差二乗和で他の手法以下になる。定義どおりであることの確認。
        /// </summary>
        [Test]
        public void LeastSquares_MinimisesTotalResidual()
        {
            var a = Reference();
            var b = new Triangle(
                a.V0 + new Vector3(0.10f, 0.05f, 0f),
                a.V1 + new Vector3(0.10f, 0.05f, 0.06f),
                a.V2 + new Vector3(0.10f, 0.05f, -0.03f));
            var calibration = CalibrationFor(a);

            float leastSquares = Registration.ResidualRms(
                Registration.Solve(RegistrationMethod.ThreeVertexLeastSquares, a, b, calibration, calibration),
                a, b);
            float neckAnchored = Registration.ResidualRms(
                Registration.Solve(RegistrationMethod.NeckAnchored, a, b, calibration, calibration),
                a, b);
            float handsOnly = Registration.ResidualRms(
                Registration.Solve(RegistrationMethod.HandsOnly, a, b, calibration, calibration),
                a, b);

            Assert.That(leastSquares, Is.LessThanOrEqualTo(neckAnchored + 1e-6f));
            Assert.That(leastSquares, Is.LessThanOrEqualTo(handsOnly + 1e-6f));
        }

        // ------------------------------------------------------------------
        // 変換の適用
        // ------------------------------------------------------------------

        [Test]
        public void ApplyDirection_IgnoresScaleAndTranslation()
        {
            var transform = new StimulusTransform
            {
                scale = 2.5f,
                scalePivot = new Vector3(1f, 2f, 3f),
                yawDegrees = 90f,
                translation = new Vector3(10f, -5f, 7f),
            };

            Vector3 rotated = transform.ApplyDirection(Vector3.forward);

            Assert.That(Vector3.Distance(rotated, Vector3.right), Is.LessThan(1e-5f));
            Assert.That(rotated.magnitude, Is.EqualTo(1f).Within(1e-5f), "方向はスケールされない");
        }

        [Test]
        public void ApplyRotation_AddsYawOnlyAndKeepsPitch()
        {
            var transform = new StimulusTransform
            {
                scale = 1f, scalePivot = Vector3.zero, yawDegrees = 30f, translation = Vector3.zero,
            };

            var original = Quaternion.Euler(20f, 0f, 0f);
            var result = transform.ApplyRotation(original);
            var euler = result.eulerAngles;

            Assert.That(Mathf.DeltaAngle(euler.y, 30f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Mathf.DeltaAngle(euler.x, 20f), Is.EqualTo(0f).Within(1e-3f),
                "A のピッチはそのまま残す");
        }
    }
}
