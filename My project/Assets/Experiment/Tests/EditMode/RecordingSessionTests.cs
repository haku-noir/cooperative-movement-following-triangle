using System.Linq;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 記録モードの進行と保存形式（仕様書 §3.1, §5.1, §5.4）。
    /// HMD を使わずに録画 1 本分を通しで走らせ、区間マーカーと JSON 往復まで確認する。
    /// </summary>
    public class RecordingSessionTests
    {
        private const double SampleIntervalSeconds = 1.0 / 90.0;

        private ExperimentSettings settings;

        [SetUp]
        public void SetUp() => settings = SettingsEditing.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        // ------------------------------------------------------------------
        // キャリブレーション
        // ------------------------------------------------------------------

        /// <summary>
        /// 仕様書 §7.1：Quest は視野外で手の姿勢を外挿する。
        /// 実験全体の基準になるキャリブレーションに外挿値を混ぜてはならない。
        /// </summary>
        [Test]
        public void Calibration_RejectsLowConfidenceSamples()
        {
            var session = new RecordingSession(settings);
            session.BeginCalibration();

            bool accepted = session.AddCalibrationSample(
                SyntheticSamples.StaticPose(0.0, confidence: HandConfidence.Low));

            Assert.That(accepted, Is.False);
            Assert.That(session.CalibrationSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void Calibration_FailsWithoutSamples()
        {
            var session = new RecordingSession(settings);
            session.BeginCalibration();

            Assert.That(session.CompleteCalibration(out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(session.State, Is.EqualTo(RecorderState.Calibrating));
        }

        [Test]
        public void Recording_CannotStartBeforeCalibration()
        {
            var session = new RecordingSession(settings);

            Assert.That(session.BeginRecording(out string error), Is.False);
            Assert.That(error, Does.Contain("キャリブレーション"));
        }

        // ------------------------------------------------------------------
        // 区間マーカー（§5.4）
        // ------------------------------------------------------------------

        [Test]
        public void TimeBasedMarkers_AreEmittedAtEveryPhaseBoundary()
        {
            var session = RunFullRecording(out _);

            var markers = session.Markers;
            var schedule = session.Schedule;

            Assert.That(markers.Count, Is.EqualTo(schedule.Phases.Count),
                "フェーズ数ぶんのマーカーが必要");

            for (int i = 0; i < markers.Count; i++)
            {
                Assert.That(markers[i].phaseId, Is.EqualTo(schedule.Phases[i].Id));
                Assert.That(markers[i].plannedStartSeconds,
                    Is.EqualTo(schedule.Phases[i].StartSeconds).Within(1e-4f));

                // 1 フレーム（1/90 s）以内に打たれていること。
                Assert.That(markers[i].elapsedSeconds,
                    Is.EqualTo(schedule.Phases[i].StartSeconds).Within(SampleIntervalSeconds * 1.5),
                    $"marker {markers[i].phaseId}");
            }
        }

        /// <summary>
        /// フレーム落ちで複数の境界を一度にまたいでも、マーカーを打ち漏らさないこと。
        /// 1 つ落ちると解析側の区間数が合わなくなり、その録画を使った試行がすべて無駄になる。
        /// </summary>
        [Test]
        public void TimeBasedMarkers_SurviveDroppedFrames()
        {
            var session = new RecordingSession(settings);
            Calibrate(session);
            Assert.That(session.BeginRecording(out _), Is.True);

            // 基準姿勢 3 s と導入 10 s の境界をまとめてまたぐ 1 フレーム。
            session.AddFrame(SyntheticSamples.Moving(0.0));
            session.AddFrame(SyntheticSamples.Moving(20.0));

            var ids = session.Markers.Select(m => m.phaseId).ToArray();

            Assert.That(ids, Is.EqualTo(new[] { "baseline", "lead_in", "segment_1" }));
        }

        [Test]
        public void ManualMarkers_AreIgnoredInTimeBasedMode()
        {
            var session = new RecordingSession(settings);
            Calibrate(session);
            session.BeginRecording(out _);
            session.AddFrame(SyntheticSamples.Moving(0.0));

            Assert.That(session.AddManualMarker(0.5, out string error), Is.False);
            Assert.That(error, Does.Contain("時間ベース"));
        }

        [Test]
        public void ManualMarkers_AssignPhasesInOrder()
        {
            SettingsEditing.SetEnum(settings, "segmentMarkerMode", (int)SegmentMarkerMode.Controller);

            var session = new RecordingSession(settings);
            Calibrate(session);
            session.BeginRecording(out _);
            session.AddFrame(SyntheticSamples.Moving(0.0));

            Assert.That(session.AddManualMarker(0.0, out _), Is.True);
            session.AddFrame(SyntheticSamples.Moving(4.0));
            Assert.That(session.AddManualMarker(4.0, out _), Is.True);

            var ids = session.Markers.Select(m => m.phaseId).ToArray();
            Assert.That(ids, Is.EqualTo(new[] { "baseline", "lead_in" }));

            // 手動マーカーは予定時刻とずれてよい。ずれ自体が後処理で見えることが大事。
            Assert.That(session.Markers[1].elapsedSeconds, Is.EqualTo(4.0).Within(1e-6));
            Assert.That(session.Markers[1].plannedStartSeconds, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void ControllerMode_MarksRecordingInvalidWhenMarkersAreMissing()
        {
            SettingsEditing.SetEnum(settings, "segmentMarkerMode", (int)SegmentMarkerMode.Controller);

            var session = RunFullRecording(out var file);

            Assert.That(session.Markers.Count, Is.EqualTo(0));
            Assert.That(file.metadata.valid, Is.False);
            Assert.That(file.metadata.invalidReason, Does.Contain("区間マーカー"));
        }

        // ------------------------------------------------------------------
        // 記録の完了（§3.1, §1）
        // ------------------------------------------------------------------

        [Test]
        public void Recording_StopsAtScheduledDuration()
        {
            var session = RunFullRecording(out var file);

            Assert.That(session.State, Is.EqualTo(RecorderState.Completed));
            Assert.That(file.metadata.recordedDurationSeconds,
                Is.EqualTo(session.Schedule.TotalSeconds).Within(SampleIntervalSeconds * 2));
            Assert.That(file.metadata.valid, Is.True, file.metadata.invalidReason);
        }

        [Test]
        public void Metadata_RecordsFrameRateStatistics()
        {
            var session = RunFullRecording(out var file);

            Assert.That(file.metadata.meanFrameRateHz, Is.EqualTo(90.0).Within(0.5));
            Assert.That(file.metadata.maxFrameIntervalSeconds,
                Is.EqualTo(SampleIntervalSeconds).Within(1e-6));
            Assert.That(file.frames.Count, Is.EqualTo(session.Frames.Count));
        }

        /// <summary>
        /// 仕様書 §1：試行中の再センタリングは禁止。記録中に起きた録画は刺激に使えない。
        /// ワールド原点が動いた時点で、前半と後半のフレームが同じ座標系に乗っていない。
        /// </summary>
        [Test]
        public void Recenter_MarksRecordingInvalid()
        {
            var session = new RecordingSession(settings);
            Calibrate(session);
            session.BeginRecording(out _);
            session.AddFrame(SyntheticSamples.Moving(0.0));
            session.RecenterCount = 1;
            session.AddFrame(SyntheticSamples.Moving(SampleIntervalSeconds));

            var file = session.Complete(default);

            Assert.That(file.metadata.valid, Is.False);
            Assert.That(file.metadata.invalidReason, Does.Contain("再センタリング"));
        }

        [Test]
        public void Metadata_CarriesCalibrationAndNormalizedOffset()
        {
            RunFullRecording(out var file);

            var c = file.metadata.calibration;
            Assert.That(c.IsValid, Is.True);
            Assert.That(c.characteristicLength, Is.EqualTo(0.40f).Within(1e-3f));

            // d / h（仕様書 §2.2）。d = 0.15、h = 1.60。
            Assert.That(file.metadata.normalizedNeckOffset,
                Is.EqualTo(0.15f / 1.60f).Within(1e-4f));
            Assert.That(file.metadata.neckOffsetD, Is.EqualTo(0.15f).Within(1e-6f));
        }

        // ------------------------------------------------------------------
        // JSON 往復（§3.1）
        // ------------------------------------------------------------------

        [Test]
        public void JsonRoundTrip_PreservesFramesMarkersAndMetadata()
        {
            RunFullRecording(out var original);

            string json = RecordingSerializer.ToJson(original, prettyPrint: false);

            Assert.That(RecordingSerializer.TryFromJson(json, out var restored, out string error),
                Is.True, error);

            Assert.That(restored.frames.Count, Is.EqualTo(original.frames.Count));
            Assert.That(restored.segmentMarkers.Count, Is.EqualTo(original.segmentMarkers.Count));
            Assert.That(restored.metadata.recordingId, Is.EqualTo(original.metadata.recordingId));
            Assert.That(restored.metadata.calibration.characteristicLength,
                Is.EqualTo(original.metadata.calibration.characteristicLength).Within(1e-6f));

            var a = original.frames[100];
            var b = restored.frames[100];
            Assert.That(b.t, Is.EqualTo(a.t).Within(1e-9), "タイムスタンプは double で往復すること");
            Assert.That(Vector3.Distance(a.v1, b.v1), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(a.headRotation, b.headRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Deserialize_RejectsUnknownFormatVersion()
        {
            RunFullRecording(out var file);
            file.metadata.formatVersion = 999;

            string json = RecordingSerializer.ToJson(file, prettyPrint: false);

            Assert.That(RecordingSerializer.TryFromJson(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("形式バージョン"));
        }

        [Test]
        public void Deserialize_RejectsNonMonotonicTimestamps()
        {
            RunFullRecording(out var file);
            var broken = file.frames[10];
            broken.t = file.frames[9].t - 1.0;
            file.frames[10] = broken;

            string json = RecordingSerializer.ToJson(file, prettyPrint: false);

            Assert.That(RecordingSerializer.TryFromJson(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("単調増加"));
        }

        // ------------------------------------------------------------------
        // V0 の二重保存（記録時の値と head pose の両方）
        // ------------------------------------------------------------------

        /// <summary>
        /// 録画には head pose と V0 の両方を保存する。再生時に d を変えたい場合、
        /// head position から V0 を引き直せることを確認する。
        /// d はパイロットで確定する値（§9）なので、録画を撮り直さずに変更できる必要がある。
        /// </summary>
        [Test]
        public void RecordedFrame_CanRecomputeNeckVertexWithDifferentOffset()
        {
            var sample = SyntheticSamples.StaticPose(0.0, neckOffsetD: 0.15f);
            var frame = RecordedFrame.From(sample);

            var asRecorded = frame.ToSample(recomputeNeckVertex: false, neckOffsetD: 0.30f);
            var recomputed = frame.ToSample(recomputeNeckVertex: true, neckOffsetD: 0.30f);

            Assert.That(Vector3.Distance(asRecorded.v0, sample.v0), Is.LessThan(1e-6f),
                "保存値をそのまま使う場合、d を変えても V0 は変わらない");

            Vector3 expected = SyntheticSamples.HeadRest + new Vector3(0f, -0.30f, 0f);
            Assert.That(Vector3.Distance(recomputed.v0, expected), Is.LessThan(1e-6f),
                "再計算する場合、新しい d が反映される");

            // 再計算しても水平方向は動かない。重力方向固定の性質は再生側でも保たれる（§2.2）。
            Assert.That(recomputed.v0.x, Is.EqualTo(sample.headPosition.x).Within(1e-6f));
            Assert.That(recomputed.v0.z, Is.EqualTo(sample.headPosition.z).Within(1e-6f));
        }

        [Test]
        public void RecordedFrame_PreservesHandTrackingState()
        {
            var sample = SyntheticSamples.StaticPose(0.0, confidence: HandConfidence.Low);
            var restored = RecordedFrame.From(sample).ToSample(false, 0.15f);

            Assert.That(restored.leftHand.confidence, Is.EqualTo(HandConfidence.Low));
            Assert.That(restored.rightHand.confidence, Is.EqualTo(HandConfidence.Low));
            Assert.That(restored.leftHand.isTracked, Is.True);
        }

        // ------------------------------------------------------------------

        private void Calibrate(RecordingSession session)
        {
            session.BeginCalibration();
            for (int i = 0; i < 90; i++)
            {
                session.AddCalibrationSample(
                    SyntheticSamples.StaticPose(i * SampleIntervalSeconds, armLength: 0.40f));
            }

            Assert.That(session.CompleteCalibration(out string error), Is.True, error);
        }

        private RecordingSession RunFullRecording(out RecordingFile file)
        {
            var session = new RecordingSession(settings);
            Calibrate(session);
            Assert.That(session.BeginRecording(out string error), Is.True, error);

            // 録画は基準姿勢の直後から始まる。時刻はキャリブレーションの続きにする。
            double t = 1.0;
            while (!session.AddFrame(SyntheticSamples.Moving(t)))
            {
                t += SampleIntervalSeconds;
            }

            file = session.Complete(new RecordingEnvironment
            {
                recordingId = "take1",
                performerId = "A01",
                createdAtIso8601 = "2026-09-15T00:00:00+09:00",
                vertexSourceKind = "SyntheticSamples",
                sdkVersion = "test",
                unityVersion = Application.unityVersion,
                requestedDisplayFrequencyHz = 90f,
                effectiveDisplayFrequencyHz = 90f,
                displayFrequencyApplied = true,
                trackingOriginType = "FloorLevel",
                trackingOriginIsFloorLevel = true,
            });

            return session;
        }
    }
}
