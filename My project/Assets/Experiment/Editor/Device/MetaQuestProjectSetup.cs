using System.Text;
using UnityEditor;
using UnityEngine;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// Meta Quest 向けのプロジェクト設定（仕様書 §1）。
    ///
    /// 仕様書が要求する設定のうち、プロジェクト全体に関わるものをここでまとめて適用し、
    /// 適用できたかを検証して報告する。設定して終わりにしないのは、
    /// 要求どおりになっていないことに気づかないまま本番データを取る事態を避けるため。
    ///
    /// ハンドトラッキング関連はとくに重要で、ControllersOnly のままだと
    /// OVRSkeleton が骨格データを返さず V1/V2（両手の頂点）が取れない。
    /// エディタ上のモック検証ではこの問題が一切現れないため、実機で初めて発覚する。
    /// </summary>
    public static class MetaQuestProjectSetup
    {
        // Quest 3 の Horizon OS が要求する最小 API レベル。
        private const int MinimumAndroidSdkVersion = 32;

        [MenuItem("Following Triangle/Device (Quest)/Configure Project For Quest")]
        public static void Configure()
        {
            var report = new StringBuilder("[Quest Setup] 設定を適用しました\n");

            ApplyHandTracking(report);
            ApplyAndroidSettings(report);

            report.AppendLine();
            report.Append(BuildVerificationReport(out bool allSatisfied));

            if (allSatisfied) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        [MenuItem("Following Triangle/Device (Quest)/Verify Quest Configuration")]
        public static void Verify()
        {
            string report = BuildVerificationReport(out bool allSatisfied);

            if (allSatisfied) Debug.Log("[Quest Setup] 設定の検証\n" + report);
            else Debug.LogError("[Quest Setup] 設定の検証\n" + report);
        }

        private static void ApplyHandTracking(StringBuilder report)
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
            {
                report.AppendLine("  OVRProjectConfig を取得できませんでした。");
                return;
            }

            // 仕様書 §1：Hand Tracking Support = Controllers And Hands
            // 演者 A / 被験者 B は素手、実験者はコントローラ、という運用のため両方必要。
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;

            OVRProjectConfig.CommitProjectConfig(config);
            report.AppendLine("  Hand Tracking Support = ControllersAndHands");
        }

        private static void ApplyAndroidSettings(StringBuilder report)
        {
            // Quest は ARM64 のみ。
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            if ((int)PlayerSettings.Android.minSdkVersion < MinimumAndroidSdkVersion)
            {
                PlayerSettings.Android.minSdkVersion =
                    (AndroidSdkVersions)MinimumAndroidSdkVersion;
            }

            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // 色空間は Linear。Quest 向けの推奨設定で、刺激の見え方にも影響する。
            PlayerSettings.colorSpace = ColorSpace.Linear;

            report.AppendLine(
                $"  Android: ARM64 / IL2CPP / minSdk {(int)PlayerSettings.Android.minSdkVersion} / " +
                $"ColorSpace {PlayerSettings.colorSpace}");
        }

        private static string BuildVerificationReport(out bool allSatisfied)
        {
            var report = new StringBuilder();
            allSatisfied = true;

            var config = OVRProjectConfig.CachedProjectConfig;
            bool handsOk = config != null
                           && config.handTrackingSupport
                           != OVRProjectConfig.HandTrackingSupport.ControllersOnly;

            allSatisfied &= Line(report, handsOk,
                "§1 Hand Tracking Support",
                config != null ? config.handTrackingSupport.ToString() : "不明",
                "ControllersAndHands（ControllersOnly だと V1/V2 が取れない）");

            bool architectureOk = PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64;
            allSatisfied &= Line(report, architectureOk,
                "Android アーキテクチャ",
                PlayerSettings.Android.targetArchitectures.ToString(), "ARM64");

            bool backendOk = PlayerSettings.GetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP;
            allSatisfied &= Line(report, backendOk,
                "スクリプティングバックエンド",
                PlayerSettings.GetScriptingBackend(
                    UnityEditor.Build.NamedBuildTarget.Android).ToString(), "IL2CPP");

            bool sdkOk = (int)PlayerSettings.Android.minSdkVersion >= MinimumAndroidSdkVersion;
            allSatisfied &= Line(report, sdkOk,
                "Android minSdkVersion",
                ((int)PlayerSettings.Android.minSdkVersion).ToString(),
                MinimumAndroidSdkVersion + " 以上");

            bool platformOk = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            allSatisfied &= Line(report, platformOk,
                "アクティブなビルドターゲット",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                "Android（File > Build Profiles で切替）");

            // XR Plug-in Management のローダー登録はアセット側の状態なので、
            // ここでは存在確認のみ行う。詳細は docs/device_setup.md を参照。
            bool loaderAssetExists =
                AssetDatabase.LoadAssetAtPath<Object>("Assets/XR/Loaders/OpenXRLoader.asset") != null;
            allSatisfied &= Line(report, loaderAssetExists,
                "§1 OpenXR ローダー資産",
                loaderAssetExists ? "あり" : "なし",
                "XR Plug-in Management の Android タブで OpenXR を有効化");

            return report.ToString();
        }

        private static bool Line(
            StringBuilder report, bool ok, string label, string actual, string expected)
        {
            report.AppendLine($"  [{(ok ? "OK" : "NG")}] {label}: {actual}（期待: {expected}）");
            return ok;
        }
    }
}
