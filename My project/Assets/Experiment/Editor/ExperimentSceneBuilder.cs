using System.Collections.Generic;
using System.Linq;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 実験モードのシーンと、起動時のモード選択シーンを生成する（仕様書 §3, §5.4, §5.5）。
    ///
    /// 実機用とエディタ検証用を作り分けない。<see cref="ExperimentDriver"/> は
    /// どちらでも同じものが走り、差し替わるのは頂点供給元と入力だけ。
    /// 検証したコードと本番のコードが違うと、検証の意味がなくなる。
    /// </summary>
    public static class ExperimentSceneBuilder
    {
        private const string ExperimentScenePath = SceneBuildUtility.SceneDirectory + "/Experiment.unity";
        private const string BootScenePath = SceneBuildUtility.SceneDirectory + "/Boot.unity";

        [MenuItem("Following Triangle/Build Experiment Scene")]
        public static void BuildExperimentScene()
        {
            const string title = "Build Experiment Scene";
            if (!SceneBuildUtility.CanBuildScene(title)) return;

            TmpEssentialResourcesInstaller.WarnIfMissing(title);
            SceneBuildUtility.EnsureSettingsAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = SceneBuildUtility.ReloadSettings();
            if (settings == null)
            {
                Debug.LogError($"[{title}] {SceneBuildUtility.SettingsPath} を読み込めませんでした。");
                return;
            }

            PopulateExperiment(settings);
            SceneBuildUtility.SaveScene(scene, ExperimentScenePath);

            Debug.Log($"[{title}] {ExperimentScenePath} を生成しました。");
        }

        [MenuItem("Following Triangle/Build Boot Scene")]
        public static void BuildBootScene()
        {
            const string title = "Build Boot Scene";
            if (!SceneBuildUtility.CanBuildScene(title)) return;

            TmpEssentialResourcesInstaller.WarnIfMissing(title);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SceneBuildUtility.ApplyCommonRenderSettings();
            SceneBuildUtility.CreatePreviewCamera(
                new Vector3(0f, 1.6f, -1.2f), Quaternion.Euler(4f, 0f, 0f));
            SceneBuildUtility.CreateBackgroundRoom();

            var inputObject = new GameObject("Session Input (Keyboard)");
            var input = inputObject.AddComponent<KeyboardRecorderInput>();

            // メニューは頭部に追従させない。起動直後は頭部参照が無いので、
            // ワールド固定の位置にテキストを置く。
            var hudObject = new GameObject("Menu HUD");
            hudObject.transform.position = new Vector3(0f, 1.5f, 0f);
            var hud = hudObject.AddComponent<RecorderHud>();

            var menuObject = new GameObject("Mode Selection Menu");
            var menu = menuObject.AddComponent<ModeSelectionMenu>();
            SceneBuildUtility.SetPrivateObjectField(menu, "inputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(menu, "hud", hud);

            SceneBuildUtility.SaveScene(scene, BootScenePath);

            Debug.Log($"[{title}] {BootScenePath} を生成しました。");
        }

        /// <summary>
        /// Boot / Recorder / Experiment を Build Settings に登録する。
        /// モード選択からのシーン遷移（§3）は登録されていないと失敗する。
        /// </summary>
        [MenuItem("Following Triangle/Register Scenes In Build Settings")]
        public static void RegisterScenes()
        {
            string[] wanted =
            {
                BootScenePath,
                SceneBuildUtility.SceneDirectory + "/Recorder.unity",
                ExperimentScenePath,
            };

            var existing = EditorBuildSettings.scenes.ToList();
            var result = new List<EditorBuildSettingsScene>();
            var missing = new List<string>();

            foreach (string path in wanted)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    missing.Add(path);
                    continue;
                }

                result.Add(new EditorBuildSettingsScene(path, true));
            }

            // 既存の登録のうち、ここで扱っていないものは順序を保って後ろに残す。
            foreach (var entry in existing)
            {
                if (wanted.Contains(entry.path)) continue;
                result.Add(entry);
            }

            EditorBuildSettings.scenes = result.ToArray();

            if (missing.Count > 0)
            {
                Debug.LogWarning(
                    "[Register Scenes] 未生成のシーンがあります。先に生成してください:\n  " +
                    string.Join("\n  ", missing));
            }

            Debug.Log(
                "[Register Scenes] Build Settings を更新しました:\n  " +
                string.Join("\n  ", result.Select(s => s.path)));
        }

        /// <summary>割付表を Console に出す。実験前に紙で確認するため（§5.5）。</summary>
        [MenuItem("Following Triangle/Print Counterbalance Table")]
        public static void PrintCounterbalanceTable()
        {
            Debug.Log(GraecoLatinSquare.DescribeCycle());
        }

        private static void PopulateExperiment(ExperimentSettings settings)
        {
            SceneBuildUtility.ApplyCommonRenderSettings();

            // 自己三角形と相手三角形の両方が同時に見える位置から。
            // 実機では OVRCameraRig に置き換える。
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

            var loggerObject = new GameObject("Trial Logger");
            var logger = loggerObject.AddComponent<TrialLogger>();
            SceneBuildUtility.SetPrivateObjectField(logger, "settings", settings);

            var instructionObject = new GameObject("Instruction Loader");
            var instructions = instructionObject.AddComponent<InstructionLoader>();

            var catalogObject = new GameObject("Stimulus Catalog");
            var catalog = catalogObject.AddComponent<StimulusCatalog>();
            SceneBuildUtility.SetPrivateObjectField(catalog, "settings", settings);

            var hudObject = new GameObject("HUD");
            var hud = hudObject.AddComponent<RecorderHud>();
            SceneBuildUtility.SetPrivateObjectField(hud, "headTransform", headProxy);

            var driverObject = new GameObject("Experiment Driver");
            var driver = driverObject.AddComponent<ExperimentDriver>();
            SceneBuildUtility.SetPrivateObjectField(driver, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(driver, "vertexSourceBehaviour", mockSource);
            SceneBuildUtility.SetPrivateObjectField(driver, "inputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(driver, "selfTriangleView", selfView);
            SceneBuildUtility.SetPrivateObjectField(driver, "stimulusPresenter", presenter);
            SceneBuildUtility.SetPrivateObjectField(driver, "trialLogger", logger);
            SceneBuildUtility.SetPrivateObjectField(driver, "instructionLoader", instructions);
            SceneBuildUtility.SetPrivateObjectField(driver, "stimulusCatalog", catalog);
            SceneBuildUtility.SetPrivateObjectField(driver, "hud", hud);
        }
    }
}
