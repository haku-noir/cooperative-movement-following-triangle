using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 頂点定義の単体テスト（仕様書 §8.3 の一部）。
    /// 段階1 の時点でこれを書いておく理由は、V0 の定義ミスが実機では目視で気づけないため。
    /// 頭部ローカル down を使ってしまっても画面上は「首のあたりに点がある」ようにしか見えず、
    /// 誤差データだけが静かに汚染される。
    /// </summary>
    public class VertexMathTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void NeckVertex_IsCenterEyeOffsetAlongWorldDown()
        {
            var centerEye = new Vector3(0.10f, 1.62f, -0.30f);
            const float d = 0.15f;

            var actual = VertexMath.NeckVertex(centerEye, d);

            AssertApproximately(new Vector3(0.10f, 1.62f - 0.15f, -0.30f), actual);
        }

        [Test]
        public void NeckVertex_WithZeroOffset_EqualsCenterEye()
        {
            var centerEye = new Vector3(-1.2f, 1.7f, 2.4f);

            AssertApproximately(centerEye, VertexMath.NeckVertex(centerEye, 0f));
        }

        /// <summary>
        /// 仕様書 §2.2 の中核。頭部がどの姿勢であっても V0 は変わらない。
        ///
        /// VertexMath.NeckVertex は回転を引数に取らないため、この性質は本来「実装上あたりまえ」だが、
        /// あたりまえであること自体をテストで固定しておく。将来だれかが利便性のために
        /// 回転付きのオーバーロードを足したとき、このテストが残っていれば意図が伝わる。
        /// </summary>
        [Test]
        public void NeckVertex_IsInvariantToHeadRotation()
        {
            var centerEye = new Vector3(0.05f, 1.58f, 0.12f);
            const float d = 0.15f;
            var expected = centerEye + d * Vector3.down;

            foreach (var euler in HeadRotationCases)
            {
                // 回転は計算に関与しない。引数として渡す先すら存在しない。
                var actual = VertexMath.NeckVertex(centerEye, d);
                AssertApproximately(expected, actual, $"head euler = {euler}");
            }
        }

        /// <summary>
        /// 禁止実装（頭部ローカル座標系の down を使う実装）が、いつ正しい実装と食い違うかを固定する。
        ///
        /// ここが実験運用上いちばん危険な点である。重力方向 -Y はヨー回転（Y 軸まわり）の不動軸なので、
        /// **被験者が左右を見回すだけでは、禁止実装でも正しい実装とまったく同じ値になる**。
        /// 差が出るのはピッチ（うなずき）とロール（首をかしげる）のときだけ。
        ///
        /// つまり「HMD をかぶって左右を見て、首の点がついてこないから大丈夫」という確認では
        /// この実装ミスを検出できない。実機での目視確認に頼れないからこそ、
        /// この性質をテストとして書き残しておく。
        /// </summary>
        [Test]
        public void ForbiddenLocalDown_MatchesUnderPureYaw_ButDivergesUnderPitchOrRoll()
        {
            var centerEye = new Vector3(0.05f, 1.58f, 0.12f);
            const float d = 0.15f;
            var correct = VertexMath.NeckVertex(centerEye, d);

            // ヨーのみ：差が出ない。検出できない。
            foreach (var yaw in new[] { 15f, 45f, 90f, 180f })
            {
                var forbidden = ForbiddenNeckVertex(centerEye, d, Quaternion.Euler(0f, yaw, 0f));

                Assert.That(Vector3.Distance(correct, forbidden), Is.LessThan(Tolerance),
                    $"ヨー {yaw} 度では禁止実装でも差が出ないはず");
            }

            // ピッチ・ロール：差が出る。
            var tiltingRotations = new[]
            {
                new Vector3(30f, 0f, 0f),     // 下を向く
                new Vector3(-20f, 0f, 0f),    // 上を向く
                new Vector3(0f, 0f, 25f),     // 首をかしげる
                new Vector3(-25f, 110f, 15f), // 複合
            };

            foreach (var euler in tiltingRotations)
            {
                var forbidden = ForbiddenNeckVertex(centerEye, d, Quaternion.Euler(euler));

                Assert.That(Vector3.Distance(correct, forbidden), Is.GreaterThan(1e-3f),
                    $"ピッチ／ロールを含む姿勢では差が出るはず (euler = {euler})");
            }
        }

        /// <summary>
        /// 頭部が下を向くと、禁止実装では V0 が前方へ大きく変位する。
        /// d = 0.15 m でピッチ 45 度なら約 10.6 cm。頂点球の直径 0.03 m の 3 倍以上であり、
        /// 追従誤差の指標としては致命的な大きさになる。
        /// </summary>
        [Test]
        public void ForbiddenLocalDownImplementation_ShiftsVertexBeyondSphereDiameter()
        {
            var centerEye = new Vector3(0f, 1.6f, 0f);
            const float d = 0.15f;
            const float sphereDiameter = 0.03f;

            var correct = VertexMath.NeckVertex(centerEye, d);
            var forbidden = ForbiddenNeckVertex(centerEye, d, Quaternion.Euler(45f, 0f, 0f));

            float displacement = Vector3.Distance(correct, forbidden);

            Assert.That(displacement, Is.GreaterThan(sphereDiameter * 3f),
                $"ピッチ 45 度での混入量 = {displacement:F4} m");
        }

        [Test]
        public void CharacteristicLength_IsMeanOfNeckToHandDistances()
        {
            var v0 = new Vector3(0f, 1.45f, 0f);
            var v1 = new Vector3(-0.30f, 1.45f, 0f); // 首から 0.30 m
            var v2 = new Vector3(0.50f, 1.45f, 0f);  // 首から 0.50 m

            float length = VertexMath.CharacteristicLength(v0, v1, v2);

            Assert.That(length, Is.EqualTo(0.40f).Within(Tolerance));
        }

        [Test]
        public void CharacteristicLength_IsIndependentOfWorldPosition()
        {
            var v0 = new Vector3(0f, 1.45f, 0f);
            var v1 = new Vector3(-0.30f, 1.45f, 0f);
            var v2 = new Vector3(0.50f, 1.45f, 0f);
            var offset = new Vector3(3f, -1f, 7f);

            float atOrigin = VertexMath.CharacteristicLength(v0, v1, v2);
            float translated = VertexMath.CharacteristicLength(v0 + offset, v1 + offset, v2 + offset);

            Assert.That(translated, Is.EqualTo(atOrigin).Within(Tolerance));
        }

        private static readonly Vector3[] HeadRotationCases =
        {
            Vector3.zero,
            new Vector3(0f, 45f, 0f),     // ヨーのみ
            new Vector3(30f, 0f, 0f),     // ピッチのみ（下を向く）
            new Vector3(0f, 0f, 25f),     // ロールのみ（首をかしげる）
            new Vector3(-25f, 110f, 15f), // 複合
        };

        /// <summary>
        /// 仕様書 §2.2 が禁じている実装。テストの中だけに存在し、本番コードには無い。
        /// 「何をしてはいけないか」を実行可能な形で残すために書いてある。
        /// </summary>
        private static Vector3 ForbiddenNeckVertex(
            Vector3 centerEyePosition, float d, Quaternion headRotation)
        {
            return centerEyePosition + d * (headRotation * Vector3.down);
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual, string message = "")
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance),
                $"expected {expected} but was {actual}. {message}");
        }
    }
}
