using System.IO;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 段階1 の検証用シーンを手続き的に組み立てる。
    ///
    /// シーンを手で作らず生成する理由：
    ///   * .unity は YAML なので、手作業で編集すると壊れやすく差分も読めない
    ///   * 検証シーンの構成を「コードとして」レビューできる
    ///   * batchmode から -executeMethod で再生成でき、CI 的な確認に載せられる
    /// </summary>
    public static class SandboxSceneBuilder
    {
        private const string SettingsDirectory = "Assets/Experiment/Settings";
        private const string SettingsPath = SettingsDirectory + "/ExperimentSettings.asset";
        private const string SceneDirectory = "Assets/Experiment/Scenes";
        private const string ScenePath = SceneDirectory + "/Sandbox.unity";

        [MenuItem("Following Triangle/Build Sandbox Scene")]
        public static void BuildAndSave()
        {
            // EditorSceneManager.NewScene は Play モード中に使えない。
            // ここで止めずに進むと、設定アセットだけ作られてシーンが生成されないという
            // 中途半端な状態になり、原因が分かりにくい。
            // Play モードを勝手に終了させることはしない。編集中の Play 状態を
            // メニュー操作で破棄されるのは予期しない挙動になるため。
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message =
                    "Play モード中はシーンを生成できません。Play を停止してから再実行してください。";

                Debug.LogError($"[SandboxSceneBuilder] {message}");

                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Build Sandbox Scene", message, "OK");
                }

                return;
            }

            // 未保存の変更があれば先に確認する。NewScene は現在のシーンを破棄するため。
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[SandboxSceneBuilder] ユーザー操作により中止しました。");
                return;
            }

            EnsureSettingsAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 設定アセットはシーンを作り直した「後」に読み直す。
            // NewScene をまたいで持ち越したインスタンスを使うと、参照が
            // {fileID: 0} として保存されてしまう（batchmode で実際に発生した）。
            var settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogError($"[SandboxSceneBuilder] {SettingsPath} を読み込めませんでした。中止します。");
                return;
            }

            Populate(settings);

            Directory.CreateDirectory(SceneDirectory);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SandboxSceneBuilder] {ScenePath} を生成しました。");
        }

        /// <summary>プロジェクト共通の設定アセット。無ければ既定値で作る。</summary>
        public static ExperimentSettings EnsureSettingsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            if (existing != null) return existing;

            Directory.CreateDirectory(SettingsDirectory);
            var settings = ScriptableObject.CreateInstance<ExperimentSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SandboxSceneBuilder] {SettingsPath} を既定値で作成しました。");
            return AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
        }

        private static void Populate(ExperimentSettings settings)
        {
            // 照明は均一（仕様書 §4.4）。刺激も背景も Unlit シェーダなので、
            // 環境光は「Unlit でない何かを後から足したときに極端な見え方にならない」ための保険。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f, 1f);
            RenderSettings.fog = false;

            var cameraObject = new GameObject("Editor Preview Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            cameraObject.AddComponent<AudioListener>();

            // 被験者のおおよその位置を斜め前から見る配置。実機の視点ではなく、
            // エディタで三角形の形を目視確認するためのカメラ。
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 1.6f, -2.2f),
                Quaternion.Euler(5f, 0f, 0f));

            var roomObject = new GameObject("Background Room");
            roomObject.AddComponent<BackgroundRoom>();

            var sourceObject = new GameObject("Mock Vertex Source");
            var mockSource = sourceObject.AddComponent<MockVertexSource>();
            SetPrivateObjectField(mockSource, "settings", settings);

            var selfViewObject = new GameObject("Self Triangle");
            var selfView = selfViewObject.AddComponent<TriangleView>();

            var presenterObject = new GameObject("Self Triangle Presenter");
            var presenter = presenterObject.AddComponent<SelfTrianglePresenter>();
            SetPrivateObjectField(presenter, "settings", settings);
            SetPrivateObjectField(presenter, "vertexSourceBehaviour", mockSource);
            SetPrivateObjectField(presenter, "selfTriangleView", selfView);

            SceneManager.SetActiveScene(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// private [SerializeField] をエディタから設定する。
        /// インスペクタで配線するのと完全に同じ結果になり、実行時 API を増やさずに済む。
        ///
        /// 代入したあと必ず読み戻して検証する。配線が黙って失われても、
        /// シーンは正常に生成されたように見えてしまい、Play するまで気づけないため。
        /// </summary>
        private static void SetPrivateObjectField(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[SandboxSceneBuilder] {target.GetType().Name}.{fieldName} が見つかりません。");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            serialized.Update();
            if (serialized.FindProperty(fieldName).objectReferenceValue != value)
            {
                Debug.LogError(
                    $"[SandboxSceneBuilder] {target.GetType().Name}.{fieldName} への参照設定に失敗しました " +
                    $"(設定しようとした値: {value})。生成されたシーンは不完全です。");
            }
        }
    }
}
