using System;
using System.Collections.Generic;

namespace FollowingTriangle.Core
{
    public enum RecorderState
    {
        Idle = 0,
        Calibrating = 1,
        ReadyToRecord = 2,
        Recording = 3,
        Completed = 4,
    }

    /// <summary>
    /// 記録モードの進行そのもの。Unity の時間にもシーンにも依存しない。
    ///
    /// 時刻はすべて呼び出し側が渡すサンプルの timestampSeconds を使う
    /// （実機では OVRPlugin.GetTimeInSeconds()、仕様書 §3.1）。
    /// Time.time を内部で読まないので、合成録画の生成（§8.2）や単体テストから
    /// 任意の時間軸を流し込める。
    /// </summary>
    public sealed class RecordingSession
    {
        private readonly ExperimentSettings settings;
        private readonly RecordingSchedule schedule;
        private readonly CalibrationAccumulator calibration = new CalibrationAccumulator();

        private readonly List<RecordedFrame> frames = new List<RecordedFrame>();
        private readonly List<SegmentMarker> markers = new List<SegmentMarker>();

        private PerformerCalibration calibrationResult;
        private double recordingStartTimestamp;
        private double lastFrameTimestamp;
        private double maxFrameInterval;
        private int nextAutomaticPhaseIndex;
        private int nextManualPhaseIndex;

        public RecorderState State { get; private set; } = RecorderState.Idle;

        public RecordingSchedule Schedule => schedule;

        public IReadOnlyList<RecordedFrame> Frames => frames;

        public IReadOnlyList<SegmentMarker> Markers => markers;

        public PerformerCalibration Calibration => calibrationResult;

        /// <summary>録画開始からの経過時刻 [s]。録画中でなければ 0。</summary>
        public double ElapsedSeconds { get; private set; }

        /// <summary>現在のフェーズ。録画中のみ有効。</summary>
        public SchedulePhase CurrentPhase { get; private set; }

        public bool HasCurrentPhase { get; private set; }

        /// <summary>記録中に再センタリングが起きた回数。呼び出し側が設定する（§1）。</summary>
        public int RecenterCount { get; set; }

        public RecordingSession(ExperimentSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            schedule = RecordingSchedule.FromSettings(settings);

            // 93 s x 90 Hz = 8370 フレーム。録画中に List が再確保されて
            // フレーム落ちの原因になるのを避けるため、あらかじめ確保しておく。
            int expectedFrames = (int)(schedule.TotalSeconds * settings.TargetDisplayFrequencyHz) + 256;
            frames.Capacity = Math.Max(expectedFrames, 1024);
        }

        // ------------------------------------------------------------------
        // キャリブレーション（§5.1）
        // ------------------------------------------------------------------

        public void BeginCalibration()
        {
            calibration.Reset();
            calibrationResult = null;
            State = RecorderState.Calibrating;
        }

        /// <summary>
        /// キャリブレーション用のサンプルを 1 つ取り込む。
        /// 手のトラッキングが低信頼なフレームは取り込まない。キャリブレーション値は
        /// 実験全体の基準になるため、外挿された手の位置を混ぜてはならない（§7.1）。
        /// </summary>
        /// <returns>取り込んだら true。低信頼で捨てたら false。</returns>
        public bool AddCalibrationSample(in BodyTriangleSample sample)
        {
            if (State != RecorderState.Calibrating) return false;

            if (!sample.leftHand.isTracked || !sample.rightHand.isTracked) return false;
            if (sample.leftHand.confidence != HandConfidence.High) return false;
            if (sample.rightHand.confidence != HandConfidence.High) return false;

            calibration.Add(sample);
            return true;
        }

        public double CalibrationHoldSeconds => calibration.HoldDurationSeconds;

        public int CalibrationSampleCount => calibration.SampleCount;

        public bool CompleteCalibration(out string error)
        {
            if (State != RecorderState.Calibrating)
            {
                error = "キャリブレーション中ではありません。";
                return false;
            }

            var result = calibration.Build(settings.NeckOffsetD);
            if (!result.IsValid)
            {
                error = $"有効なサンプルが足りません (取得 {calibration.SampleCount} 件)。" +
                        "両手が高信頼でトラッキングされている状態で基準姿勢を保持してください。";
                return false;
            }

            calibrationResult = result;
            State = RecorderState.ReadyToRecord;
            error = null;
            return true;
        }

