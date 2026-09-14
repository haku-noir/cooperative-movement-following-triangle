using System;
using System.Collections.Generic;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 録画 1 本ぶんの全内容（仕様書 §3.1）。JsonUtility でそのまま直列化できる形にしてある。
    /// </summary>
    [Serializable]
    public class RecordingFile
    {
        public RecordingMetadata metadata = new RecordingMetadata();
        public List<SegmentMarker> segmentMarkers = new List<SegmentMarker>();
        public List<RecordedFrame> frames = new List<RecordedFrame>();
    }

    /// <summary>
    /// 録画の再現に必要なメタデータ。仕様書 §6.2 は実験ログのヘッダに
    /// d・d/h・SDK バージョン・実効リフレッシュレートを求めているが、
    /// それらの一部は録画側にしか存在しないため、録画ファイルにも同じ情報を持たせる。
    /// </summary>
    [Serializable]
    public class RecordingMetadata
    {
        /// <summary>
        /// 形式のバージョン。後方互換を壊す変更をしたら上げる。
        /// 解析スクリプトが古い録画を誤読しないようにするため。
        /// </summary>
        public int formatVersion = CurrentFormatVersion;

        public const int CurrentFormatVersion = 1;

        public string recordingId = "";
        public string performerId = "";
        public string createdAtIso8601 = "";
        public string notes = "";

        // --- 頂点定義（§2.2, §2.3）---

        /// <summary>
        /// 記録時の重力方向オフセット d [m]。
        ///
        /// フレームには head pose と V0 の両方を保存してあるので、再生時は
        /// (a) 保存済み V0 をそのまま使う、(b) 再生時の d で head position から再計算する、
        /// のどちらも選べる。この値は (b) を選んだときに「元は何だったか」を示す。
        /// </summary>
        public float neckOffsetD;

        /// <summary>d / h。h はキャリブレーションで計測した眼高（§2.2）。</summary>
        public float normalizedNeckOffset;

        public string handVertexBone = "";

        // --- 取得環境（§1, §6.2）---

        public string vertexSourceKind = "";
        public string sdkVersion = "";
        public string unityVersion = "";
        public float requestedDisplayFrequencyHz;
        public float effectiveDisplayFrequencyHz;
        public bool displayFrequencyApplied;
        public string trackingOriginType = "";
        public bool trackingOriginIsFloorLevel;

        /// <summary>
        /// 記録中に再センタリングが起きた回数（仕様書 §1）。
        /// 0 でない録画を刺激として使ってはならない。ワールド原点が動いた時点で
        /// それ以降のフレームは前半と同じ座標系に乗っていない。
        /// </summary>
        public int recenterCount;

        // --- 演者の体格（§5.1）---

        public PerformerCalibration calibration = new PerformerCalibration();

        // --- スケジュール（§5.4）---

        public float baselineHoldSeconds;
        public float leadInSeconds;
        public float segmentSeconds;
        public int segmentCount;
        public string segmentMarkerMode = "";

        /// <summary>実測の記録長 [s]。スケジュール上の総時間とは一致しないことがある。</summary>
        public double recordedDurationSeconds;

        /// <summary>実測の平均フレームレート [Hz]（仕様書 §7.3）。</summary>
        public double meanFrameRateHz;

        /// <summary>フレーム間隔の最大値 [s]。取りこぼしの有無を後処理で判断するため。</summary>
        public double maxFrameIntervalSeconds;

        /// <summary>この録画が刺激として使える状態か。false なら理由が invalidReason に入る。</summary>
        public bool valid = true;

        public string invalidReason = "";
    }

    /// <summary>
    /// 仕様書 §5.1 のキャリブレーション結果。演者 A のものは録画ファイルに、
    /// 被験者 B のものは被験者プロファイルに入る。
    /// 体格正規化のスケール係数 s = (B の L 平均) / (A の L 平均) に使う（§5.2）。
    /// </summary>
    [Serializable]
    public class PerformerCalibration
    {
        /// <summary>床から CenterEyeAnchor までの高さ h [m]（§5.1-1）。</summary>
        public float eyeHeightMeters;

        /// <summary>首頂点から左手頂点までの距離 L_left [m]。</summary>
        public float neckToLeftHandMeters;

        /// <summary>首頂点から右手頂点までの距離 L_right [m]。</summary>
        public float neckToRightHandMeters;

        /// <summary>
        /// 体格指標 L = (L_left + L_right) / 2。
        /// これが §5.2 のスケール係数と §6.1 の正規化分母の両方の元になる固定値。
        /// </summary>
        public float characteristicLength;

        /// <summary>
        /// 保持中の L のばらつき [m]。基準姿勢が安定していたかの指標。
        /// 大きい場合はキャリブレーションをやり直す判断材料になる。
        /// </summary>
        public float characteristicLengthStdDev;

        public int sampleCount;
        public float holdDurationSeconds;

        public bool IsValid => sampleCount > 0 && characteristicLength > 0f && eyeHeightMeters > 0f;
    }

    /// <summary>区間マーカー（仕様書 §5.4）。</summary>
    [Serializable]
    public struct SegmentMarker
    {
        /// <summary>フェーズ識別子（baseline / lead_in / segment_1 ...）。</summary>
        public string phaseId;

        /// <summary>録画開始からの経過時刻 [s]。解析はこちらを使う。</summary>
        public double elapsedSeconds;

        /// <summary>OVRPlugin.GetTimeInSeconds() 基準の絶対時刻 [s]。</summary>
        public double absoluteTimestampSeconds;

        /// <summary>スケジュール上の予定時刻 [s]。手動マーカーとのずれを後から見るため。</summary>
        public float plannedStartSeconds;

        /// <summary>解析対象の区間か（基準姿勢・導入は false）。</summary>
        public bool includedInAnalysis;
    }

    /// <summary>
    /// 録画 1 フレーム。
    ///
    /// head pose と V0 の両方を保存する。V0 は記録時の d から算出した値であり、
    /// 再生時に d を変えたい場合は headPosition から再計算できる。
    /// どちらを使うかは再生側の設定で選ぶ。
    /// </summary>
    [Serializable]
    public struct RecordedFrame
    {
        /// <summary>OVRPlugin.GetTimeInSeconds() 基準の絶対時刻 [s]（§3.1）。</summary>
        public double t;

        public Vector3 headPosition;
        public Quaternion headRotation;

        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;

        public bool leftTracked;
        public int leftConfidence;
        public bool rightTracked;
        public int rightConfidence;

        public static RecordedFrame From(BodyTriangleSample sample)
        {
            return new RecordedFrame
            {
                t = sample.timestampSeconds,
                headPosition = sample.headPosition,
                headRotation = sample.headRotation,
                v0 = sample.v0,
                v1 = sample.v1,
                v2 = sample.v2,
                leftTracked = sample.leftHand.isTracked,
                leftConfidence = sample.leftHand.ConfidenceNumeric,
                rightTracked = sample.rightHand.isTracked,
                rightConfidence = sample.rightHand.ConfidenceNumeric,
            };
        }

        /// <summary>
        /// 再生側で使うサンプルに戻す。
        /// </summary>
        /// <param name="recomputeNeckVertex">
        /// true なら V0 を headPosition と <paramref name="neckOffsetD"/> から再計算する。
        /// false なら記録時の V0 をそのまま使う。
        /// </param>
        public BodyTriangleSample ToSample(bool recomputeNeckVertex, float neckOffsetD)
        {
            return new BodyTriangleSample
            {
                timestampSeconds = t,
                headPosition = headPosition,
                headRotation = headRotation,
                v0 = recomputeNeckVertex ? VertexMath.NeckVertex(headPosition, neckOffsetD) : v0,
                v1 = v1,
                v2 = v2,
                leftHand = new HandTrackingState
                {
                    isTracked = leftTracked,
                    confidence = (HandConfidence)leftConfidence,
                },
                rightHand = new HandTrackingState
                {
                    isTracked = rightTracked,
                    confidence = (HandConfidence)rightConfidence,
                },
            };
        }
    }
}
