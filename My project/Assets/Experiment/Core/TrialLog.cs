using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// CSV 1 行ぶん（仕様書 §6.2）。
    /// 録画中にメモリへ溜め、試行終了時にまとめて書き出す。
    /// 毎フレーム同期書き込みをすると GC とストールでフレームレートが落ち、
    /// それ自体が追従誤差になる。
    /// </summary>
    public struct TrialLogRow
    {
        /// <summary>試行開始からの経過時刻 [s]。</summary>
        public double t;

        /// <summary>フェーズ識別子（baseline / lead_in / segment_1 ...）。</summary>
        public string phaseMarker;

        /// <summary>演者 A の頭部姿勢。変換後（B が見た空間）。</summary>
        public Vector3 headAPosition;
        public Quaternion headARotation;

        public Vector3 headBPosition;
        public Quaternion headBRotation;

        /// <summary>演者 A の 3 頂点。変換後。</summary>
        public Vector3 v0A;
        public Vector3 v1A;
        public Vector3 v2A;

        public Vector3 v0B;
        public Vector3 v1B;
        public Vector3 v2B;

        public FrameMetrics metrics;

        public bool handLeftTracked;
        public int handLeftConfidence;
        public bool handRightTracked;
        public int handRightConfidence;

        /// <summary>
        /// このフレームで再センタリングが発生したら 1、それ以外は 0（仕様書 §1）。
        ///
        /// 「この試行が汚染済みか」ではなく「このフレームで起きたか」を記録する。
        /// 後処理で累積和を取れば汚染区間が出るので、こちらのほうが情報を失わない。
        /// 試行全体の発生回数と有効フラグはヘッダに書く。
        /// </summary>
        public int recenterFlag;
    }

    /// <summary>
    /// CSV ヘッダに書くメタデータ（仕様書 §6.2）。
    ///
    /// 仕様書が要求するのは被験者ID・条件・刺激ID・d・d/h・スケール係数・
    /// SDK バージョン・実効リフレッシュレートだが、それだけでは
    /// A 側の座標列（変換後）から元の録画座標を復元できない。
    /// 再現性のため、適用した変換そのものと Registration の手法・残差も残す。
    /// </summary>
    public class TrialLogHeader
    {
        public string participantId = "";
        public string conditionId = "";
        public string stimulusId = "";
        public int trialIndex;

        public string recordedAtIso8601 = "";

        // §2.2
        public float neckOffsetD;
        public float normalizedNeckOffset;
        public string handVertexBone = "";

        // §5.1 / §5.2
        public float characteristicLengthA;
        public float characteristicLengthB;
        public float bodyScaleFactor;

        /// <summary>誤差の正規化に使った分母 [m]。キャリブレーション時の固定値。</summary>
        public float normalizationLength;

        // §5.3
        public string registrationMethod = "";
        public float registrationResidualRms;
        public StimulusTransform transform;

        // §1 / §6.2
        public string sdkVersion = "";
        public string unityVersion = "";
        public float requestedDisplayFrequencyHz;
        public float effectiveDisplayFrequencyHz;
        public bool displayFrequencyApplied;
        public string trackingOriginType = "";

        // §1 再センタリング
        public int recenterCount;
        public bool trialValid = true;
        public string invalidReason = "";

        // §5.4
        public float baselineHoldSeconds;
        public float leadInSeconds;
        public float segmentSeconds;
        public int segmentCount;

        // 刺激の出自
        public string recordingId = "";
        public string performerId = "";
        public string recordingFileName = "";

        public bool recomputeNeckVertexOnPlayback;

        /// <summary>実測の平均フレームレート [Hz]（§7.3）。</summary>
        public double meanFrameRateHz;

        public double maxFrameIntervalSeconds;
    }
}