        // ------------------------------------------------------------------
        // 記録（§3.1, §5.4）
        // ------------------------------------------------------------------

        public bool BeginRecording(out string error)
        {
            if (State != RecorderState.ReadyToRecord)
            {
                error = "キャリブレーションが完了していません (§3.1)。";
                return false;
            }

            if (!schedule.Validate(out string scheduleError))
            {
                error = $"スケジュールが不正です: {scheduleError}";
                return false;
            }

            frames.Clear();
            markers.Clear();
            ElapsedSeconds = 0.0;
            maxFrameInterval = 0.0;
            nextAutomaticPhaseIndex = 0;
            nextManualPhaseIndex = 0;
            RecenterCount = 0;
            HasCurrentPhase = false;
            State = RecorderState.Recording;
            error = null;
            return true;
        }

        /// <summary>
        /// 1 フレームを記録する。最初の呼び出しの時刻が録画の原点になる。
        /// </summary>
        /// <returns>スケジュールの総時間に達して録画が終わるべきなら true。</returns>
        public bool AddFrame(in BodyTriangleSample sample)
        {
            if (State != RecorderState.Recording) return false;

            if (frames.Count == 0)
            {
                recordingStartTimestamp = sample.timestampSeconds;
                lastFrameTimestamp = sample.timestampSeconds;
            }
            else
            {
                double interval = sample.timestampSeconds - lastFrameTimestamp;
                if (interval > maxFrameInterval) maxFrameInterval = interval;
                lastFrameTimestamp = sample.timestampSeconds;
            }

            ElapsedSeconds = sample.timestampSeconds - recordingStartTimestamp;
            frames.Add(RecordedFrame.From(sample));

            if (settings.SegmentMarkerMode == SegmentMarkerMode.TimeBased)
            {
                EmitDueAutomaticMarkers(sample.timestampSeconds);
            }

            HasCurrentPhase = schedule.TryGetPhaseAt((float)ElapsedSeconds, out var phase);
            if (HasCurrentPhase) CurrentPhase = phase;

            return ElapsedSeconds >= schedule.TotalSeconds;
        }

        /// <summary>
        /// 時間ベースのマーカーを、予定時刻を過ぎたぶんだけ打つ。
        ///
        /// フレーム落ちで複数の境界を一度にまたぐことがあるため while で回す。
        /// 打ち漏らすと区間が 1 つ消えて解析側の区間数が合わなくなる。
        /// </summary>
        private void EmitDueAutomaticMarkers(double absoluteTimestamp)
        {
            while (nextAutomaticPhaseIndex < schedule.Phases.Count
                   && ElapsedSeconds >= schedule.Phases[nextAutomaticPhaseIndex].StartSeconds)
            {
                var phase = schedule.Phases[nextAutomaticPhaseIndex];
                markers.Add(new SegmentMarker
                {
                    phaseId = phase.Id,
                    elapsedSeconds = ElapsedSeconds,
                    absoluteTimestampSeconds = absoluteTimestamp,
                    plannedStartSeconds = phase.StartSeconds,
                    includedInAnalysis = phase.IncludedInAnalysis,
                });
                nextAutomaticPhaseIndex++;
            }
        }

        /// <summary>
        /// コントローラボタンによる手動マーカー（§5.4）。
        /// フェーズはスケジュールの順に割り当てる。予定時刻との差は
        /// plannedStartSeconds との比較で後から見られる。
        /// </summary>
        public bool AddManualMarker(double absoluteTimestamp, out string error)
        {
            if (State != RecorderState.Recording)
            {
                error = "録画中ではありません。";
                return false;
            }

            if (settings.SegmentMarkerMode != SegmentMarkerMode.Controller)
            {
                error = "区間マーカーが時間ベースに設定されています。手動マーカーは無視されます。";
                return false;
            }

            if (nextManualPhaseIndex >= schedule.Phases.Count)
            {
                error = "予定されたフェーズをすべて打ち終えています。";
                return false;
            }

            var phase = schedule.Phases[nextManualPhaseIndex];
            markers.Add(new SegmentMarker
            {
                phaseId = phase.Id,
                elapsedSeconds = ElapsedSeconds,
                absoluteTimestampSeconds = absoluteTimestamp,
                plannedStartSeconds = phase.StartSeconds,
                includedInAnalysis = phase.IncludedInAnalysis,
            });
            nextManualPhaseIndex++;

            error = null;
            return true;
        }

