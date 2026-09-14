using System;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 被験者プロファイル（仕様書 §5.1-3）。キャリブレーションは被験者ごとに 1 回だけ行い、
    /// 結果をここに固定する。
    ///
    /// 保存して使い回す理由は 2 つ。
    ///   1. §5.2 が「実行時のスケール自由度として残してはならない」と明示している。
    ///      試行ごとに取り直すと、そのたびに体格指標が変わり、実質的に実行時自由度になる。
    ///   2. 条件を跨いだ比較のためには、同一被験者の全試行で同じ L を使う必要がある。
    ///      試行ごとに L が変われば、正規化誤差の条件間比較が成立しない。
    /// </summary>
    [Serializable]
    public class ParticipantProfile
    {
        public int formatVersion = CurrentFormatVersion;

        public const int CurrentFormatVersion = 1;

        public string participantId = "";
        public string createdAtIso8601 = "";
        public string notes = "";

        /// <summary>キャリブレーション時の d [m]。d/h の分子。</summary>
        public float neckOffsetD;

        /// <summary>d / h（仕様書 §2.2）。CSV ヘッダに書き出す（§6.2）。</summary>
        public float normalizedNeckOffset;

        public PerformerCalibration calibration = new PerformerCalibration();

        public bool IsValid =>
            !string.IsNullOrEmpty(participantId) && calibration != null && calibration.IsValid;

        public static ParticipantProfile Create(
            string participantId, PerformerCalibration calibration, float neckOffsetD, string notes = "")
        {
            return new ParticipantProfile
            {
                formatVersion = CurrentFormatVersion,
                participantId = participantId ?? "",
                createdAtIso8601 = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                notes = notes ?? "",
                neckOffsetD = neckOffsetD,
                normalizedNeckOffset = calibration != null && calibration.eyeHeightMeters > 0f
                    ? neckOffsetD / calibration.eyeHeightMeters
                    : 0f,
                calibration = calibration ?? new PerformerCalibration(),
            };
        }

        public override string ToString()
        {
            var c = calibration;
            return $"{participantId}: h={c.eyeHeightMeters:F3}m L={c.characteristicLength:F3}m " +
                   $"(sd={c.characteristicLengthStdDev * 1000f:F1}mm, n={c.sampleCount}) " +
                   $"d={neckOffsetD:F3}m d/h={normalizedNeckOffset:F4}";
        }
    }
}
