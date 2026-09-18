using FollowingTriangle.Core;
using FollowingTriangle.Runtime;
using FollowingTriangle.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// Meta Quest 実機で走らせるシーンを生成する。
    ///
    /// エディタ用シーンとの違いは 2 箇所だけ。
    ///   頂点供給元  MockVertexSource   → OvrVertexSource
    ///   入力        KeyboardRecorderInput → OvrRecorderInput
    /// これに XR ランタイムの検証（XrRuntimeConfig）と OVRCameraRig が加わる。
    /// ExperimentDriver 以下のロジックはエディタとまったく同じものが走る。
    ///
    /// 手のメッシュは描画しない（後述）。
    /// </summary>
    public static class DeviceSceneBuilder
    {
        private const string CameraRigPrefabPath =
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        private const string HandPrefabPath =
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        private const string ExperimentScenePath =
            SceneBuildUtility.SceneDirectory + "/ExperimentDevice.unity";

        private const string RecorderScenePath =
            SceneBuildUtility.SceneDirectory + "/RecorderDevice.unity";

        // 仕様書 §1 / §2.3 が要求する設定値。SDK のソースから確認した数値。
        private const int TrackingOriginFloorLevel = 1;   // OVRPlugin.TrackingOrigin.FloorLevel
        private const int HandLeft = 0;                   // OVRPlugin.Hand / SkeletonType / MeshType
        private const int HandRight = 1;
        private const int OvrHandTypeLeft = 0;            // OVRPlugin.Hand.HandLeft
        private const int OvrHandTypeRight = 1;

        [MenuItem("Following Triangle/Device (Quest)/Build Device Scenes")]
        public static void BuildDeviceScenes()
        {
            const string title = "Build Device Scenes";
            if (!SceneBuildUtility.CanBuildScene(title)) return;

            TmpEssentialResourcesInstaller.WarnIfMissing(title);
            SceneBuildUtility.EnsureSettingsAsset();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath) == null)
            {
                Debug.LogError(
                    $"[{title}] {CameraRigPrefabPath} が見つかりません。" +
                    "Meta XR SDK が導入されているか確認してください。");
                return;
            }

            BuildExperimentScene();
            BuildRecorderScene();
            RegisterScenes();

            Debug.Log(
                $"[{title}] 実機用シーンを生成しました。\n" +
                $"  {ExperimentScenePath}\n" +
                $"  {RecorderScenePath}\n" +
                "Build Settings にも登録しました。Boot シーンのモード選択から遷移します。");
        }

        private static void BuildExperimentScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = SceneBuildUtility.ReloadSettings();
            if (settings == null) return;

            SceneBuildUtility.ApplyCommonRenderSettings();
            SceneBuildUtility.CreateBackgroundRoom();

            var rig = CreateOvrRig(settings, out var vertexSource, out var input, out var xrRuntime,
                out var centerEyeAnchor);

            var selfView = new GameObject("Self Triangle (B)").AddComponent<TriangleView>();
            var otherView = new GameObject("Other Triangle (A, recorded)").AddComponent<TriangleView>();

            var presenter = new GameObject("Stimulus Presenter").AddComponent<StimulusPresenter>();
            SceneBuildUtility.SetPrivateObjectField(presenter, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(presenter, "otherTriangleView", otherView);

            var logger = new GameObject("Trial Logger").AddComponent<TrialLogger>();
            SceneBuildUtility.SetPrivateObjectField(logger, "settings", settings);

            var instructions = new GameObject("Instruction Loader").AddComponent<InstructionLoader>();

            var catalog = new GameObject("Stimulus Catalog").AddComponent<StimulusCatalog>();
            SceneBuildUtility.SetPrivateObjectField(catalog, "settings", settings);

            var hud = new GameObject("HUD").AddComponent<RecorderHud>();
            SceneBuildUtility.SetPrivateObjectField(hud, "headTransform", centerEyeAnchor);

            var driver = new GameObject("Experiment Driver").AddComponent<ExperimentDriver>();
            SceneBuildUtility.SetPrivateObjectField(driver, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(driver, "vertexSourceBehaviour", vertexSource);
            SceneBuildUtility.SetPrivateObjectField(driver, "inputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(driver, "recenterMonitorBehaviour", xrRuntime);
            SceneBuildUtility.SetPrivateObjectField(driver, "xrRuntimeInfoBehaviour", xrRuntime);
            SceneBuildUtility.SetPrivateObjectField(driver, "selfTriangleView", selfView);
            SceneBuildUtility.SetPrivateObjectField(driver, "stimulusPresenter", presenter);
            SceneBuildUtility.SetPrivateObjectField(driver, "trialLogger", logger);
            SceneBuildUtility.SetPrivateObjectField(driver, "instructionLoader", instructions);
            SceneBuildUtility.SetPrivateObjectField(driver, "stimulusCatalog", catalog);
            SceneBuildUtility.SetPrivateObjectField(driver, "hud", hud);

            rig.name = "OVRCameraRig";
            SceneBuildUtility.SaveScene(scene, ExperimentScenePath);
        }

        private static void BuildRecorderScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var settings = SceneBuildUtility.ReloadSettings();
            if (settings == null) return;

            SceneBuildUtility.ApplyCommonRenderSettings();
            SceneBuildUtility.CreateBackgroundRoom();

            var rig = CreateOvrRig(settings, out var vertexSource, out var input, out var xrRuntime,
                out var centerEyeAnchor);

            var performerView = new GameObject("Performer Triangle").AddComponent<TriangleView>();

            var hud = new GameObject("HUD").AddComponent<RecorderHud>();
            SceneBuildUtility.SetPrivateObjectField(hud, "headTransform", centerEyeAnchor);

            var controller = new GameObject("Recorder Controller").AddComponent<RecorderController>();
            SceneBuildUtility.SetPrivateObjectField(controller, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(controller, "vertexSourceBehaviour", vertexSource);
            SceneBuildUtility.SetPrivateObjectField(controller, "recorderInputBehaviour", input);
            SceneBuildUtility.SetPrivateObjectField(controller, "recenterMonitorBehaviour", xrRuntime);
            SceneBuildUtility.SetPrivateObjectField(controller, "xrRuntimeInfoBehaviour", xrRuntime);
            SceneBuildUtility.SetPrivateObjectField(controller, "performerTriangleView", performerView);
            SceneBuildUtility.SetPrivateObjectField(controller, "hud", hud);

            rig.name = "OVRCameraRig";
            SceneBuildUtility.SaveScene(scene, RecorderScenePath);
        }

        /// <summary>
        /// OVRCameraRig と両手を配置し、実機用の頂点供給元・入力・ランタイム検証を作る。
        /// </summary>
        private static GameObject CreateOvrRig(
            ExperimentSettings settings, out MonoBehaviour vertexSource, out MonoBehaviour input,
            out MonoBehaviour xrRuntime, out Transform centerEyeAnchor)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // 仕様書 §1：トラッキング原点は Floor Level。
            // 眼高 h の計測（§5.1-1）が CenterEye のワールド Y に依存しているため、
            // ここが Eye Level だとキャリブレーションが成立しない。
            var manager = rig.GetComponent<OVRManager>();
            if (manager != null)
            {
                SceneBuildUtility.SetPrivateInt(manager, "_trackingOriginType", TrackingOriginFloorLevel);
            }

            centerEyeAnchor = FindDeep(rig.transform, "CenterEyeAnchor");
            var leftAnchor = FindDeep(rig.transform, "LeftHandAnchor");
            var rightAnchor = FindDeep(rig.transform, "RightHandAnchor");

            var leftHand = CreateHand(leftAnchor, "LeftHand", OvrHandTypeLeft, HandLeft);
            var rightHand = CreateHand(rightAnchor, "RightHand", OvrHandTypeRight, HandRight);

            var xrRuntimeObject = new GameObject("XR Runtime Config");
            var runtimeConfig = xrRuntimeObject.AddComponent<XrRuntimeConfig>();
            SceneBuildUtility.SetPrivateObjectField(runtimeConfig, "settings", settings);
            xrRuntime = runtimeConfig;

            var sourceObject = new GameObject("OVR Vertex Source");
            var ovrSource = sourceObject.AddComponent<OvrVertexSource>();
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "settings", settings);
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "centerEyeAnchor", centerEyeAnchor);
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "leftHand", leftHand.hand);
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "leftHandSkeleton", leftHand.skeleton);
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "rightHand", rightHand.hand);
            SceneBuildUtility.SetPrivateObjectField(ovrSource, "rightHandSkeleton", rightHand.skeleton);
            vertexSource = ovrSource;

            var inputObject = new GameObject("Session Input (OVR Controller)");
            input = inputObject.AddComponent<OvrRecorderInput>();

            return rig;
        }

        /// <summary>
        /// 片手ぶんの OVRHandPrefab を配置する。
        ///
        /// **手のメッシュは描画しない。** 仕様書 §4 が提示すると定めているのは
        /// 頂点球と（C3 では）辺、そして背景だけである。手が見えていると、
        /// 三角形を介さずに相手の手と自分の手を直接見比べられてしまい、
        /// 三角形提示の効果を測るという実験の目的が成立しない。
        /// 骨格データ（OVRSkeleton / OVRHand）は必要なので、描画だけを止める。
        /// </summary>
        private static (OVRHand hand, OVRSkeleton skeleton) CreateHand(
            Transform anchor, string handName, int handType, int skeletonAndMeshType)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            instance.name = handName;

            var hand = instance.GetComponent<OVRHand>();
            var skeleton = instance.GetComponent<OVRSkeleton>();

            if (hand != null) SceneBuildUtility.SetPrivateInt(hand, "HandType", handType);
            if (skeleton != null)
            {
                SceneBuildUtility.SetPrivateInt(skeleton, "_skeletonType", skeletonAndMeshType);
            }

            var mesh = instance.GetComponent<OVRMesh>();
            if (mesh != null) SceneBuildUtility.SetPrivateInt(mesh, "_meshType", skeletonAndMeshType);

            // 描画コンポーネントを無効化する。骨格データの取得には影響しない。
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.enabled = false;
            }

            var meshRenderer = instance.GetComponent<OVRMeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;

            var skeletonRenderer = instance.GetComponent<OVRSkeletonRenderer>();
            if (skeletonRenderer != null) skeletonRenderer.enabled = false;

            return (hand, skeleton);
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            var found = SearchDeep(root, childName);

            if (found == null)
            {
                Debug.LogError(
                    $"[DeviceSceneBuilder] {childName} が OVRCameraRig 内に見つかりません。" +
                    "Meta XR SDK のプレハブ構造が変わった可能性があります。");
            }

            return found;
        }

        /// <summary>
        /// 再帰の途中でログを出さない探索。見つからない部分木ごとにエラーを出すと、
        /// 全体としては成功していても大量のエラーが出て成否が判別できなくなる。
        /// </summary>
        private static Transform SearchDeep(Transform root, string childName)
        {
            if (root.name == childName) return root;

            foreach (Transform child in root)
            {
                var found = SearchDeep(child, childName);
                if (found != null) return found;
            }

            return null;
        }

        private static void RegisterScenes()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            foreach (string path in new[] { ExperimentScenePath, RecorderScenePath })
            {
                if (scenes.Exists(s => s.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
