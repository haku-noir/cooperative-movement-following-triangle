using System.IO;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 検証用シーンを手続き的に組み立てるための共通処理。
    ///
    /// シーンを手で作らず生成する理由：
    ///   * .unity は YAML なので、手作業で編集すると壊れやすく差分も読めない
    ///   * 検証シーンの構成を「コードとして」レビューできる
    ///   * batchmode から -executeMethod で再生成でき、自動確認に載せられる
    /// </summary>
    public static class SceneBuildUtility
    {
        public const string SettingsDirectory = "Assets/Experiment/Settings";
        public const string SettingsPath = SettingsDirectory + "/ExperimentSettings.asset";
        public const string SceneDirectory = "Assets/Experiment/Scenes";

        /// <summary>
        /// シーン生成の前提条件を確認する。
        /// EditorSceneManager.NewScene は Play モード中に使えず、途中で例外になると
        /// 設定アセットだけ作られてシーンが無いという分かりにくい状態になる。
        /// </summary>
        public static bool CanBuildScene(string title)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                const string message =
                    "Play モード中はシーンを生成できません。Play を停止してから再実行してください。";

                Debug.LogError($"[{title}] {message}");
                if (!Application.isBatchMode) EditorUtility.DisplayDialog(title, message, "OK");
                return false;
            }

            // NewScene は現在のシーンを破棄するので、未保存の変更を先に確認する。
            if (!Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log($"[{title}] ユーザー操作により中止しました。");
                return false;
            }

            return true;
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

            Debug.Log($"[SceneBuildUtility] {SettingsPath} を既定値で作成しました。");
            return AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
        }

        /// <summary>
        /// 設定アセットをシーン生成の「後」に読み直すためのヘルパ。
        /// NewScene をまたいで持ち越したインスタンスを使うと、参照が
        /// {fileID: 0} として保存されてしまう。
        /// </summary>
        public static ExperimentSettings ReloadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
        }

        public static void ApplyCommonRenderSettings()
        {
            // 照明は均一（仕様書 §4.4）。刺激も背景も Unlit シェーダなので、
            // 環境光は Unlit でない何かを後から足したときの保険。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f, 1f);
            RenderSettings.fog = false;
        }

        public static Camera CreatePreviewCamera(Vector3 position, Quaternion rotation)
        {
            var cameraObject = new GameObject("Editor Preview Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.SetPositionAndRotation(position, rotation);

            return camera;
        }

        public static BackgroundRoom CreateBackgroundRoom()
        {
            var roomObject = new GameObject("Background Room");
            return roomObject.AddComponent<BackgroundRoom>();
        }

        /// <summary>
        /// モック頂点供給元を、ダミー Transform を明示的にシーンへ置いた状態で作る。
        ///
        /// MockVertexSource は未設定なら Awake で自動生成するが、それだと生成前に
        /// 他のコンポーネント（HUD など）から参照を張れない。検証シーンでは
        /// 手で動かして確認したいので、シーンに実体として置いておく。
        /// </summary>
        public static MockVertexSource CreateMockVertexSource(
            ExperimentSettings settings,
            out Transform headProxy, out Transform leftHandProxy, out Transform rightHandProxy)
        {
            var sourceObject = new GameObject("Mock Vertex Source");
            var source = sourceObject.AddComponent<MockVertexSource>();

            headProxy = CreateChild(sourceObject.transform, "HeadProxy", new Vector3(0f, 1.60f, 0f));
            leftHandProxy = CreateChild(sourceObject.transform, "LeftHandProxy", new Vector3(-0.30f, 1.20f, 0.35f));
            rightHandProxy = CreateChild(sourceObject.transform, "RightHandProxy", new Vector3(0.30f, 1.20f, 0.35f));

            SetPrivateObjectField(source, "settings", settings);
            SetPrivateObjectField(source, "headProxy", headProxy);
            SetPrivateObjectField(source, "leftHandProxy", leftHandProxy);
            SetPrivateObjectField(source, "rightHandProxy", rightHandProxy);

            return source;
        }

        public static Transform CreateChild(Transform parent, string childName, Vector3 localPosition)
        {
            var child = new GameObject(childName).transform;
            child.SetParent(parent, worldPositionStays: false);
            child.localPosition = localPosition;
            return child;
        }

        public static void SaveScene(UnityEngine.SceneManagement.Scene scene, string scenePath)
        {
            Directory.CreateDirectory(SceneDirectory);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// private [SerializeField] をエディタから設定する。
        /// インスペクタで配線するのと完全に同じ結果になり、実行時 API を増やさずに済む。
        ///
        /// 代入したあと必ず読み戻して検証する。配線が黙って失われても、
        /// シーンは正常に生成されたように見えてしまい、Play するまで気づけないため。
        /// </summary>
        public static void SetPrivateObjectField(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[SceneBuildUtility] {target.GetType().Name}.{fieldName} が見つかりません。");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            serialized.Update();
            if (serialized.FindProperty(fieldName).objectReferenceValue != value)
            {
                Debug.LogError(
                    $"[SceneBuildUtility] {target.GetType().Name}.{fieldName} への参照設定に失敗しました " +
                    $"(設定しようとした値: {value})。生成されたシーンは不完全です。");
            }
        }

        public static void SetPrivateString(Object target, string fieldName, string value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[SceneBuildUtility] {target.GetType().Name}.{fieldName} が見つかりません。");
                return;
            }

            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
