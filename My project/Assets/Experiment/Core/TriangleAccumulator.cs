using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 三角形の平均を取る。基準姿勢 3 s のあいだの B の三角形（仕様書 §5.3-2）と、
    /// 録画側 A の基準姿勢区間の平均に使う。
    ///
    /// ここでは「頂点位置」を平均する。<see cref="CalibrationAccumulator"/> が
    /// 「距離」を平均するのと対照的だが、目的が違う。
    ///   * こちらは空間内の代表的な三角形が欲しい → 位置の平均
    ///   * あちらは体格を表すスカラ L が欲しい → 距離の平均
    /// 位置を平均してから距離を取ると、静止保持中のジッタで L がわずかに小さく出る。
    /// </summary>
    public sealed class TriangleAccumulator
    {
        private int sampleCount;
        private Vector3 sumV0;
        private Vector3 sumV1;
        private Vector3 sumV2;

        public int SampleCount => sampleCount;

        public bool HasSamples => sampleCount > 0;

        public void Reset()
        {
            sampleCount = 0;
            sumV0 = Vector3.zero;
            sumV1 = Vector3.zero;
            sumV2 = Vector3.zero;
        }

        public void Add(in BodyTriangleSample sample)
        {
            sumV0 += sample.v0;
            sumV1 += sample.v1;
            sumV2 += sample.v2;
            sampleCount++;
        }

        public void Add(in Triangle triangle)
        {
            sumV0 += triangle.V0;
            sumV1 += triangle.V1;
            sumV2 += triangle.V2;
            sampleCount++;
        }

        public Triangle Average()
        {
            if (sampleCount == 0) return new Triangle(Vector3.zero, Vector3.zero, Vector3.zero);

            float inverse = 1f / sampleCount;
            return new Triangle(sumV0 * inverse, sumV1 * inverse, sumV2 * inverse);
        }
    }
}
