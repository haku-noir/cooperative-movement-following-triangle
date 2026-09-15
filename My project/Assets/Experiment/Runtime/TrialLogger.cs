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

        /// <summary>
        /// 基準姿勢フェーズで取り込んだ生サンプル。
        ///
        /// この時点では Registration がまだ確定しておらず A 側の座標が存在しないため、
        /// 行に組み立てられない。確定後に遡って変換を適用する。
        /// </summary>
        public readonly struct BaselineCapture
        {
            public readonly double ElapsedSeconds;
            public readonly BodyTriangleSample Sample;
            public readonly int RecenterFlag;

            public BaselineCapture(double elapsedSeconds, in BodyTriangleSample sample, int recenterFlag)
            {
                ElapsedSeconds = elapsedSeconds;
                Sample = sample;
                RecenterFlag = recenterFlag;
            }
        }

        private readonly List<TrialLogRow> rows = new List<TrialLogRow>();
        private readonly List<BaselineCapture> baselineCaptures = new List<BaselineCapture>();
        private TrialLogHeader header;
        private float normalizationLength;
        private double lastTimestamp;
        private double firstTimestamp;
        private double maxInterval;
        private int pendingRecenterFlag;
        private int baselineRecenterCount;

        public bool IsRecording { get; private set; }

        public int RowCount => rows.Count;

        public string LastSavedPath { get; private set; }

        /// <summary>基準姿勢ログの保存先。<see cref="WriteBaselineLog"/> の後に有効。</summary>
        public string LastBaselineLogPath { get; private set; }

        public int BaselineCaptureCount => baselineCaptures.Count;

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

            // 基準姿勢フェーズ中はまだ試行ヘッダが存在しない。そこで起きた再センタリングは
            // Registration そのものを無効にするので、別に数えて基準姿勢ログへ載せる。
            if (IsRecording && header != null) header.recenterCount++;
            else baselineRecenterCount++;
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

            var row = BuildRow(
                elapsedSeconds, phaseMarker, transformedA, participantB,
                normalizationLength, pendingRecenterFlag);

            LatestMetrics = row.metrics;
            rows.Add(row);

            pendingRecenterFlag = 0;
        }

        /// <summary>
        /// 行を組み立てる。試行ログと基準姿勢ログで同じ経路を通す。
        /// 2 つのログの列の意味がずれないよう、組み立ては 1 箇所に閉じておく。
        /// </summary>
        private static TrialLogRow BuildRow(
            double elapsedSeconds, string phaseMarker,
            in BodyTriangleSample transformedA, in BodyTriangleSample participantB,
            float normalizationLength, int recenterFlag)
        {
            return new TrialLogRow
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

                metrics = FrameMetrics.Compute(transformedA, participantB, normalizationLength),

                handLeftTracked = participantB.leftHand.isTracked,
                handLeftConfidence = participantB.leftHand.ConfidenceNumeric,
                handRightTracked = participantB.rightHand.isTracked,
                handRightConfidence = participantB.rightHand.ConfidenceNumeric,

                recenterFlag = recenterFlag,
            };
        }

        // ------------------------------------------------------------------
        // 基準姿勢フェーズのログ（§5.3）
        // ------------------------------------------------------------------

        /// <summary>
        /// 基準姿勢フェーズの取り込みを開始する。試行の開始時に呼ぶ。
        /// </summary>
        public void BeginBaselineCapture()
        {
            baselineCaptures.Clear();
            baselineRecenterCount = 0;
            pendingRecenterFlag = 0;
            LastBaselineLogPath = null;
        }

        /// <summary>
        /// 基準姿勢フェーズの 1 フレームを取り込む。
        ///
        /// 低信頼のフレームも捨てずに取り込む。Registration の平均に採用されたかどうかは
        /// hand_L_conf / hand_R_conf 列から後処理で厳密に判別できるので、
        /// 採用フラグを別に持つ必要はない。捨ててしまうと「何フレーム落ちたのか」が
        /// 分からなくなる。
        /// </summary>
        public void CaptureBaselineSample(double elapsedSeconds, in BodyTriangleSample participantB)
        {
            baselineCaptures.Add(new BaselineCapture(elapsedSeconds, participantB, pendingRecenterFlag));
            pendingRecenterFlag = 0;
        }

        /// <summary>
        /// 基準姿勢フェーズのログを別ファイルとして書き出す。
        ///
        /// 試行ログとは別ファイルにする理由：基準姿勢は追従課題ではない。
        /// A の三角形はまだ見えておらず、この区間の「誤差」は追従成績ではなく
        /// Registration の当てはまり具合を表す量である。同じファイルに混ぜると、
        /// 後処理で取り違えて課題成績に数えてしまう余地が残る。
        ///
        /// 列構成は試行ログと完全に同一にしてある。同じ読み込みコードで扱えるほうが
        /// 解析側の間違いが減る。区別はファイル名と、ヘッダの log_kind で行う。
        /// </summary>
        /// <param name="baselineHeader">
        /// ヘッダ。log_kind は呼び出し側で "registration" にしておくこと。
        /// </param>
        /// <param name="normalizationLengthMeters">誤差の正規化分母 [m]。試行ログと同じ値。</param>
        /// <param name="transformedAAt">
        /// 経過時刻から、変換適用後の A のサンプルを返す関数。
        /// Registration が確定してから呼ぶこと。
        /// </param>
        public bool WriteBaselineLog(
            TrialLogHeader baselineHeader, float normalizationLengthMeters,
            Func<double, BodyTriangleSample> transformedAAt,
            out string savedPath, out string error)
        {
            savedPath = null;

            if (baselineHeader == null)
            {
                error = "ヘッダがありません。";
                return false;
            }

            if (baselineCaptures.Count == 0)
            {
                error = "基準姿勢のサンプルがありません。";
                return false;
            }

            if (transformedAAt == null)
            {
                error = "A のサンプル取得関数が未指定です。";
                return false;
            }

            var baselineRows = new List<TrialLogRow>(baselineCaptures.Count);
            double maxBaselineInterval = 0.0;

            for (int i = 0; i < baselineCaptures.Count; i++)
            {
                var capture = baselineCaptures[i];

                if (i > 0)
                {
                    double interval = capture.ElapsedSeconds - baselineCaptures[i - 1].ElapsedSeconds;
                    if (interval > maxBaselineInterval) maxBaselineInterval = interval;
                }

                baselineRows.Add(BuildRow(
                    capture.ElapsedSeconds, RecordingSchedule.BaselinePhaseId,
                    transformedAAt(capture.ElapsedSeconds), capture.Sample,
                    normalizationLengthMeters, capture.RecenterFlag));
            }

            double duration = baselineCaptures[baselineCaptures.Count - 1].ElapsedSeconds
                              - baselineCaptures[0].ElapsedSeconds;

            baselineHeader.meanFrameRateHz =
                duration > 0.0 ? (baselineRows.Count - 1) / duration : 0.0;
            baselineHeader.maxFrameIntervalSeconds = maxBaselineInterval;
            baselineHeader.recenterCount = baselineRecenterCount;

            if (baselineRecenterCount > 0)
            {
                // 基準姿勢中にワールド原点が動いたということは、蓄積した B の三角形が
                // 途中で別の座標系のものに入れ替わっている。この Registration は使えない。
                baselineHeader.trialValid = false;
                baselineHeader.invalidReason =
                    $"基準姿勢中に再センタリングが {baselineRecenterCount} 回発生しました (§1)。" +
                    "この Registration は無効です。";
            }

            try
            {
                string directory = DirectoryFor(settings);
                Directory.CreateDirectory(directory);

                string path = MakeUnique(
                    Path.Combine(directory, BuildFileName(baselineHeader, "_registration")));

                File.WriteAllText(
                    path, TrialCsvWriter.Write(baselineHeader, baselineRows),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                savedPath = path;
                LastBaselineLogPath = path;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"基準姿勢ログの書き出しに失敗しました: {exception.Message}";
                return false;
            }
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

                string path = MakeUnique(Path.Combine(directory, BuildFileName(header, "")));
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

        public static string BuildFileName(TrialLogHeader header, string suffix)
        {
            string participant = Sanitize(header.participantId, "participant");
            string condition = Sanitize(header.conditionId, "cond");
            string stimulus = Sanitize(header.stimulusId, "stim");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string validity = header.trialValid ? "" : "_INVALID";

            return $"{stamp}_{participant}_trial{header.trialIndex:D2}_" +
                   $"{condition}_{stimulus}{suffix}{validity}.csv";
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
