using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 録画ファイルの JSON 入出力（仕様書 §3.1：可読性優先、1 ファイル 1 録画）。
    ///
    /// JsonUtility を使う。外部ライブラリを足さないのは、依存を増やさないためと、
    /// Unity の Vector3 / Quaternion がそのまま素直な JSON になるため。
    /// 整形出力すると 93 s の録画で 10 MB 前後になるが、
    /// 人が中身を確認できることを優先する（設定で切り替え可能）。
    /// </summary>
    public static class RecordingSerializer
    {
        public static string ToJson(RecordingFile file, bool prettyPrint)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            return JsonUtility.ToJson(file, prettyPrint);
        }

        /// <summary>
        /// JSON から録画を復元する。壊れたファイルや将来の形式を黙って読み込まない。
        /// 解析結果が静かに狂うより、読めないと言って止まるほうがよい。
        /// </summary>
        public static bool TryFromJson(string json, out RecordingFile file, out string error)
        {
            file = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON が空です。";
                return false;
            }

            RecordingFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<RecordingFile>(json);
            }
            catch (Exception exception)
            {
                error = $"JSON を解釈できません: {exception.Message}";
                return false;
            }

            if (parsed == null)
            {
                error = "JSON を解釈できません (null)。";
                return false;
            }

            if (parsed.metadata == null)
            {
                error = "metadata がありません。";
                return false;
            }

            if (parsed.metadata.formatVersion != RecordingMetadata.CurrentFormatVersion)
            {
                error = $"形式バージョンが {parsed.metadata.formatVersion} です " +
                        $"(このビルドが読めるのは {RecordingMetadata.CurrentFormatVersion})。";
                return false;
            }

            if (parsed.frames == null || parsed.frames.Count == 0)
            {
                error = "フレームが 1 つもありません。";
                return false;
            }

            // タイムスタンプが単調増加でない録画は、再生時の補間（§7.3）が破綻する。
            for (int i = 1; i < parsed.frames.Count; i++)
            {
                if (parsed.frames[i].t > parsed.frames[i - 1].t) continue;

                error = $"タイムスタンプが単調増加していません (frame {i}: " +
                        $"{parsed.frames[i - 1].t} -> {parsed.frames[i].t})。";
                return false;
            }

            file = parsed;
            error = null;
            return true;
        }

        /// <summary>
        /// 録画ファイルの要約。エディタのログや検証で中身を素早く確認するため。
        /// </summary>
        public static string Summarize(RecordingFile file)
        {
            if (file == null) return "(null)";

            var m = file.metadata;
            var c = m.calibration;

            return
                $"id={m.recordingId} performer={m.performerId} valid={m.valid}\n" +
                $"  frames={file.frames.Count} duration={m.recordedDurationSeconds:F2}s " +
                $"meanFps={m.meanFrameRateHz:F1} maxInterval={m.maxFrameIntervalSeconds * 1000.0:F1}ms\n" +
                $"  d={m.neckOffsetD:F3}m d/h={m.normalizedNeckOffset:F4} bone={m.handVertexBone}\n" +
                $"  calibration: h={c.eyeHeightMeters:F3}m L={c.characteristicLength:F3}m " +
                $"(sd={c.characteristicLengthStdDev * 1000f:F1}mm, n={c.sampleCount})\n" +
                $"  markers={file.segmentMarkers.Count} mode={m.segmentMarkerMode} " +
                $"recenter={m.recenterCount}" +
                (string.IsNullOrEmpty(m.invalidReason) ? "" : $"\n  invalid: {m.invalidReason}");
        }
    }
}
