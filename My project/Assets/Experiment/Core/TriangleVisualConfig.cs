using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 三角形の描画パラメータ一式。自己三角形と相手三角形は同一の値を共有する
    /// （仕様書 §4.2：頂点球のサイズ・色・シェーダは完全に同一、§4.3：辺は自他両方に描画）。
    /// </summary>
    [Serializable]
    public struct TriangleVisualConfig : IEquatable<TriangleVisualConfig>
    {
        public float vertexSphereDiameter;
        public float edgeLineWidth;
        public Color vertexColor;
        public Color edgeColor;
        public bool drawEdges;

        public bool Equals(TriangleVisualConfig other)
        {
            return vertexSphereDiameter.Equals(other.vertexSphereDiameter)
                   && edgeLineWidth.Equals(other.edgeLineWidth)
                   && vertexColor.Equals(other.vertexColor)
                   && edgeColor.Equals(other.edgeColor)
                   && drawEdges == other.drawEdges;
        }

        public override bool Equals(object obj) => obj is TriangleVisualConfig other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = vertexSphereDiameter.GetHashCode();
                hash = (hash * 397) ^ edgeLineWidth.GetHashCode();
                hash = (hash * 397) ^ vertexColor.GetHashCode();
                hash = (hash * 397) ^ edgeColor.GetHashCode();
                hash = (hash * 397) ^ drawEdges.GetHashCode();
                return hash;
            }
        }

        public override string ToString() =>
            $"sphere={vertexSphereDiameter} edge={edgeLineWidth} " +
            $"vertexColor={vertexColor} edgeColor={edgeColor} drawEdges={drawEdges}";
    }

    /// <summary>
    /// 条件から描画パラメータを決める唯一の場所。
    ///
    /// 「C1 と C2 の表示が 1 ピクセルも違わない」ことを実装者の注意深さではなく構造で保証するため、
    /// 条件が描画に影響しうる経路を <see cref="DrawsEdges"/> ただ一つに絞っている。
    /// C1 と C2 はここで同じ分岐結果になるので、以降の描画コードは両者を区別する手段を持たない。
    ///
    /// さらに <see cref="For"/> は「自己か相手か」を引数に取らない。自他で異なる見た目を
    /// 作ろうとしても、呼び出し側から渡せる情報が存在しない（仕様書 §4.2, §4.3）。
    ///
    /// C1 と C2 の同一性は ConditionVisualsTests で固定してある。
    /// </summary>
    public static class ConditionVisuals
    {
        /// <summary>辺を描くのは C3 のみ（仕様書 §4.1, §4.3）。条件が描画に触れる唯一の分岐。</summary>
        public static bool DrawsEdges(Condition condition) => condition == Condition.C3;

        /// <summary>自己三角形・相手三角形の双方に、区別なく適用される描画パラメータ。</summary>
        public static TriangleVisualConfig For(Condition condition, ExperimentSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return new TriangleVisualConfig
            {
                vertexSphereDiameter = settings.VertexSphereDiameter,
                edgeLineWidth = settings.EdgeLineWidth,
                vertexColor = settings.VertexColor,
                edgeColor = settings.EdgeColor,
                drawEdges = DrawsEdges(condition),
            };
        }
    }
}
