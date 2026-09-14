using FollowingTriangle.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// エディタ検証用のキーボード入力（仕様書 §8.1）。
    ///
    /// Input System パッケージを使う。旧 Input Manager（UnityEngine.Input）は
    /// Unity 6.3 で非推奨になっており、本プロジェクトの activeInputHandler も
    /// Input System のみに設定されているため、UnityEngine.Input は使えない。
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyboardRecorderInput : MonoBehaviour, IRecorderInput
    {
        [SerializeField] private Key confirmKey = Key.Space;
        [SerializeField] private Key markerKey = Key.M;
        [SerializeField] private Key abortKey = Key.Escape;

        public string Description => $"Keyboard (confirm={confirmKey}, marker={markerKey}, abort={abortKey})";

        public bool ConfirmPressedThisFrame => WasPressed(confirmKey);
        public bool MarkerPressedThisFrame => WasPressed(markerKey);
        public bool AbortPressedThisFrame => WasPressed(abortKey);

        private static bool WasPressed(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }
    }
}
