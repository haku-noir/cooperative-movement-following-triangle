using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §6.2 の CSV 仕様。
    ///
    /// 列の並びと書式は解析スクリプトが直接依存する。黙って変わると、
    /// 後処理が別の列を読んだまま動いてしまい、結果だけが静かに狂う。
    /// </summary>
    public class TrialCsvWriterTests
    {
        private static TrialLogHeader SampleHeader() => new TrialLogHeader
        {
            participantId = "B01",
            conditionId = "C2",
            stimulusId = "take1",
            trialIndex = 3,
            recordedAtIso8601 = "2026-09-15T12:34:56+09:00",
            neckOffsetD = 0.15f,
            normalizedNeckOffset = 0.09375f,
            handVertexBone = "MiddleMcp",
            characteristicLengthA = 0.42f,
            characteristicLengthB = 0.48f,
            bodyScaleFactor = 1.142857f,
            normalizationLength = 0.48f,
            registrationMethod = "ThreeVertexLeastSquares",
            registrationResidualRms = 0.0031f,
            transform = new StimulusTransform
            {
                scale = 1.142857f,
                scalePivot = new Vector3(0f, 1.45f, 0f),
                yawDegrees = 12.5f,
                translation = new Vector3(0.1f, -0.02f, 0.3f),
            },
            sdkVersion = "1.205.0",
            unityVersion = "6000.3.24f1",
            requestedDisplayFrequencyHz = 90f,
            effectiveDisplayFrequencyHz = 90f,
            displayFrequencyApplied = true,
            trackingOriginType = "FloorLevel",
            baselineHoldSeconds = 3f,
            leadInSeconds = 10f,
            segmentSeconds = 20f,
            segmentCount = 4,
            recordingId = "take1",
            performerId = "A01",
            recordingFileName = "20260915-120000_A01_take1.json",
            recomputeNeckVertexOnPlayback = true,
        };

        private static TrialLogRow SampleRow(double t, string phase) => new TrialLogRow
        {
            t = t,
            phaseMarker = phase,
            headAPosition = new Vector3(0.1f, 1.6f, 0.2f),
            headARotation = Quaternion.Euler(0f, 10f, 0f),
            headBPosition = new Vector3(0.11f, 1.61f, 0.21f),
            headBRotation = Quaternion.Euler(0f, 12f, 0f),
            v0A = new Vector3(0.1f, 1.45f, 0.2f),
            v1A = new Vector3(-0.3f, 1.45f, 0.4f),
            v2A = new Vector3(0.5f, 1.45f, 0.4f),
            v0B = new Vector3(0.11f, 1.46f, 0.21f),
            v1B = new Vector3(-0.29f, 1.46f, 0.41f),
            v2B = new Vector3(0.51f, 1.46f, 0.41f),
            metrics = new FrameMetrics
            {
                distanceV0 = 0.017f,
                distanceV1 = 0.017f,
                distanceV2 = 0.017f,
                distanceSum = 0.051f,
                distanceSumNormalized = 0.10625f,
                procrustesTranslation = 0.017f,
                procrustesRotationDegrees = 1.5f,
                procrustesResidual = 0.0001f,
                procrustesResidualNormalized = 0.000208f,
                procrustesRotationWellDefined = true,
                headDirectionResidualDegrees = 2f,
                triangleAreaB = 0.08f,
                triangleMinAngleB = 26.57f,
            },
            handLeftTracked = true,
            handLeftConfidence = 1,
            handRightTracked = true,
            handRightConfidence = 1,
            recenterFlag = 0,
        };

        private static string[] DataLines(string csv) =>
            csv.Split('\n')
                .Where(line => line.Length > 0 && !line.StartsWith("#"))
                .ToArray();

        // ------------------------------------------------------------------

        /// <summary>仕様書 §6.2 の列一覧と順序をそのまま固定する。</summary>
        [Test]
        public void ColumnHeader_MatchesSpecOrder()
        {
            var columns = TrialCsvWriter.ColumnHeader.Split(',');

            var expected = new[]
            {
                "t", "phase_marker",
                "headA_px", "headA_py", "headA_pz", "headA_qx", "headA_qy", "headA_qz", "headA_qw",
                "headB_px", "headB_py", "headB_pz", "headB_qx", "headB_qy", "headB_qz", "headB_qw",
                "V0A_x", "V0A_y", "V0A_z", "V1A_x", "V1A_y", "V1A_z", "V2A_x", "V2A_y", "V2A_z",
                "V0B_x", "V0B_y", "V0B_z", "V1B_x", "V1B_y", "V1B_z", "V2B_x", "V2B_y", "V2B_z",
                "dist_V0", "dist_V1", "dist_V2", "dist_sum", "dist_sum_norm",
                "proc_trans", "proc_rot_deg", "proc_residual", "proc_residual_norm",
                "proc_rot_defined",
                "head_dir_residual_deg",
                "tri_area_B", "tri_min_angle_B",
                "hand_L_tracked", "hand_L_conf", "hand_R_tracked", "hand_R_conf",
                "recenter_flag",
            };

            Assert.That(columns, Is.EqualTo(expected));
        }

        [Test]
        public void EveryRowHasSameColumnCountAsHeader()
        {
            var rows = new List<TrialLogRow>
            {
                SampleRow(3.0, "lead_in"),
                SampleRow(13.0, "segment_1"),
                SampleRow(33.0, "segment_2"),
            };

            string csv = TrialCsvWriter.Write(SampleHeader(), rows);
            var lines = DataLines(csv);

            int expectedColumns = TrialCsvWriter.ColumnHeader.Split(',').Length;

            Assert.That(lines.Length, Is.EqualTo(rows.Count + 1), "列名行 + データ行");

            foreach (string line in lines)
            {
                Assert.That(line.Split(',').Length, Is.EqualTo(expectedColumns), line);
            }
        }

        /// <summary>
        /// 仕様書 §6.2 がヘッダに求める項目が揃っていること。
        /// 被験者ID・条件・刺激ID・d・d/h・スケール係数・SDK バージョン・実効リフレッシュレート。
        /// </summary>
        [Test]
        public void HeaderComments_ContainRequiredMetadata()
        {
            string csv = TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { SampleRow(3.0, "lead_in") });

            Assert.That(csv, Does.Contain("# participant_id: B01"));
            Assert.That(csv, Does.Contain("# condition: C2"));
            Assert.That(csv, Does.Contain("# stimulus_id: take1"));
            Assert.That(csv, Does.Contain("# neck_offset_d_m: 0.150000"));
            Assert.That(csv, Does.Contain("# neck_offset_d_over_h: 0.093750"));
            Assert.That(csv, Does.Contain("# body_scale_factor: 1.142857"));
            Assert.That(csv, Does.Contain("# sdk_version: 1.205.0"));
            Assert.That(csv, Does.Contain("# display_frequency_effective_hz: 90.00"));
        }

        /// <summary>
        /// A 側の座標列が変換後であることと、その変換の内容がヘッダに残っていること。
        /// これが無いと、A 側の生の録画座標を後処理で復元できない。
        /// </summary>
        [Test]
        public void HeaderComments_DocumentTheAppliedTransform()
        {
            string csv = TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { SampleRow(3.0, "lead_in") });

            Assert.That(csv, Does.Contain("# a_columns_are: post_transform"));
            Assert.That(csv, Does.Contain("# registration_method: ThreeVertexLeastSquares"));
            Assert.That(csv, Does.Contain("# transform_scale: 1.142857"));
            Assert.That(csv, Does.Contain("# transform_yaw_deg: 12.5000"));
            Assert.That(csv, Does.Contain("# transform_scale_pivot: 0.000000 1.450000 0.000000"));
            Assert.That(csv, Does.Contain("# transform_translation: 0.100000 -0.020000 0.300000"));
        }

        /// <summary>
        /// 正規化分母がキャリブレーション由来の固定値であることをヘッダに明記する。
        /// 後処理が「毎フレーム値で割り直す」ことのないように。
        /// </summary>
        [Test]
        public void HeaderComments_StateNormalizationSource()
        {
            string csv = TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { SampleRow(3.0, "lead_in") });

            Assert.That(csv, Does.Contain("# normalization_length_m: 0.480000"));
            Assert.That(csv, Does.Contain("# normalization_source: calibration_fixed"));
        }

        [Test]
        public void HeaderComments_AreAllPrefixedForPandas()
        {
            string csv = TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { SampleRow(3.0, "lead_in") });

            var lines = csv.Split('\n').Where(l => l.Length > 0).ToArray();
            int columnHeaderIndex = System.Array.IndexOf(lines, TrialCsvWriter.ColumnHeader);

            Assert.That(columnHeaderIndex, Is.GreaterThan(0), "列名行が見つかること");

            for (int i = 0; i < columnHeaderIndex; i++)
            {
                Assert.That(lines[i], Does.StartWith("# "),
                    "列名行より前はすべてコメント行でなければならない");
            }
        }

        [Test]
        public void PhaseMarker_IsWrittenAsPhaseId()
        {
            var rows = new List<TrialLogRow>
            {
                SampleRow(3.0, "lead_in"),
                SampleRow(13.0, "segment_1"),
            };

            var lines = DataLines(TrialCsvWriter.Write(SampleHeader(), rows));

            Assert.That(lines[1].Split(',')[1], Is.EqualTo("lead_in"));
            Assert.That(lines[2].Split(',')[1], Is.EqualTo("segment_1"));
        }

        [Test]
        public void RecenterFlag_IsPerFrameEventNotCumulative()
        {
            var normal = SampleRow(20.0, "segment_1");
            var atRecenter = SampleRow(21.0, "segment_1");
            atRecenter.recenterFlag = 1;
            var afterRecenter = SampleRow(22.0, "segment_1");

            var lines = DataLines(TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { normal, atRecenter, afterRecenter }));

            Assert.That(lines[1].Split(',').Last(), Is.EqualTo("0"));
            Assert.That(lines[2].Split(',').Last(), Is.EqualTo("1"));
            Assert.That(lines[3].Split(',').Last(), Is.EqualTo("0"),
                "発生フレームだけ 1。累積は後処理で取る");
        }

        [Test]
        public void DegenerateRotation_StillWritesValueAndLowersFlag()
        {
            var row = SampleRow(20.0, "segment_1");
            row.metrics.procrustesRotationWellDefined = false;
            row.metrics.procrustesRotationDegrees = 137.5f;

            var lines = DataLines(TrialCsvWriter.Write(
                SampleHeader(), new List<TrialLogRow> { row }));
            var columns = TrialCsvWriter.ColumnHeader.Split(',');
            var values = lines[1].Split(',');

            int rotationIndex = System.Array.IndexOf(columns, "proc_rot_deg");
            int flagIndex = System.Array.IndexOf(columns, "proc_rot_defined");

            Assert.That(values[rotationIndex], Is.EqualTo("137.5000"), "値は空にしない");
            Assert.That(values[flagIndex], Is.EqualTo("0"));
        }

        /// <summary>
        /// この環境のロケールは日本語。将来ロケール依存の書式で出力されると、
        /// 過去の CSV と後の CSV が別物になる。InvariantCulture 固定であることを確認する。
        /// </summary>
        [Test]
        public void NumberFormatting_IsLocaleIndependent()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                // 小数点にカンマを使うロケール。固定していなければ CSV が壊れる。
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                string csv = TrialCsvWriter.Write(
                    SampleHeader(), new List<TrialLogRow> { SampleRow(3.5, "lead_in") });

                var lines = DataLines(csv);
                Assert.That(lines[1].Split(',').Length,
                    Is.EqualTo(TrialCsvWriter.ColumnHeader.Split(',').Length),
                    "小数点がカンマになると列数が増えてしまう");

                Assert.That(lines[1], Does.StartWith("3.500000,"));
                Assert.That(csv, Does.Contain("# neck_offset_d_m: 0.150000"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void TimeColumn_KeepsSubMillisecondResolution()
        {
            var rows = new List<TrialLogRow>
            {
                SampleRow(13.011111, "segment_1"),
                SampleRow(13.022222, "segment_1"),
            };

            var lines = DataLines(TrialCsvWriter.Write(SampleHeader(), rows));

            Assert.That(lines[1].Split(',')[0], Is.EqualTo("13.011111"));
            Assert.That(lines[2].Split(',')[0], Is.EqualTo("13.022222"));
        }
    }
}
