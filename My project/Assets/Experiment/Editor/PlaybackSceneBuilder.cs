using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 段階3 の検証用シーン：録画の再生・Registration・体格正規化を HMD なしで確認する
    /// （仕様書 §5.2, §5.3, §7.3, §8.1）。
    ///
    /// 事前に Recorder シーンで録画を 1 本取っておくこと。
    /// recordings フォルダの最新ファイルが自動で読み込まれる。
    /// </summary>
    public static class PlaybackSceneBuilder
    {
        private const string Title = "Build Playback Scene";
        private const string ScenePath = SceneBuildUtility.SceneDirectory + "/Playback.unity";

        [MenuItem("Following Triangle/Build Playback Scene")]
        public static void BuildAndSave()
        {
            if (!SceneBuildUtility.CanBuildScene(Title)) return;

            TmpEssentialResourcesInstaller.WarnIfMissing(Title);
            SceneBuildUtility.EnsureSettingsAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = SceneBuildUtility.ReloadSettings();
            if (settings == null)
            {
                Debug.LogError($"[{Title}] {SceneBuildUtility.SettingsPath} を読み込めませんでした。");
                return;
            }

            Populate(settings);
            SceneBuildUtility.SaveScene(scene, ScenePath);

            Debug.Log($"[{Title}] {ScenePath} を生成しました。");
        }

        private static void Populate(ExperimentSettings settings)
        {
            SceneBuildUtility.ApplyCommonRenderSettings();

            // 自己三角形と相手三角形の両方が同時に見える位置から。
            SceneBuildUtility.CreatePreviewCamera(
                new Vector3(0f, 1.7f, -2.6f), Quaternion.Euler(6f, 0f, 0f));

            SceneBuildUtility.CreateBackgroundRoom();

            var mockSource = SceneBuildUtility.CreateMockVertexSource(
                settings, out var headProxy, out _, out _);

            var inputObject = new GameObject("Session Input (Keyboard)");
            var input = inputObject.AddComponent<KeyboardRecorderInput>();

            var selfViewObject = new GameObject("Self Triangle (B)");
            var selfView = selfViewObject.AddComponent<TriangleView>();

            var otherViewObject = new GameObject("Other Triangle (A, recorded)");
            var otherView = otherViewObject.AddComponent<TriangleView>();

            var presenterObject = new GameObject("Stimulus Presenter");
            var presenter = presenterObject.AddComponent<StimulusPresenter>();
            SceneBuildUtility.SetPrivateObjectField(presenter, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(presenter, "otherTriangleView", otherView);

            var hudObject = new GameObject("HUD");
            var hud = hudObject.AddComponent<RecorderHud>();
            SceneBuildUtility.SetPrivateObjectField(hud, "headTransform", headProxy);

            var driverObject = new GameObject("Playback Verification Driver");
            var driver = driverObject.AddComponent<PlaybackVerificationDriver>();
            SceneBuildUtility.SetPrivateObjectField(driver, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(driver, "vertexSourceBehaviour", mockSource);
            SceneBuildUtility.SetPrivateObjectField(driver, "inputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(driver, "selfTriangleView", selfView);
            SceneBuildUtility.SetPrivateObjectField(driver, "stimulusPresenter", presenter);
            SceneBuildUtility.SetPrivateObjectField(driver, "hud", hud);
            SceneBuildUtility.SetPrivateString(driver, "participantId", "MOCK-B");
        }
    }
}
