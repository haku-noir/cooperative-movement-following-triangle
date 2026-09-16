using System.Text;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 合成録画の生成（仕様書 §8.2）。
    ///
    /// 実機で演者を撮らなくても、刺激 3 本が揃った状態を作れる。
    /// 生成物は実機の録画とまったく同じ形式なので、StimulusCatalog からも
    /// 通常の刺激として解決され、実験モードをそのまま最後まで通せる。
    ///
    /// 3 本は位相と周波数だけを変えて別物にしてある。
    /// 運動の種類（区間ごとの複合／並進のみ／手のみ）は仕様書 §5.4 のとおり共通。
    /// </summary>
    public static class SyntheticRecordingGenerator
    {
        private const float SampleRateHz = 90f;

        [MenuItem("Following Triangle/Generate Synthetic Stimuli (take1-3)")]
        public static void GenerateThreeStimuli()
        {
            var settings = SceneBuildUtility.EnsureSettingsAsset();
            if (settings == null)
            {
                Debug.LogError("[SyntheticRecordingGenerator] ExperimentSettings を用意できません。");
                return;
            }

            GenerateThreeStimuli(settings, "take");
        }

        /// <summary>
        /// 指定した設定とスケジュールに合わせて刺激 3 本を生成する。
        ///
        /// 刺激は設定のスケジュールに一致していなければならない。区間長を変えたのに
        /// 刺激を作り直さないと、区間2（並進のみ）のつもりの時間帯に
        /// 別の運動が再生されることになる。
        /// </summary>
        /// <param name="idPrefix">刺激 ID の接頭辞。"take" なら take1/take2/take3。</param>
        public static bool GenerateThreeStimuli(ExperimentSettings settings, string idPrefix)
        {
            var schedule = RecordingSchedule.FromSettings(settings);
            if (!schedule.Validate(out string scheduleError))
            {
                Debug.LogError($"[SyntheticRecordingGenerator] スケジュールが不正です: {scheduleError}");
                return false;
            }

            var report = new StringBuilder("[SyntheticRecordingGenerator] 合成刺激を生成しました\n");
            bool allSucceeded = true;

            for (int i = 0; i < GraecoLatinSquare.Order; i++)
            {
                var parameters = SyntheticMotionParameters.Default;
                parameters.neckOffsetD = settings.NeckOffsetD;

                // 刺激ごとに位相と周波数をずらして別物にする。
                // 運動の振幅は変えない。刺激間で難易度が変わると条件効果と交絡する。
                parameters.phaseOffsetRadians = i * (2f * Mathf.PI / 3f);
                parameters.headTranslationFrequencyHz = 0.25f + 0.03f * i;
                parameters.handFrequencyHz = 0.35f - 0.04f * i;

                var motion = new SyntheticMotion(parameters, schedule);
                string recordingId = $"{idPrefix}{i + 1}";

                var file = SyntheticRecordingBuilder.Build(
                    motion, SampleRateHz, recordingId,
                    startTimestamp: 1000.0 + i * 500.0);

                if (!RecordingStorage.TrySave(file, settings, out string path, out string error))
                {
                    Debug.LogError($"[SyntheticRecordingGenerator] {recordingId}: {error}");
                    allSucceeded = false;
                    continue;
                }

                report.AppendLine(
                    $"  {recordingId}: {file.frames.Count} フレーム / " +
                    $"{file.segmentMarkers.Count} マーカー / " +
                    $"L = {motion.RestCharacteristicLength:F4} m\n    {path}");
            }

            report.AppendLine(
                $"  刺激 ID: {idPrefix}1 / {idPrefix}2 / {idPrefix}3" +
                $"  総時間 {schedule.TotalSeconds:F0} s" +
                $"  保存先 {RecordingStorage.DirectoryFor(settings)}");

            if (allSucceeded) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());

            return allSucceeded;
        }

        /// <summary>
        /// 生成済みの合成刺激に対して、パイプライン全体の解析的な自己検査を行う。
        ///
        /// B を A から既知の量だけずらした状態を作り、
        /// Registration → 誤差算出 の結果が閉形式の期待値と一致するかを確認する。
        /// 単体テストと同じ内容だが、実験者が「今のビルドで」確認できるようにしてある。
        /// </summary>
        [MenuItem("Following Triangle/Run Pipeline Self-Check")]
        public static void RunSelfCheck()
        {
            var settings = SceneBuildUtility.EnsureSettingsAsset();
            var schedule = RecordingSchedule.FromSettings(settings);

            var parameters = SyntheticMotionParameters.Default;
            parameters.neckOffsetD = settings.NeckOffsetD;

            var motion = new SyntheticMotion(parameters, schedule);
            var recording = SyntheticRecordingBuilder.Build(motion, SampleRateHz, "selfcheck");

            var playback = new RecordingPlayback(
                recording, settings.RecomputeNeckVertexOnPlayback, settings.NeckOffsetD);

            // B は A と同じ体格・同じ基準姿勢。Registration は恒等変換になるはず。
            var referenceA = playback.BaselineAverage(
                recording.metadata.baselineHoldSeconds, out int referenceSamples);

            var profileB = ParticipantProfile.Create(
                "SELFCHECK-1", recording.metadata.calibration, settings.NeckOffsetD);

            var transform = Registration.Solve(
                settings.RegistrationMethod, referenceA, referenceA,
                recording.metadata.calibration, profileB.calibration);

            float registrationResidual = Registration.ResidualRms(transform, referenceA, referenceA);

            // 既知のずれ。追従中の B が A からこれだけ離れている状況を作る。
            var offset = new Vector3(0.1f, 0f, 0f);

            double probeTime = schedule.Phases[2].StartSeconds + 5.0;
            var a = transform.Apply(playback.SampleAt(probeTime));

            var b = a;
            b.headPosition += offset;
            b.v0 += offset;
            b.v1 += offset;
            b.v2 += offset;

            float normalization = profileB.calibration.characteristicLength;
            var metrics = FrameMetrics.Compute(a, b, normalization);

            var report = new StringBuilder("[Pipeline Self-Check]\n");
            report.AppendLine($"  刺激: {recording.frames.Count} フレーム, " +
                              $"基準姿勢 {referenceSamples} フレーム平均");
            report.AppendLine($"  L = {normalization:F4} m");
            report.AppendLine(
                $"  Registration ({settings.RegistrationMethod}): " +
                $"scale={transform.scale:F6} yaw={transform.yawDegrees:F4} deg " +
                $"残差 RMS={registrationResidual * 1000f:F4} mm");
            report.AppendLine($"  既知のずれ: {offset} (|offset| = {offset.magnitude:F4} m)");
            report.AppendLine(
                $"  dist_V0={metrics.distanceV0:F6}  dist_V1={metrics.distanceV1:F6}  " +
                $"dist_V2={metrics.distanceV2:F6}");
            report.AppendLine(
                $"  dist_sum={metrics.distanceSum:F6}  " +
                $"dist_sum_norm={metrics.distanceSumNormalized:F6}");
            report.AppendLine(
                $"  proc_trans={metrics.procrustesTranslation:F6}  " +
                $"proc_rot_deg={metrics.procrustesRotationDegrees:F4}  " +
                $"proc_residual={metrics.procrustesResidual:F6}");

            bool ok = true;
            ok &= Check(report, "Registration が恒等", registrationResidual, 0f, 1e-4f);
            ok &= Check(report, "dist_V0 = |offset|", metrics.distanceV0, offset.magnitude, 1e-4f);
            ok &= Check(report, "dist_sum = 3|offset|", metrics.distanceSum, 3f * offset.magnitude, 1e-4f);
            ok &= Check(report, "dist_sum_norm",
                metrics.distanceSumNormalized, 3f * offset.magnitude / normalization, 1e-4f);
            ok &= Check(report, "proc_trans = |offset|",
                metrics.procrustesTranslation, offset.magnitude, 1e-4f);
            ok &= Check(report, "proc_rot_deg = 0", metrics.procrustesRotationDegrees, 0f, 0.01f);
            ok &= Check(report, "proc_residual = 0", metrics.procrustesResidual, 0f, 1e-4f);

            if (ok) Debug.Log(report.ToString() + "\n  すべて期待値と一致しました。");
            else Debug.LogError(report.ToString() + "\n  期待値と一致しない項目があります。");
        }

        private static bool Check(
            StringBuilder report, string label, float actual, float expected, float tolerance)
        {
            bool ok = Mathf.Abs(actual - expected) <= tolerance;
            report.AppendLine(
                $"    [{(ok ? "OK" : "NG")}] {label}: 実測 {actual:F6} / 期待 {expected:F6}");
            return ok;
        }
    }
}
