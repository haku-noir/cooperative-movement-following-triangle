using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 合成運動のパラメータ（仕様書 §8.2）。
    /// すべて既知の値なので、ここから作った運動に対する誤差量は解析的に予測できる。
    /// </summary>
    [Serializable]
    public struct SyntheticMotionParameters
    {
        [Tooltip("床から CenterEye までの高さ [m]。")]
        public float eyeHeight;

        [Tooltip("首頂点の重力方向オフセット d [m] (§2.2)。")]
        public float neckOffsetD;

        [Tooltip("首頂点から手までの左右方向の距離 [m]。")]
        public float armSpan;

        [Tooltip("首頂点から手までの前方向の距離 [m]。真上から見て 3 点が一直線にならないために必要。")]
        public float armForward;

        [Tooltip("頭部並進の振幅 [m]。")]
        public float headTranslationAmplitude;

        [Tooltip("頭部並進の周波数 [Hz]。")]
        public float headTranslationFrequencyHz;

        [Tooltip("頭部ヨーの振幅 [度]。V0 には影響しない (§2.2)。")]
        public float headYawAmplitudeDegrees;

        [Tooltip("頭部ピッチの振幅 [度]。V0 には影響しない (§2.2)。")]
        public float headPitchAmplitudeDegrees;

        [Tooltip("手の運動の振幅 [m]。")]
        public float handAmplitude;

        [Tooltip("手の運動の周波数 [Hz]。")]
        public float handFrequencyHz;

        [Tooltip("位相のずれ [rad]。刺激どうしを別物にするために使う。")]
        public float phaseOffsetRadians;

        /// <summary>仕様書 §9 の暫定値に沿った既定パラメータ。</summary>
        public static SyntheticMotionParameters Default => new SyntheticMotionParameters
        {
            eyeHeight = 1.60f,
            neckOffsetD = 0.15f,
            armSpan = 0.40f,
            armForward = 0.20f,
            headTranslationAmplitude = 0.15f,
            headTranslationFrequencyHz = 0.25f,
            headYawAmplitudeDegrees = 25f,
            headPitchAmplitudeDegrees = 12f,
            handAmplitude = 0.18f,
            handFrequencyHz = 0.35f,
            phaseOffsetRadians = 0f,
        };
    }

    /// <summary>
    /// 既知の正弦波運動（仕様書 §8.2）。
    ///
    /// 仕様書 §5.4 の区間構成をそのまま再現する。
    ///
    ///   基準姿勢  静止
    ///   導入      複合（頭部並進 + 手）
    ///   区間1     複合
    ///   区間2     並進のみ（手は身体に対して静止。身体と一緒に並進はする）
    ///   区間3     手のみ（頭部は静止）
    ///   区間4     複合
    ///
    /// 区間の切れ目で位置が飛ばないよう、各区間の運動には
    /// 両端で 0 になる包絡（sin^2）を掛けている。これにより
    /// 周波数と区間長の関係に制約を設けずに連続性が保たれる。
    ///
    /// **頭部の回転は V0 に一切影響しない。** V0 は必ず
    /// <see cref="VertexMath.NeckVertex"/> 経由で作られ、回転は渡されない（§2.2）。
    /// 合成運動でヨーとピッチを大きく振っているのは、その性質を
    /// 生成された録画でも確認できるようにするため。
    /// </summary>
    public sealed class SyntheticMotion
    {
        private readonly SyntheticMotionParameters parameters;
        private readonly RecordingSchedule schedule;

        public SyntheticMotionParameters Parameters => parameters;

        public RecordingSchedule Schedule => schedule;

        /// <summary>静止時の首頂点。スケールの基準にもなる。</summary>
        public Vector3 RestNeck => VertexMath.NeckVertex(RestHead, parameters.neckOffsetD);

        public Vector3 RestHead => new Vector3(0f, parameters.eyeHeight, 0f);

        /// <summary>静止姿勢での首-手距離。キャリブレーションの L に一致する。</summary>
        public float RestCharacteristicLength =>
            Mathf.Sqrt(parameters.armSpan * parameters.armSpan
                       + parameters.armForward * parameters.armForward);

        public SyntheticMotion(SyntheticMotionParameters parameters, RecordingSchedule schedule)
        {
            this.parameters = parameters;
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        /// <summary>
        /// 経過時刻におけるサンプル。
        /// </summary>
        /// <param name="elapsedSeconds">録画開始からの経過時刻 [s]。</param>
        /// <param name="absoluteTimestamp">サンプルに書き込むタイムスタンプ [s]。</param>
        public BodyTriangleSample Sample(double elapsedSeconds, double absoluteTimestamp)
        {
            ResolveMotionGates(elapsedSeconds, out bool headMoves, out bool handsMove, out float tau,
                out float segmentDuration);

            float envelope = Envelope(tau, segmentDuration);

            float headPhase = 2f * Mathf.PI * parameters.headTranslationFrequencyHz * tau
                              + parameters.phaseOffsetRadians;
            float handPhase = 2f * Mathf.PI * parameters.handFrequencyHz * tau
                              + parameters.phaseOffsetRadians;

            float headShift = headMoves
                ? parameters.headTranslationAmplitude * Mathf.Sin(headPhase) * envelope
                : 0f;

            Vector3 head = RestHead + new Vector3(headShift, 0f, 0f);

            // 頭部の回転。V0 の算出には使わない（§2.2）。
            var headRotation = headMoves
                ? Quaternion.Euler(
                    parameters.headPitchAmplitudeDegrees * Mathf.Cos(headPhase) * envelope,
                    parameters.headYawAmplitudeDegrees * Mathf.Sin(headPhase) * envelope,
                    0f)
                : Quaternion.identity;

            Vector3 neck = VertexMath.NeckVertex(head, parameters.neckOffsetD);

            // 手は首頂点からの相対位置で決める。区間2 では相対位置が固定され、
            // 身体の並進にそのまま乗る（「手を静止させ身体だけ並進させる」）。
            float handLift = handsMove
                ? parameters.handAmplitude * Mathf.Cos(handPhase) * envelope
                : 0f;

            var leftOffset = new Vector3(-parameters.armSpan, handLift, parameters.armForward);
            var rightOffset = new Vector3(parameters.armSpan, -handLift, parameters.armForward);

            return new BodyTriangleSample
            {
                timestampSeconds = absoluteTimestamp,
                headPosition = head,
                headRotation = headRotation,
                v0 = neck,
                v1 = neck + leftOffset,
                v2 = neck + rightOffset,
                leftHand = new HandTrackingState
                {
                    isTracked = true, confidence = HandConfidence.High,
                },
                rightHand = new HandTrackingState
                {
                    isTracked = true, confidence = HandConfidence.High,
                },
            };
        }

        /// <summary>
        /// その時刻でどの部位が動くかを、仕様書 §5.4 の区間定義から決める。
        /// </summary>
        private void ResolveMotionGates(
            double elapsedSeconds, out bool headMoves, out bool handsMove,
            out float segmentRelativeTime, out float segmentDuration)
        {
            headMoves = false;
            handsMove = false;
            segmentRelativeTime = 0f;
            segmentDuration = 1f;

            if (!schedule.TryGetPhaseAt((float)elapsedSeconds, out var phase)) return;

            segmentRelativeTime = (float)elapsedSeconds - phase.StartSeconds;
            segmentDuration = phase.DurationSeconds;

            switch (phase.Id)
            {
                case RecordingSchedule.BaselinePhaseId:
                    // 基準姿勢は静止（§5.3-1）。Registration の基準になるので動かしてはいけない。
                    break;

                case "segment_2":
                    // 並進のみ：手は身体に対して静止させ、身体だけ並進させる。
                    headMoves = true;
                    break;

                case "segment_3":
                    // 手のみ：頭部を静止させ、手だけ動かす。
                    handsMove = true;
                    break;

                default:
                    // 導入・区間1・区間4 は複合。
                    headMoves = true;
                    handsMove = true;
                    break;
            }
        }

        /// <summary>
        /// 区間の両端で 0 になる包絡。区間の切れ目で位置が飛ばないようにする。
        /// 微分も両端で 0 になるので、速度も不連続にならない。
        /// </summary>
        private static float Envelope(float tau, float duration)
        {
            if (duration <= 0f) return 0f;

            float normalized = Mathf.Clamp01(tau / duration);
            float s = Mathf.Sin(Mathf.PI * normalized);
            return s * s;
        }
    }
}
