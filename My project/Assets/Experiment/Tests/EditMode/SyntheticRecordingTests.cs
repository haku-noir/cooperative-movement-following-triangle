using System.Linq;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 合成録画の生成（仕様書 §8.2）。
    ///
    /// 合成録画が仕様書 §5.4 の区間構成を実際に再現していなければ、
    /// それを使った検証は「何を検証したのか分からない」ものになる。
    /// 区間ごとの運動の性質そのものをテストで固定する。
    /// </summary>
    public class SyntheticRecordingTests
    {
        private ExperimentSettings settings;
        private RecordingSchedule schedule;
        private SyntheticMotion motion;

        private const float SampleRateHz = 90f;

        [SetUp]
        public void SetUp()
        {
            settings = SettingsEditing.CreateDefault();
            schedule = RecordingSchedule.FromSettings(settings);
            motion = new SyntheticMotion(SyntheticMotionParameters.Default, schedule);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private double TimeInside(string phaseId, float fraction)
        {
            var phase = schedule.Phases.First(p => p.Id == phaseId);
            return phase.StartSeconds + phase.DurationSeconds * fraction;
        }

        // ------------------------------------------------------------------
        // 区間ごとの運動（§5.4）
        // ------------------------------------------------------------------

        /// <summary>
        /// 基準姿勢は完全に静止。Registration の基準になるので動いてはいけない（§5.3）。
        /// </summary>
        [Test]
        public void BaselinePhase_IsPerfectlyStatic()
        {
            var first = motion.Sample(0.0, 0.0);

            for (double t = 0.0; t < settings.BaselineHoldSeconds; t += 0.05)
            {
                var sample = motion.Sample(t, t);

                Assert.That(Vector3.Distance(sample.v0, first.v0), Is.LessThan(1e-6f), $"t={t}");
                Assert.That(Vector3.Distance(sample.v1, first.v1), Is.LessThan(1e-6f), $"t={t}");
                Assert.That(Vector3.Distance(sample.v2, first.v2), Is.LessThan(1e-6f), $"t={t}");
                Assert.That(Quaternion.Angle(sample.headRotation, first.headRotation),
                    Is.LessThan(1e-3f), $"t={t}");
            }
        }

        /// <summary>
        /// 区間2 は「手を静止させ身体だけ並進させる」。
        /// 手は首頂点に対して固定されたまま、身体と一緒に並進する。
        /// </summary>
        [Test]
        public void Segment2_TranslatesBodyWithHandsFixedRelativeToNeck()
        {
            var reference = motion.Sample(TimeInside("segment_2", 0.1f), 0.0);
            Vector3 referenceLeft = reference.v1 - reference.v0;
            Vector3 referenceRight = reference.v2 - reference.v0;

            bool neckMoved = false;

            for (float fraction = 0.1f; fraction <= 0.9f; fraction += 0.05f)
            {
                double t = TimeInside("segment_2", fraction);
                var sample = motion.Sample(t, t);

                // 手の相対位置は変わらない。
                Assert.That(Vector3.Distance(sample.v1 - sample.v0, referenceLeft),
                    Is.LessThan(1e-5f), $"左手の相対位置 t={t}");
                Assert.That(Vector3.Distance(sample.v2 - sample.v0, referenceRight),
                    Is.LessThan(1e-5f), $"右手の相対位置 t={t}");

                if (Vector3.Distance(sample.v0, reference.v0) > 0.01f) neckMoved = true;
            }

            Assert.That(neckMoved, Is.True, "身体は並進しているはず");
        }

        /// <summary>
        /// 区間3 は「頭部を静止させ手だけ動かす」。
        /// </summary>
        [Test]
        public void Segment3_MovesHandsWithStaticHead()
        {
            var reference = motion.Sample(TimeInside("segment_3", 0.1f), 0.0);
            bool handsMoved = false;

            for (float fraction = 0.1f; fraction <= 0.9f; fraction += 0.05f)
            {
                double t = TimeInside("segment_3", fraction);
                var sample = motion.Sample(t, t);

                Assert.That(Vector3.Distance(sample.headPosition, reference.headPosition),
                    Is.LessThan(1e-5f), $"頭部が動いています t={t}");
                Assert.That(Vector3.Distance(sample.v0, reference.v0),
                    Is.LessThan(1e-5f), $"首頂点が動いています t={t}");

                if (Vector3.Distance(sample.v1, reference.v1) > 0.01f) handsMoved = true;
            }

            Assert.That(handsMoved, Is.True, "手は動いているはず");
        }

        [Test]
        public void Segment1AndSegment4_MoveBothHeadAndHands()
        {
            foreach (string phaseId in new[] { "segment_1", "segment_4" })
            {
                var reference = motion.Sample(TimeInside(phaseId, 0.05f), 0.0);
                bool neckMoved = false;
                bool handsMovedRelative = false;

                for (float fraction = 0.05f; fraction <= 0.95f; fraction += 0.05f)
                {
                    double t = TimeInside(phaseId, fraction);
                    var sample = motion.Sample(t, t);

                    if (Vector3.Distance(sample.v0, reference.v0) > 0.01f) neckMoved = true;

                    Vector3 relative = sample.v1 - sample.v0;
                    if (Vector3.Distance(relative, reference.v1 - reference.v0) > 0.01f)
                    {
                        handsMovedRelative = true;
                    }
                }

                Assert.That(neckMoved, Is.True, $"{phaseId}: 頭部並進");
                Assert.That(handsMovedRelative, Is.True, $"{phaseId}: 手の運動");
            }
        }

        /// <summary>
        /// 区間の切れ目で位置が飛ばないこと。飛ぶと刺激に不自然な段差が入り、
        /// そこでの追従誤差が運動の性質ではなく生成の都合で跳ね上がる。
        /// </summary>
        [Test]
        public void PhaseBoundaries_AreContinuous()
        {
            const double epsilon = 1.0 / SampleRateHz;

            foreach (var phase in schedule.Phases.Skip(1))
            {
                double before = phase.StartSeconds - epsilon;
                double after = phase.StartSeconds;

                var a = motion.Sample(before, before);
                var b = motion.Sample(after, after);

                // 1 フレーム分の移動量として妥当な範囲に収まること。
                Assert.That(Vector3.Distance(a.v0, b.v0), Is.LessThan(0.005f),
                    $"{phase.Id} の境界で首頂点が飛んでいます");
                Assert.That(Vector3.Distance(a.v1, b.v1), Is.LessThan(0.005f),
                    $"{phase.Id} の境界で左手が飛んでいます");
            }
        }

        /// <summary>
        /// 仕様書 §2.2：頭部の回転は V0 に影響しない。
        /// 合成運動はヨーとピッチを大きく振っているので、
        /// 生成された録画でもこの性質が保たれていることを確認できる。
        /// </summary>
        [Test]
        public void HeadRotation_DoesNotAffectNeckVertex()
        {
            bool sawLargeRotation = false;

            for (double t = 0.0; t < schedule.TotalSeconds; t += 0.25)
            {
                var sample = motion.Sample(t, t);

                Vector3 expected = VertexMath.NeckVertex(
                    sample.headPosition, SyntheticMotionParameters.Default.neckOffsetD);

                Assert.That(Vector3.Distance(sample.v0, expected), Is.LessThan(1e-6f), $"t={t}");

                if (Quaternion.Angle(sample.headRotation, Quaternion.identity) > 10f)
                {
                    sawLargeRotation = true;
                }
            }

            Assert.That(sawLargeRotation, Is.True, "頭部を十分に回していないとテストの意味がない");
        }

        // ------------------------------------------------------------------
        // 録画ファイルとしての妥当性
        // ------------------------------------------------------------------

        [Test]
        public void GeneratedRecording_HasExpectedStructure()
        {
            var file = SyntheticRecordingBuilder.Build(motion, SampleRateHz, "take1");

            Assert.That(file.metadata.valid, Is.True);
            Assert.That(file.metadata.recordingId, Is.EqualTo("take1"));
            Assert.That(file.segmentMarkers.Count, Is.EqualTo(schedule.Phases.Count));
            Assert.That(file.metadata.recordedDurationSeconds,
                Is.EqualTo(schedule.TotalSeconds).Within(1e-6));

            // 93 s x 90 Hz + 1
            Assert.That(file.frames.Count, Is.EqualTo(93 * 90 + 1));
        }

        /// <summary>
        /// キャリブレーションが静止姿勢から解析的に決まること。
        /// 実機の録画と同じ形式でメタデータが埋まっていないと、
        /// Registration も体格正規化も動かない。
        /// </summary>
        [Test]
        public void GeneratedRecording_CarriesAnalyticCalibration()
        {
            var file = SyntheticRecordingBuilder.Build(motion, SampleRateHz, "take1");
            var calibration = file.metadata.calibration;

            var p = SyntheticMotionParameters.Default;
            float expectedLength = Mathf.Sqrt(p.armSpan * p.armSpan + p.armForward * p.armForward);

            Assert.That(calibration.IsValid, Is.True);
            Assert.That(calibration.eyeHeightMeters, Is.EqualTo(p.eyeHeight).Within(1e-6f));
            Assert.That(calibration.characteristicLength, Is.EqualTo(expectedLength).Within(1e-6f));
            Assert.That(calibration.characteristicLengthStdDev, Is.EqualTo(0f).Within(1e-6f),
                "静止姿勢なのでばらつきは 0");
        }

        /// <summary>
        /// 合成録画が実機の録画とまったく同じ経路で扱えること。
        /// 特別扱いが必要なら、それを使った検証は本番の経路を通らなくなる。
        /// </summary>
        [Test]
        public void GeneratedRecording_SurvivesJsonRoundTrip()
        {
            var file = SyntheticRecordingBuilder.Build(motion, SampleRateHz, "take1");
            string json = RecordingSerializer.ToJson(file, prettyPrint: false);

            Assert.That(RecordingSerializer.TryFromJson(json, out var restored, out string error),
                Is.True, error);
            Assert.That(restored.frames.Count, Is.EqualTo(file.frames.Count));
            Assert.That(restored.segmentMarkers.Count, Is.EqualTo(file.segmentMarkers.Count));
        }

        /// <summary>
        /// 絶対時刻の原点が 0 でなくても扱えること。
        /// 実機の OVRPlugin.GetTimeInSeconds() は起動からの経過なので 0 にならない。
        /// </summary>
        [Test]
        public void GeneratedRecording_WorksWithNonZeroStartTimestamp()
        {
            var file = SyntheticRecordingBuilder.Build(
                motion, SampleRateHz, "take1", startTimestamp: 987654.0);

            var playback = new RecordingPlayback(file, recomputeNeckVertex: true, neckOffsetD: 0.15f);

            Assert.That(playback.DurationSeconds,
                Is.EqualTo(schedule.TotalSeconds).Within(1e-6));

            var atFive = playback.SampleAt(5.0);
            var direct = motion.Sample(5.0, 0.0);

            Assert.That(Vector3.Distance(atFive.v0, direct.v0), Is.LessThan(1e-4f));
        }

        [Test]
        public void DistinctPhaseOffsets_ProduceDistinctStimuli()
        {
            var second = SyntheticMotionParameters.Default;
            second.phaseOffsetRadians = 2f * Mathf.PI / 3f;

            var other = new SyntheticMotion(second, schedule);

            double t = TimeInside("segment_1", 0.5f);
            var a = motion.Sample(t, t);
            var b = other.Sample(t, t);

            Assert.That(Vector3.Distance(a.v0, b.v0), Is.GreaterThan(0.01f),
                "位相をずらした刺激は別の運動になるべき");
        }
    }
}
