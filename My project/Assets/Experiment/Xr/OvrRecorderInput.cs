using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Xr
{
    /// <summary>
    /// 記録モードのコントローラ入力（仕様書 §5.4 の「コントローラボタン」）。
    ///
    /// 重要：演者 A の手頂点はハンドトラッキングから取る（§2.3）ため、
    /// A 自身がコントローラを握ることはできない。握った手の骨格は取得できなくなる。
    /// このコンポーネントは **実験者が別に持つコントローラ** を前提としている。
    /// Quest は Controllers And Hands 設定下でコントローラと手を同時に追跡できるので、
    /// A は素手のまま、実験者が手元のコントローラで区間を区切る運用になる。
    ///
    /// OVRInput を使うのは Meta 固有のボタンマッピングをそのまま扱うため。
    /// 入力の抽象は Core の IRecorderInput 側にあるので、将来 Input System の
    /// XR バインディングへ移行する場合もこのクラスを差し替えるだけで済む。
    /// </summary>
    [DisallowMultipleComponent]
    public class OvrRecorderInput : MonoBehaviour, IRecorderInput
    {
        [SerializeField]
        [Tooltip("確定操作（次のステップへ進む）。既定は A / X ボタン。")]
        private OVRInput.Button confirmButton = OVRInput.Button.One;

        [SerializeField]
        [Tooltip("区間マーカー。既定は人差し指トリガー。押し間違いを避けるため確定操作とは分ける。")]
        private OVRInput.Button markerButton = OVRInput.Button.PrimaryIndexTrigger;

        [SerializeField]
        [Tooltip("中断。既定は B / Y ボタン。")]
        private OVRInput.Button abortButton = OVRInput.Button.Two;

        [SerializeField]
        [Tooltip("どのコントローラを見るか。既定は Active（いま使われているほう）。")]
        private OVRInput.Controller controller = OVRInput.Controller.Active;

        public string Description =>
            $"OVRInput (confirm={confirmButton}, marker={markerButton}, " +
            $"abort={abortButton}, controller={controller})";

        public bool ConfirmPressedThisFrame => OVRInput.GetDown(confirmButton, controller);

        public bool MarkerPressedThisFrame => OVRInput.GetDown(markerButton, controller);

        public bool AbortPressedThisFrame => OVRInput.GetDown(abortButton, controller);
    }
}
