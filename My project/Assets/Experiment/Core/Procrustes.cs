using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// Procrustes 分解の結果（仕様書 §6.1-2）。
    /// スケールは固定（1 倍）で、平行移動と回転のみを推定する。
    ///
    /// スケールを推定に含めないのは、体格正規化が既にキャリブレーション時の固定値として
    /// 適用済みだから（§5.2）。ここでスケールまで当てにいくと、
    /// 「腕を伸ばして大きさを合わせる」戦略が誤差を下げてしまう。
    /// </summary>
    public readonly struct ProcrustesResult
    {
        /// <summary>
        /// 剛体成分の平行移動 [m]。回転を重心まわりに取ったときの、重心そのものの変位。
        ///
        /// 回転中心の取り方で「平行移動量」の値は変わる。原点まわりの回転にすると、
        /// 重心が一致していても回転が大きいだけで平行移動量が大きく出る
        /// （首の高さ 1.5 m で 30 度回せば 0.7 m を超える）。
        /// 「三角形全体がどれだけずれたか」を表す量として意味を持つのは重心変位のほう。
        /// </summary>
        public readonly Vector3 Translation;

        /// <summary>平行移動量の大きさ [m]。CSV の proc_trans。</summary>
        public readonly float TranslationMagnitude;

        /// <summary>
        /// 剛体成分の回転角 [度]。3 自由度の完全回転を軸角表現にしたときの角度。
        /// CSV の proc_rot_deg。
        /// </summary>
        public readonly float RotationDegrees;

        /// <summary>
        /// 形状残差 [m]。推定した剛体変換を適用したあとに残る頂点距離の RMS。
        /// CSV の proc_residual。
        /// </summary>
        public readonly float ResidualRms;

        /// <summary>
        /// 回転が一意に定まったか。
        ///
        /// これは幾何的な退化の判定ではない。数値的に固有値が縮退して
        /// 回転が一意に決まらなかったかどうかだけを示す。
        /// 「三角形が細くなりすぎたか」という判断は閾値を含むので、
        /// アプリ側では行わない（§6.3）。そのための情報は最小内角の列として別に出す。
        /// </summary>
        public readonly bool RotationWellDefined;

        public ProcrustesResult(
            Vector3 translation, float rotationDegrees, float residualRms, bool rotationWellDefined)
        {
            Translation = translation;
            TranslationMagnitude = translation.magnitude;
            RotationDegrees = rotationDegrees;
            ResidualRms = residualRms;
            RotationWellDefined = rotationWellDefined;
        }
    }

    /// <summary>
    /// スケール固定の Procrustes 解析（仕様書 §6.1-2）。
    ///
    /// 3 点の対応から、a を b に最も近づける剛体変換（回転 R と並進 t）を求める。
    /// 回転は Horn の四元数法で解く。3x3 の相関行列から作った 4x4 対称行列の
    /// 最大固有値に対応する固有ベクトルが、求める回転の四元数になる。
    ///
    /// Unity の Quaternion 型を経由せず、生の成分計算から回転行列を組み立てている。
    /// Horn の定式化は座標系の向き（右手系／左手系）に依存した符号規約を持つため、
    /// Unity の Quaternion に変換する過程で符号を取り違えると、
    /// 「回転角は正しいのに残差だけ間違っている」という気づきにくい壊れ方をする。
    /// 生成した回転行列を実際に点へ適用して残差を測ることで、この種の誤りは
    /// 残差が 0 にならないという形で必ず表面化する。
    /// </summary>
    public static class Procrustes
    {
        /// <summary>
        /// a を b に重ねる剛体変換を求める。
        /// </summary>
        public static ProcrustesResult Solve(in Triangle a, in Triangle b)
        {
            Vector3 centroidA = a.Centroid;
            Vector3 centroidB = b.Centroid;

            var ca = new[] { a.V0 - centroidA, a.V1 - centroidA, a.V2 - centroidA };
            var cb = new[] { b.V0 - centroidB, b.V1 - centroidB, b.V2 - centroidB };

            double[,] rotation = SolveRotation(ca, cb, out bool wellDefined);

            // 回転は重心まわりに取る：  b ~= R * (a - centroidA) + centroidB
            // したがって剛体成分の平行移動は重心そのものの変位になる。
            Vector3 translation = centroidB - centroidA;

            // 残差は実際に変換を適用して測る。回転の符号規約を取り違えていれば
            // ここが 0 にならないので、間違いが必ず表に出る。
            double sumSquared = 0.0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 mapped = Multiply(rotation, ca[i]);
                sumSquared += (mapped - cb[i]).sqrMagnitude;
            }

            float residualRms = Mathf.Sqrt((float)(sumSquared / 3.0));
            float rotationDegrees = RotationAngleDegrees(rotation);

            return new ProcrustesResult(translation, rotationDegrees, residualRms, wellDefined);
        }

        /// <summary>
        /// 回転行列の軸角表現における角度 [度]。
        /// trace(R) = 1 + 2 cos(theta) から求める。座標系の向きに依存しないスカラ量。
        /// </summary>
        private static float RotationAngleDegrees(double[,] r)
        {
            double trace = r[0, 0] + r[1, 1] + r[2, 2];
            double cos = (trace - 1.0) * 0.5;

            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;

            return (float)(Math.Acos(cos) * Mathf.Rad2Deg);
        }

        private static Vector3 Multiply(double[,] r, Vector3 v)
        {
            return new Vector3(
                (float)(r[0, 0] * v.x + r[0, 1] * v.y + r[0, 2] * v.z),
                (float)(r[1, 0] * v.x + r[1, 1] * v.y + r[1, 2] * v.z),
                (float)(r[2, 0] * v.x + r[2, 1] * v.y + r[2, 2] * v.z));
        }

        /// <summary>
        /// Horn (1987) の四元数法。重心を除いた対応点から最適回転を求める。
        /// </summary>
        private static double[,] SolveRotation(Vector3[] a, Vector3[] b, out bool wellDefined)
        {
            // 相関行列 S[r,c] = sum_i a_i[r] * b_i[c]
            double sxx = 0, sxy = 0, sxz = 0;
            double syx = 0, syy = 0, syz = 0;
            double szx = 0, szy = 0, szz = 0;

            for (int i = 0; i < a.Length; i++)
            {
                sxx += (double)a[i].x * b[i].x; sxy += (double)a[i].x * b[i].y; sxz += (double)a[i].x * b[i].z;
                syx += (double)a[i].y * b[i].x; syy += (double)a[i].y * b[i].y; syz += (double)a[i].y * b[i].z;
                szx += (double)a[i].z * b[i].x; szy += (double)a[i].z * b[i].y; szz += (double)a[i].z * b[i].z;
            }

            // Horn の 4x4 対称行列。最大固有値の固有ベクトルが四元数 (w, x, y, z)。
            var n = new double[4, 4];
            n[0, 0] = sxx + syy + szz;
            n[0, 1] = n[1, 0] = syz - szy;
            n[0, 2] = n[2, 0] = szx - sxz;
            n[0, 3] = n[3, 0] = sxy - syx;
            n[1, 1] = sxx - syy - szz;
            n[1, 2] = n[2, 1] = sxy + syx;
            n[1, 3] = n[3, 1] = szx + sxz;
            n[2, 2] = -sxx + syy - szz;
            n[2, 3] = n[3, 2] = syz + szy;
            n[3, 3] = -sxx - syy + szz;

            SymmetricEigen.Decompose(n, 4, out double[] eigenvalues, out double[,] eigenvectors);

            int best = 0;
            for (int i = 1; i < 4; i++)
            {
                if (eigenvalues[i] > eigenvalues[best]) best = i;
            }

            // 最大固有値が 2 番目とほぼ同じなら、回転は一意に決まっていない。
            double second = double.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                if (i != best && eigenvalues[i] > second) second = eigenvalues[i];
            }

            double scaleReference = Math.Max(Math.Abs(eigenvalues[best]), 1e-12);
            wellDefined = (eigenvalues[best] - second) / scaleReference > 1e-6;

            double qw = eigenvectors[0, best];
            double qx = eigenvectors[1, best];
            double qy = eigenvectors[2, best];
            double qz = eigenvectors[3, best];

            double norm = Math.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
            if (norm < 1e-12)
            {
                wellDefined = false;
                return Identity3();
            }

            qw /= norm; qx /= norm; qy /= norm; qz /= norm;

            // 四元数から回転行列へ。この R は R * a_i ~= b_i を満たす。
            var r = new double[3, 3];
            r[0, 0] = qw * qw + qx * qx - qy * qy - qz * qz;
            r[0, 1] = 2.0 * (qx * qy - qw * qz);
            r[0, 2] = 2.0 * (qx * qz + qw * qy);
            r[1, 0] = 2.0 * (qy * qx + qw * qz);
            r[1, 1] = qw * qw - qx * qx + qy * qy - qz * qz;
            r[1, 2] = 2.0 * (qy * qz - qw * qx);
            r[2, 0] = 2.0 * (qz * qx - qw * qy);
            r[2, 1] = 2.0 * (qz * qy + qw * qx);
            r[2, 2] = qw * qw - qx * qx - qy * qy + qz * qz;

            return r;
        }

        private static double[,] Identity3()
        {
            var r = new double[3, 3];
            r[0, 0] = r[1, 1] = r[2, 2] = 1.0;
            return r;
        }
    }

    /// <summary>
    /// 対称行列の固有値分解（循環 Jacobi 法）。
    /// Procrustes の 4x4 だけに使う小さな実装。外部依存を増やさないために自前で持つ。
    /// </summary>
    internal static class SymmetricEigen
    {
        private const int MaxSweeps = 64;

        /// <summary>
        /// <paramref name="input"/> を破壊せずに固有値分解する。
        /// eigenvectors[:, k] が eigenvalues[k] に対応する固有ベクトル。
        /// </summary>
        public static void Decompose(
            double[,] input, int size, out double[] eigenvalues, out double[,] eigenvectors)
        {
            var a = new double[size, size];
            Array.Copy(input, a, input.Length);

            eigenvectors = new double[size, size];
            for (int i = 0; i < size; i++) eigenvectors[i, i] = 1.0;

            for (int sweep = 0; sweep < MaxSweeps; sweep++)
            {
                double offDiagonal = 0.0;
                for (int p = 0; p < size - 1; p++)
                {
                    for (int q = p + 1; q < size; q++) offDiagonal += a[p, q] * a[p, q];
                }

                if (offDiagonal < 1e-30) break;

                for (int p = 0; p < size - 1; p++)
                {
                    for (int q = p + 1; q < size; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;

                        double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        if (theta == 0.0) t = 1.0;

                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        Rotate(a, eigenvectors, size, p, q, c, s);
                    }
                }
            }

            eigenvalues = new double[size];
            for (int i = 0; i < size; i++) eigenvalues[i] = a[i, i];
        }

        private static void Rotate(
            double[,] a, double[,] v, int size, int p, int q, double c, double s)
        {
            double app = a[p, p];
            double aqq = a[q, q];
            double apq = a[p, q];

            a[p, p] = c * c * app - 2.0 * s * c * apq + s * s * aqq;
            a[q, q] = s * s * app + 2.0 * s * c * apq + c * c * aqq;
            a[p, q] = a[q, p] = 0.0;

            for (int i = 0; i < size; i++)
            {
                if (i == p || i == q) continue;

                double aip = a[i, p];
                double aiq = a[i, q];
                a[i, p] = a[p, i] = c * aip - s * aiq;
                a[i, q] = a[q, i] = s * aip + c * aiq;
            }

            for (int i = 0; i < size; i++)
            {
                double vip = v[i, p];
                double viq = v[i, q];
                v[i, p] = c * vip - s * viq;
                v[i, q] = s * vip + c * viq;
            }
        }
    }
}
