using System.IO;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 動作テスト用の短縮構成を用意する。
    ///
    /// 本番のスケジュール（3 + 10 + 20x4 = 93 s）だと 1 セッション 5 分以上かかり、
    /// 「一通り動くか」を見るには長すぎる。ここでは 1 試行 16 s の短縮版を作る。
    ///
    /// **本番の設定・シーン・データには一切触れない。**
    ///   * 設定は別アセット      ExperimentSettings.QuickTest.asset
    ///   * シーンは別ファイル    ExperimentQuickTest.unity
    ///   * 刺激の保存先は別      recordings-quicktest/
    ///   * ログの保存先は別      logs-quicktest/
    ///   * 刺激 ID も別          qt1 / qt2 / qt3
    ///
    /// 短い区間長で取ったデータが本番データに混ざると、後から見分けるのは
    /// CSV ヘッダの segment_s を見るしかなくなる。物理的に分けておくほうが安全。
    ///
    /// 走るコードは本番とまったく同じ。ExperimentDriver も TrialLogger も
    /// 同じものが動き、違うのは与える設定値だけ。
    /// </summary>
    public static class QuickTestSetup
    {
        private const string SettingsPath =
            SceneBuildUtility.SettingsDirectory + "/ExperimentSettings.QuickTest.asset";

        private const string ScenePath =
            SceneBuildUtility.SceneDirectory + "/ExperimentQuickTest.unity";

        private const string StimulusPrefix = "qt";

        // 1 試行 = 2 + 2 + 3x4 = 16 s。3 試行で約 48 s。
        private const float CalibrationHoldSeconds = 2f;
        private const float BaselineHoldSeconds = 2f;
        private const float LeadInSeconds = 2f;
        private const float SegmentSeconds = 3f;

        [MenuItem("Following Triangle/Quick Test/Set Up Quick Test")]
        public static void SetUp()
        {
            const string title = "Set Up Quick Test";
            if (!SceneBuildUtility.CanBuildScene(title)) return;

            TmpEssentialResourcesInstaller.WarnIfMissing(title);

            var settings = EnsureQuickTestSettings();
            if (settings == null) return;

            if (!SyntheticRecordingGenerator.GenerateThreeStimuli(settings, StimulusPrefix))
            {
                Debug.LogError($"[{title}] 刺激の生成に失敗しました。中止します。");
                return;
            }

            BuildScene(settings);
            RegisterScene();

            // シーン生成の中で AssetDatabase.Refresh() が走り、設定アセットが
            // 読み直される。報告は必ずディスク上の最新を見る。
            settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);

            Debug.Log(
                $"[{title}] 準備できました。\n" +
                $"  シーン: {ScenePath}\n" +
                $"  設定  : {SettingsPath} (1 試行 {settings.TrialDurationSeconds:F0} s, " +
                $"3 試行で約 {settings.TrialDurationSeconds * 3f:F0} s)\n" +
                $"  刺激  : {RecordingStorage.DirectoryFor(settings)}\n" +
                $"  ログ  : {TrialLogger.DirectoryFor(settings)}\n" +
                "\n" +
                $"{ScenePath} を開いて Play してください。\n" +
                "  M キー: 選択を進める / Space キー: 決定 / Esc キー: 中断");
        }

        [MenuItem("Following Triangle/Quick Test/Open Quick Test Logs Folder")]
        public static void OpenLogs()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[Quick Test] 先に Set Up Quick Test を実行してください。");
                return;
            }

            string directory = TrialLogger.DirectoryFor(settings);
            Directory.CreateDirectory(directory);
            Debug.Log($"[Quick Test] ログフォルダ: {directory}");

            if (!Application.isBatchMode) EditorUtility.RevealInFinder(directory);
        }

        /// <summary>
        /// 動作テストで出た刺激とログを消す。本番のフォルダには触れない。
        /// </summary>
        [MenuItem("Following Triangle/Quick Test/Delete Quick Test Data")]
        public static void DeleteData()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[Quick Test] 設定アセットがありません。");
                return;
            }

            foreach (string directory in new[]
                     {
                         RecordingStorage.DirectoryFor(settings),
                         TrialLogger.DirectoryFor(settings),
                     })
            {
                if (!Directory.Exists(directory)) continue;

                Directory.Delete(directory, recursive: true);
                Debug.Log($"[Quick Test] 削除しました: {directory}");
            }
        }

        private static ExperimentSettings EnsureQuickTestSettings()
        {
            // 既定値から作り直す。前回の実行で誰かが値を変えていても、
            // 動作テストの条件が毎回同じになるようにする。
            var settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);

            if (settings == null)
            {
                Directory.CreateDirectory(SceneBuildUtility.SettingsDirectory);
                settings = ScriptableObject.CreateInstance<ExperimentSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            }

            if (settings == null)
            {
                Debug.LogError($"[Quick Test] {SettingsPath} を用意できません。");
                return null;
            }

            SceneBuildUtility.SetPrivateFloat(settings, "calibrationHoldSeconds", CalibrationHoldSeconds);
            SceneBuildUtility.SetPrivateFloat(settings, "baselineHoldSeconds", BaselineHoldSeconds);
            SceneBuildUtility.SetPrivateFloat(settings, "leadInSeconds", LeadInSeconds);
            SceneBuildUtility.SetPrivateFloat(settings, "segmentSeconds", SegmentSeconds);
            SceneBuildUtility.SetPrivateInt(settings, "segmentCount", 4);

            // 保存先を本番と分ける。混ざると後から見分けるのが難しい。
            SceneBuildUtility.SetPrivateString(settings, "recordingsDirectoryName", "recordings-quicktest");
            SceneBuildUtility.SetPrivateString(settings, "logsDirectoryName", "logs-quicktest");

            // 動作テストでは可読性より速度を優先する。
            SceneBuildUtility.SetPrivateBool(settings, "prettyPrintRecordingJson", false);

            // SetDirty してから SaveAssets しないと、変更がディスクへ確実に降りない。
            // その状態で AssetDatabase.Refresh() が走ると、アセットがディスクの
            // 古い内容で再読込され、設定が黙って本番の既定値に戻る。
            // （シーン生成の SaveScene 内の Refresh で実際に起きた）
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void BuildScene(ExperimentSettings settingsBeforeNewScene)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NewScene をまたいで持ち越したアセットのインスタンスを配線に使ってはいけない。
            // 代入自体は成功し、読み戻しても一致するのに、シーン保存時だけ
            // {fileID: 0} になる。必ず作り直したシーンの「後」に読み直す。
            var settings = AssetDatabase.LoadAssetAtPath<ExperimentSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogError($"[Quick Test] {SettingsPath} を読み込めませんでした。");
                return;
            }

            SceneBuildUtility.ApplyCommonRenderSettings();
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
            SceneBuildUtility.SetPrivateStringArray(
                catalog, "stimulusIds",
                new[] { StimulusPrefix + "1", StimulusPrefix + "2", StimulusPrefix + "3" });

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

            // 本番の被験者プロファイルを上書きしないよう、別の ID 接頭辞を使う。
            SceneBuildUtility.SetPrivateString(driver, "participantIdPrefix", "QT");

            SceneBuildUtility.SaveScene(scene, ScenePath);
        }

        private static void RegisterScene()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            if (scenes.Exists(s => s.path == ScenePath)) return;

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
