using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 録画の再生（仕様書 §7.3）。
    ///
    /// 再生は **フレーム番号ではなくタイムスタンプ基準** で行う。
    /// 録画時と再生時でフレームレートが一致する保証はなく、どちらも変動するため、
    /// フレーム番号で送ると刺激の時間軸が静かに伸び縮みする。それは
    /// 「追従の遅れ」として誤差に化けるが、後処理から区別できない。
    ///
    /// フレーム間は位置を線形補間、回転を Slerp で補間する。
    /// </summary>
    public sealed class RecordingPlayback
    {
        private readonly RecordingFile recording;
        private readonly bool recomputeNeckVertex;
        private readonly float neckOffsetD;
        private readonly double startTimestamp;

        /// <summary>
        /// 直前に参照したフレーム。再生は基本的に時間順に進むので、
        /// ここから線形に探すほうが毎回二分探索するより速い。巻き戻された場合は探索し直す。
        /// </summary>
        private int cursor;

        /// <summary>録画の全長 [s]。</summary>
        public double DurationSeconds { get; }

        public int FrameCount => recording.frames.Count;

        public RecordingFile Recording => recording;

        /// <param name="recomputeNeckVertex">
        /// true なら V0 を headPosition と <paramref name="neckOffsetD"/> から引き直す。
        /// false なら録画時の V0 をそのまま使う。録画には両方の情報が入っている。
        /// </param>
        public RecordingPlayback(
            RecordingFile recording, bool recomputeNeckVertex, float neckOffsetD)
        {
            this.recording = recording ?? throw new ArgumentNullException(nameof(recording));
            this.recomputeNeckVertex = recomputeNeckVertex;
            this.neckOffsetD = neckOffsetD;

            if (recording.frames.Count == 0)
            {
                throw new ArgumentException("フレームが 1 つもありません。", nameof(recording));
            }

            startTimestamp = recording.frames[0].t;
            DurationSeconds = recording.frames[recording.frames.Count - 1].t - startTimestamp;
        }

        /// <summary>
        /// 録画開始からの経過時刻でサンプルを取り出す。
        /// 範囲外なら端のフレームにクランプし、<paramref name="clamped"/> に true を返す。
        /// </summary>
        public BodyTriangleSample SampleAt(double elapsedSeconds, out bool clamped)
        {
            var frames = recording.frames;

            if (elapsedSeconds <= 0.0)
            {
                clamped = elapsedSeconds < 0.0;
                return ToSample(frames[0]);
            }

            if (elapsedSeconds >= DurationSeconds)
            {
                clamped = elapsedSeconds > DurationSeconds;
                return ToSample(frames[frames.Count - 1]);
            }

            clamped = false;
            double target = startTimestamp + elapsedSeconds;
            int index = FindBracketIndex(target);

            var before = frames[index];
            var after = frames[index + 1];

            double span = after.t - before.t;

            // 単調増加は RecordingSerializer が読み込み時に検証しているので span > 0。
            // それでも 0 除算を避けるため保険を入れる。
            float u = span > 1e-12 ? (float)((target - before.t) / span) : 0f;

            return Interpolate(before, after, u);
        }

        public BodyTriangleSample SampleAt(double elapsedSeconds) => SampleAt(elapsedSeconds, out _);

        /// <summary>
        /// 録画の基準姿勢区間を平均した三角形。Registration の A 側基準になる（§5.3）。
        ///
        /// 単一フレームではなく平均を使うのは、ハンドトラッキングのジッタ（数 mm）が
        /// 全試行に効く定数バイアスとして固定されるのを避けるため。
        /// B 側も 3 s の平均なので、扱いが揃う。
        /// </summary>
        public Triangle BaselineAverage(float baselineSeconds, out int sampleCount)
        {
            var accumulator = new TriangleAccumulator();

            foreach (var frame in recording.frames)
            {
                double elapsed = frame.t - startTimestamp;
                if (elapsed < 0.0) continue;
                if (elapsed >= baselineSeconds) break;

                accumulator.Add(ToSample(frame));
            }

            // 基準姿勢区間にフレームが無い録画（区間長 0 など）では、先頭フレームで代用する。
            if (!accumulator.HasSamples) accumulator.Add(ToSample(recording.frames[0]));

            sampleCount = accumulator.SampleCount;
            return accumulator.Average();
        }

        /// <summary>再生位置を先頭へ戻す。試行の開始時に呼ぶ。</summary>
        public void Rewind() => cursor = 0;

        private int FindBracketIndex(double targetTimestamp)
        {
            var frames = recording.frames;

            // 直前の位置から前進できるならそれで済ませる。
            if (cursor < frames.Count - 1
                && frames[cursor].t <= targetTimestamp
                && targetTimestamp < frames[cursor + 1].t)
            {
                return cursor;
            }

            if (cursor < frames.Count - 2
                && frames[cursor + 1].t <= targetTimestamp
                && targetTimestamp < frames[cursor + 2].t)
            {
                cursor++;
                return cursor;
            }

            // 大きく飛んだ場合は二分探索。frames[lo].t <= target < frames[lo+1].t を満たす lo を探す。
            int low = 0;
            int high = frames.Count - 1;

            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (frames[mid].t <= targetTimestamp) low = mid;
                else high = mid;
            }

            cursor = low;
            return low;
        }

        private BodyTriangleSample Interpolate(in RecordedFrame before, in RecordedFrame after, float u)
        {
            var sample = new BodyTriangleSample
            {
                timestampSeconds = before.t + (after.t - before.t) * u,
                headPosition = Vector3.Lerp(before.headPosition, after.headPosition, u),

                // 回転は Slerp（§7.3）。Lerp だと角速度が一定にならず、
                // 大きな回転で中間フレームの向きがずれる。
                headRotation = Quaternion.Slerp(before.headRotation, after.headRotation, u),

                v1 = Vector3.Lerp(before.v1, after.v1, u),
                v2 = Vector3.Lerp(before.v2, after.v2, u),

                // トラッキング状態は連続量ではないので補間しない。近いほうのフレームの値を採る。
                // 中間的な「0.5 の信頼度」を作ってしまうと、後処理での区間除外が曖昧になる。
                leftHand = (u < 0.5f ? before : after).LeftHandState(),
                rightHand = (u < 0.5f ? before : after).RightHandState(),
            };

            sample.v0 = recomputeNeckVertex
                ? VertexMath.NeckVertex(sample.headPosition, neckOffsetD)
                : Vector3.Lerp(before.v0, after.v0, u);

            return sample;
        }

        private BodyTriangleSample ToSample(in RecordedFrame frame) =>
            frame.ToSample(recomputeNeckVertex, neckOffsetD);
    }

    internal static class RecordedFrameStateExtensions
    {
        public static HandTrackingState LeftHandState(this in RecordedFrame frame) =>
            new HandTrackingState
            {
                isTracked = frame.leftTracked,
                confidence = (HandConfidence)frame.leftConfidence,
            };

        public static HandTrackingState RightHandState(this in RecordedFrame frame) =>
            new HandTrackingState
            {
                isTracked = frame.rightTracked,
                confidence = (HandConfidence)frame.rightConfidence,
            };
    }
}
