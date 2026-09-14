using System;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 段階3 の検証用ドライバ。再生・Registration・体格正規化が繋がって動くことを
    /// HMD なしで確認するためのもの。
    ///
    /// **これは本番の試行フローではない。** 教示提示、条件のラテン方格割付、
    /// 試行間の進行制御（仕様書 §5.4, §5.5）は段階5 で実装し、このクラスは置き換わる。
    /// ここにあるのは「基準姿勢 → Registration → 再生」の 1 本を通すだけの最小構成。
    ///
    /// 誤差算出と CSV 出力（§6）は段階4。ここでは何も記録しない。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlaybackVerificationDriver : MonoBehaviour
    {
        private enum Phase
        {
            Idle,
            CalibratingParticipant,
            ReadyForTrial,
            Baseline,
            Playing,
            Finished,
        }

        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("IVertexSource を実装した MonoBehaviour。被験者 B 側の入力。")]
        private MonoBehaviour vertexSourceBehaviour;

        [SerializeField]
        [Tooltip("IRecorderInput を実装した MonoBehaviour。確定 / 中断に使う。")]
        private MonoBehaviour inputBehaviour;

        [SerializeField] private TriangleView selfTriangleView;
        [SerializeField] private StimulusPresenter stimulusPresenter;
        [SerializeField] private RecorderHud hud;

        [Header("刺激")]
        [SerializeField]
        [Tooltip("再生する録画のパス。空なら recordings フォルダの最新を使う。")]
        private string recordingPath = "";

        [Header("被験者")]
        [SerializeField] private string participantId = "B01";

        [SerializeField]
        [Tooltip("既存のプロファイルがあれば読み込んで再利用する (§5.2: L を試行間で変えない)。")]
        private bool reuseExistingProfile = true;

        [Header("条件 (段階5 で割付に置き換わる)")]
        [SerializeField] private Condition condition = Condition.C3;

        private IVertexSource vertexSource;
        private IRecorderInput input;
        private RecordingSchedule schedule;
        private readonly CalibrationAccumulator calibration = new CalibrationAccumulator();
        private ParticipantProfile profile;

        private Phase phase = Phase.Idle;
        private double phaseStartTimestamp;
        private double trialStartTimestamp;

        public StimulusTransform Transform => stimulusPresenter != null
            ? stimulusPresenter.Transform
            : StimulusTransform.Identity;

        private void Awake()
        {
            vertexSource = vertexSourceBehaviour as IVertexSource;
            input = inputBehaviour as IRecorderInput;

            if (settings == null || vertexSource == null || input == null || stimulusPresenter == null)
            {
                Debug.LogError(
                    $"[{nameof(PlaybackVerificationDriver)}] 参照が不足しています。無効にします。", this);
                enabled = false;
                return;
            }

            schedule = RecordingSchedule.FromSettings(settings);

            // 自己と相手に **同一の** 描画設定を渡す。ConditionVisuals.For は
            // 自他の区別を引数に取らないので、同じ条件からは必ず同じ値が出る（§4.2, §4.3）。
            var visuals = ConditionVisuals.For(condition, settings);
            if (selfTriangleView != null) selfTriangleView.Configure(visuals);
            stimulusPresenter.Configure(visuals);
            stimulusPresenter.SetVisible(false);

            ShowMessage(BuildIdleMessage());
        }

        private void Update()
        {
            if (!vertexSource.TryGetSample(out var sample))
            {
                if (selfTriangleView != null) selfTriangleView.SetVisible(false);
                ShowWarning("トラッキングが取得できていません");
                return;
            }

            ShowWarning(null);

            if (selfTriangleView != null)
            {
                selfTriangleView.SetVisible(true);
                selfTriangleView.SetVertices(sample.v0, sample.v1, sample.v2);
            }

            if (input.AbortPressedThisFrame && phase != Phase.Idle)
            {
                ResetToIdle("中断しました");
                return;
            }

            switch (phase)
            {
                case Phase.Idle:
                    if (input.ConfirmPressedThisFrame) BeginParticipantCalibration(sample);
                    break;

                case Phase.CalibratingParticipant:
                    TickCalibration(sample);
                    break;

                case Phase.ReadyForTrial:
                    if (input.ConfirmPressedThisFrame) BeginTrial(sample);
                    break;

                case Phase.Baseline:
                    TickBaseline(sample);
                    break;

                case Phase.Playing:
                    TickPlayback(sample);
                    break;

                case Phase.Finished:
                    if (input.ConfirmPressedThisFrame) ResetToIdle(null);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // §5.1 被験者のキャリブレーション
        // ------------------------------------------------------------------

        private void BeginParticipantCalibration(in BodyTriangleSample sample)
        {
            if (reuseExistingProfile
                && ParticipantProfileStorage.TryLoad(participantId, out var existing, out _))
            {
                profile = existing;
                Debug.Log($"[{nameof(PlaybackVerificationDriver)}] プロファイルを再利用: {profile}");
                PrepareTrial();
                return;
            }

            calibration.Reset();
            phase = Phase.CalibratingParticipant;
            phaseStartTimestamp = sample.timestampSeconds;
            ShowMessage("基準姿勢：両手を前方に開いて静止してください");
        }

        private void TickCalibration(in BodyTriangleSample sample)
        {
            if (IsHighConfidence(sample)) calibration.Add(sample);

            double held = sample.timestampSeconds - phaseStartTimestamp;
            float required = settings.CalibrationHoldSeconds;

            if (held < required)
            {
                ShowMessage(
                    $"基準姿勢を保持してください  {Math.Max(0.0, required - held):F1} s\n" +
                    $"（有効サンプル {calibration.SampleCount}）");
                return;
            }

            var result = calibration.Build(settings.NeckOffsetD);
            if (!result.IsValid)
            {
                ShowMessage("キャリブレーション失敗。確定キーでやり直します");
                phase = Phase.Idle;
                return;
            }

            profile = ParticipantProfile.Create(participantId, result, settings.NeckOffsetD);

            if (!ParticipantProfileStorage.TrySave(profile, out string path, out string saveError))
            {
                Debug.LogWarning($"[{nameof(PlaybackVerificationDriver)}] {saveError}");
            }
            else
            {
                Debug.Log($"[{nameof(PlaybackVerificationDriver)}] プロファイル保存: {path}\n  {profile}");
            }

            PrepareTrial();
        }

        // ------------------------------------------------------------------
        // 試行
        // ------------------------------------------------------------------

        private void PrepareTrial()
        {
            if (!TryLoadRecording(out string error))
            {
                Debug.LogError($"[{nameof(PlaybackVerificationDriver)}] {error}");
                ShowMessage($"刺激を読み込めません\n{error}");
                phase = Phase.Idle;
                return;
            }

            phase = Phase.ReadyForTrial;
            ShowMessage(
                $"被験者 {profile.participantId}  L={profile.calibration.characteristicLength:F2} m\n" +
                $"刺激: {System.IO.Path.GetFileName(ResolveRecordingPath())}\n" +
                "確定キーで試行を開始します");
        }

        private bool TryLoadRecording(out string error)
        {
            string path = ResolveRecordingPath();
            if (string.IsNullOrEmpty(path))
            {
                error = $"録画が見つかりません: {RecordingStorage.DirectoryFor(settings)}";
                return false;
            }

            if (!RecordingStorage.TryLoad(path, out var recording, out error)) return false;

            return stimulusPresenter.LoadRecording(recording, out error);
        }

        private string ResolveRecordingPath()
        {
            if (!string.IsNullOrWhiteSpace(recordingPath)) return recordingPath;

            var paths = RecordingStorage.ListRecordings(settings);
            return paths.Count == 0 ? null : paths[paths.Count - 1];
        }

        private void BeginTrial(in BodyTriangleSample sample)
        {
            trialStartTimestamp = sample.timestampSeconds;
            stimulusPresenter.BeginBaseline();
            phase = Phase.Baseline;
        }

        /// <summary>
        /// 基準姿勢フェーズ（§5.3-1）。この間、相手三角形は既定で非表示。
        /// 見えていると B が A に寄せに行き、基準姿勢が「自然な姿勢」でなくなって
        /// Registration 自体が汚染される。
        /// </summary>
        private void TickBaseline(in BodyTriangleSample sample)
        {
            double elapsed = sample.timestampSeconds - trialStartTimestamp;
            stimulusPresenter.AddBaselineSample(sample);

            if (elapsed < settings.BaselineHoldSeconds)
            {
                ShowMessage(
                    $"基準姿勢：両手を前方に開いて静止してください  " +
                    $"{Math.Max(0.0, settings.BaselineHoldSeconds - elapsed):F1} s");
                return;
            }

            if (!stimulusPresenter.CompleteRegistration(profile, out string error))
            {
                Debug.LogError($"[{nameof(PlaybackVerificationDriver)}] Registration 失敗: {error}");
                ShowMessage($"Registration 失敗\n{error}");
                phase = Phase.Idle;
                return;
            }

            stimulusPresenter.SetVisible(true);
            phase = Phase.Playing;
        }

        private void TickPlayback(in BodyTriangleSample sample)
        {
            double elapsed = sample.timestampSeconds - trialStartTimestamp;

            if (!stimulusPresenter.Tick(elapsed, out _))
            {
                ShowMessage("再生できません");
                return;
            }

            if (elapsed >= schedule.TotalSeconds)
            {
                phase = Phase.Finished;
                stimulusPresenter.SetVisible(false);
                ShowMessage(
                    $"試行終了\n" +
                    $"Registration 残差 RMS {stimulusPresenter.RegistrationResidualRms * 1000f:F1} mm\n" +
                    "確定キーで最初に戻ります");
                return;
            }

            string phaseName = schedule.TryGetPhaseAt((float)elapsed, out var current)
                ? current.Id
                : "-";

            ShowMessage($"{phaseName}   {elapsed:F1} / {schedule.TotalSeconds:F1} s");
        }

        private void ResetToIdle(string message)
        {
            phase = Phase.Idle;
            stimulusPresenter.SetVisible(false);
            ShowMessage(message ?? BuildIdleMessage());
        }

        private string BuildIdleMessage() =>
            $"確定キーで開始します（被験者 {participantId}, 条件 {condition}）";

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
