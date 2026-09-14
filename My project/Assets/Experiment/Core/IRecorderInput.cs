namespace FollowingTriangle.Core
{
    /// <summary>
    /// 記録モードの操作入力。
    ///
    /// 抽象にしてある理由は 2 つ。
    ///   1. 実機ではコントローラ、エディタ検証ではキーボードを使うため（仕様書 §8.1）
    ///   2. 旧 Input Manager は Unity 6.3 で非推奨。UnityEngine.Input への依存を
    ///      1 クラスに閉じ込めておけば、入力系の移行が記録ロジックに波及しない
    ///
    /// なお演者 A の手はハンドトラッキングで頂点を取る（§2.3）ため、
    /// A 自身がコントローラを持つことはできない。コントローラ入力を使う場合は
    /// 実験者が別に持つ前提になる。
    /// </summary>
    public interface IRecorderInput
    {
        /// <summary>このフレームで確定操作（次のステップへ進む）が押されたか。</summary>
        bool ConfirmPressedThisFrame { get; }

        /// <summary>このフレームで区間マーカーが押されたか（§5.4 のコントローラボタン）。</summary>
        bool MarkerPressedThisFrame { get; }

        /// <summary>このフレームで中断が押されたか。</summary>
        bool AbortPressedThisFrame { get; }

        /// <summary>この入力源の説明。ログとメタデータに残す。</summary>
        string Description { get; }
    }
}