        /// <summary>録画を締めて、保存できる形にまとめる。</summary>
        public RecordingFile Complete(RecordingEnvironment environment)
        {
            State = RecorderState.Completed;

            var file = new RecordingFile();
            file.frames.AddRange(frames);
            file.segmentMarkers.AddRange(markers);

            double duration = frames.Count > 1 ? lastFrameTimestamp - recordingStartTimestamp : 0.0;

            var metadata = file.metadata;
            metadata.formatVersion = RecordingMetadata.CurrentFormatVersion;
            metadata.recordingId = environment.recordingId ?? "";
            metadata.performerId = environment.performerId ?? "";
            metadata.createdAtIso8601 = environment.createdAtIso8601 ?? "";
            metadata.notes = environment.notes ?? "";

            metadata.neckOffsetD = settings.NeckOffsetD;
            metadata.normalizedNeckOffset = calibrationResult != null && calibrationResult.eyeHeightMeters > 0f
                ? settings.NeckOffsetD / calibrationResult.eyeHeightMeters
                : 0f;
            metadata.handVertexBone = settings.HandVertexBone.ToString();

            metadata.vertexSourceKind = environment.vertexSourceKind ?? "";
            metadata.sdkVersion = environment.sdkVersion ?? "";
            metadata.unityVersion = environment.unityVersion ?? "";
            metadata.requestedDisplayFrequencyHz = environment.requestedDisplayFrequencyHz;
            metadata.effectiveDisplayFrequencyHz = environment.effectiveDisplayFrequencyHz;
            metadata.displayFrequencyApplied = environment.displayFrequencyApplied;
            metadata.trackingOriginType = environment.trackingOriginType ?? "";
            metadata.trackingOriginIsFloorLevel = environment.trackingOriginIsFloorLevel;
            metadata.recenterCount = RecenterCount;

            metadata.calibration = calibrationResult ?? new PerformerCalibration();

            metadata.baselineHoldSeconds = settings.BaselineHoldSeconds;
            metadata.leadInSeconds = settings.LeadInSeconds;
            metadata.segmentSeconds = settings.SegmentSeconds;
            metadata.segmentCount = settings.SegmentCount;
            metadata.segmentMarkerMode = settings.SegmentMarkerMode.ToString();

            metadata.recordedDurationSeconds = duration;
            metadata.meanFrameRateHz = duration > 0.0 ? (frames.Count - 1) / duration : 0.0;
            metadata.maxFrameIntervalSeconds = maxFrameInterval;

            ApplyValidity(metadata);

            return file;
        }

        /// <summary>
        /// 刺激として使える録画かを判定してメタデータに書く。
        /// アプリ側でファイルを捨てることはしない。判断材料を残して実験者に委ねる。
        /// </summary>
        private void ApplyValidity(RecordingMetadata metadata)
        {
            if (RecenterCount > 0)
            {
                metadata.valid = false;
                metadata.invalidReason =
                    $"記録中に再センタリングが {RecenterCount} 回発生しました (§1)。" +
                    "ワールド原点が動いているため刺激として使用できません。";
                return;
            }

            int expectedMarkers = schedule.Phases.Count;
            if (metadata.segmentMarkerMode == SegmentMarkerMode.Controller.ToString()
                && markers.Count != expectedMarkers)
            {
                metadata.valid = false;
                metadata.invalidReason =
                    $"区間マーカーが {markers.Count} 個しかありません (期待 {expectedMarkers} 個)。";
                return;
            }

            if (metadata.recordedDurationSeconds < schedule.TotalSeconds - 0.5)
            {
                metadata.valid = false;
                metadata.invalidReason =
                    $"記録長 {metadata.recordedDurationSeconds:F2} s がスケジュール " +
                    $"{schedule.TotalSeconds:F2} s に足りません。";
                return;
            }

            metadata.valid = true;
            metadata.invalidReason = "";
        }
    }

    /// <summary>
    /// 録画時の環境情報。XR ランタイムから取れる値を Core に持ち込まないための受け渡し用。
    /// </summary>
    public struct RecordingEnvironment
    {
        public string recordingId;
        public string performerId;
        public string createdAtIso8601;
        public string notes;
        public string vertexSourceKind;
        public string sdkVersion;
        public string unityVersion;
        public float requestedDisplayFrequencyHz;
        public float effectiveDisplayFrequencyHz;
        public bool displayFrequencyApplied;
        public string trackingOriginType;
        public bool trackingOriginIsFloorLevel;
    }
}
