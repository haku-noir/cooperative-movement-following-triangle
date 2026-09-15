using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// Registration の推定法（仕様書 §5.3）。
    ///
    /// 3 頂点 9 拘束に対して自由度は 4（並進 3 + ヨー 1）なので優決定であり、
    /// 「どの残差を最小化するか」を決めないと解が一意に定まらない。
    /// 仕様書は解法を指定していないため、3 通りを実装して切り替えられるようにしてある。
    /// どれを使ったかは試行ログのヘッダに記録すること。
    /// </summary>
    public enum RegistrationMethod
    {
        /// <summary>
        /// 3 頂点を等重みで最小二乗。重心を一致させ、水平面に射影した Kabsch でヨーを閉形式で解く。
        /// 残差が 3 頂点に均等配分される。標準的な解法。
        /// </summary>
        ThreeVertexLeastSquares = 0,

        /// <summary>
        /// 首頂点を厳密に一致させ、ヨーは左右の手から決める。
        /// 「首が身体の基点」という解釈。残差はすべて手側に寄る。
        /// </summary>
        NeckAnchored = 1,

        /// <summary>
        /// 両手だけで並進とヨーを決め、首頂点は結果に従う。
        /// 首頂点は d だけのオフセットで頭部由来なので、手の一致を優先する立場。
        /// 首の残差が評価対象として残る。
        /// </summary>
        HandsOnly = 2,
    }

    /// <summary>三角形の 3 頂点。Registration の入出力に使う軽い値型。</summary>
    public readonly struct Triangle
    {
        public readonly Vector3 V0;
        public readonly Vector3 V1;
        public readonly Vector3 V2;

        public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        public static Triangle From(in BodyTriangleSample sample) =>
            new Triangle(sample.v0, sample.v1, sample.v2);

        public Vector3 Centroid => (V0 + V1 + V2) / 3f;

        public Vector3 HandMidpoint => (V1 + V2) * 0.5f;
    }

    /// <summary>
    /// 仕様書 §5.2 の体格正規化と §5.3 の位置合わせを解く。
    ///
    /// 出力は <see cref="StimulusTransform"/> 1 つ。試行の開始時に一度だけ求め、
    /// 以後の全フレームに同じものを適用する。フレームごとに解き直してはならない。
    /// 解き直すと、B が動くたびに A が追いかけてくることになり、
    /// 「B が A に追従する」という課題そのものが成立しなくなる。
    /// </summary>
    public static class Registration
    {
        /// <summary>
        /// 変換を求める。
        /// </summary>
        /// <param name="method">推定法（§5.3 が未指定のため選択式）。</param>
        /// <param name="referenceA">
        /// A の基準三角形。録画の基準姿勢区間を平均したもの。
        /// 単一フレームだとハンドトラッキングのジッタ（数 mm）が
        /// 全試行に効く定数バイアスとして固定されてしまう。
        /// </param>
        /// <param name="baselineB">B の基準姿勢 3 s の平均三角形。</param>
        /// <param name="calibrationA">演者 A のキャリブレーション（録画メタデータ）。</param>
        /// <param name="calibrationB">被験者 B のキャリブレーション。</param>
        public static StimulusTransform Solve(
            RegistrationMethod method,
            in Triangle referenceA, in Triangle baselineB,
            PerformerCalibration calibrationA, PerformerCalibration calibrationB)
        {
            float scale = CalibrationAccumulator.BodyScaleFactor(calibrationA, calibrationB);

            // スケールの中心は基準フレームの A の首頂点（§5.2）。毎フレームの V0_A ではない。
            Vector3 pivot = referenceA.V0;

            // 剛体変換は「スケール済みの A」と B の間で推定する。
            // 順序を逆にすると、スケールが並進成分まで拡大してしまう。
            var scaledA = ScaleAbout(referenceA, pivot, scale);

            SolveRigid(method, scaledA, baselineB, out float yawDegrees, out Vector3 translation);

            return new StimulusTransform
            {
                scale = scale,
                scalePivot = pivot,
                yawDegrees = yawDegrees,
                translation = translation,
            };
        }

        public static Triangle ScaleAbout(in Triangle triangle, Vector3 pivot, float scale)
        {
            return new Triangle(
                pivot + scale * (triangle.V0 - pivot),
                pivot + scale * (triangle.V1 - pivot),
                pivot + scale * (triangle.V2 - pivot));
        }

        private static void SolveRigid(
            RegistrationMethod method, in Triangle a, in Triangle b,
            out float yawDegrees, out Vector3 translation)
        {
            Vector3 anchorA, anchorB;
            Vector3[] fromA, fromB;

            switch (method)
            {
                case RegistrationMethod.NeckAnchored:
                    // 首を固定点にし、ヨーは手 2 点だけから決める。
                    anchorA = a.V0;
                    anchorB = b.V0;
                    fromA = new[] { a.V1, a.V2 };
                    fromB = new[] { b.V1, b.V2 };
                    break;

                case RegistrationMethod.HandsOnly:
                    anchorA = a.HandMidpoint;
                    anchorB = b.HandMidpoint;
                    fromA = new[] { a.V1, a.V2 };
                    fromB = new[] { b.V1, b.V2 };
                    break;

                case RegistrationMethod.ThreeVertexLeastSquares:
                default:
                    anchorA = a.Centroid;
                    anchorB = b.Centroid;
                    fromA = new[] { a.V0, a.V1, a.V2 };
                    fromB = new[] { b.V0, b.V1, b.V2 };
                    break;
            }

            yawDegrees = EstimateYawDegrees(fromA, anchorA, fromB, anchorB);

            // 並進は、アンカーを回転させたあとに残る差。
            // 回転中心を別に持たず並進へ吸収させることで、変換が
            // 「Y 軸回転 + 平行移動」という素直な形に収まる。
            translation = anchorB - Quaternion.Euler(0f, yawDegrees, 0f) * anchorA;
        }

        /// <summary>
        /// 水平面に射影した Kabsch 解（ヨーのみ）。
        ///
        /// sum_i | R(theta) a_i - b_i |^2 を最小化する theta は、
        /// R が Y 軸回転のとき閉形式で書ける。Unity の左手系で
        ///     R(theta) * (x, y, z) = (x cos + z sin, y, -x sin + z cos)
        /// なので、内積の theta 依存部分は
        ///     cos * sum(ax*bx + az*bz) + sin * sum(az*bx - ax*bz)
        /// となり、theta = atan2(sin 係数, cos 係数) で最大化される。
        ///
        /// Y 成分は R に影響されないため、ヨーの推定には一切寄与しない。
        /// これは意図どおりで、A と B の身長差がヨーに漏れ込まないことを意味する。
        /// </summary>
        private static float EstimateYawDegrees(
            Vector3[] pointsA, Vector3 anchorA, Vector3[] pointsB, Vector3 anchorB)
        {
            double cosTerm = 0.0;
            double sinTerm = 0.0;

            for (int i = 0; i < pointsA.Length; i++)
            {
                Vector3 a = pointsA[i] - anchorA;
                Vector3 b = pointsB[i] - anchorB;

                cosTerm += (double)a.x * b.x + (double)a.z * b.z;
                sinTerm += (double)a.z * b.x - (double)a.x * b.z;
            }

            // 水平成分がほぼ無い（三角形が真上から見て潰れている）場合、ヨーは不定。
            // 黙って任意の角度を返すより 0 を返して、退化を後処理で検出できるようにする。
            if (Math.Abs(cosTerm) < 1e-12 && Math.Abs(sinTerm) < 1e-12) return 0f;

            return (float)(Math.Atan2(sinTerm, cosTerm) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// 求めた変換の当てはまり具合。基準姿勢どうしの残差 [m]。
        /// 大きい場合は基準姿勢の取り方か体格差の扱いに問題がある。
        /// 試行ログのヘッダに残して、後処理で試行の質を見られるようにする。
        /// </summary>
        public static float ResidualRms(
            in StimulusTransform transform, in Triangle referenceA, in Triangle baselineB)
        {
            var perVertex = ResidualPerVertex(transform, referenceA, baselineB);

            float sum = perVertex.x * perVertex.x
                        + perVertex.y * perVertex.y
                        + perVertex.z * perVertex.z;

            return Mathf.Sqrt(sum / 3f);
        }

        /// <summary>
        /// 頂点ごとの残差 [m]（x = V0, y = V1, z = V2）。
        ///
        /// RMS だけだと、どの頂点で合っていないのかが分からない。推定法によって
        /// 残差の配分が変わる（首頂点固定法なら V0 が 0 になる）ので、
        /// 手法の選択が妥当だったかを後から評価するために内訳を残す。
        /// </summary>
        public static Vector3 ResidualPerVertex(
            in StimulusTransform transform, in Triangle referenceA, in Triangle baselineB)
        {
            return new Vector3(
                Vector3.Distance(transform.Apply(referenceA.V0), baselineB.V0),
                Vector3.Distance(transform.Apply(referenceA.V1), baselineB.V1),
                Vector3.Distance(transform.Apply(referenceA.V2), baselineB.V2));
        }
    }
}
