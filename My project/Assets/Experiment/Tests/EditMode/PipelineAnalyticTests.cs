using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §8.2 の目的そのもの：
    /// 「既知の正弦波運動から録画ファイルを生成する機能。
    ///   これにより、誤差算出が正しいかを解析的に検証できる」
    ///
    /// 個々の関数の単体テストは通っていても、それらを繋いだ経路が正しいとは限らない。
    /// ここでは合成録画 → 再生 → 体格正規化 → Registration → 誤差算出 という
    /// 本番と同じ順序で通し、結果が閉形式の期待値と一致することを確認する。
    ///
    /// MonoBehaviour を経由しないのは、シーンや Time.time に依存させないため。
    /// 経由する部分（ExperimentDriver）は配線しかしていない。
    /// </summary>
    public class PipelineAnalyticTests
    {
        private const float SampleRateHz = 90f;
        private const float Tolerance = 1e-4f;

        private ExperimentSettings settings;
        private RecordingSchedule schedule;
        private SyntheticMotion motion;
        private RecordingFile recording;

        [SetUp]
        public void SetUp()
        {
            settings = SettingsEditing.CreateDefault();
            schedule = RecordingSchedule.FromSettings(settings);
            motion = new SyntheticMotion(SyntheticMotionParameters.Default, schedule);
            recording = SyntheticRecordingBuilder.Build(motion, SampleRateHz, "analytic");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private RecordingPlayback CreatePlayback() => new RecordingPlayback(
            recording, settings.RecomputeNeckVertexOnPlayback, settings.NeckOffsetD);

        private ParticipantProfile ProfileMatchingPerformer() => ParticipantProfile.Create(
            "ANALYTIC-1", recording.metadata.calibration, settings.NeckOffsetD);

        /// <summary>B が A とまったく同じ運動をしている場合の変換。恒等になるはず。</summary>
        private StimulusTransform IdentityRegistration(RecordingPlayback playback)
        {
            var referenceA = playback.BaselineAverage(
                recording.metadata.baselineHoldSeconds, out _);

            return Registration.Solve(
                settings.RegistrationMethod, referenceA, referenceA,
                recording.metadata.calibration, ProfileMatchingPerformer().calibration);
        }

        // ------------------------------------------------------------------
        // 完全追従
        // ------------------------------------------------------------------

        /// <summary>
        /// 同じ体格・同じ基準姿勢なら Registration は恒等変換になる。
        /// ここが恒等にならないなら、以降の誤差はすべて位置合わせの失敗に汚染される。
        /// </summary>
        [Test]
        public void IdenticalPerformerAndParticipant_YieldIdentityTransform()
        {
            var transform = IdentityRegistration(CreatePlayback());

            Assert.That(transform.scale, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(transform.yawDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(transform.translation.magnitude, Is.LessThan(Tolerance));
        }

        /// <summary>
        /// B が A を完全に再現していれば、全区間で誤差は 0。
        /// パイプラインのどこかに定数バイアスが入っていれば、ここで露見する。
        /// </summary>
        [Test]
        public void PerfectFollowing_ProducesZeroErrorThroughoutTheTrial()
        {
            var playback = CreatePlayback();
            var transform = IdentityRegistration(playback);
            float normalization = ProfileMatchingPerformer().calibration.characteristicLength;

            for (double t = 0.0; t <= schedule.TotalSeconds; t += 0.5)
            {
                var a = transform.Apply(playback.SampleAt(t));
                var b = motion.Sample(t, t);

                var metrics = FrameMetrics.Compute(a, b, normalization);

                Assert.That(metrics.distanceSum, Is.LessThan(1e-3f), $"t={t}");
                Assert.That(metrics.procrustesResidual, Is.LessThan(1e-3f), $"t={t}");
                Assert.That(metrics.procrustesTranslation, Is.LessThan(1e-3f), $"t={t}");
            }
        }

        // ------------------------------------------------------------------
        // 既知の平行移動（§8.3 の指定を通し経路で行う）
        // ------------------------------------------------------------------

        /// <summary>
        /// 追従中の B が A から既知の量だけずれている場合、
        /// 各頂点距離はその量に一致し、Procrustes は純粋な平行移動になる。
        /// </summary>
        [TestCase(0.1f, 0f, 0f)]
        [TestCase(0f, 0.05f, 0f)]
        [TestCase(0.03f, -0.04f, 0.12f)]
        public void KnownOffsetDuringTrial_MatchesClosedForm(float dx, float dy, float dz)
        {
            var offset = new Vector3(dx, dy, dz);
            var playback = CreatePlayback();
            var transform = IdentityRegistration(playback);
            float normalization = ProfileMatchingPerformer().calibration.characteristicLength;

            // 解析対象の区間だけを見る（§5.4：基準姿勢と導入は解析から除外）。
            foreach (var phase in schedule.Phases)
            {
                if (!phase.IncludedInAnalysis) continue;

                double t = phase.StartSeconds + phase.DurationSeconds * 0.5f;

                var a = transform.Apply(playback.SampleAt(t));
                var b = a;
                b.headPosition += offset;
                b.v0 += offset;
                b.v1 += offset;
                b.v2 += offset;

                var metrics = FrameMetrics.Compute(a, b, normalization);

                Assert.That(metrics.distanceV0, Is.EqualTo(offset.magnitude).Within(Tolerance),
                    $"{phase.Id} dist_V0");
                Assert.That(metrics.distanceV1, Is.EqualTo(offset.magnitude).Within(Tolerance),
                    $"{phase.Id} dist_V1");
                Assert.That(metrics.distanceV2, Is.EqualTo(offset.magnitude).Within(Tolerance),
                    $"{phase.Id} dist_V2");
                Assert.That(metrics.distanceSum, Is.EqualTo(3f * offset.magnitude).Within(Tolerance),
                    $"{phase.Id} dist_sum");
                Assert.That(metrics.distanceSumNormalized,
                    Is.EqualTo(3f * offset.magnitude / normalization).Within(Tolerance),
                    $"{phase.Id} dist_sum_norm");

                // 剛体成分は平行移動だけ。形状は変わっていないので残差 0。
                Assert.That(metrics.procrustesTranslation,
                    Is.EqualTo(offset.magnitude).Within(Tolerance), $"{phase.Id} proc_trans");
                Assert.That(metrics.procrustesRotationDegrees, Is.EqualTo(0f).Within(0.01f),
                    $"{phase.Id} proc_rot_deg");
                Assert.That(metrics.procrustesResidual, Is.LessThan(Tolerance),
                    $"{phase.Id} proc_residual");
            }
        }

        // ------------------------------------------------------------------
        // 体格差
        // ------------------------------------------------------------------

        /// <summary>
        /// B の体格が A の 1.25 倍でも、B が正しく追従していれば誤差は 0 になる。
        /// 体格正規化（§5.2）が効いていることの通し確認。
        /// </summary>
        [Test]
        public void LargerParticipant_StillYieldsZeroErrorWhenFollowingCorrectly()
        {
            const float scale = 1.25f;

            var bodyB = SyntheticMotionParameters.Default;
            bodyB.armSpan *= scale;
            bodyB.armForward *= scale;
            bodyB.headTranslationAmplitude *= scale;
            bodyB.handAmplitude *= scale;

            var motionB = new SyntheticMotion(bodyB, schedule);

            var calibrationB = new PerformerCalibration
            {
                eyeHeightMeters = bodyB.eyeHeight,
                neckToLeftHandMeters = motionB.RestCharacteristicLength,
                neckToRightHandMeters = motionB.RestCharacteristicLength,
                characteristicLength = motionB.RestCharacteristicLength,
                sampleCount = 100,
            };
            var profileB = ParticipantProfile.Create("BIG-1", calibrationB, settings.NeckOffsetD);

            var playback = CreatePlayback();
            var referenceA = playback.BaselineAverage(recording.metadata.baselineHoldSeconds, out _);
            var baselineB = new Triangle(
                motionB.Sample(0.0, 0.0).v0, motionB.Sample(0.0, 0.0).v1, motionB.Sample(0.0, 0.0).v2);

            var transform = Registration.Solve(
                settings.RegistrationMethod, referenceA, baselineB,
                recording.metadata.calibration, calibrationB);

            Assert.That(transform.scale, Is.EqualTo(scale).Within(1e-3f),
                "体格比がそのままスケール係数になるはず");

            float normalization = profileB.calibration.characteristicLength;

            // 手の運動は首頂点まわりなのでスケールされるが、頭部並進も同じ比で
            // スケールされるため、B が「自分の体格で同じ運動をする」と誤差 0 になる。
            for (double t = 0.0; t <= schedule.TotalSeconds; t += 1.0)
            {
                var a = transform.Apply(playback.SampleAt(t));
                var b = motionB.Sample(t, t);

                var metrics = FrameMetrics.Compute(a, b, normalization);

                Assert.That(metrics.distanceSum, Is.LessThan(2e-3f), $"t={t}");
            }
        }

        // ------------------------------------------------------------------
        // 追従の遅れ
        // ------------------------------------------------------------------

        /// <summary>
        /// B が A を一定の遅れで追っている場合、誤差はその遅れの間に
        /// A が動いた距離になる。区間によって値が変わることも確認する。
        ///
        /// 区間3（手のみ）では首頂点が動かないので、遅れがあっても
        /// V0 の誤差はほぼ 0 になる。これは仕様書 §5.4 の区間設計が
        /// 誤差の内訳に現れることの確認でもある。
        /// </summary>
        [Test]
        public void ConstantLag_ProducesErrorEqualToPerformerDisplacement()
        {
            const double lag = 0.2;

            var playback = CreatePlayback();
            var transform = IdentityRegistration(playback);
            float normalization = ProfileMatchingPerformer().calibration.characteristicLength;

            double handsOnlyTime = schedule.Phases[4].StartSeconds + 10.0;  // segment_3
            double translationTime = schedule.Phases[3].StartSeconds + 10.0; // segment_2

            AssertLagMatchesDisplacement(playback, transform, normalization, handsOnlyTime, lag);
            AssertLagMatchesDisplacement(playback, transform, normalization, translationTime, lag);

            // 区間3（手のみ）は首頂点が静止しているので、遅れても V0 の誤差は出ない。
            var aHands = transform.Apply(playback.SampleAt(handsOnlyTime));
            var bHands = motion.Sample(handsOnlyTime - lag, handsOnlyTime);
            var handsMetrics = FrameMetrics.Compute(aHands, bHands, normalization);

            Assert.That(handsMetrics.distanceV0, Is.LessThan(1e-3f),
                "区間3 では首頂点が動かないので、遅れても V0 の誤差は出ない");
            Assert.That(handsMetrics.distanceV1, Is.GreaterThan(1e-3f),
                "手には遅れの誤差が出るはず");

            // 区間2（並進のみ）は手が身体に対して静止しているので、
            // 3 頂点すべてが同じだけずれる。つまり純粋な平行移動になる。
            var aTrans = transform.Apply(playback.SampleAt(translationTime));
            var bTrans = motion.Sample(translationTime - lag, translationTime);
            var transMetrics = FrameMetrics.Compute(aTrans, bTrans, normalization);

            Assert.That(transMetrics.distanceV1, Is.EqualTo(transMetrics.distanceV0).Within(1e-3f));
            Assert.That(transMetrics.distanceV2, Is.EqualTo(transMetrics.distanceV0).Within(1e-3f));
            Assert.That(transMetrics.procrustesResidual, Is.LessThan(1e-3f),
                "区間2 の遅れは純粋な平行移動として現れ、形状残差は出ない");
        }

        private void AssertLagMatchesDisplacement(
            RecordingPlayback playback, StimulusTransform transform,
            float normalization, double time, double lag)
        {
            var a = transform.Apply(playback.SampleAt(time));
            var b = motion.Sample(time - lag, time);

            var metrics = FrameMetrics.Compute(a, b, normalization);

            // 期待値は「遅れの間に A が動いた距離」。B は A の過去の姿勢そのものなので、
            // 各頂点距離は A の その区間での変位に一致する。
            var past = motion.Sample(time - lag, time);
            var now = motion.Sample(time, time);

            Assert.That(metrics.distanceV0,
                Is.EqualTo(Vector3.Distance(now.v0, past.v0)).Within(1e-3f), $"t={time} V0");
            Assert.That(metrics.distanceV1,
                Is.EqualTo(Vector3.Distance(now.v1, past.v1)).Within(1e-3f), $"t={time} V1");
        }
    }
}
