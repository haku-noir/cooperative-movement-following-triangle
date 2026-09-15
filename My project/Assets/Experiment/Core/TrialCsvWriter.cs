using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 試行ログの CSV 生成（仕様書 §6.2）。1 試行 1 ファイル。
    ///
    /// 文字列生成だけを行い、ファイル入出力も Unity のシーン状態も触らない。
    /// 列の並びと数値の書式は解析スクリプトが直接依存する部分なので、
    /// 単体テストで固定できる形にしてある。
    ///
    /// 数値は必ず InvariantCulture で書く。このプロジェクトが動く環境のロケールは
    /// 日本語であり、将来ロケールによって小数点記号や桁区切りが変わると、
    /// 出力済みの CSV と後から出力した CSV が別物になる。
    /// </summary>
    public static class TrialCsvWriter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        /// 列定義（仕様書 §6.2）。
        ///
        /// 仕様書の一覧に対する追加は proc_rot_defined の 1 列のみ。
        /// 三角形が退化して回転成分が不定になった場合でも値は出し、
        /// 一意に決まったかどうかをこの列で示す。
        /// </summary>
        public const string ColumnHeader =
            "t,phase_marker," +
            "headA_px,headA_py,headA_pz,headA_qx,headA_qy,headA_qz,headA_qw," +
            "headB_px,headB_py,headB_pz,headB_qx,headB_qy,headB_qz,headB_qw," +
            "V0A_x,V0A_y,V0A_z,V1A_x,V1A_y,V1A_z,V2A_x,V2A_y,V2A_z," +
            "V0B_x,V0B_y,V0B_z,V1B_x,V1B_y,V1B_z,V2B_x,V2B_y,V2B_z," +
            "dist_V0,dist_V1,dist_V2,dist_sum,dist_sum_norm," +
            "proc_trans,proc_rot_deg,proc_residual,proc_residual_norm,proc_rot_defined," +
            "head_dir_residual_deg," +
            "tri_area_B,tri_min_angle_B," +
            "hand_L_tracked,hand_L_conf,hand_R_tracked,hand_R_conf," +
            "recenter_flag";

        private const string TimeFormat = "F6";
        private const string PositionFormat = "F6";
        private const string RotationFormat = "F6";
        private const string AngleFormat = "F4";
        private const string MetricFormat = "F6";

        public static string Write(TrialLogHeader header, IReadOnlyList<TrialLogRow> rows)
        {
            var builder = new StringBuilder(rows.Count * 400 + 4096);

            AppendHeaderComments(builder, header);
            builder.Append(ColumnHeader).Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                AppendRow(builder, rows[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// コメント行。# 始まりにしてあるので pandas.read_csv(comment='#') でそのまま読める。
        /// </summary>
        public static void AppendHeaderComments(StringBuilder builder, TrialLogHeader h)
        {
            Comment(builder, "format", "following-triangle-trial-log v1");

            // 追従課題のログか、基準姿勢のログか。列構成は同一なので中身で区別できるようにする。
            Comment(builder, "log_kind", h.logKind);
            Comment(builder, "recorded_at", h.recordedAtIso8601);

            Comment(builder, "participant_id", h.participantId);
            Comment(builder, "participant_number", h.participantNumber.ToString(Invariant));
            Comment(builder, "assignment_row", h.assignmentRow.ToString(Invariant));
            Comment(builder, "assignment_design", "graeco_latin_square_order3");
            Comment(builder, "condition", h.conditionId);
            Comment(builder, "stimulus_id", h.stimulusId);
            Comment(builder, "stimulus_catalog_complete", h.stimulusCatalogComplete ? "true" : "false");
            Comment(builder, "trial_index", h.trialIndex.ToString(Invariant));

            // 実際に提示した教示文そのもの（§7.4）。C1 と C2 の差はここだけ。
            Comment(builder, "instruction_text", SingleLine(h.instructionText));
            Comment(builder, "instruction_source", h.instructionSource);

            // §2.2 頂点定義
            Comment(builder, "neck_offset_d_m", h.neckOffsetD.ToString(PositionFormat, Invariant));
            Comment(builder, "neck_offset_d_over_h", h.normalizedNeckOffset.ToString("F6", Invariant));
            Comment(builder, "hand_vertex_bone", h.handVertexBone);
            Comment(builder, "recompute_neck_vertex_on_playback",
                h.recomputeNeckVertexOnPlayback ? "true" : "false");

            // §5.1 / §5.2 体格
            Comment(builder, "characteristic_length_A_m",
                h.characteristicLengthA.ToString(PositionFormat, Invariant));
            Comment(builder, "characteristic_length_B_m",
                h.characteristicLengthB.ToString(PositionFormat, Invariant));
            Comment(builder, "body_scale_factor", h.bodyScaleFactor.ToString("F6", Invariant));
            Comment(builder, "normalization_length_m",
                h.normalizationLength.ToString(PositionFormat, Invariant));
            Comment(builder, "normalization_source", "calibration_fixed");

            // §5.3 Registration。A 側の列は変換後なので、元に戻すためにここを使う。
            Comment(builder, "registration_method", h.registrationMethod);
            Comment(builder, "registration_residual_rms_m",
                h.registrationResidualRms.ToString(PositionFormat, Invariant));
            Comment(builder, "registration_residual_per_vertex_m",
                FormatVector(h.registrationResidualPerVertex));
            Comment(builder, "registration_reference_A_v0", FormatVector(h.referenceTriangleA.V0));
            Comment(builder, "registration_reference_A_v1", FormatVector(h.referenceTriangleA.V1));
            Comment(builder, "registration_reference_A_v2", FormatVector(h.referenceTriangleA.V2));
            Comment(builder, "registration_baseline_B_v0", FormatVector(h.baselineTriangleB.V0));
            Comment(builder, "registration_baseline_B_v1", FormatVector(h.baselineTriangleB.V1));
            Comment(builder, "registration_baseline_B_v2", FormatVector(h.baselineTriangleB.V2));
            Comment(builder, "baseline_frame_count", h.baselineFrameCount.ToString(Invariant));
            Comment(builder, "baseline_accepted_count", h.baselineAcceptedCount.ToString(Invariant));
            Comment(builder, "transform_scale", h.transform.scale.ToString("F6", Invariant));
            Comment(builder, "transform_scale_pivot", FormatVector(h.transform.scalePivot));
            Comment(builder, "transform_yaw_deg", h.transform.yawDegrees.ToString(AngleFormat, Invariant));
            Comment(builder, "transform_translation", FormatVector(h.transform.translation));
            Comment(builder, "a_columns_are", "post_transform");

            // §1 実行環境
            Comment(builder, "sdk_version", h.sdkVersion);
            Comment(builder, "unity_version", h.unityVersion);
            Comment(builder, "display_frequency_requested_hz",
                h.requestedDisplayFrequencyHz.ToString("F2", Invariant));
            Comment(builder, "display_frequency_effective_hz",
                h.effectiveDisplayFrequencyHz.ToString("F2", Invariant));
            Comment(builder, "display_frequency_applied", h.displayFrequencyApplied ? "true" : "false");
            Comment(builder, "tracking_origin", h.trackingOriginType);

            // §7.3 実効フレームレート
            Comment(builder, "mean_frame_rate_hz", h.meanFrameRateHz.ToString("F3", Invariant));
            Comment(builder, "max_frame_interval_s",
                h.maxFrameIntervalSeconds.ToString("F6", Invariant));

            // §1 再センタリング
            Comment(builder, "recenter_count", h.recenterCount.ToString(Invariant));
            Comment(builder, "trial_valid", h.trialValid ? "true" : "false");
            Comment(builder, "invalid_reason", h.invalidReason);

            // §5.4 スケジュール
            Comment(builder, "baseline_hold_s", h.baselineHoldSeconds.ToString("F3", Invariant));
            Comment(builder, "lead_in_s", h.leadInSeconds.ToString("F3", Invariant));
            Comment(builder, "segment_s", h.segmentSeconds.ToString("F3", Invariant));
            Comment(builder, "segment_count", h.segmentCount.ToString(Invariant));

            // 刺激の出自
            Comment(builder, "recording_id", h.recordingId);
            Comment(builder, "performer_id", h.performerId);
            Comment(builder, "recording_file", h.recordingFileName);
        }

        private static void AppendRow(StringBuilder b, in TrialLogRow row)
        {
            b.Append(row.t.ToString(TimeFormat, Invariant)).Append(',');
            b.Append(row.phaseMarker ?? "").Append(',');

            AppendPose(b, row.headAPosition, row.headARotation);
            AppendPose(b, row.headBPosition, row.headBRotation);

            AppendVector(b, row.v0A);
            AppendVector(b, row.v1A);
            AppendVector(b, row.v2A);
            AppendVector(b, row.v0B);
            AppendVector(b, row.v1B);
            AppendVector(b, row.v2B);

            var m = row.metrics;
            b.Append(m.distanceV0.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.distanceV1.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.distanceV2.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.distanceSum.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.distanceSumNormalized.ToString(MetricFormat, Invariant)).Append(',');

            b.Append(m.procrustesTranslation.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.procrustesRotationDegrees.ToString(AngleFormat, Invariant)).Append(',');
            b.Append(m.procrustesResidual.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.procrustesResidualNormalized.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.procrustesRotationWellDefined ? '1' : '0').Append(',');

            b.Append(m.headDirectionResidualDegrees.ToString(AngleFormat, Invariant)).Append(',');

            b.Append(m.triangleAreaB.ToString(MetricFormat, Invariant)).Append(',');
            b.Append(m.triangleMinAngleB.ToString(AngleFormat, Invariant)).Append(',');

            b.Append(row.handLeftTracked ? '1' : '0').Append(',');
            b.Append(row.handLeftConfidence.ToString(Invariant)).Append(',');
            b.Append(row.handRightTracked ? '1' : '0').Append(',');
            b.Append(row.handRightConfidence.ToString(Invariant)).Append(',');

            b.Append(row.recenterFlag.ToString(Invariant)).Append('\n');
        }

        private static void AppendPose(StringBuilder b, Vector3 position, Quaternion rotation)
        {
            b.Append(position.x.ToString(PositionFormat, Invariant)).Append(',');
            b.Append(position.y.ToString(PositionFormat, Invariant)).Append(',');
            b.Append(position.z.ToString(PositionFormat, Invariant)).Append(',');
            b.Append(rotation.x.ToString(RotationFormat, Invariant)).Append(',');
            b.Append(rotation.y.ToString(RotationFormat, Invariant)).Append(',');
            b.Append(rotation.z.ToString(RotationFormat, Invariant)).Append(',');
            b.Append(rotation.w.ToString(RotationFormat, Invariant)).Append(',');
        }

        private static void AppendVector(StringBuilder b, Vector3 v)
        {
            b.Append(v.x.ToString(PositionFormat, Invariant)).Append(',');
            b.Append(v.y.ToString(PositionFormat, Invariant)).Append(',');
            b.Append(v.z.ToString(PositionFormat, Invariant)).Append(',');
        }

        private static string FormatVector(Vector3 v) =>
            $"{v.x.ToString(PositionFormat, Invariant)} " +
            $"{v.y.ToString(PositionFormat, Invariant)} " +
            $"{v.z.ToString(PositionFormat, Invariant)}";

        private static void Comment(StringBuilder builder, string key, string value)
        {
            builder.Append("# ").Append(key).Append(": ").Append(value ?? "").Append('\n');
        }

        /// <summary>
        /// コメント行に入れる文字列から改行を落とす。
        /// 教示文が複数行だと、2 行目以降が # で始まらないコメント行になり、
        /// pandas がそれをデータ行として読もうとして壊れる。
        /// </summary>
        private static string SingleLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            return value.Replace("\r\n", " / ").Replace('\n', '/').Replace('\r', '/');
        }
    }
}
