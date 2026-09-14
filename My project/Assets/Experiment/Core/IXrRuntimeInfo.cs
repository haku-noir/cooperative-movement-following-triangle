namespace FollowingTriangle.Core
{
    /// <summary>
    /// 実行環境の情報。録画メタデータと CSV ヘッダ（仕様書 §6.2）に書き出す。
    ///
    /// 実装は XR SDK 側にあるが、記録・実験の進行ロジックからは「値が読めればよい」だけ
    /// なので Core に抽象を置く。エディタ検証時は実装が存在せず null になるため、
    /// 呼び出し側は未設定を許容する。
    /// </summary>
    public interface IXrRuntimeInfo
    {
        string SdkVersion { get; }

        float RequestedDisplayFrequencyHz { get; }

        /// <summary>設定後に読み戻した実効値。要求どおりに適用されたとは限らない（§1）。</summary>
        float EffectiveDisplayFrequencyHz { get; }

        bool DisplayFrequencyApplied { get; }

        string TrackingOriginType { get; }

        bool TrackingOriginIsFloorLevel { get; }
    }
}
