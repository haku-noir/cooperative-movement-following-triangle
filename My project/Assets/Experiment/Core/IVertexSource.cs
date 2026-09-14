namespace FollowingTriangle.Core
{
    /// <summary>
    /// 身体三角形の供給元。実装は以下を想定する。
    ///   1. OvrVertexSource    : 実機の HMD / ハンドトラッキングから取得
    ///   2. MockVertexSource   : HMD なしでエディタ検証するための合成入力（仕様書 §8.1）
    ///   3. （段階3で追加）録画再生ソース
    ///
    /// この抽象を段階1の時点で入れる理由：実装者は実機での動作確認を毎回は行えないため、
    /// 頂点の供給元を差し替えられない構造にすると、仕様書 §8 のエディタ検証手段が
    /// 後から実装不能になる。
    /// </summary>
    public interface IVertexSource
    {
        /// <summary>このフレームで有効なサンプルを供給できるか。</summary>
        bool IsAvailable { get; }

        /// <summary>現在のサンプルを取得する。取得できない場合は false を返し、sample は未定義。</summary>
        bool TryGetSample(out BodyTriangleSample sample);
    }
}
