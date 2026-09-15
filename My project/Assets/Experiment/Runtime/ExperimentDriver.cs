using System;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 実験モードの試行フロー（仕様書 §5.4, §5.5）。
    ///
    /// 1 セッションの流れ
    ///   教示ファイル読み込み → 被験者番号の選択 → キャリブレーション（§5.1, 被験者ごとに 1 回）
    ///   → 試行 x 3（グレコ・ラテン方格による割付, §5.5）→ 終了
    ///
    /// 1 試行の流れ（§5.4）
    ///   教示表示（被験者ペース） → 基準姿勢 3 s（Registration, §5.3）
    ///   → 導入 10 s → 区間 1〜4 各 20 s → ログ保存
    ///
    /// **このクラスは実機でもエディタでも同じものが走る。** 検証用の別ドライバは持たない。
    /// 検証したコードと本番のコードが違うと、検証の意味がなくなるため。
    /// 差し替わるのは頂点供給元（OvrVertexSource / MockVertexSource）と
    /// 入力（OvrRecorderInput / KeyboardRecorderInput）だけ。
    /// </summary>
    [DisallowMultipleComponent]
    public class ExperimentDriver : MonoBehaviour
    {
        private enum Phase
        {
            LoadingInstructions,
            SelectingParticipant,
            CalibratingParticipant,
            TrialInstruction,
            TrialBaseline,
            TrialRunning,
            TrialComplete,
            SessionComplete,
            Halted,
        }

        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("IVertexSource を実装した MonoBehaviour。被験者 B 側の入力。")]
        private MonoBehaviour vertexSourceBehaviour;

        [SerializeField]
        [Tooltip("IRecorderInput を実装した MonoBehaviour。実験者が操作する。" +
                 "被験者 B の手はハンドトラッキングに使うため、B がコントローラを握ってはいけない。")]
        private MonoBehaviour inputBehaviour;

        [SerializeField]
        [Tooltip("IRecenterMonitor を実装した MonoBehaviour（実機では XrRuntimeConfig）。")]
        private MonoBehaviour recenterMonitorBehaviour;

        [SerializeField]
        [Tooltip("IXrRuntimeInfo を実装した MonoBehaviour（実機では XrRuntimeConfig）。")]
        private MonoBehaviour xrRuntimeInfoBehaviour;

        [SerializeField] private TriangleView selfTriangleView;
        [SerializeField] private StimulusPresenter stimulusPresenter;
        [SerializeField] private TrialLogger trialLogger;
        [SerializeField] private InstructionLoader instructionLoader;
        [SerializeField] private StimulusCatalog stimulusCatalog;
        [SerializeField] private RecorderHud hud;

        [Header("被験者")]
        [SerializeField]
        [Tooltip("被験者 ID の接頭辞。ID は接頭辞 + 2 桁の番号（例: B01）。" +
                 "割付は末尾の番号で決まる (§5.5)。")]
        private string participantIdPrefix = "B";

        [SerializeField]
        [Tooltip("開始時の被験者番号。実行中にマーカーキーで増やせる。")]
        private int participantNumber = 1;

        [SerializeField]
        [Tooltip("既存のプロファイルがあれば読み込んで再利用する。" +
                 "同一被験者の全試行で同じ L を使わないと、条件間の正規化誤差が比較できない (§5.2)。")]
        private bool reuseExistingProfile = true;

        private IVertexSource vertexSource;
        private IRecorderInput input;
        private IRecenterMonitor recenterMonitor;
        private IXrRuntimeInfo xrRuntimeInfo;

        private RecordingSchedule schedule;
        private readonly CalibrationAccumulator calibration = new CalibrationAccumulator();
        private ParticipantProfile profile;

        private TrialAssignment[] assignments;
        private int trialCursor;
        private RecordingFile currentRecording;
        private string currentStimulusId = "";
        private string currentRecordingPath = "";

        private Phase phase = Phase.LoadingInstructions;
        private double phaseStartTimestamp;
        private double trialStartTimestamp;
        private string haltReason = "";

        public string ParticipantId => $"{participantIdPrefix}{participantNumber:D2}";

        private InstructionSet Instructions => instructionLoader != null ? instructionLoader.Set : null;

        private TrialAssignment CurrentAssignment =>
            assignments != null && trialCursor < assignments.Length
                ? assignments[trialCursor]
                : default;

        private void Awake()
        {
            vertexSource = vertexSourceBehaviour as IVertexSource;
            input = inputBehaviour as IRecorderInput;
            recenterMonitor = recenterMonitorBehaviour as IRecenterMonitor;
            xrRuntimeInfo = xrRuntimeInfoBehaviour as IXrRuntimeInfo;

            if (recenterMonitor != null) recenterMonitor.Recentered += OnRecentered;

            if (settings == null || vertexSource == null || input == null
                || stimulusPresenter == null || instructionLoader == null)
            {
                Halt("参照が不足しています。シーンの配線を確認してください。");
                enabled = false;
                return;
            }

            schedule = RecordingSchedule.FromSettings(settings);
            stimulusPresenter.SetVisible(false);
        }

        private void OnDestroy()
        {
            if (recenterMonitor != null) recenterMonitor.Recentered -= OnRecentered;
        }

        private void Update()
        {
            if (phase == Phase.Halted)
            {
                ShowMessage($"実験を継続できません\n{haltReason}");
                return;
            }

            if (phase == Phase.LoadingInstructions)
            {
                TickLoading();
                return;
            }

            if (!vertexSource.TryGetSample(out var sample))
            {
                if (selfTriangleView != null) selfTriangleView.SetVisible(false);
                ShowWarning("トラッキングが取得できていません");
                return;
            }

            ShowWarning(null);
            UpdateSelfTriangle(sample);

            switch (phase)
            {
                case Phase.SelectingParticipant:
                    TickParticipantSelection();
                    break;

                case Phase.CalibratingParticipant:
                    TickCalibration(sample);
                    break;

                case Phase.TrialInstruction:
                    if (input.ConfirmPressedThisFrame) BeginBaseline(sample);
                    break;

                case Phase.TrialBaseline:
                    TickBaseline(sample);
                    break;

                case Phase.TrialRunning:
                    TickRunning(sample);
                    break;

                case Phase.TrialComplete:
                    if (input.ConfirmPressedThisFrame) AdvanceToNextTrial();
                    break;

                case Phase.SessionComplete:
                    break;
            }
        }

        // ------------------------------------------------------------------
        // セッションの準備
        // ------------------------------------------------------------------

        private void TickLoading()
        {
            if (!instructionLoader.IsFinished)
            {
                ShowMessage("教示文を読み込んでいます...");
                return;
            }

            if (!instructionLoader.IsLoaded)
            {
                // 教示が無いまま実験を始めてはいけない。C1 と C2 の差は教示文だけであり、
                // 教示が出ないなら条件操作そのものが存在しないことになる。
                Halt($"教示文を読み込めませんでした。\n{instructionLoader.Error}");
                return;
            }

            stimulusCatalog?.Scan();
            phase = Phase.SelectingParticipant;
        }

        private void TickParticipantSelection()
        {
            if (input.MarkerPressedThisFrame) participantNumber++;

            ShowMessage(
                $"被験者 {ParticipantId}\n" +
                $"割付行 {GraecoLatinSquare.RowFor(participantNumber) + 1} / {GraecoLatinSquare.Order}\n" +
                "マーカーキーで番号を進める / 確定キーで開始");

            if (!input.ConfirmPressedThisFrame) return;

            if (!ParticipantNumber.TryParse(ParticipantId, out int number, out string error))
            {
                Halt(error);
                return;
            }

            assignments = GraecoLatinSquare.For(number);
            trialCursor = 0;

            Debug.Log(
                $"[{nameof(ExperimentDriver)}] 被験者 {ParticipantId} (番号 {number}) の割付: " +
                string.Join(" / ", Array.ConvertAll(assignments, a => a.ToString())));

            if (stimulusCatalog != null && !stimulusCatalog.IsComplete)
            {
                // 刺激が揃っていなければカウンターバランスは成立しない。
                // 止めはしないが、状態は CSV ヘッダに残り続ける。
                Debug.LogError(
                    $"[{nameof(ExperimentDriver)}] 刺激が揃っていません。" +
                    $"カウンターバランスは成立しません: {stimulusCatalog.LastScanSummary}");
            }

            BeginParticipantCalibration();
        }

        private void BeginParticipantCalibration()
        {
            if (reuseExistingProfile
                && ParticipantProfileStorage.TryLoad(ParticipantId, out var existing, out _))
            {
                profile = existing;
                Debug.Log($"[{nameof(ExperimentDriver)}] プロファイルを再利用: {profile}");
                BeginTrialInstruction();
                return;
            }

            calibration.Reset();
            phase = Phase.CalibratingParticipant;
            phaseStartTimestamp = double.NaN;
        }

        private void TickCalibration(in BodyTriangleSample sample)
        {
            if (double.IsNaN(phaseStartTimestamp)) phaseStartTimestamp = sample.timestampSeconds;

            if (IsHighConfidence(sample)) calibration.Add(sample);

            double held = sample.timestampSeconds - phaseStartTimestamp;
            float required = settings.CalibrationHoldSeconds;

            if (held < required)
            {
                ShowMessage(
                    $"{Instructions.baselinePrompt}\n" +
                    $"{Math.Max(0.0, required - held):F0} 秒");
                return;
            }

            var result = calibration.Build(settings.NeckOffsetD);
            if (!result.IsValid)
            {
                Debug.LogWarning($"[{nameof(ExperimentDriver)}] キャリブレーション失敗。やり直します。");
                calibration.Reset();
                phaseStartTimestamp = sample.timestampSeconds;
                return;
            }

            profile = ParticipantProfile.Create(ParticipantId, result, settings.NeckOffsetD);

            if (ParticipantProfileStorage.TrySave(profile, out string path, out string saveError))
            {
                Debug.Log($"[{nameof(ExperimentDriver)}] プロファイル保存: {path}\n  {profile}");
            }
            else
            {
                Debug.LogWarning($"[{nameof(ExperimentDriver)}] {saveError}");
            }

            BeginTrialInstruction();
        }

        // ------------------------------------------------------------------
        // 試行（§5.4）
        // ------------------------------------------------------------------

        /// <summary>
        /// 教示フェーズ。被験者ペースで進む（§5.4）。
        /// 教示文は外部 JSON から来る（§7.4）。コードには文言を持たない。
        /// </summary>
        private void BeginTrialInstruction()
        {
            if (trialCursor >= assignments.Length)
            {
                phase = Phase.SessionComplete;
                stimulusPresenter.SetVisible(false);
                ShowMessage(Instructions.sessionCompletePrompt);
                Debug.Log($"[{nameof(ExperimentDriver)}] セッション終了: {ParticipantId}");
                return;
            }

            var assignment = CurrentAssignment;

            if (stimulusCatalog == null)
            {
                Halt("Stimulus Catalog が未設定です。刺激を解決できません (§5.5)。");
                return;
            }

            if (!stimulusCatalog.TryLoad(
                    assignment.StimulusIndex, out currentRecording,
                    out currentStimulusId, out currentRecordingPath, out string loadError))
            {
                Halt($"刺激を読み込めません: {loadError}");
                return;
            }

            if (!stimulusPresenter.LoadRecording(currentRecording, out string presenterError))
            {
                Halt($"刺激を読み込めません: {presenterError}");
                return;
            }

            // 自己と相手に同一の描画設定を渡す。ConditionVisuals.For は自他の区別を
            // 引数に取らないので、同じ条件からは必ず同じ値が出る（§4.2, §4.3）。
            var visuals = ConditionVisuals.For(assignment.Condition, settings);
            if (selfTriangleView != null) selfTriangleView.Configure(visuals);
            stimulusPresenter.Configure(visuals);
            stimulusPresenter.SetVisible(false);

            phase = Phase.TrialInstruction;

            ShowMessage(
                $"{Instructions.TextFor(assignment.Condition)}\n\n" +
                $"{Instructions.readyPrompt}");

            Debug.Log(
                $"[{nameof(ExperimentDriver)}] 試行 {trialCursor + 1}/{assignments.Length}: " +
                $"{assignment.Condition} / 刺激 {currentStimulusId}");
        }

        private void BeginBaseline(in BodyTriangleSample sample)
        {
            trialStartTimestamp = sample.timestampSeconds;
            stimulusPresenter.BeginBaseline();
            trialLogger?.BeginBaselineCapture();
            recenterMonitor?.ResetPerTrialState();
            phase = Phase.TrialBaseline;
        }

        /// <summary>
        /// 基準姿勢フェーズ（§5.3-1）。この間、相手三角形は既定で非表示。
        /// 見えていると B が A に寄せに行き、基準姿勢が自然な姿勢でなくなって
        /// Registration 自体が汚染される。
        /// </summary>
        private void TickBaseline(in BodyTriangleSample sample)
        {
            double elapsed = sample.timestampSeconds - trialStartTimestamp;
            stimulusPresenter.AddBaselineSample(sample);
            trialLogger?.CaptureBaselineSample(elapsed, sample);

            if (elapsed < settings.BaselineHoldSeconds)
            {
                ShowMessage(
                    $"{Instructions.baselinePrompt}\n" +
                    $"{Math.Max(0.0, settings.BaselineHoldSeconds - elapsed):F0} 秒");
                return;
            }

            if (!stimulusPresenter.CompleteRegistration(profile, out string error))
            {
                Debug.LogError($"[{nameof(ExperimentDriver)}] Registration 失敗: {error}");
                ShowMessage($"やり直します\n{Instructions.baselinePrompt}");
                BeginBaseline(sample);
                return;
            }

            WriteBaselineLog();
            trialLogger?.BeginTrial(BuildHeader("trial"), profile.calibration.characteristicLength);

            stimulusPresenter.SetVisible(true);
            phase = Phase.TrialRunning;
        }

        private void TickRunning(in BodyTriangleSample sample)
        {
            double elapsed = sample.timestampSeconds - trialStartTimestamp;

            if (!stimulusPresenter.Tick(elapsed, out var transformedA))
            {
                Halt("刺激を再生できません。");
                return;
            }

            string marker = schedule.TryGetPhaseAt((float)elapsed, out var phaseAtNow)
                ? phaseAtNow.Id
                : "";

            // 低信頼のフレームも含めてすべて記録する。除外の判断は後処理（§6.3, §7.1）。
            trialLogger?.Record(elapsed, marker, transformedA, sample);

            // 仕様書 §10-3：進行状況フィードバックは既定で非表示。
            // 残り時間が見えると、被験者の注意配分が課題から時間へ移る。
            if (settings.ShowProgressToParticipant)
            {
                ShowMessage($"{elapsed:F0} / {schedule.TotalSeconds:F0} s");
            }
            else
            {
                ShowMessage("");
            }

            if (elapsed >= schedule.TotalSeconds) CompleteTrial();
        }

        private void CompleteTrial()
        {
            stimulusPresenter.SetVisible(false);
            phase = Phase.TrialComplete;

            if (trialLogger != null)
            {
                if (trialLogger.EndTrial(out string path, out string error))
                {
                    Debug.Log(
                        $"[{nameof(ExperimentDriver)}] 試行ログを保存しました " +
                        $"({trialLogger.RowCount} 行): {path}");
                }
                else
                {
                    Debug.LogError($"[{nameof(ExperimentDriver)}] {error}");
                }
            }

            ShowMessage(Instructions.trialCompletePrompt);
        }

        private void AdvanceToNextTrial()
        {
            trialCursor++;
            BeginTrialInstruction();
        }

        // ------------------------------------------------------------------
        // ログ
        // ------------------------------------------------------------------

        private void WriteBaselineLog()
        {
            if (trialLogger == null || trialLogger.BaselineCaptureCount == 0) return;

            bool written = trialLogger.WriteBaselineLog(
                BuildHeader("registration"),
                profile.calibration.characteristicLength,
                elapsed => stimulusPresenter.TrySampleTransformed(elapsed, out var a) ? a : default,
                out string path, out string error);

            if (!written)
            {
                Debug.LogError($"[{nameof(ExperimentDriver)}] {error}");
                return;
            }

            Debug.Log(
                $"[{nameof(ExperimentDriver)}] 基準姿勢ログを保存しました " +
                $"({trialLogger.BaselineCaptureCount} 行, " +
                $"うち平均採用 {stimulusPresenter.BaselineSampleCount} 件): {path}");
        }

        private TrialLogHeader BuildHeader(string logKind)
        {
            var assignment = CurrentAssignment;
            var calibrationA = currentRecording.metadata.calibration;
            ParticipantNumber.TryParse(ParticipantId, out int number, out _);

            return new TrialLogHeader
            {
                logKind = logKind,
                participantId = ParticipantId,
                participantNumber = number,
                assignmentRow = GraecoLatinSquare.RowFor(number),
                conditionId = assignment.Condition.ToString(),
                stimulusId = currentStimulusId,
                trialIndex = assignment.Position + 1,
                recordedAtIso8601 = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),

                // 実際に被験者へ出した文言をそのまま残す（§7.4）。
                // 教示ファイルを差し替えても、どの試行でどの文言だったかが追える。
                instructionText = Instructions.TextFor(assignment.Condition),
                instructionSource = instructionLoader.SourcePath,

                stimulusCatalogComplete = stimulusCatalog == null || stimulusCatalog.IsComplete,

                neckOffsetD = settings.NeckOffsetD,
                normalizedNeckOffset = profile.normalizedNeckOffset,
                handVertexBone = settings.HandVertexBone.ToString(),
                recomputeNeckVertexOnPlayback = settings.RecomputeNeckVertexOnPlayback,

                characteristicLengthA = calibrationA.characteristicLength,
                characteristicLengthB = profile.calibration.characteristicLength,
                bodyScaleFactor = stimulusPresenter.Transform.scale,
                normalizationLength = profile.calibration.characteristicLength,

                registrationMethod = settings.RegistrationMethod.ToString(),
                registrationResidualRms = stimulusPresenter.RegistrationResidualRms,
                registrationResidualPerVertex = stimulusPresenter.RegistrationResidualPerVertex,
                referenceTriangleA = stimulusPresenter.ReferenceA,
                baselineTriangleB = stimulusPresenter.BaselineB,
                baselineFrameCount = stimulusPresenter.BaselineFrameCount,
                baselineAcceptedCount = stimulusPresenter.BaselineSampleCount,
                transform = stimulusPresenter.Transform,

                sdkVersion = xrRuntimeInfo?.SdkVersion ?? "unknown",
                unityVersion = Application.unityVersion,
                requestedDisplayFrequencyHz = xrRuntimeInfo?.RequestedDisplayFrequencyHz ?? 0f,
                effectiveDisplayFrequencyHz = xrRuntimeInfo?.EffectiveDisplayFrequencyHz ?? 0f,
                displayFrequencyApplied = xrRuntimeInfo?.DisplayFrequencyApplied ?? false,
                trackingOriginType = xrRuntimeInfo?.TrackingOriginType ?? "unknown",

                baselineHoldSeconds = settings.BaselineHoldSeconds,
                leadInSeconds = settings.LeadInSeconds,
                segmentSeconds = settings.SegmentSeconds,
                segmentCount = settings.SegmentCount,

                recordingId = currentRecording.metadata.recordingId,
                performerId = currentRecording.metadata.performerId,
                recordingFileName = System.IO.Path.GetFileName(currentRecordingPath),
            };
        }

        /// <summary>
        /// 仕様書 §1：試行中の再センタリングは禁止。
        /// 試行は中断せず続行し、無効フラグだけを立てる。中断すると被験者の課題文脈が
        /// 壊れ、やり直しによる疲労と学習効果が増える。除外の判断は後処理に委ねる。
        /// </summary>
        private void OnRecentered(double timestamp)
        {
            if (trialLogger == null) return;
            if (phase != Phase.TrialBaseline && phase != Phase.TrialRunning) return;

            trialLogger.NotifyRecenter();

            string when = phase == Phase.TrialBaseline
                ? "基準姿勢中。Registration が無効になります"
                : "追従中。この試行は無効フラグ付きで保存されます";

            Debug.LogError(
                $"[{nameof(ExperimentDriver)}] 再センタリングが発生しました " +
                $"(t={timestamp:F3} s, {when}) (§1)。");
        }

        // ------------------------------------------------------------------

        private void Halt(string reason)
        {
            haltReason = reason;
            phase = Phase.Halted;
            stimulusPresenter?.SetVisible(false);
            Debug.LogError($"[{nameof(ExperimentDriver)}] {reason}");
        }

        private void UpdateSelfTriangle(in BodyTriangleSample sample)
        {
            if (selfTriangleView == null) return;

            // 自己三角形は常時表示（§4.1）。
            selfTriangleView.SetVisible(true);
            selfTriangleView.SetVertices(sample.v0, sample.v1, sample.v2);
        }

        private static bool IsHighConfidence(in BodyTriangleSample sample) =>
            sample.leftHand.isTracked && sample.rightHand.isTracked
            && sample.leftHand.confidence == HandConfidence.High
            && sample.rightHand.confidence == HandConfidence.High;

        private void ShowMessage(string message)
        {
            if (hud != null) hud.SetMessage(message);
        }

        private void ShowWarning(string message)
        {
            if (hud != null) hud.SetWarning(message);
        }
    }
}
