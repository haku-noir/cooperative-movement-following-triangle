using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// Quest Link で「Play ボタンを押すと実機で動く」状態にする。
    ///
    /// APK をビルドして転送するより圧倒的に速く回せるので、実機での確認作業
    /// （§10-1 の視認性、§10-2 の重なり、ハンドトラッキングの実効範囲）に向く。
    ///
    /// 重要な前提：
    /// Unity のエディタ Play モードは、**アクティブなビルドターゲットに関係なく
    /// 必ず Standalone の XR 設定を使う**。
    /// XRGeneralSettingsPerBuildTarget.PlayModeStateChanged が
    /// SettingsForBuildTarget(BuildTargetGroup.Standalone) を読んでいる。
    ///
    /// つまりビルドターゲットを Android にしたままでも Link で Play できるが、
    /// Standalone 側に OpenXR ローダーが登録されていないと何も起きない。
    /// 実機ビルド用に Android だけ設定していると、ここが抜けたままになる。
    /// </summary>
    public static class QuestLinkSetup
    {
        private const string OpenXrLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("Following Triangle/Device (Quest)/Enable Play Mode Over Quest Link")]
        public static void Enable()
        {
            const string title = "Quest Link";

            if (!TryGetStandaloneSettings(out var settings, out string error))
            {
                Debug.LogError($"[{title}] {error}");
                return;
            }

            if (settings.Manager == null)
            {
                Debug.LogError($"[{title}] Standalone の XRManagerSettings がありません。");
                return;
            }

            if (IsOpenXrAssigned(settings.Manager))
            {
                Debug.Log($"[{title}] Standalone の OpenXR ローダーは既に有効です。\n" + ManualSteps());
                return;
            }

            bool assigned = XRPackageMetadataStore.AssignLoader(
                settings.Manager, OpenXrLoaderTypeName, BuildTargetGroup.Standalone);

            if (!assigned)
            {
                Debug.LogError(
                    $"[{title}] OpenXR ローダーを Standalone に割り当てられませんでした。\n" +
                    "Edit > Project Settings > XR Plug-in Management の " +
                    "Windows, Mac, Linux タブから手動で OpenXR にチェックを入れてください。");
                return;
            }

            // InitManagerOnStart が false だと Play モードで XR が起動しない。
            settings.InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
            AssetDatabase.SaveAssets();

            // 割り当てが登録済みの設定に入ったかを読み直して確認する。
            // 別のインスタンスに入っていた場合、ここで気づけないと
            // 「有効にしました」と言いながら Play モードで何も起きない状態になる。
            if (!TryGetStandaloneSettings(out var written, out _)
                || written.Manager == null || !IsOpenXrAssigned(written.Manager))
            {
                Debug.LogError(
                    $"[{title}] 割り当てが反映されていません。" +
                    "Edit > Project Settings > XR Plug-in Management の " +
                    "Windows, Mac, Linux タブから手動で OpenXR にチェックを入れてください。");
                return;
            }

            Debug.Log(
                $"[{title}] Standalone の OpenXR ローダーを有効にしました。\n" + ManualSteps());
        }

        [MenuItem("Following Triangle/Device (Quest)/Verify Quest Link Setup")]
        public static void Verify()
        {
            var report = new StringBuilder("[Quest Link] 設定の検証\n");
            bool ok = true;

            if (!TryGetStandaloneSettings(out var settings, out string error))
            {
                Debug.LogError(report + "  " + error);
                return;
            }

            bool loaderOk = settings.Manager != null && IsOpenXrAssigned(settings.Manager);
            ok &= Line(report, loaderOk, "Standalone の OpenXR ローダー",
                loaderOk ? "有効" : "無効",
                "Enable Play Mode Over Quest Link を実行");

            bool initOk = settings.InitManagerOnStart;
            ok &= Line(report, initOk, "Initialize XR on Startup",
                initOk ? "有効" : "無効", "有効");

            report.AppendLine();
            report.Append(ManualSteps());

            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        private static bool TryGetStandaloneSettings(out XRGeneralSettings settings, out string error)
        {
            settings = null;

            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget)
                || perTarget == null)
            {
                error = "XR Plug-in Management の設定が見つかりません。" +
                        "Edit > Project Settings > XR Plug-in Management を一度開いてください。";
                return false;
            }

            // 既存があれば必ずそれを使う。CreateDefaultManagerSettingsForBuildTarget を
            // 無条件に呼ぶと、既に Standalone の設定があっても新しいエントリが作られ、
            // そちらにローダーを割り当ててしまう。実際に登録されているほうは空のままなので、
            // 「有効にしました」と表示されるのに Play モードで XR が起動しない。
            settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);

            if (settings == null)
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
                settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            }

            if (settings == null)
            {
                error = "Standalone 用の XR 設定を作成できませんでした。";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsOpenXrAssigned(XRManagerSettings manager)
        {
            return manager.activeLoaders != null
                   && manager.activeLoaders.Any(
                       loader => loader != null
                                 && loader.GetType().FullName == OpenXrLoaderTypeName);
        }

        /// <summary>
        /// Unity 側では完結しない手順。とくにハンドトラッキングは既定で無効なので、
        /// これを忘れると V1/V2 が取れず「実機なのに手が認識されない」ことになる。
        /// </summary>
        private static string ManualSteps()
        {
            return
                "PC 側で必要な設定:\n" +
                "  1. Meta Quest Link (PC アプリ) をインストールして起動\n" +
                "  2. 設定 > 一般 > OpenXR ランタイム を「Meta Quest Link」に設定\n" +
                "  3. 設定 > ベータ > 開発者ランタイム機能 を有効化\n" +
                "     さらに「Link 経由のハンドトラッキング」を有効化\n" +
                "     ※ これを忘れると手の骨格が取れず V1/V2 が欠落する (§2.3)\n" +
                "  4. Quest を Link ケーブルで接続し、ヘッドセット内で Link を開始\n" +
                "     (Air Link でも可。ただし遅延が増えるため計測には有線を推奨)\n" +
                "\n" +
                "その後 Assets/Experiment/Scenes/ExperimentDevice.unity を開いて Play。\n" +
                "ビルドターゲットは Android のままで構わない。\n" +
                "エディタの Play モードは常に Standalone の XR 設定を使うため。";
        }

        private static bool Line(
            StringBuilder report, bool ok, string label, string actual, string expected)
        {
            report.AppendLine($"  [{(ok ? "OK" : "NG")}] {label}: {actual}（期待: {expected}）");
            return ok;
        }
    }
}
