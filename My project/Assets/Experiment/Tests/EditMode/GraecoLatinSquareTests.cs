using System.Collections.Generic;
using System.Linq;
using FollowingTriangle.Core;
using NUnit.Framework;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 仕様書 §5.5 のカウンターバランス。
    ///
    /// 割付が崩れていても実験は最後まで走ってしまい、データを取り終えてから
    /// 「順序効果と条件効果が分離できない」ことに気づくことになる。
    /// 均衡の性質そのものをテストで固定する。
    /// </summary>
    public class GraecoLatinSquareTests
    {
        private static TrialAssignment[][] OneCycle() =>
            Enumerable.Range(1, GraecoLatinSquare.Order)
                .Select(GraecoLatinSquare.For)
                .ToArray();

        [Test]
        public void EachParticipantSeesEveryConditionExactlyOnce()
        {
            foreach (var assignments in OneCycle())
            {
                var conditions = assignments.Select(a => a.Condition).ToList();

                Assert.That(conditions.Count, Is.EqualTo(3));
                Assert.That(conditions.Distinct().Count(), Is.EqualTo(3),
                    $"条件が重複しています: {string.Join(", ", conditions)}");
            }
        }

        [Test]
        public void EachParticipantSeesEveryStimulusExactlyOnce()
        {
            foreach (var assignments in OneCycle())
            {
                var stimuli = assignments.Select(a => a.StimulusIndex).ToList();

                Assert.That(stimuli.Distinct().Count(), Is.EqualTo(3),
                    $"刺激が重複しています: {string.Join(", ", stimuli)}");
            }
        }

        /// <summary>
        /// 均衡その 1：各条件が各提示位置にちょうど 1 回ずつ現れる。
        /// これが崩れると順序効果と条件効果が交絡する。
        /// </summary>
        [Test]
        public void EveryConditionAppearsOnceAtEveryPosition()
        {
            var cycle = OneCycle();

            for (int position = 0; position < GraecoLatinSquare.Order; position++)
            {
                var atPosition = cycle.Select(a => a[position].Condition).ToList();

                Assert.That(atPosition.Distinct().Count(), Is.EqualTo(3),
                    $"位置 {position + 1} に同じ条件が複数回現れています: " +
                    string.Join(", ", atPosition));
            }
        }

        /// <summary>
        /// 均衡その 2：各刺激が各提示位置にちょうど 1 回ずつ現れる。
        /// 刺激ごとの難易度差が提示位置と交絡しないようにする。
        /// </summary>
        [Test]
        public void EveryStimulusAppearsOnceAtEveryPosition()
        {
            var cycle = OneCycle();

            for (int position = 0; position < GraecoLatinSquare.Order; position++)
            {
                var atPosition = cycle.Select(a => a[position].StimulusIndex).ToList();

                Assert.That(atPosition.Distinct().Count(), Is.EqualTo(3),
                    $"位置 {position + 1} に同じ刺激が複数回現れています: " +
                    string.Join(", ", atPosition));
            }
        }

        /// <summary>
        /// 均衡その 3（グレコ・ラテン方格の直交性）：
        /// 9 通りの（条件, 刺激）の組が 3 名でちょうど 1 回ずつ現れる。
        ///
        /// これが成立しないと、ある条件が特定の刺激とだけ組み合わされ、
        /// 条件の効果と刺激の効果が分離できなくなる。
        /// </summary>
        [Test]
        public void EveryConditionStimulusPairAppearsExactlyOnceInOneCycle()
        {
            var pairs = new List<(Condition, int)>();

            foreach (var assignments in OneCycle())
            {
                pairs.AddRange(assignments.Select(a => (a.Condition, a.StimulusIndex)));
            }

            Assert.That(pairs.Count, Is.EqualTo(9));
            Assert.That(pairs.Distinct().Count(), Is.EqualTo(9),
                "9 通りの組がすべて 1 回ずつ現れるべき: " +
                string.Join(", ", pairs.Select(p => $"{p.Item1}/S{p.Item2 + 1}")));
        }

        /// <summary>
        /// 被験者 4 は被験者 1 と同じ割付になる（3 名 1 周期）。
        /// 被験者数が 3 の倍数でないところで打ち切ると均衡が崩れることの裏返し。
        /// </summary>
        [Test]
        public void AssignmentRepeatsEveryThreeParticipants()
        {
            for (int participant = 1; participant <= 3; participant++)
            {
                var first = GraecoLatinSquare.For(participant);
                var next = GraecoLatinSquare.For(participant + GraecoLatinSquare.Order);

                for (int i = 0; i < first.Length; i++)
                {
                    Assert.That(next[i].Condition, Is.EqualTo(first[i].Condition));
                    Assert.That(next[i].StimulusIndex, Is.EqualTo(first[i].StimulusIndex));
                }
            }
        }

        /// <summary>
        /// 同じ被験者番号なら必ず同じ並びが返る。
        /// 実験を中断して再開しても割付が変わらないよう、乱数を使っていないこと。
        /// </summary>
        [Test]
        public void AssignmentIsDeterministic()
        {
            for (int participant = 1; participant <= 12; participant++)
            {
                var a = GraecoLatinSquare.For(participant);
                var b = GraecoLatinSquare.For(participant);

                for (int i = 0; i < a.Length; i++)
                {
                    Assert.That(b[i].Condition, Is.EqualTo(a[i].Condition));
                    Assert.That(b[i].StimulusIndex, Is.EqualTo(a[i].StimulusIndex));
                }
            }
        }

        [Test]
        public void PositionsAreSequential()
        {
            var assignments = GraecoLatinSquare.For(2);

            for (int i = 0; i < assignments.Length; i++)
            {
                Assert.That(assignments[i].Position, Is.EqualTo(i));
            }
        }

        [Test]
        public void ParticipantNumberBelowOneIsRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => GraecoLatinSquare.For(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => GraecoLatinSquare.For(-1));
        }

        // ------------------------------------------------------------------
        // 被験者 ID から番号を取り出す
        // ------------------------------------------------------------------

        [TestCase("B01", 1)]
        [TestCase("B12", 12)]
        [TestCase("P007", 7)]
        [TestCase("subject-3", 3)]
        [TestCase("9", 9)]
        public void ParticipantNumber_IsTakenFromTrailingDigits(string id, int expected)
        {
            Assert.That(ParticipantNumber.TryParse(id, out int number, out string error), Is.True, error);
            Assert.That(number, Is.EqualTo(expected));
        }

        /// <summary>
        /// 番号が取れない ID はエラーにして先へ進まない。
        /// ハッシュ等で無理に番号を作ると割付が実質ランダムになり、
        /// カウンターバランスが成立しなくなる。
        /// </summary>
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("B")]
        [TestCase("subject")]
        [TestCase("B00")]
        public void ParticipantNumber_RejectsIdsWithoutUsableNumber(string id)
        {
            Assert.That(ParticipantNumber.TryParse(id, out _, out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void RowFor_CyclesEveryThreeParticipants()
        {
            Assert.That(GraecoLatinSquare.RowFor(1), Is.EqualTo(0));
            Assert.That(GraecoLatinSquare.RowFor(2), Is.EqualTo(1));
            Assert.That(GraecoLatinSquare.RowFor(3), Is.EqualTo(2));
            Assert.That(GraecoLatinSquare.RowFor(4), Is.EqualTo(0));
        }
    }
}
