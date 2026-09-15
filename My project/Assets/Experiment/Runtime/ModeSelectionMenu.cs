using FollowingTriangle.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 起動時のモード選択（仕様書 §3）。
    ///
    /// 単一プロジェクトに記録モードと実験モードの両方があるので、
    /// 起動直後にどちらを使うか選ぶ。実験者がコントローラで操作する。
    ///
    /// 誤って実験モードで刺激を撮ったり、記録モードで被験者を座らせたりしないよう、
    /// 選んだモードは Console にも残す。
    /// </summary>
    [DisallowMultipleComponent]
    public class ModeSelectionMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("IRecorderInput を実装した MonoBehaviour。マーカーキーで選択を切替、確定キーで決定。")]
        private MonoBehaviour inputBehaviour;

        [SerializeField] private RecorderHud hud;

        [SerializeField]
        [Tooltip("記録モードのシーン名 (§3.1)。Build Settings に登録が必要。")]
        private string recorderSceneName = "Recorder";

        [SerializeField]
        [Tooltip("実験モードのシーン名 (§3.2)。Build Settings に登録が必要。")]
        private string experimentSceneName = "Experiment";

        private IRecorderInput input;
        private bool experimentSelected = true;
        private bool loading;

        private void Awake()
        {
            input = inputBehaviour as IRecorderInput;

            if (input == null)
            {
                Debug.LogError(
                    $"[{nameof(ModeSelectionMenu)}] 入力が未設定です。モードを選択できません。", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (loading) return;

            if (input.MarkerPressedThisFrame) experimentSelected = !experimentSelected;

            string experimentMark = experimentSelected ? "▶" : "  ";
            string recorderMark = experimentSelected ? "  " : "▶";

            if (hud != null)
            {
                hud.SetMessage(
                    "モードを選択してください\n\n" +
                    $"{experimentMark} 実験モード（被験者の追従課題）\n" +
                    $"{recorderMark} 記録モード（刺激の撮影）\n\n" +
                    "マーカーキーで切替 / 確定キーで開始");
            }

            if (!input.ConfirmPressedThisFrame) return;

            string sceneName = experimentSelected ? experimentSceneName : recorderSceneName;
            Debug.Log($"[{nameof(ModeSelectionMenu)}] {sceneName} を開始します。");

            loading = true;
            SceneManager.LoadScene(sceneName);
        }
    }
}
