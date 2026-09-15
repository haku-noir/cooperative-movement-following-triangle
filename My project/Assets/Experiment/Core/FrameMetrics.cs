using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 1 フレームぶんの誤差量（仕様書 §6.1）。
    ///
    /// ここには **閾値判定も二値化も入れない**（§6.3）。
    /// 一致判定、継続長分布、破綻回数、復帰時間はすべて後処理で行う。
    /// アプリは生の量を出すところまでを担当する。
    ///
    /// 唯一の例外は <see cref="procrustesRotationWellDefined"/> だが、これは
    /// 「幾何的に退化しているか」ではなく「数値的に回転が一意に決まったか」を示すもので、
    /// 実験上の判断を含まない。幾何的な退化の閾値は最小内角の列から後処理で決める。
    /// </summary>
    public struct FrameMetrics
    {
        // --- §6.1-1 頂点距離 ---
        public float distanceV0;
        public float distanceV1;
        public float distanceV2;
        public float distanceSum;

        /// <summary>
        /// 正規化した総和。分母はキャリブレーション時に確定した B の体格指標 L_B。
        ///
        /// 毎フレームの三角形から分母を取ってはならない。被験者が腕を伸ばすだけで
        /// 正規化誤差が下がる抜け道になり、仕様書 §5.2 が塞いだ穴が別経路で開く。
        /// </summary>
        public float distanceSumNormalized;

        // --- §6.1-2 Procrustes 分解 ---
        public float procrustesTranslation;
        public float procrustesRotationDegrees;
        public float procrustesResidual;
        public float procrustesResidualNormalized;
        public bool procrustesRotationWellDefined;

        // --- §6.1-3 頭部方向残差（課題外の事後評価用）---
        public float headDirectionResidualDegrees;

        // --- §6.1-4 退化検出 ---
        public float triangleAreaB;
        public float triangleMinAngleB;

        /// <summary>
        /// 誤差量を計算する。
        /// </summary>
        /// <param name="a">
        /// 演者 A のサンプル。**体格正規化と Registration を適用した後** のもの。
        /// B が実際に見た座標でなければ、誤差の意味が変わる。
        /// </param>
        /// <param name="b">被験者 B のサンプル。</param>
        /// <param name="normalizationLength">
        /// 正規化の分母 [m]。キャリブレーション時に確定した B の体格指標 L_B（§5.1, §6.1）。
        /// </param>
        public static FrameMetrics Compute(
            in BodyTriangleSample a, in BodyTriangleSample b, float normalizationLength)
        {
            var metrics = new FrameMetrics
            {
                distanceV0 = Vector3.Distance(b.v0, a.v0),
                distanceV1 = Vector3.Distance(b.v1, a.v1),
                distanceV2 = Vector3.Distance(b.v2, a.v2),
            };

            metrics.distanceSum = metrics.distanceV0 + metrics.distanceV1 + metrics.distanceV2;

            // A を B に重ねる剛体変換を求める（§6.1-2）。
            // 向きを A -> B に固定しておく。逆向きにすると proc_trans の符号の意味が変わる。
            var procrustes = Procrustes.Solve(
                new Triangle(a.v0, a.v1, a.v2),
                new Triangle(b.v0, b.v1, b.v2));

            metrics.procrustesTranslation = procrustes.TranslationMagnitude;
            metrics.procrustesRotationDegrees = procrustes.RotationDegrees;
            metrics.procrustesResidual = procrustes.ResidualRms;
            metrics.procrustesRotationWellDefined = procrustes.RotationWellDefined;

            metrics.headDirectionResidualDegrees =
                VertexMath.ForwardDirectionAngleDegrees(a.headRotation, b.headRotation);

            metrics.triangleAreaB = VertexMath.Area(b.v0, b.v1, b.v2);
            metrics.triangleMinAngleB = VertexMath.MinInteriorAngleDegrees(b.v0, b.v1, b.v2);

            if (normalizationLength > 1e-6f)
            {
                float inverse = 1f / normalizationLength;
                metrics.distanceSumNormalized = metrics.distanceSum * inverse;
                metrics.procrustesResidualNormalized = metrics.procrustesResidual * inverse;
            }

            return metrics;
        }
    }
}
