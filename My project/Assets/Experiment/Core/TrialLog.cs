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
        /// <summary>
        /// このファイルが何のログか。"trial"（追従課題）または "registration"（基準姿勢）。
        ///
        /// 2 つのログは列構成が完全に同一なので、ファイル名だけでなく中身でも
        /// 区別できるようにしておく。基準姿勢のデータを追従精度の解析に混ぜてしまうと、
        /// A がまだ見えていない区間の「誤差」を課題成績として数えることになる。
        /// </summary>
        public string logKind = "trial";

        public string participantId = "";

        /// <summary>被験者 ID 末尾の連番。割付行の決定に使った値（§5.5）。</summary>
        public int participantNumber;

        /// <summary>グレコ・ラテン方格の割付行（0 始まり）。</summary>
        public int assignmentRow;

        public string conditionId = "";
        public string stimulusId = "";

        /// <summary>セッション内の提示順（1 始まり）。順序効果の解析に使う。</summary>
        public int trialIndex;

        /// <summary>
        /// 実際に被験者へ提示した教示文（§7.4）。
        /// 教示ファイルを差し替えても、どの試行でどの文言を出したかが追える。
        /// C1 と C2 の差は教示文だけなので、これは実験操作そのものの記録である。
        /// </summary>
        public string instructionText = "";

        /// <summary>教示文を読み込んだファイルのパス。</summary>
        public string instructionSource = "";

        /// <summary>
        /// 刺激が 3 本すべて揃っていたか（§5.5）。
        /// false の試行はカウンターバランスが成立していない。
        /// </summary>
        public bool stimulusCatalogComplete = true;

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

        /// <summary>頂点ごとの Registration 残差 [m]（x = V0, y = V1, z = V2）。</summary>
        public Vector3 registrationResidualPerVertex;

        public StimulusTransform transform;

        /// <summary>Registration に使った A の基準三角形（変換前、録画の座標系）。</summary>
        public Triangle referenceTriangleA;

        /// <summary>Registration に使った B の基準三角形（3 s の平均）。</summary>
        public Triangle baselineTriangleB;

        /// <summary>基準姿勢フェーズの総フレーム数。</summary>
        public int baselineFrameCount;

        /// <summary>
        /// そのうち平均に採用されたサンプル数。両手が高信頼だったフレームのみ。
        /// 総フレーム数との差が大きい試行は Registration の信頼性が低い。
        /// </summary>
        public int baselineAcceptedCount;

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
