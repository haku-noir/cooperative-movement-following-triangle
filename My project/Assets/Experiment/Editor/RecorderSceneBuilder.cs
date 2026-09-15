using System.IO;
using System.Text;
using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// 段階2 の検証用シーン：記録モードを HMD なしで通しで動かす（仕様書 §3.1, §8.1）。
    ///
    /// 実機用のシーンではない。実機では MockVertexSource を OvrVertexSource に、
    /// KeyboardRecorderInput を OvrRecorderInput に、それぞれ差し替える。
    /// 差し替えても RecorderController から先の処理は同一のコードが走る。
    /// </summary>
    public static class RecorderSceneBuilder
    {
        private const string Title = "Build Recorder Scene";
        private const string ScenePath = SceneBuildUtility.SceneDirectory + "/Recorder.unity";

        [MenuItem("Following Triangle/Build Recorder Scene")]
        public static void BuildAndSave()
        {
            if (!SceneBuildUtility.CanBuildScene(Title)) return;

            // HUD が TextMeshPro を使うため、必須リソースが無いと文字が出ない。
            // ここで取り込まないのは、AssetDatabase.ImportPackage が非同期で、
            // 同一バッチ内でシーン生成と順序が競合するため。導入は別メニューに分ける。
            TmpEssentialResourcesInstaller.WarnIfMissing(Title);

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

            // 記録モードの確認は HUD の文字を読む必要があるので、演者の背後やや上から見る。
            SceneBuildUtility.CreatePreviewCamera(
                new Vector3(0f, 1.75f, -1.2f), Quaternion.Euler(8f, 0f, 0f));

            SceneBuildUtility.CreateBackgroundRoom();

            var mockSource = SceneBuildUtility.CreateMockVertexSource(
                settings, out var headProxy, out _, out _);

            var inputObject = new GameObject("Recorder Input (Keyboard)");
            var input = inputObject.AddComponent<KeyboardRecorderInput>();

            var performerViewObject = new GameObject("Performer Triangle");
            var performerView = performerViewObject.AddComponent<TriangleView>();

            var hudObject = new GameObject("Recorder HUD");
            var hud = hudObject.AddComponent<RecorderHud>();
            SceneBuildUtility.SetPrivateObjectField(hud, "headTransform", headProxy);

            var controllerObject = new GameObject("Recorder Controller");
            var controller = controllerObject.AddComponent<RecorderController>();
            SceneBuildUtility.SetPrivateObjectField(controller, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(controller, "vertexSourceBehaviour", mockSource);
            SceneBuildUtility.SetPrivateObjectField(controller, "recorderInputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(controller, "performerTriangleView", performerView);
            SceneBuildUtility.SetPrivateObjectField(controller, "hud", hud);
            SceneBuildUtility.SetPrivateString(controller, "performerId", "MOCK");
            SceneBuildUtility.SetPrivateString(controller, "recordingId", "editor-take");

            // recenterMonitorBehaviour と xrRuntimeInfoBehaviour は実機（XrRuntimeConfig）専用。
            // エディタ検証では未設定のままでよく、RecorderController は未設定を許容する。
        }

        /// <summary>
        /// 保存された録画を確認するために、保存先フォルダを開く。
        /// Quest 実機の persistentDataPath は adb で取り出すことになるが、
        /// エディタ検証ではこのメニューで足りる。
        /// </summary>
        [MenuItem("Following Triangle/Open Recordings Folder")]
        public static void OpenRecordingsFolder()
        {
            var settings = SceneBuildUtility.ReloadSettings();
            string directory = RecordingStorage.DirectoryFor(settings);

            Directory.CreateDirectory(directory);
            Debug.Log($"[{nameof(RecorderSceneBuilder)}] 録画フォルダ: {directory}");

            if (!Application.isBatchMode) EditorUtility.RevealInFinder(directory);
        }

        /// <summary>
        /// 試行 CSV ログの保存先フォルダを開く（仕様書 §6.2）。
        /// </summary>
        [MenuItem("Following Triangle/Open Logs Folder")]
        public static void OpenLogsFolder()
        {
            var settings = SceneBuildUtility.ReloadSettings();
            string directory = TrialLogger.DirectoryFor(settings);

            Directory.CreateDirectory(directory);
            Debug.Log($"[{nameof(RecorderSceneBuilder)}] 試行ログフォルダ: {directory}");

            if (!Application.isBatchMode) EditorUtility.RevealInFinder(directory);
        }

        /// <summary>
        /// 保存済み録画の要約をコンソールに出す。
        /// JSON を直接開かずに、フレーム数・区間マーカー・キャリブレーション値・
        /// 有効フラグを確認するため。
        /// </summary>
        [MenuItem("Following Triangle/Inspect Latest Recording")]
        public static void InspectLatestRecording()
        {
            var settings = SceneBuildUtility.ReloadSettings();
            var paths = RecordingStorage.ListRecordings(settings);

            if (paths.Count == 0)
            {
                Debug.LogWarning(
                    $"[{nameof(RecorderSceneBuilder)}] 録画がありません: " +
                    RecordingStorage.DirectoryFor(settings));
                return;
            }

            string latest = paths[paths.Count - 1];
            if (!RecordingStorage.TryLoad(latest, out var file, out string error))
            {
                Debug.LogError($"[{nameof(RecorderSceneBuilder)}] {latest}\n{error}");
                return;
            }

            double megabytes = new FileInfo(latest).Length / (1024.0 * 1024.0);
            Debug.Log(
                $"[{nameof(RecorderSceneBuilder)}] {latest} ({megabytes:F1} MB)\n" +
                RecordingSerializer.Summarize(file) + "\n" +
                BuildMarkerTable(file));
        }

        private static string BuildMarkerTable(RecordingFile file)
        {
            var builder = new StringBuilder("  区間マーカー:\n");
            foreach (var marker in file.segmentMarkers)
            {
                double drift = marker.elapsedSeconds - marker.plannedStartSeconds;
                builder.AppendLine(
                    $"    {marker.phaseId,-12} t={marker.elapsedSeconds,7:F3}s " +
                    $"(予定 {marker.plannedStartSeconds,6:F2}s, 差 {drift,7:F3}s) " +
                    $"analysis={marker.includedInAnalysis}");
            }

            return builder.ToString();
        }
    }
}
