using System.IO;
using UnityEditor;
using UnityEngine;

// UnityEditor.PackageInfo（旧 API）と名前が衝突するため明示的に別名にする。
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace FollowingTriangle.Editor
{
    /// <summary>
    /// TextMeshPro の必須リソース（TMP Settings と既定フォントアセット）を導入する。
    ///
    /// 記録モードの HUD が TextMeshPro を使うため、これが無いと実行時にフォントが
    /// 解決できず文字が出ない。TMP のリソースは ugui パッケージ内の .unitypackage として
    /// 配布されており、プロジェクトへ取り込むまで Resources.Load&lt;TMP_Settings&gt;("TMP Settings")
    /// が null を返す。
    ///
    /// TMP 純正のメニュー（Window &gt; TextMeshPro &gt; Import TMP Essential Resources）は
    /// 対話ダイアログ前提で batchmode から呼べないため、非対話で取り込む経路を用意する。
    /// これにより、実装者が実機やエディタ UI を触れない状況でも導入状態を再現できる。
    ///
    /// 旧 TextMesh（UnityEngine.TextRenderingModule）を使えばリソース導入は不要だが、
    /// そちらはレガシー API であり、将来の移行先としては TextMeshPro が正である。
    /// </summary>
    public static class TmpEssentialResourcesInstaller
    {
        private const string PackageName = "com.unity.ugui";
        private const string RelativePackagePath = "Package Resources/TMP Essential Resources.unitypackage";

        /// <summary>TMP Settings がプロジェクトに存在するか。</summary>
        public static bool IsInstalled => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        [MenuItem("Following Triangle/Import TMP Essential Resources")]
        public static void Install()
        {
            if (IsInstalled)
            {
                Debug.Log(
                    $"[{nameof(TmpEssentialResourcesInstaller)}] TMP Settings は既に存在します。何もしません。");
                return;
            }

            if (!TryResolvePackagePath(out string packagePath, out string error))
            {
                Debug.LogError($"[{nameof(TmpEssentialResourcesInstaller)}] {error}");
                return;
            }

            Debug.Log($"[{nameof(TmpEssentialResourcesInstaller)}] 取り込み開始: {packagePath}");

            // interactive: false。batchmode でもダイアログを出さずに取り込む。
            AssetDatabase.ImportPackage(packagePath, interactive: false);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// batchmode 用のエントリポイント。-quit を付けずに呼ぶこと。
        ///
        /// AssetDatabase.ImportPackage は非同期で、-quit を付けると取り込みが終わる前に
        /// Editor が落ちてしまう（何も取り込まれないまま exit 0 で成功したように見える）。
        /// 完了コールバックを待ってから明示的に終了する。
        ///
        /// 使い方:
        ///   Unity.exe -batchmode -nographics -projectPath &lt;path&gt;
        ///     -executeMethod FollowingTriangle.Editor.TmpEssentialResourcesInstaller.InstallAndExit
        /// </summary>
        public static void InstallAndExit()
        {
            if (IsInstalled)
            {
                Debug.Log($"[{nameof(TmpEssentialResourcesInstaller)}] 既に導入済みです。");
                EditorApplication.Exit(0);
                return;
            }

            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageFailed += OnImportFailed;
            AssetDatabase.importPackageCancelled += OnImportCancelled;

            // 取り込みが何らかの理由で返ってこない場合に、バッチが永遠にぶら下がるのを防ぐ。
            watchdogDeadline = EditorApplication.timeSinceStartup + WatchdogSeconds;
            EditorApplication.update += Watchdog;

            Install();
        }

        private const double WatchdogSeconds = 300.0;
        private static double watchdogDeadline;

        private static void OnImportCompleted(string packageName)
        {
            AssetDatabase.Refresh();

            bool installed = IsInstalled;
            Debug.Log(
                $"[{nameof(TmpEssentialResourcesInstaller)}] 取り込み完了: {packageName} " +
                $"(TMP Settings {(installed ? "あり" : "なし")})");

            EditorApplication.Exit(installed ? 0 : 1);
        }

        private static void OnImportFailed(string packageName, string errorMessage)
        {
            Debug.LogError(
                $"[{nameof(TmpEssentialResourcesInstaller)}] 取り込み失敗: {packageName} / {errorMessage}");
            EditorApplication.Exit(1);
        }

        private static void OnImportCancelled(string packageName)
        {
            Debug.LogError($"[{nameof(TmpEssentialResourcesInstaller)}] 取り込みが中断されました: {packageName}");
            EditorApplication.Exit(1);
        }

        private static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < watchdogDeadline) return;

            EditorApplication.update -= Watchdog;
            Debug.LogError(
                $"[{nameof(TmpEssentialResourcesInstaller)}] " +
                $"{WatchdogSeconds:F0} 秒待っても取り込みが完了しませんでした。");
            EditorApplication.Exit(1);
        }

        /// <summary>導入されていなければ警告する。シーン生成時の事前確認に使う。</summary>
        public static void WarnIfMissing(string context)
        {
            if (IsInstalled) return;

            Debug.LogWarning(
                $"[{context}] TextMeshPro の必須リソースが未導入です。記録モードの HUD に" +
                "文字が表示されません。メニュー " +
                "Following Triangle > Import TMP Essential Resources を実行してください。");
        }

        private static bool TryResolvePackagePath(out string packagePath, out string error)
        {
            packagePath = null;

            var packageInfo = UpmPackageInfo.FindForAssetPath($"Packages/{PackageName}/package.json");
            if (packageInfo == null)
            {
                error = $"{PackageName} パッケージが見つかりません。";
                return false;
            }

            string candidate = Path.Combine(packageInfo.resolvedPath, RelativePackagePath)
                .Replace('\\', '/');

            if (!File.Exists(candidate))
            {
                error = $"{candidate} が見つかりません。ugui パッケージの構成が変わった可能性があります。";
                return false;
            }

            packagePath = candidate;
            error = null;
            return true;
        }
    }
}
