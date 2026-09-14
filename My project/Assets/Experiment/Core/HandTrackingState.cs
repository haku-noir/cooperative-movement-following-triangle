using System;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// ハンドトラッキングの信頼度（仕様書 §6.1-5, §7.1）。
    /// Quest のランタイムはカメラ視野外で外挿を行うため、後処理で低信頼区間を除外できるよう
    /// 毎フレーム記録する。数値は CSV にそのまま出す想定：Unknown = -1, Low = 0, High = 1。
    /// </summary>
    public enum HandConfidence
    {
        Unknown = -1,
        Low = 0,
        High = 1,
    }

    /// <summary>片手ぶんのトラッキング状態。</summary>
    [Serializable]
    public struct HandTrackingState
    {
        public bool isTracked;
        public HandConfidence confidence;

        public static HandTrackingState Untracked =>
            new HandTrackingState { isTracked = false, confidence = HandConfidence.Unknown };

        /// <summary>CSV 列 hand_L_conf / hand_R_conf 用の数値表現。</summary>
        public int ConfidenceNumeric => (int)confidence;
    }
}
