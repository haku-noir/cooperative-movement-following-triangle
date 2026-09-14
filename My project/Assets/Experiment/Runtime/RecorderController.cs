using System;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 記録モードの進行役（仕様書 §3.1）。
    ///
    /// 流れ
    ///   Idle          → 確定操作で キャリブレーション開始
    ///   Calibrating   → 基準姿勢を calibrationHoldSeconds 保持（§5.1）
    ///   ReadyToRecord → 確定操作で 録画開始
    ///   Recording     → スケジュール（§5.4）に沿って収録、区間マーカーを打つ
    ///   Completed     → JSON を保存（§3.1）
    ///
    /// 進行の判断はすべて Core の <see cref="RecordingSession"/> にあり、
    /// このクラスは Unity のフレームループと表示への橋渡しだけを行う。
    /// Time.time を進行判断に使わないのは、時刻基準をサンプルの
    /// OVRPlugin.GetTimeInSeconds() に一本化するため（§3.1, §7.3）。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecorderController : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("IVertexSource を実装した MonoBehaviour。実機では OvrVertexSource。")]
        private MonoBehaviour vertexSourceBehaviour;

        [SerializeField]
        [Tooltip("IRecorderInput を実装した MonoBehaviour。")]
        private MonoBehaviour recorderInputBehaviour;

        [SerializeField]
        [Tooltip("IRecenterMonitor を実装した MonoBehaviour（実機では XrRuntimeConfig）。" +
                 "未設定でも動作するが、再センタリング検出が無効になる。")]
        private MonoBehaviour recenterMonitorBehaviour;

        [SerializeField]
        [Tooltip("演者 A に見せる自分の三角形。settings.ShowPerformerTriangle が false なら使わない。")]
        private TriangleView performerTriangleView;

        [SerializeField] private RecorderHud hud;

        [Header("録画のメタデータ")]
        [SerializeField] private string performerId = "A01";
        [SerializeField] private string recordingId = "take1";
        [SerializeField] private string notes = "";

        [Header("実機情報 (未設定なら不明として記録)")]
        [SerializeField]
        [Tooltip("XrRuntimeConfig を実装した MonoBehaviour。リフレッシュレートや SDK " +
                 "バージョンをメタデータに載せるために使う。")]
        private MonoBehaviour xrRuntimeInfoBehaviour;

        private IVertexSource vertexSource;
        private IRecorderInput input;
        private IRecenterMonitor recenterMonitor;
        private IXrRuntimeInfo xrRuntimeInfo;
        private RecordingSession session;

        /// <summary>最後に保存した録画のパス。エディタ検証で参照する。</summary>
        public string LastSavedPath { get; private set; }

        /// <summary>最後に完成した録画。保存せずに中身を検証したいとき用。</summary>
        public RecordingFile LastRecording { get; private set; }

        public RecorderState State => session?.State ?? RecorderState.Idle;

        private void Awake()
        {
            vertexSource = vertexSourceBehaviour as IVertexSource;
            input = recorderInputBehaviour as IRecorderInput;
            recenterMonitor = recenterMonitorBehaviour as IRecenterMonitor;
            xrRuntimeInfo = xrRuntimeInfoBehaviour as IXrRuntimeInfo;

            if (settings == null || vertexSource == null || input == null)
            {
                Debug.LogError(
                    $"[{nameof(RecorderController)}] settings / vertexSource / input のいずれかが " +
                    "未設定です。記録モードを無効にします。", this);
                enabled = false;
                return;
            }

            session = new RecordingSession(settings);

            if (recenterMonitor != null) recenterMonitor.Recentered += OnRecentered;

            if (performerTriangleView != null)
            {
                // 演者に見せる三角形は刺激ではないが、実験モードと同じ見え方にしておく。
                // 記録時と実験時で頂点球の大きさが違うと、演者の振幅感覚がずれる。
                performerTriangleView.Configure(ConditionVisuals.For(Condition.C3, settings));
                performerTriangleView.SetVisible(settings.ShowPerformerTriangle);
            }

            ShowMessage("確定キーでキャリブレーションを開始します");
        }

        private void OnDestroy()
        {
            if (recenterMonitor != null) recenterMonitor.Recentered -= OnRecentered;
        }

        private void Update()
        {
            if (!vertexSource.TryGetSample(out var sample))
            {
                UpdatePerformerTriangle(hasSample: false, sample);
                SetWarning("トラッキングが取得できていません");
                return;
            }

            UpdatePerformerTriangle(hasSample: true, sample);
            UpdateTrackingWarning(sample);

            if (input.AbortPressedThisFrame && session.State != RecorderState.Idle)
            {
                Abort();
                return;
            }

            switch (session.State)
            {
                case RecorderState.Idle:
                    if (input.ConfirmPressedThisFrame) StartCalibration();
                    break;

                case RecorderState.Calibrating:
                    TickCalibration(sample);
                    break;

                case RecorderState.ReadyToRecord:
                    if (input.ConfirmPressedThisFrame) StartRecording();
                    break;

                case RecorderState.Recording:
                    TickRecording(sample);
                    break;

                case RecorderState.Completed:
                    if (input.ConfirmPressedThisFrame) ResetToIdle();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // キャリブレーション（§5.1）
        // ------------------------------------------------------------------

        private void StartCalibration()
        {
            session.BeginCalibration();
            ShowMessage("基準姿勢：両手を前方に開いて静止してください");
        }

        private void TickCalibration(in BodyTriangleSample sample)
        {
            session.AddCalibrationSample(sample);

            double held = session.CalibrationHoldSeconds;
            float required = settings.CalibrationHoldSeconds;

            if (held < required)
            {
                ShowMessage(
                    $"基準姿勢を保持してください  {Math.Max(0.0, required - held):F1} s\n" +
                    $"（有効サンプル {session.CalibrationSampleCount}）");
                return;
            }

            if (!session.CompleteCalibration(out string error))
            {
                Debug.LogError($"[{nameof(RecorderController)}] {error}", this);
                ShowMessage($"キャリブレーション失敗\n{error}\n確定キーでやり直します");
                session.BeginCalibration();
                return;
            }

            var c = session.Calibration;
            Debug.Log(
                $"[{nameof(RecorderController)}] キャリブレーション完了: " +
                $"h={c.eyeHeightMeters:F3} m, L_left={c.neckToLeftHandMeters:F3} m, " +
                $"L_right={c.neckToRightHandMeters:F3} m, L={c.characteristicLength:F3} m " +
                $"(sd={c.characteristicLengthStdDev * 1000f:F1} mm, n={c.sampleCount}), " +
                $"d/h={settings.NeckOffsetD / c.eyeHeightMeters:F4}");

            ShowMessage(
                $"キャリブレーション完了\n" +
                $"眼高 {c.eyeHeightMeters:F2} m / 体格指標 {c.characteristicLength:F2} m\n" +
                $"確定キーで録画を開始します");
        }

        // ------------------------------------------------------------------
        // 録画（§3.1, §5.4）
        // ------------------------------------------------------------------

        private void StartRecording()
        {
            if (!session.BeginRecording(out string error))
            {
                Debug.LogError($"[{nameof(RecorderController)}] {error}", this);
                ShowMessage($"録画を開始できません\n{error}");
                return;
            }

            recenterMonitor?.ResetPerTrialState();
            Debug.Log($"[{nameof(RecorderController)}] 録画開始 (総時間 {session.Schedule.TotalSeconds:F1} s)");
        }

        private void TickRecording(in BodyTriangleSample sample)
        {
            if (settings.SegmentMarkerMode == SegmentMarkerMode.Controller
                && input.MarkerPressedThisFrame)
            {
                if (!session.AddManualMarker(sample.timestampSeconds, out string markerError))
                {
                    Debug.LogWarning($"[{nameof(RecorderController)}] {markerError}", this);
                }
            }

            bool finished = session.AddFrame(sample);

            ShowMessage(BuildRecordingMessage());

            if (finished) CompleteRecording();
        }

        private string BuildRecordingMessage()
        {
            double elapsed = session.ElapsedSeconds;
            double total = session.Schedule.TotalSeconds;

            if (!session.HasCurrentPhase)
            {
                return $"録画中  {elapsed:F1} / {total:F1} s";
            }

            var phase = session.CurrentPhase;
            double remaining = Math.Max(0.0, phase.EndSeconds - elapsed);

            string manualHint = settings.SegmentMarkerMode == SegmentMarkerMode.Controller
                ? "\n（区間の切れ目でマーカーボタンを押してください）"
                : "";

            return
                $"{phase.Instruction}\n" +
                $"残り {remaining:F1} s   /   全体 {elapsed:F1} / {total:F1} s" +
                manualHint;
        }

        private void CompleteRecording()
        {
            var environment = BuildEnvironment();
            var file = session.Complete(environment);
            LastRecording = file;

            if (!RecordingStorage.TrySave(file, settings, out string path, out string error))
            {
                Debug.LogError($"[{nameof(RecorderController)}] {error}", this);
                ShowMessage($"保存に失敗しました\n{error}");
                return;
            }

            LastSavedPath = path;
            Debug.Log(
                $"[{nameof(RecorderController)}] 録画を保存しました: {path}\n" +
                RecordingSerializer.Summarize(file));

            string validity = file.metadata.valid
                ? "保存しました"
                : $"保存しましたが無効フラグ付きです\n{file.metadata.invalidReason}";

            ShowMessage($"{validity}\n{System.IO.Path.GetFileName(path)}\n確定キーで次のテイクへ");
        }

        private RecordingEnvironment BuildEnvironment()
        {
            return new RecordingEnvironment
            {
                recordingId = recordingId,
                performerId = performerId,
                createdAtIso8601 = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                notes = notes,
                vertexSourceKind = vertexSourceBehaviour != null
                    ? vertexSourceBehaviour.GetType().Name
                    : "unknown",
                sdkVersion = xrRuntimeInfo?.SdkVersion ?? "unknown",
                unityVersion = Application.unityVersion,
                requestedDisplayFrequencyHz = xrRuntimeInfo?.RequestedDisplayFrequencyHz ?? 0f,
                effectiveDisplayFrequencyHz = xrRuntimeInfo?.EffectiveDisplayFrequencyHz ?? 0f,
                displayFrequencyApplied = xrRuntimeInfo?.DisplayFrequencyApplied ?? false,
                trackingOriginType = xrRuntimeInfo?.TrackingOriginType ?? "unknown",
                trackingOriginIsFloorLevel = xrRuntimeInfo?.TrackingOriginIsFloorLevel ?? false,
            };
        }

        private void Abort()
        {
            Debug.LogWarning($"[{nameof(RecorderController)}] 中断しました (state={session.State})。");
            ResetToIdle();
        }

        private void ResetToIdle()
        {
            session = new RecordingSession(settings);
            ShowMessage("確定キーでキャリブレーションを開始します");
            SetWarning(null);
        }

        private void OnRecentered(double timestamp)
        {
            if (session.State != RecorderState.Recording) return;

            session.RecenterCount = recenterMonitor.RecenterCount;
            Debug.LogError(
                $"[{nameof(RecorderController)}] 録画中に再センタリングが発生しました " +
                $"(t={timestamp:F3} s)。この録画は刺激として使用できません。");
        }

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------

        private void UpdatePerformerTriangle(bool hasSample, in BodyTriangleSample sample)
        {
            if (performerTriangleView == null) return;

            if (!settings.ShowPerformerTriangle || !hasSample)
            {
                performerTriangleView.SetVisible(false);
                return;
            }

            performerTriangleView.SetVisible(true);
            performerTriangleView.SetVertices(sample.v0, sample.v1, sample.v2);
        }

        /// <summary>
        /// 仕様書 §7.1：手がカメラ視野外に出ると外挿が始まる。
        /// 収録後に低信頼区間が判明するより、その場で気づいて撮り直せるほうが安全なので、
        /// 記録モードでは演者に警告を出す（実験モードでは被験者への干渉になるため出さない）。
        /// </summary>
        private void UpdateTrackingWarning(in BodyTriangleSample sample)
        {
            if (!settings.ShowPerformerTrackingWarning)
            {
                SetWarning(null);
                return;
            }

            bool leftLow = !sample.leftHand.isTracked
                           || sample.leftHand.confidence != HandConfidence.High;
            bool rightLow = !sample.rightHand.isTracked
                            || sample.rightHand.confidence != HandConfidence.High;

            if (!leftLow && !rightLow)
            {
                SetWarning(null);
                return;
            }

            string which = leftLow && rightLow ? "両手" : leftLow ? "左手" : "右手";
            SetWarning($"{which}のトラッキングが不安定です。手を視野内に戻してください");
        }

        private void ShowMessage(string message)
        {
            if (hud != null) hud.SetMessage(message);
        }

        private void SetWarning(string message)
        {
            if (hud != null) hud.SetWarning(message);
        }
    }
}
