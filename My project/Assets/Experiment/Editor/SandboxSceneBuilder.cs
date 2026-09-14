using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 段階1 の検証用シーン：自己三角形の描画だけを確認する（仕様書 §2.2, §4.1）。
    /// </summary>
    public static class SandboxSceneBuilder
    {
        private const string Title = "Build Sandbox Scene";
        private const string ScenePath = SceneBuildUtility.SceneDirectory + "/Sandbox.unity";

        [MenuItem("Following Triangle/Build Sandbox Scene")]
        public static void BuildAndSave()
        {
            if (!SceneBuildUtility.CanBuildScene(Title)) return;

            SceneBuildUtility.EnsureSettingsAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = SceneBuildUtility.ReloadSettings();
            if (settings == null)
            {
                Debug.LogError($"[{Title}] {SceneBuildUtility.SettingsPath} を読み込めませんでした。中止します。");
                return;
            }

            Populate(settings);
            SceneBuildUtility.SaveScene(scene, ScenePath);

            Debug.Log($"[{Title}] {ScenePath} を生成しました。");
        }

        private static void Populate(ExperimentSettings settings)
        {
            SceneBuildUtility.ApplyCommonRenderSettings();

            // 被験者のおおよその位置を斜め前から見る配置。実機の視点ではなく、
            // エディタで三角形の形を目視確認するためのカメラ。
            SceneBuildUtility.CreatePreviewCamera(
                new Vector3(0f, 1.6f, -2.2f), Quaternion.Euler(5f, 0f, 0f));

            SceneBuildUtility.CreateBackgroundRoom();

            var mockSource = SceneBuildUtility.CreateMockVertexSource(settings, out _, out _, out _);

            var selfViewObject = new GameObject("Self Triangle");
            var selfView = selfViewObject.AddComponent<TriangleView>();

            var presenterObject = new GameObject("Self Triangle Presenter");
            var presenter = presenterObject.AddComponent<SelfTrianglePresenter>();
            SceneBuildUtility.SetPrivateObjectField(presenter, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(presenter, "vertexSourceBehaviour", mockSource);
            SceneBuildUtility.SetPrivateObjectField(presenter, "selfTriangleView", selfView);
        }
    }
}
