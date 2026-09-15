using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 試行ログの収集と書き出し（仕様書 §6）。
    ///
    /// 毎フレームの誤差量を計算してメモリへ溜め、試行終了時に CSV として一括書き出しする。
    /// 毎フレーム同期書き込みをすると GC とストールでフレームレートが落ち、
    /// それ自体が追従誤差になる。93 s x 90 Hz でも 1 万行に満たないので、
    /// メモリに持てる量として問題ない。
    ///
    /// **このクラスは閾値判定を一切行わない**（§6.3）。一致判定・継続長・破綻回数・
    /// 復帰時間はすべて後処理の仕事であり、ここは生の量を出すところまでを担当する。
    /// </summary>
    [DisallowMultipleComponent]
    public class TrialLogger : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        private readonly List<TrialLogRow> rows = new List<TrialLogRow>();
        private TrialLogHeader header;
        private float normalizationLength;
        private double lastTimestamp;
        private double firstTimestamp;
        private double maxInterval;
        private int pendingRecenterFlag;

        public bool IsRecording { get; private set; }

        public int RowCount => rows.Count;

        public string LastSavedPath { get; private set; }

        /// <summary>直近フレームの誤差量。デバッグ表示や検証に使う。</summary>
        public FrameMetrics LatestMetrics { get; private set; }

        public void SetSettings(ExperimentSettings value) => settings = value;

        /// <summary>
        /// 試行の記録を開始する。
        /// </summary>
        /// <param name="trialHeader">
        /// CSV ヘッダに書くメタデータ。フレームレート統計と再センタリング回数は
        /// 記録終了時にこちらで埋めるので、呼び出し側で設定する必要はない。
        /// </param>
        /// <param name="normalizationLengthMeters">
        /// 誤差の正規化分母 [m]。キャリブレーション時に確定した B の体格指標。
        /// 毎フレーム再計算した値を渡してはならない（§5.2 の趣旨）。
        /// </param>
        public void BeginTrial(TrialLogHeader trialHeader, float normalizationLengthMeters)
        {
            header = trialHeader ?? throw new ArgumentNullException(nameof(trialHeader));
            normalizationLength = normalizationLengthMeters;

            rows.Clear();

            int expected = settings != null
                ? (int)(settings.TrialDurationSeconds * settings.TargetDisplayFrequencyHz) + 256
                : 16384;
            if (rows.Capacity < expected) rows.Capacity = expected;

            firstTimestamp = 0.0;
            lastTimestamp = 0.0;
            maxInterval = 0.0;
            pendingRecenterFlag = 0;
            IsRecording = true;
        }

        /// <summary>
        /// 再センタリングの発生をこのフレームのログに載せる（§1）。
        /// XrRuntimeConfig のイベントから呼ぶ。
        /// </summary>
        public void NotifyRecenter()
        {
            pendingRecenterFlag = 1;
            if (header != null) header.recenterCount++;
        }

        /// <summary>
        /// 1 フレーム分を記録する。
        /// </summary>
        /// <param name="elapsedSeconds">試行開始からの経過時刻 [s]。</param>
        /// <param name="phaseMarker">フェーズ識別子（§5.4 の区間マーカー）。</param>
        /// <param name="transformedA">
        /// 体格正規化と Registration を適用した後の A のサンプル。
        /// CSV の A 側座標列は変換後の値を出す。
        /// </param>
        /// <param name="participantB">被験者 B のサンプル。</param>
        public void Record(
            double elapsedSeconds, string phaseMarker,
            in BodyTriangleSample transformedA, in BodyTriangleSample participantB)
        {
            if (!IsRecording) return;

            if (rows.Count == 0)
            {
                firstTimestamp = elapsedSeconds;
            }
            else
            {
                double interval = elapsedSeconds - lastTimestamp;
                if (interval > maxInterval) maxInterval = interval;
            }

            lastTimestamp = elapsedSeconds;

            var metrics = FrameMetrics.Compute(transformedA, participantB, normalizationLength);
            LatestMetrics = metrics;

            rows.Add(new TrialLogRow
            {
                t = elapsedSeconds,
                phaseMarker = phaseMarker,

                headAPosition = transformedA.headPosition,
                headARotation = transformedA.headRotation,
                headBPosition = participantB.headPosition,
                headBRotation = participantB.headRotation,

                v0A = transformedA.v0,
                v1A = transformedA.v1,
                v2A = transformedA.v2,
                v0B = participantB.v0,
                v1B = participantB.v1,
                v2B = participantB.v2,

                metrics = metrics,

                handLeftTracked = participantB.leftHand.isTracked,
                handLeftConfidence = participantB.leftHand.ConfidenceNumeric,
                handRightTracked = participantB.rightHand.isTracked,
                handRightConfidence = participantB.rightHand.ConfidenceNumeric,

                recenterFlag = pendingRecenterFlag,
            });

            pendingRecenterFlag = 0;
        }

        /// <summary>試行を締めて CSV を書き出す。</summary>
        public bool EndTrial(out string savedPath, out string error)
        {
            savedPath = null;
            IsRecording = false;

            if (header == null || rows.Count == 0)
            {
                error = "記録された行がありません。";
                return false;
            }

            double duration = lastTimestamp - firstTimestamp;
            header.meanFrameRateHz = duration > 0.0 ? (rows.Count - 1) / duration : 0.0;
            header.maxFrameIntervalSeconds = maxInterval;

            if (header.recenterCount > 0)
            {
                // アプリ側でファイルを捨てることはしない。判断材料を残して実験者に委ねる。
                header.trialValid = false;
                header.invalidReason =
                    $"試行中に再センタリングが {header.recenterCount} 回発生しました (§1)。";
            }

            try
            {
                string directory = DirectoryFor(settings);
                Directory.CreateDirectory(directory);

                string path = MakeUnique(Path.Combine(directory, BuildFileName(header)));
                string csv = TrialCsvWriter.Write(header, rows);

                // UTF-8 BOM なし。解析側（Python 等）が素直に読めるようにするため。
                File.WriteAllText(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                savedPath = path;
                LastSavedPath = path;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"CSV の書き出しに失敗しました: {exception.Message}";
                return false;
            }
        }

        public static string DirectoryFor(ExperimentSettings settings)
        {
            string name = settings != null ? settings.LogsDirectoryName : "logs";
            return Path.Combine(Application.persistentDataPath, name);
        }

        public static string BuildFileName(TrialLogHeader header)
        {
            string participant = Sanitize(header.participantId, "participant");
            string condition = Sanitize(header.conditionId, "cond");
            string stimulus = Sanitize(header.stimulusId, "stim");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string validity = header.trialValid ? "" : "_INVALID";

            return $"{stamp}_{participant}_trial{header.trialIndex:D2}_" +
                   $"{condition}_{stimulus}{validity}.csv";
        }

        private static string MakeUnique(string path)
        {
            if (!File.Exists(path)) return path;

            string directory = Path.GetDirectoryName(path) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
                if (!File.Exists(candidate)) return candidate;
            }

            throw new IOException($"重複しないファイル名を作れません: {path}");
        }

        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            }

            return builder.ToString();
        }
    }
}
