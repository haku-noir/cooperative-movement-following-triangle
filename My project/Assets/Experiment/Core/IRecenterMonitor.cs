using System;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 再センタリングの検出（仕様書 §1）。
    ///
    /// 実装は XR SDK 側にあるが、記録モードも実験モードも「何回起きたか」しか必要としない。
    /// Core に抽象を置くことで、記録・実験の進行ロジックが SDK アセンブリに依存せずに済み、
    /// エディタ上のモック検証（§8.1）がそのまま成立する。
    ///
    /// 再センタリングが起きるとワールド原点そのものが動く。発生前後のフレームは
    /// 同じ座標系に乗っていないため、記録なら刺激として使えず、
    /// 実験なら以降の誤差値が意味を持たない。
    /// </summary>
    public interface IRecenterMonitor
    {
        /// <summary>現在の計数区間で再センタリングが起きた回数。</summary>
        int RecenterCount { get; }

        /// <summary>再センタリング発生。引数は発生時刻 [s]。</summary>
        event Action<double> Recentered;

        /// <summary>試行／録画の開始時に呼び、計数を初期化する。</summary>
        void ResetPerTrialState();
    }
}
