using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 既知の正弦波運動から録画ファイルを作る（仕様書 §8.2）。
    ///
    /// 実機で撮った録画と同じ形式・同じ経路で扱えることが重要。
    /// 合成録画だけ特別扱いすると、それを使った検証が本番の経路を通らなくなる。
    /// 生成物は RecordingStorage でそのまま保存でき、StimulusCatalog からも
    /// 通常の刺激として解決される。
    /// </summary>
    public static class SyntheticRecordingBuilder
    {
        /// <summary>
        /// 合成録画を組み立てる。
        /// </summary>
        /// <param name="motion">運動モデル。</param>
        /// <param name="sampleRateHz">サンプリング周波数 [Hz]。実機の録画に合わせる。</param>
        /// <param name="recordingId">刺激 ID。StimulusCatalog がこれで解決する。</param>
        /// <param name="startTimestamp">
        /// 先頭フレームの絶対時刻 [s]。実機では OVRPlugin.GetTimeInSeconds() 由来の
        /// 起動からの経過なので、0 以外の値でも扱えることを確認できるよう外から与える。
        /// </param>
        public static RecordingFile Build(
            SyntheticMotion motion, float sampleRateHz, string recordingId,
            string performerId = "SYNTHETIC", double startTimestamp = 1000.0)
        {
            if (motion == null) throw new ArgumentNullException(nameof(motion));
            if (sampleRateHz <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));

            var schedule = motion.Schedule;
            var file = new RecordingFile();

            double interval = 1.0 / sampleRateHz;
            int frameCount = (int)Math.Round(schedule.TotalSeconds / interval) + 1;
            file.frames.Capacity = frameCount;

            int nextPhase = 0;

            for (int i = 0; i < frameCount; i++)
            {
                double elapsed = i * interval;
                if (elapsed > schedule.TotalSeconds) elapsed = schedule.TotalSeconds;

                double timestamp = startTimestamp + elapsed;
                var sample = motion.Sample(elapsed, timestamp);
                file.frames.Add(RecordedFrame.From(sample));

                // 区間マーカーは記録モードと同じ規則で打つ（§5.4）。
                while (nextPhase < schedule.Phases.Count
                       && elapsed >= schedule.Phases[nextPhase].StartSeconds)
                {
                    var phase = schedule.Phases[nextPhase];
                    file.segmentMarkers.Add(new SegmentMarker
                    {
                        phaseId = phase.Id,
                        elapsedSeconds = elapsed,
                        absoluteTimestampSeconds = timestamp,
                        plannedStartSeconds = phase.StartSeconds,
                        includedInAnalysis = phase.IncludedInAnalysis,
                    });
                    nextPhase++;
                }
            }

            FillMetadata(file, motion, sampleRateHz, recordingId, performerId, interval);
            return file;
        }

        private static void FillMetadata(
            RecordingFile file, SyntheticMotion motion, float sampleRateHz,
            string recordingId, string performerId, double interval)
        {
            var parameters = motion.Parameters;
            var schedule = motion.Schedule;
            var metadata = file.metadata;

            metadata.formatVersion = RecordingMetadata.CurrentFormatVersion;
            metadata.recordingId = recordingId ?? "synthetic";
            metadata.performerId = performerId ?? "SYNTHETIC";
            metadata.createdAtIso8601 = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            metadata.notes =
                "仕様書 §8.2 の合成録画。既知の正弦波運動から生成されており、" +
                "誤差算出の解析的な検証に使う。実機の記録ではない。";

            metadata.neckOffsetD = parameters.neckOffsetD;
            metadata.normalizedNeckOffset = parameters.eyeHeight > 0f
                ? parameters.neckOffsetD / parameters.eyeHeight
                : 0f;
            metadata.handVertexBone = HandVertexBone.MiddleMcp.ToString();

            metadata.vertexSourceKind = nameof(SyntheticMotion);
            metadata.sdkVersion = "synthetic";
            metadata.unityVersion = Application.unityVersion;
            metadata.requestedDisplayFrequencyHz = sampleRateHz;
            metadata.effectiveDisplayFrequencyHz = sampleRateHz;
            metadata.displayFrequencyApplied = true;
            metadata.trackingOriginType = "FloorLevel";
            metadata.recenterCount = 0;

            // キャリブレーションは静止姿勢から解析的に決まる。
            // 基準姿勢が完全に静止しているので、ばらつきは 0。
            float length = motion.RestCharacteristicLength;
            metadata.calibration = new PerformerCalibration
            {
                eyeHeightMeters = parameters.eyeHeight,
                neckToLeftHandMeters = length,
                neckToRightHandMeters = length,
                characteristicLength = length,
                characteristicLengthStdDev = 0f,
                sampleCount = Math.Max(1, (int)(schedule.Phases[0].DurationSeconds * sampleRateHz)),
                holdDurationSeconds = schedule.Phases[0].DurationSeconds,
            };

            metadata.baselineHoldSeconds = schedule.Phases[0].DurationSeconds;
            metadata.leadInSeconds = schedule.Phases.Count > 1 ? schedule.Phases[1].DurationSeconds : 0f;
            metadata.segmentSeconds = schedule.Phases.Count > 2 ? schedule.Phases[2].DurationSeconds : 0f;
            metadata.segmentCount = Math.Max(0, schedule.Phases.Count - 2);
            metadata.segmentMarkerMode = SegmentMarkerMode.TimeBased.ToString();

            metadata.recordedDurationSeconds = schedule.TotalSeconds;
            metadata.meanFrameRateHz = sampleRateHz;
            metadata.maxFrameIntervalSeconds = interval;

            metadata.valid = true;
            metadata.invalidReason = "";
        }
    }
}
