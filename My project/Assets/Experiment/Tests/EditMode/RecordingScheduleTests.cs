using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §5.4 の試行構成が設定値から正しく組み立たるかを固定する。
    ///
    /// 区間境界がずれると、録画の区間マーカーと実験ログのマーカーが対応しなくなり、
    /// 「区間2（並進のみ）の追従精度」といった解析そのものが成立しなくなる。
    /// </summary>
    public class RecordingScheduleTests
    {
        private ExperimentSettings settings;
        private RecordingSchedule schedule;

        [SetUp]
        public void SetUp()
        {
            settings = SettingsEditing.CreateDefault();
            schedule = RecordingSchedule.FromSettings(settings);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void Schedule_MatchesSpecTable()
        {
            // 基準姿勢 3 s + 導入 10 s + 区間 20 s x 4
            Assert.That(schedule.Phases.Count, Is.EqualTo(6));
            Assert.That(schedule.TotalSeconds, Is.EqualTo(93f).Within(1e-4f));

            Assert.That(schedule.Phases[0].Id, Is.EqualTo("baseline"));
            Assert.That(schedule.Phases[0].StartSeconds, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(schedule.Phases[0].DurationSeconds, Is.EqualTo(3f).Within(1e-4f));

            Assert.That(schedule.Phases[1].Id, Is.EqualTo("lead_in"));
            Assert.That(schedule.Phases[1].StartSeconds, Is.EqualTo(3f).Within(1e-4f));

            Assert.That(schedule.Phases[2].Id, Is.EqualTo("segment_1"));
            Assert.That(schedule.Phases[2].StartSeconds, Is.EqualTo(13f).Within(1e-4f));
            Assert.That(schedule.Phases[3].StartSeconds, Is.EqualTo(33f).Within(1e-4f));
            Assert.That(schedule.Phases[4].StartSeconds, Is.EqualTo(53f).Within(1e-4f));
            Assert.That(schedule.Phases[5].StartSeconds, Is.EqualTo(73f).Within(1e-4f));
            Assert.That(schedule.Phases[5].EndSeconds, Is.EqualTo(93f).Within(1e-4f));
        }

        /// <summary>
        /// 仕様書 §5.4：基準姿勢と導入は解析から除外。区間 1〜4 のみ解析対象。
        /// これを取り違えると導入 10 s の「追従が立ち上がる途中」のデータが
        /// 精度指標に混ざる。
        /// </summary>
        [Test]
        public void OnlySegments_AreIncludedInAnalysis()
        {
            Assert.That(schedule.Phases[0].IncludedInAnalysis, Is.False, "基準姿勢");
            Assert.That(schedule.Phases[1].IncludedInAnalysis, Is.False, "導入");

            for (int i = 2; i < schedule.Phases.Count; i++)
            {
                Assert.That(schedule.Phases[i].IncludedInAnalysis, Is.True, schedule.Phases[i].Id);
            }
        }

        /// <summary>境界は [start, end)。20.0 s ちょうどは次の区間の先頭。</summary>
        [Test]
        public void PhaseBoundaries_AreHalfOpenIntervals()
        {
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(0f)].Id, Is.EqualTo("baseline"));
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(2.999f)].Id, Is.EqualTo("baseline"));
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(3f)].Id, Is.EqualTo("lead_in"));
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(12.999f)].Id, Is.EqualTo("lead_in"));
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(13f)].Id, Is.EqualTo("segment_1"));
            Assert.That(schedule.Phases[schedule.PhaseIndexAt(92.999f)].Id, Is.EqualTo("segment_4"));
        }

        [Test]
        public void PhaseIndexAt_ReturnsMinusOneOutsideSchedule()
        {
            Assert.That(schedule.PhaseIndexAt(-0.1f), Is.EqualTo(-1));
            Assert.That(schedule.PhaseIndexAt(93f), Is.EqualTo(-1));
            Assert.That(schedule.PhaseIndexAt(120f), Is.EqualTo(-1));
        }

        [Test]
        public void Schedule_FollowsSettingsWhenSegmentLengthChanges()
        {
            SettingsEditing.SetFloat(settings, "segmentSeconds", 15f);
            var shortened = RecordingSchedule.FromSettings(settings);

            Assert.That(shortened.TotalSeconds, Is.EqualTo(3f + 10f + 15f * 4f).Within(1e-4f));
            Assert.That(shortened.Phases[2].StartSeconds, Is.EqualTo(13f).Within(1e-4f));
            Assert.That(shortened.Phases[3].StartSeconds, Is.EqualTo(28f).Within(1e-4f));
        }

        [Test]
        public void Validate_RejectsZeroLengthPhase()
        {
            SettingsEditing.SetFloat(settings, "segmentSeconds", 0f);
            var broken = RecordingSchedule.FromSettings(settings);

            Assert.That(broken.Validate(out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }
    }
}
