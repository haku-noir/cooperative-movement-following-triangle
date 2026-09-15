using System.Collections.Generic;
using System.Linq;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 基準姿勢フェーズのログ（Registration ログ）。
    ///
    /// 追従課題のログとは別ファイルに出す。基準姿勢の間は A の三角形が見えておらず、
    /// この区間の「誤差」は追従成績ではなく Registration の当てはまり具合を表す量である。
    /// 同じファイルに混ぜると、後処理で課題成績に数えてしまう余地が残る。
    ///
    /// 一方で列構成は試行ログと完全に同一にしてある。同じ読み込みコードで扱えるほうが
    /// 解析側の間違いが減るため。区別はファイル名とヘッダの log_kind で行う。
    /// </summary>
    public class RegistrationLogTests
    {
        private static TrialLogHeader Header(string logKind) => new TrialLogHeader
        {
            logKind = logKind,
            participantId = "B01",
            conditionId = "C2",
            stimulusId = "take1",
            trialIndex = 3,
            normalizationLength = 0.48f,
            registrationMethod = "NeckAnchored",
            registrationResidualRms = 0.0031f,
            registrationResidualPerVertex = new Vector3(0f, 0.0038f, 0.0042f),
            referenceTriangleA = new Triangle(
                new Vector3(0f, 1.45f, 0f),
                new Vector3(-0.4f, 1.45f, 0.2f),
                new Vector3(0.4f, 1.45f, 0.2f)),
            baselineTriangleB = new Triangle(
                new Vector3(0.01f, 1.50f, 0f),
                new Vector3(-0.41f, 1.50f, 0.21f),
                new Vector3(0.41f, 1.50f, 0.21f)),
            baselineFrameCount = 270,
            baselineAcceptedCount = 262,
        };

        private static TrialLogRow Row(double t) => new TrialLogRow
        {
            t = t,
            phaseMarker = RecordingSchedule.BaselinePhaseId,
            headARotation = Quaternion.identity,
            headBRotation = Quaternion.identity,
            metrics = new FrameMetrics { procrustesRotationWellDefined = true },
            handLeftTracked = true,
            handLeftConfidence = 1,
            handRightTracked = true,
            handRightConfidence = 1,
        };

        private static string[] DataLines(string csv) =>
            csv.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();

        /// <summary>
        /// 2 つのログが同じ列構成であること。解析スクリプトを共通化できる前提。
        /// </summary>
        [Test]
        public void BaselineLog_UsesSameColumnsAsTrialLog()
        {
            var rows = new List<TrialLogRow> { Row(0.0), Row(0.011) };

            string trial = TrialCsvWriter.Write(Header("trial"), rows);
            string registration = TrialCsvWriter.Write(Header("registration"), rows);

            Assert.That(DataLines(registration)[0], Is.EqualTo(DataLines(trial)[0]));
            Assert.That(DataLines(registration)[0], Is.EqualTo(TrialCsvWriter.ColumnHeader));
        }

        /// <summary>
        /// ファイル名だけでなく中身でも 2 つを区別できること。
        /// 取り違えると、A がまだ見えていない区間の誤差を課題成績として数えてしまう。
        /// </summary>
        [Test]
        public void LogKind_DistinguishesTheTwoLogs()
        {
            string trial = TrialCsvWriter.Write(Header("trial"), new List<TrialLogRow> { Row(3.0) });
            string registration = TrialCsvWriter.Write(
                Header("registration"), new List<TrialLogRow> { Row(0.0) });

            Assert.That(trial, Does.Contain("# log_kind: trial"));
            Assert.That(registration, Does.Contain("# log_kind: registration"));
        }

        [Test]
        public void BaselineRows_AreMarkedWithBaselinePhaseId()
        {
            var lines = DataLines(TrialCsvWriter.Write(
                Header("registration"), new List<TrialLogRow> { Row(0.0), Row(0.011) }));

            Assert.That(lines[1].Split(',')[1], Is.EqualTo("baseline"));
            Assert.That(lines[2].Split(',')[1], Is.EqualTo("baseline"));
        }

        /// <summary>
        /// Registration の内訳がヘッダに残ること。
        /// RMS だけだとどの頂点で合っていないのか分からず、推定法の選択が
        /// 妥当だったかを後から評価できない。
        /// </summary>
        [Test]
        public void HeaderComments_ContainRegistrationDiagnostics()
        {
            string csv = TrialCsvWriter.Write(
                Header("registration"), new List<TrialLogRow> { Row(0.0) });

            Assert.That(csv, Does.Contain("# registration_method: NeckAnchored"));
            Assert.That(csv, Does.Contain("# registration_residual_rms_m: 0.003100"));
            Assert.That(csv, Does.Contain(
                "# registration_residual_per_vertex_m: 0.000000 0.003800 0.004200"));
            Assert.That(csv, Does.Contain("# registration_reference_A_v0: 0.000000 1.450000 0.000000"));
            Assert.That(csv, Does.Contain("# registration_baseline_B_v0: 0.010000 1.500000 0.000000"));
        }

        /// <summary>
        /// 採用数と総フレーム数の両方を残す。差が大きい試行はトラッキングが不安定で、
        /// Registration の信頼性が落ちていることを意味する。
        /// </summary>
        [Test]
        public void HeaderComments_ContainBaselineSampleCounts()
        {
            string csv = TrialCsvWriter.Write(
                Header("registration"), new List<TrialLogRow> { Row(0.0) });

            Assert.That(csv, Does.Contain("# baseline_frame_count: 270"));
            Assert.That(csv, Does.Contain("# baseline_accepted_count: 262"));
        }

        /// <summary>
        /// 平均に採用されたかどうかは hand_*_conf 列から厳密に導ける。
        /// だから採用フラグを別列で持つ必要がない。
        /// </summary>
        [Test]
        public void AcceptanceIsDerivableFromConfidenceColumns()
        {
            var accepted = Row(0.0);

            var rejected = Row(0.011);
            rejected.handLeftConfidence = (int)HandConfidence.Low;

            var untracked = Row(0.022);
            untracked.handRightTracked = false;
            untracked.handRightConfidence = (int)HandConfidence.Unknown;

            var lines = DataLines(TrialCsvWriter.Write(
                Header("registration"),
                new List<TrialLogRow> { accepted, rejected, untracked }));

            var columns = TrialCsvWriter.ColumnHeader.Split(',');
            int leftConf = System.Array.IndexOf(columns, "hand_L_conf");
            int rightConf = System.Array.IndexOf(columns, "hand_R_conf");
            int rightTracked = System.Array.IndexOf(columns, "hand_R_tracked");

            Assert.That(lines[1].Split(',')[leftConf], Is.EqualTo("1"));
            Assert.That(lines[2].Split(',')[leftConf], Is.EqualTo("0"), "低信頼として残ること");
            Assert.That(lines[3].Split(',')[rightTracked], Is.EqualTo("0"));
            Assert.That(lines[3].Split(',')[rightConf], Is.EqualTo("-1"), "Unknown は -1");
        }
    }
}
