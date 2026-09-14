using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 仕様書 §5.1 のキャリブレーション。基準姿勢を保持している間のサンプルを蓄積し、
    /// 眼高 h と体格指標 L を確定させる。
    ///
    /// ここで出る値は実験の要になる固定値である。
    ///   * L は §5.2 の体格正規化スケール s = L_B / L_A の分子・分母
    ///   * L は §6.1 の誤差正規化の分母
    ///   * h は §2.2 の d / h
    /// いずれも実行時に再評価してはならない。被験者が腕を伸ばすだけで誤差が下がる
    /// 抜け道を作らないため、キャリブレーション時に一度だけ決める。
    ///
    /// 平均の取り方について：
    /// 仕様書 §5.1 は「その間の平均から算出」としか書いておらず、
    /// (a) 毎フレームの距離を平均する か (b) 平均位置から距離を出す かが未定義。
    /// ここでは (a) を採る。静止保持中の微小なジッタに対して素直で、
    /// ばらつき（標準偏差）を同時に出せるため保持の安定性を後から評価できる。
    /// 静止姿勢では両者の差はサブミリメートルであり、実質的な違いは生じない。
    /// </summary>
    public sealed class CalibrationAccumulator
    {
        private int sampleCount;
        private double eyeHeightSum;
        private double leftDistanceSum;
        private double rightDistanceSum;
        private double characteristicSum;
        private double characteristicSumOfSquares;
        private double firstTimestamp;
        private double lastTimestamp;

        public int SampleCount => sampleCount;

        /// <summary>蓄積した時間幅 [s]。</summary>
        public double HoldDurationSeconds => sampleCount == 0 ? 0.0 : lastTimestamp - firstTimestamp;

        public void Reset()
        {
            sampleCount = 0;
            eyeHeightSum = 0.0;
            leftDistanceSum = 0.0;
            rightDistanceSum = 0.0;
            characteristicSum = 0.0;
            characteristicSumOfSquares = 0.0;
            firstTimestamp = 0.0;
            lastTimestamp = 0.0;
        }

        /// <summary>
        /// 1 フレーム分を取り込む。
        ///
        /// 手のトラッキングが低信頼なフレームは呼び出し側で除外すること。
        /// キャリブレーションは実験全体の基準になるため、外挿された手の位置を
        /// 混ぜてはならない。
        /// </summary>
        public void Add(in BodyTriangleSample sample)
        {
            // 眼高 h：トラッキング原点が Floor Level なので、CenterEye の world Y がそのまま
            // 床からの高さになる（仕様書 §1, §5.1-1）。
            eyeHeightSum += sample.headPosition.y;

            float left = Vector3.Distance(sample.v1, sample.v0);
            float right = Vector3.Distance(sample.v2, sample.v0);
            float characteristic = 0.5f * (left + right);

            leftDistanceSum += left;
            rightDistanceSum += right;
            characteristicSum += characteristic;
            characteristicSumOfSquares += (double)characteristic * characteristic;

            if (sampleCount == 0) firstTimestamp = sample.timestampSeconds;
            lastTimestamp = sample.timestampSeconds;

            sampleCount++;
        }

        /// <summary>蓄積結果を確定する。サンプルが無ければ IsValid = false のものを返す。</summary>
        public PerformerCalibration Build(float neckOffsetD)
        {
            if (sampleCount == 0) return new PerformerCalibration();

            double inverseCount = 1.0 / sampleCount;
            double meanCharacteristic = characteristicSum * inverseCount;

            // 母分散。負になるのは丸め誤差のときだけなので 0 で下限を切る。
            double variance = (characteristicSumOfSquares * inverseCount)
                              - (meanCharacteristic * meanCharacteristic);
            if (variance < 0.0) variance = 0.0;

            float eyeHeight = (float)(eyeHeightSum * inverseCount);

            return new PerformerCalibration
            {
                eyeHeightMeters = eyeHeight,
                neckToLeftHandMeters = (float)(leftDistanceSum * inverseCount),
                neckToRightHandMeters = (float)(rightDistanceSum * inverseCount),
                characteristicLength = (float)meanCharacteristic,
                characteristicLengthStdDev = (float)System.Math.Sqrt(variance),
                sampleCount = sampleCount,
                holdDurationSeconds = (float)HoldDurationSeconds,
            };
        }

        /// <summary>
        /// 体格正規化のスケール係数 s = L_B / L_A（仕様書 §5.2）。
        ///
        /// この関数はキャリブレーション確定時に一度だけ呼ばれることを想定している。
        /// 実行時のスケール自由度として残してはならない。
        /// </summary>
        public static float BodyScaleFactor(
            PerformerCalibration performerA, PerformerCalibration participantB)
        {
            if (performerA == null || participantB == null) return 1f;
            if (!performerA.IsValid || !participantB.IsValid) return 1f;

            return participantB.characteristicLength / performerA.characteristicLength;
        }
    }
}
