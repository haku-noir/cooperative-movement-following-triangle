using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 「C1 と C2 は教示文以外に一切の差があってはならない」という統制条件を、
    /// レビュー時の注意ではなくテストで固定する。
    ///
    /// このテストが落ちるということは、条件が描画に影響する経路が
    /// ConditionVisuals.DrawsEdges 以外に増えたということであり、実験の妥当性が壊れている。
    /// </summary>
    public class ConditionVisualsTests
    {
        private ExperimentSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<ExperimentSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void C1AndC2_ProduceIdenticalVisualConfig()
        {
            var c1 = ConditionVisuals.For(Condition.C1, settings);
            var c2 = ConditionVisuals.For(Condition.C2, settings);

            Assert.That(c2, Is.EqualTo(c1),
                $"C1 と C2 の表示が異なります。\nC1: {c1}\nC2: {c2}");
        }

        [Test]
        public void C1AndC2_DoNotDrawEdges()
        {
            Assert.That(ConditionVisuals.DrawsEdges(Condition.C1), Is.False);
            Assert.That(ConditionVisuals.DrawsEdges(Condition.C2), Is.False);
        }

        [Test]
        public void C3_DrawsEdges()
        {
            Assert.That(ConditionVisuals.DrawsEdges(Condition.C3), Is.True);
        }

        /// <summary>
        /// 条件間で変わってよいのは辺の有無だけ。球の大きさ・色・線幅は 3 条件で共通でなければならない
        /// （仕様書 §4.1, §4.2）。C3 で球が小さくなるといった差は、C2 → C3 の比較を無効にする。
        /// </summary>
        [Test]
        public void OnlyEdgeVisibilityDiffersAcrossConditions()
        {
            var c1 = ConditionVisuals.For(Condition.C1, settings);
            var c3 = ConditionVisuals.For(Condition.C3, settings);

            Assert.That(c3.vertexSphereDiameter, Is.EqualTo(c1.vertexSphereDiameter));
            Assert.That(c3.vertexColor, Is.EqualTo(c1.vertexColor));
            Assert.That(c3.edgeLineWidth, Is.EqualTo(c1.edgeLineWidth));
            Assert.That(c3.edgeColor, Is.EqualTo(c1.edgeColor));
            Assert.That(c3.drawEdges, Is.Not.EqualTo(c1.drawEdges));
        }

        /// <summary>
        /// ConditionVisuals.For は「自己か相手か」を受け取らない。
        /// 同じ条件から作った設定は、自己三角形用でも相手三角形用でも同一になる（仕様書 §4.2）。
        /// </summary>
        [Test]
        public void SelfAndOther_CannotDifferByConstruction()
        {
            foreach (Condition condition in System.Enum.GetValues(typeof(Condition)))
            {
                var forSelf = ConditionVisuals.For(condition, settings);
                var forOther = ConditionVisuals.For(condition, settings);

                Assert.That(forOther, Is.EqualTo(forSelf), $"condition = {condition}");
            }
        }

        [Test]
        public void DefaultSettings_MatchSpecifiedProvisionalValues()
        {
            // 仕様書 §2.2 / §4.1 / §4.3 の暫定値。パイロットで変えてよいが、
            // 既定値が黙って変わっていないことは確認しておく。
            Assert.That(settings.NeckOffsetD, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(settings.VertexSphereDiameter, Is.EqualTo(0.03f).Within(1e-6f));
            Assert.That(settings.EdgeLineWidth, Is.EqualTo(0.01f).Within(1e-6f));
            Assert.That(settings.HandVertexBone, Is.EqualTo(HandVertexBone.MiddleMcp));
        }

        [Test]
        public void TrialDuration_MatchesSpecTable()
        {
            // 仕様書 §5.4：基準姿勢 3 s + 導入 10 s + 区間 20 s × 4 = 93 s
            Assert.That(settings.TrialDurationSeconds, Is.EqualTo(93f).Within(1e-4f));
        }

        [Test]
        public void Experiment2Parameters_AreDisabledByDefault()
        {
            // 仕様書 §0.3：実験2の要因は実装しない。既定で有効になっていてはならない。
            Assert.That(settings.Experiment2.AnyEnabled, Is.False);
        }
    }
}
