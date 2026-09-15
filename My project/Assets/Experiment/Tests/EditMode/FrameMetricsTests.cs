using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §8.3 が名指しで要求するテスト：
    /// 「既知の変位（例：B の三角形を A から x 方向に 0.1 m ずらした状態）に対して、
    ///   頂点距離・Procrustes 成分が期待値を返すことのテスト」
    ///
    /// 誤差算出の誤りは実機では絶対に見つからない。画面上は正しく見えたまま
    /// 数値だけが狂い、しかもその数値が論文の結論になる。
    /// </summary>
    public class FrameMetricsTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>首が (0, 1.45, 0)、両手が左右前方に開いた三角形。</summary>
        private static BodyTriangleSample Pose(
            Vector3 neckPosition, Quaternion headRotation = default, float armSpan = 0.40f)
        {
            if (headRotation.Equals(default(Quaternion))) headRotation = Quaternion.identity;

            return new BodyTriangleSample
            {
                timestampSeconds = 0.0,
                headPosition = neckPosition + new Vector3(0f, 0.15f, 0f),
                headRotation = headRotation,
                v0 = neckPosition,
                v1 = neckPosition + new Vector3(-armSpan, 0f, 0.20f),
                v2 = neckPosition + new Vector3(armSpan, 0f, 0.20f),
                leftHand = new HandTrackingState { isTracked = true, confidence = HandConfidence.High },
                rightHand = new HandTrackingState { isTracked = true, confidence = HandConfidence.High },
            };
        }

        private static readonly Vector3 Neck = new Vector3(0f, 1.45f, 0f);

        // ------------------------------------------------------------------
        // §8.3 の指定：x 方向に 0.1 m ずらした状態
        // ------------------------------------------------------------------

        [Test]
        public void KnownTranslation_ProducesExpectedVertexDistances()
        {
            var a = Pose(Neck);
            var b = Pose(Neck + new Vector3(0.1f, 0f, 0f));

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            // 3 頂点すべてが同じだけずれているので、各距離はちょうど 0.1 m。
            Assert.That(metrics.distanceV0, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(metrics.distanceV1, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(metrics.distanceV2, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(metrics.distanceSum, Is.EqualTo(0.3f).Within(Tolerance));

            // 正規化は固定分母 0.5 m。0.3 / 0.5 = 0.6。
            Assert.That(metrics.distanceSumNormalized, Is.EqualTo(0.6f).Within(Tolerance));
        }

        [Test]
        public void KnownTranslation_ProducesPureTranslationInProcrustes()
        {
            var a = Pose(Neck);
            var b = Pose(Neck + new Vector3(0.1f, 0f, 0f));

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            // 純粋な平行移動なので、剛体成分は並進 0.1 m、回転 0 度、形状残差 0。
            Assert.That(metrics.procrustesTranslation, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(metrics.procrustesResidual, Is.LessThan(Tolerance));
            Assert.That(metrics.procrustesResidualNormalized, Is.LessThan(Tolerance));
            Assert.That(metrics.procrustesRotationWellDefined, Is.True);
        }

        [Test]
        public void IdenticalTriangles_ProduceZeroError()
        {
            var a = Pose(Neck);

            var metrics = FrameMetrics.Compute(a, a, normalizationLength: 0.5f);

            Assert.That(metrics.distanceSum, Is.LessThan(Tolerance));
            Assert.That(metrics.procrustesTranslation, Is.LessThan(Tolerance));
            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(metrics.procrustesResidual, Is.LessThan(Tolerance));
            Assert.That(metrics.headDirectionResidualDegrees, Is.EqualTo(0f).Within(1e-3f));
        }

        // ------------------------------------------------------------------
        // Procrustes の回転成分（3 自由度）
        // ------------------------------------------------------------------

        [TestCase(10f, 0f, 0f)]
        [TestCase(0f, 35f, 0f)]
        [TestCase(0f, 0f, -22f)]
        [TestCase(12f, -40f, 18f)]
        public void KnownRotation_IsRecoveredAsAxisAngleMagnitude(float pitch, float yaw, float roll)
        {
            var a = Pose(Neck);
            var rotation = Quaternion.Euler(pitch, yaw, roll);

            // A の三角形を首頂点まわりに回したものを B とする。
            var b = a;
            b.v0 = Neck;
            b.v1 = Neck + rotation * (a.v1 - Neck);
            b.v2 = Neck + rotation * (a.v2 - Neck);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            // 期待値は Unity が言う回転角そのもの。
            float expectedAngle = Quaternion.Angle(Quaternion.identity, rotation);

            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(expectedAngle).Within(0.05f));

            // 剛体変換で完全に一致するので、形状残差は 0。
            // ここが 0 でなければ回転行列の符号規約を取り違えている。
            Assert.That(metrics.procrustesResidual, Is.LessThan(1e-3f),
                "剛体回転で一致する配置なら形状残差は 0");
        }

        /// <summary>
        /// 回転は重心まわりに取るので、重心が動いていなければ平行移動量は 0 になる。
        ///
        /// 回転中心を原点にしてしまうと、重心が一致していても回転が大きいだけで
        /// 平行移動量が大きく出る。首の高さ 1.45 m で 30 度回せば 0.7 m を超え、
        /// 「三角形全体がどれだけずれたか」という指標として意味をなさなくなる。
        /// </summary>
        [Test]
        public void RotationAboutCentroid_ProducesZeroTranslation()
        {
            var a = Pose(Neck);
            var centroid = (a.v0 + a.v1 + a.v2) / 3f;
            var rotation = Quaternion.Euler(30f, 0f, 0f);

            // 重心まわりに回すだけ。重心は動かない。
            var b = a;
            b.v0 = centroid + rotation * (a.v0 - centroid);
            b.v1 = centroid + rotation * (a.v1 - centroid);
            b.v2 = centroid + rotation * (a.v2 - centroid);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.procrustesTranslation, Is.LessThan(1e-3f),
                "重心が動いていないなら平行移動量は 0");
            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(30f).Within(0.05f));
            Assert.That(metrics.procrustesResidual, Is.LessThan(1e-3f));
        }

        /// <summary>
        /// 並進と回転が同時に起きている場合も、平行移動量は重心変位そのものになる。
        /// </summary>
        [Test]
        public void TranslationComponent_IsCentroidDisplacement()
        {
            var a = Pose(Neck);
            var centroid = (a.v0 + a.v1 + a.v2) / 3f;
            var rotation = Quaternion.Euler(0f, 25f, 0f);
            var shift = new Vector3(0.12f, 0.03f, -0.08f);

            var b = a;
            b.v0 = centroid + rotation * (a.v0 - centroid) + shift;
            b.v1 = centroid + rotation * (a.v1 - centroid) + shift;
            b.v2 = centroid + rotation * (a.v2 - centroid) + shift;

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.procrustesTranslation, Is.EqualTo(shift.magnitude).Within(1e-3f));
            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(25f).Within(0.05f));
            Assert.That(metrics.procrustesResidual, Is.LessThan(1e-3f));
        }

        /// <summary>
        /// ピッチ・ロールの回転も拾う。Registration（§5.3）がヨー限定なのに対し、
        /// 誤差指標としての Procrustes は 3 自由度で評価する。
        /// 手の上下のずれが回転成分として現れる。
        /// </summary>
        [Test]
        public void ProcrustesRotation_CapturesNonYawRotation()
        {
            var a = Pose(Neck);
            var rollOnly = Quaternion.Euler(0f, 0f, 30f);

            var b = a;
            b.v0 = Neck;
            b.v1 = Neck + rollOnly * (a.v1 - Neck);
            b.v2 = Neck + rollOnly * (a.v2 - Neck);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(30f).Within(0.05f),
                "ヨー限定なら 0 になってしまう回転を拾えること");
        }

        /// <summary>
        /// 剛体変換では一致させられない「形の違い」は、回転ではなく形状残差に出る。
        /// これが §6.1-2 の「剛体成分と形状残差の分解」の意味。
        /// </summary>
        [Test]
        public void ShapeDifference_AppearsAsResidualNotRotation()
        {
            var a = Pose(Neck, armSpan: 0.40f);
            var b = Pose(Neck, armSpan: 0.55f);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.procrustesResidual, Is.GreaterThan(0.01f));
            Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(0f).Within(0.5f),
                "左右対称に広がっただけなら回転成分は出ない");
            Assert.That(metrics.procrustesResidualNormalized,
                Is.EqualTo(metrics.procrustesResidual / 0.5f).Within(Tolerance));
        }

        // ------------------------------------------------------------------
        // 正規化分母（§6.1）
        // ------------------------------------------------------------------

        /// <summary>
        /// 分母は引数で与えた固定値だけを使う。B の三角形の実寸には依存しない。
        ///
        /// これが仕様書 §5.2 の趣旨を誤差側でも守る要。毎フレームの三角形から
        /// 分母を取ると、被験者が腕を伸ばすだけで正規化誤差が下がる抜け道になる。
        /// </summary>
        [Test]
        public void NormalizationUsesGivenLengthNotFrameGeometry()
        {
            var a = Pose(Neck);
            var narrow = Pose(Neck + new Vector3(0.1f, 0f, 0f), armSpan: 0.30f);
            var wide = Pose(Neck + new Vector3(0.1f, 0f, 0f), armSpan: 0.90f);

            var narrowMetrics = FrameMetrics.Compute(a, narrow, normalizationLength: 0.5f);
            var wideMetrics = FrameMetrics.Compute(a, wide, normalizationLength: 0.5f);

            // どちらも首頂点は同じだけずれている。
            Assert.That(narrowMetrics.distanceV0, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(wideMetrics.distanceV0, Is.EqualTo(0.1f).Within(Tolerance));

            // 腕を広げても分母は変わらないので、首頂点の寄与は同じ比率で効く。
            float narrowV0Contribution = narrowMetrics.distanceV0 / 0.5f;
            float wideV0Contribution = wideMetrics.distanceV0 / 0.5f;
            Assert.That(wideV0Contribution, Is.EqualTo(narrowV0Contribution).Within(Tolerance));
        }

        [Test]
        public void ZeroNormalizationLength_LeavesNormalisedValuesAtZero()
        {
            var a = Pose(Neck);
            var b = Pose(Neck + new Vector3(0.1f, 0f, 0f));

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0f);

            // 0 除算で無限大や NaN を CSV に書かない。
            Assert.That(metrics.distanceSumNormalized, Is.EqualTo(0f));
            Assert.That(float.IsNaN(metrics.distanceSumNormalized), Is.False);
        }

        // ------------------------------------------------------------------
        // §6.1-3 頭部方向残差
        // ------------------------------------------------------------------

        [TestCase(0f, 0f)]
        [TestCase(30f, 30f)]
        [TestCase(-45f, 45f)]
        public void HeadDirectionResidual_IsAngleBetweenForwardVectors(float yaw, float expected)
        {
            var a = Pose(Neck, Quaternion.identity);
            var b = Pose(Neck, Quaternion.Euler(0f, yaw, 0f));

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.headDirectionResidualDegrees, Is.EqualTo(expected).Within(1e-2f));
        }

        /// <summary>
        /// 頭部のロールは前方ベクトルを変えないので、方向残差には出ない。
        /// この指標が「どこを向いているか」だけを見ていることの確認。
        /// </summary>
        [Test]
        public void HeadDirectionResidual_IgnoresRollAboutForwardAxis()
        {
            var a = Pose(Neck, Quaternion.identity);
            var b = Pose(Neck, Quaternion.Euler(0f, 0f, 45f));

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.headDirectionResidualDegrees, Is.EqualTo(0f).Within(1e-2f));
        }

        // ------------------------------------------------------------------
        // §6.1-4 退化検出
        // ------------------------------------------------------------------

        [Test]
        public void TriangleArea_MatchesAnalyticValue()
        {
            var a = Pose(Neck);
            var b = Pose(Neck, armSpan: 0.40f);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            // 底辺 0.80 m（両手間）、高さ 0.20 m（首から手の前方距離）。
            Assert.That(metrics.triangleAreaB, Is.EqualTo(0.5f * 0.80f * 0.20f).Within(Tolerance));
        }

        [Test]
        public void MinInteriorAngle_MatchesAnalyticValue()
        {
            var a = Pose(Neck);
            var b = Pose(Neck, armSpan: 0.40f);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            // 二等辺三角形。頂角は 2 * atan(0.40 / 0.20) = 126.87 度、
            // 底角は (180 - 126.87) / 2 = 26.57 度。最小はこちら。
            float apex = 2f * Mathf.Atan2(0.40f, 0.20f) * Mathf.Rad2Deg;
            float baseAngle = (180f - apex) * 0.5f;

            Assert.That(metrics.triangleMinAngleB, Is.EqualTo(baseAngle).Within(1e-2f));
        }

        /// <summary>
        /// 3 頂点が一直線に近づくと最小内角が 0 に近づく（§7.2）。
        /// アプリはこの値を出すだけで、閾値判定はしない（§6.3）。
        /// </summary>
        [Test]
        public void CollinearTriangle_ReportsNearZeroMinAngle()
        {
            var a = Pose(Neck);

            var b = a;
            b.v0 = Neck;
            b.v1 = Neck + new Vector3(-0.40f, 0f, 0.0005f);
            b.v2 = Neck + new Vector3(0.40f, 0f, 0f);

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.triangleMinAngleB, Is.LessThan(1f));
            Assert.That(metrics.triangleAreaB, Is.LessThan(0.001f));

            // 値は出す。NaN や無限大にはしない。
            Assert.That(float.IsNaN(metrics.procrustesRotationDegrees), Is.False);
            Assert.That(float.IsInfinity(metrics.procrustesRotationDegrees), Is.False);
        }

        /// <summary>
        /// 3 頂点がすべて同一点という完全な退化。回転は定義できないが、
        /// 値は出しつつ well-defined フラグを false にする。
        /// </summary>
        [Test]
        public void FullyDegenerateTriangle_FlagsRotationAsNotWellDefined()
        {
            var a = Pose(Neck);

            var b = a;
            b.v0 = b.v1 = b.v2 = Neck;

            var metrics = FrameMetrics.Compute(a, b, normalizationLength: 0.5f);

            Assert.That(metrics.procrustesRotationWellDefined, Is.False);
            Assert.That(float.IsNaN(metrics.procrustesRotationDegrees), Is.False);
            Assert.That(metrics.triangleMinAngleB, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(metrics.triangleAreaB, Is.EqualTo(0f).Within(1e-6f));
        }
    }
}
