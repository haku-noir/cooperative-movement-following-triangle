using System.Collections.Generic;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §7.3 の再生。タイムスタンプ基準であること、補間が正しいこと。
    ///
    /// フレーム番号で送ると、録画時と再生時のフレームレート差がそのまま
    /// 刺激の時間軸の伸縮になる。それは「追従の遅れ」として誤差に化けるが、
    /// 後処理から区別できない。
    /// </summary>
    public class RecordingPlaybackTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>x 方向に一定速度で進む頭部を持つ録画。補間の正解が解析的に分かる。</summary>
        private static RecordingFile LinearRecording(
            double startTimestamp, double intervalSeconds, int frameCount, float speedPerSecond)
        {
            var file = new RecordingFile();
            file.metadata.formatVersion = RecordingMetadata.CurrentFormatVersion;
            file.metadata.calibration = new PerformerCalibration
            {
                eyeHeightMeters = 1.6f,
                characteristicLength = 0.4f,
                neckToLeftHandMeters = 0.4f,
                neckToRightHandMeters = 0.4f,
                sampleCount = 100,
            };

            for (int i = 0; i < frameCount; i++)
            {
                double t = startTimestamp + i * intervalSeconds;
                float x = (float)(speedPerSecond * i * intervalSeconds);
                var head = new Vector3(x, 1.6f, 0f);
                var neck = VertexMath.NeckVertex(head, 0.15f);

                file.frames.Add(new RecordedFrame
                {
                    t = t,
                    headPosition = head,
                    headRotation = Quaternion.Euler(0f, 90f * (float)(i * intervalSeconds), 0f),
                    v0 = neck,
                    v1 = neck + new Vector3(-0.4f, 0f, 0f),
                    v2 = neck + new Vector3(0.4f, 0f, 0f),
                    leftTracked = true,
                    leftConfidence = (int)HandConfidence.High,
                    rightTracked = true,
                    rightConfidence = (int)HandConfidence.High,
                });
            }

            return file;
        }

        [Test]
        public void Duration_IsSpanOfTimestamps()
        {
            var playback = new RecordingPlayback(
                LinearRecording(1000.0, 0.1, 11, 1f), recomputeNeckVertex: true, neckOffsetD: 0.15f);

            Assert.That(playback.DurationSeconds, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(playback.FrameCount, Is.EqualTo(11));
        }

        /// <summary>
        /// 録画の絶対タイムスタンプが何であっても、経過時刻で引ける。
        /// OVRPlugin.GetTimeInSeconds() は起動からの経過なので、録画ごとに原点が違う。
        /// </summary>
        [Test]
        public void SampleAt_IsRelativeToFirstFrameRegardlessOfAbsoluteTimestamp()
        {
            var early = new RecordingPlayback(
                LinearRecording(10.0, 0.1, 11, 1f), true, 0.15f);
            var late = new RecordingPlayback(
                LinearRecording(987654.0, 0.1, 11, 1f), true, 0.15f);

            var a = early.SampleAt(0.5);
            var b = late.SampleAt(0.5);

            Assert.That(Vector3.Distance(a.headPosition, b.headPosition), Is.LessThan(Tolerance));
        }

        /// <summary>フレームの真ん中を引くと、位置はちょうど中点になる。</summary>
        [Test]
        public void SampleAt_InterpolatesPositionLinearly()
        {
            var playback = new RecordingPlayback(
                LinearRecording(0.0, 0.1, 11, 1f), true, 0.15f);

            // 速度 1 m/s なので、経過 t 秒で x = t。
            for (double t = 0.0; t <= 1.0; t += 0.05)
            {
                var sample = playback.SampleAt(t);
                Assert.That(sample.headPosition.x, Is.EqualTo((float)t).Within(Tolerance), $"t={t}");
            }
        }

        [Test]
        public void SampleAt_InterpolatesRotationBySlerp()
        {
            var playback = new RecordingPlayback(
                LinearRecording(0.0, 0.1, 11, 1f), true, 0.15f);

            // 90 deg/s。経過 0.25 s なら 22.5 度。
            var sample = playback.SampleAt(0.25);
            float yaw = sample.headRotation.eulerAngles.y;

            Assert.That(Mathf.DeltaAngle(yaw, 22.5f), Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void SampleAt_ClampsOutsideRange()
        {
            var playback = new RecordingPlayback(
                LinearRecording(0.0, 0.1, 11, 1f), true, 0.15f);

            var before = playback.SampleAt(-5.0, out bool clampedBefore);
            var after = playback.SampleAt(99.0, out bool clampedAfter);

            Assert.That(clampedBefore, Is.True);
            Assert.That(clampedAfter, Is.True);
            Assert.That(before.headPosition.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(after.headPosition.x, Is.EqualTo(1f).Within(Tolerance));
        }

        /// <summary>
        /// 時間を飛ばしても巻き戻しても正しく引ける。
        /// 内部カーソルの最適化が結果を変えないことの確認。
        /// </summary>
        [Test]
        public void SampleAt_IsOrderIndependent()
        {
            var playback = new RecordingPlayback(
                LinearRecording(0.0, 0.01, 101, 1f), true, 0.15f);

            var queries = new List<double> { 0.9, 0.1, 0.5, 0.95, 0.02, 0.75, 0.3 };

            foreach (double t in queries)
            {
                var sample = playback.SampleAt(t);
                Assert.That(sample.headPosition.x, Is.EqualTo((float)t).Within(Tolerance), $"t={t}");
            }
        }

        /// <summary>
        /// 録画時と異なるフレームレートで引いても、時刻どおりの位置が返る。
        /// これがフレーム番号基準との決定的な違い。
        /// </summary>
        [Test]
        public void PlaybackAtDifferentFrameRate_PreservesTimeline()
        {
            // 録画は 30 Hz、再生は 90 Hz を想定して 1/90 s 刻みで引く。
            var playback = new RecordingPlayback(
                LinearRecording(0.0, 1.0 / 30.0, 31, 1f), true, 0.15f);

            for (int i = 0; i <= 90; i++)
            {
                double t = i / 90.0;
                if (t > playback.DurationSeconds) break;

                var sample = playback.SampleAt(t);
                Assert.That(sample.headPosition.x, Is.EqualTo((float)t).Within(Tolerance), $"t={t}");
            }
        }

        [Test]
        public void HandTrackingState_IsNotInterpolated()
        {
            var file = LinearRecording(0.0, 0.1, 3, 1f);
            var lowFrame = file.frames[1];
            lowFrame.leftConfidence = (int)HandConfidence.Low;
            lowFrame.leftTracked = false;
            file.frames[1] = lowFrame;

            var playback = new RecordingPlayback(file, true, 0.15f);

            // フレーム 0 寄り（u < 0.5）なら High、フレーム 1 寄りなら Low。
            // 中間的な値が作られないことが重要。後処理での区間除外が曖昧になるため。
            Assert.That(playback.SampleAt(0.02).leftHand.confidence, Is.EqualTo(HandConfidence.High));
            Assert.That(playback.SampleAt(0.08).leftHand.confidence, Is.EqualTo(HandConfidence.Low));
            Assert.That(playback.SampleAt(0.08).leftHand.isTracked, Is.False);
        }

        // ------------------------------------------------------------------
        // V0 の扱い（録画には head pose と V0 の両方が入っている）
        // ------------------------------------------------------------------

        [Test]
        public void RecomputedNeckVertex_FollowsCurrentOffset()
        {
            var file = LinearRecording(0.0, 0.1, 11, 1f);

            var asRecorded = new RecordingPlayback(file, recomputeNeckVertex: false, neckOffsetD: 0.30f);
            var recomputed = new RecordingPlayback(file, recomputeNeckVertex: true, neckOffsetD: 0.30f);

            var a = asRecorded.SampleAt(0.35);
            var b = recomputed.SampleAt(0.35);

            // 録画時は d = 0.15。保存値を使えばそのまま。
            Assert.That(a.v0.y, Is.EqualTo(1.6f - 0.15f).Within(Tolerance));

            // 再計算すれば現在の d = 0.30 が効く。
            Assert.That(b.v0.y, Is.EqualTo(1.6f - 0.30f).Within(Tolerance));

            // どちらでも水平位置は頭部と同じ。重力方向固定は再生側でも保たれる（§2.2）。
            Assert.That(a.v0.x, Is.EqualTo(a.headPosition.x).Within(Tolerance));
            Assert.That(b.v0.x, Is.EqualTo(b.headPosition.x).Within(Tolerance));
        }

        // ------------------------------------------------------------------
        // 基準姿勢の平均（§5.3 の A 側基準）
        // ------------------------------------------------------------------

        /// <summary>
        /// A 側の基準は録画の基準姿勢区間の平均。単一フレームのジッタが
        /// 全試行に効く定数バイアスとして固定されるのを避ける。
        /// </summary>
        [Test]
        public void BaselineAverage_AveragesOverBaselineWindowOnly()
        {
            // 0-1 s は静止、その後 x が伸びていく録画を作る。
            var file = new RecordingFile();
            file.metadata.formatVersion = RecordingMetadata.CurrentFormatVersion;

            for (int i = 0; i < 100; i++)
            {
                double t = i * 0.05;
                float x = t < 1.0 ? 0f : (float)(t - 1.0);
                var head = new Vector3(x, 1.6f, 0f);
                var neck = VertexMath.NeckVertex(head, 0.15f);

                file.frames.Add(new RecordedFrame
                {
                    t = t,
                    headPosition = head,
                    headRotation = Quaternion.identity,
                    v0 = neck,
                    v1 = neck + new Vector3(-0.4f, 0f, 0f),
                    v2 = neck + new Vector3(0.4f, 0f, 0f),
                    leftTracked = true,
                    leftConfidence = (int)HandConfidence.High,
                    rightTracked = true,
                    rightConfidence = (int)HandConfidence.High,
                });
            }

            var playback = new RecordingPlayback(file, true, 0.15f);
            var baseline = playback.BaselineAverage(1.0f, out int sampleCount);

            Assert.That(sampleCount, Is.EqualTo(20), "0-1 s の 20 フレームだけ使うこと");
            Assert.That(baseline.V0.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(baseline.V0.y, Is.EqualTo(1.6f - 0.15f).Within(Tolerance));
        }

        /// <summary>
        /// ジッタのある基準姿勢では、平均が単一フレームより真値に近い。
        /// これが単一フレームを使わない理由。
        /// </summary>
        [Test]
        public void BaselineAverage_SuppressesJitterBetterThanSingleFrame()
        {
            var file = new RecordingFile();
            file.metadata.formatVersion = RecordingMetadata.CurrentFormatVersion;

            const float trueX = 0f;
            var random = new System.Random(12345);

            for (int i = 0; i < 270; i++)
            {
                double t = i / 90.0;
                float jitter = (float)(random.NextDouble() - 0.5) * 0.006f; // +-3 mm
                var head = new Vector3(trueX + jitter, 1.6f, 0f);
                var neck = VertexMath.NeckVertex(head, 0.15f);

                file.frames.Add(new RecordedFrame
                {
                    t = t,
                    headPosition = head,
                    headRotation = Quaternion.identity,
                    v0 = neck,
                    v1 = neck + new Vector3(-0.4f, 0f, 0f),
                    v2 = neck + new Vector3(0.4f, 0f, 0f),
                    leftTracked = true,
                    leftConfidence = (int)HandConfidence.High,
                    rightTracked = true,
                    rightConfidence = (int)HandConfidence.High,
                });
            }

            var playback = new RecordingPlayback(file, true, 0.15f);
            var averaged = playback.BaselineAverage(3f, out _);

            float averageError = Mathf.Abs(averaged.V0.x - trueX);
            float singleFrameError = Mathf.Abs(file.frames[0].v0.x - trueX);

            Assert.That(averageError, Is.LessThan(singleFrameError),
                $"平均 {averageError * 1000f:F3} mm < 単一フレーム {singleFrameError * 1000f:F3} mm");
            Assert.That(averageError, Is.LessThan(0.0005f), "平均誤差は 0.5 mm 未満に収まるはず");
        }
    }
}
