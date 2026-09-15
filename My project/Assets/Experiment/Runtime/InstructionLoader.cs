using System.Collections;
using System.IO;
using System.Text;
using FollowingTriangle.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 教示文の読み込み（仕様書 §7.4）。
    ///
    /// 探索順
    ///   1. Application.persistentDataPath/instructions.json
    ///      実験者が adb push で差し替えられる場所。再ビルド不要で文言を調整できる。
    ///   2. StreamingAssets/instructions.json
    ///      リポジトリに入っている既定の文言。1 が無ければこちらを使い、
    ///      同時に 1 へコピーする（以後は編集できる状態になる）。
    ///
    /// コードには文言を 1 文字も持たない。C1 と C2 の差は教示文だけなので、
    /// 文言そのものが実験操作である。
    ///
    /// Android では StreamingAssets が APK の中にあり File API で読めないため、
    /// UnityWebRequest を使う。したがって読み込みは非同期になる。
    /// </summary>
    [DisallowMultipleComponent]
    public class InstructionLoader : MonoBehaviour
    {
        public const string FileName = "instructions.json";

        public InstructionSet Set { get; private set; }

        public bool IsLoaded => Set != null;

        /// <summary>読み込みが終わったか（成功・失敗を問わず）。</summary>
        public bool IsFinished { get; private set; }

        public string Error { get; private set; }

        /// <summary>実際に読み込んだファイルのパス。CSV のメタデータに残す用途も想定。</summary>
        public string SourcePath { get; private set; }

        public static string OverridePath =>
            Path.Combine(Application.persistentDataPath, FileName);

        public static string DefaultPath =>
            Path.Combine(Application.streamingAssetsPath, FileName);

        private void Awake() => StartCoroutine(LoadCoroutine());

        private IEnumerator LoadCoroutine()
        {
            // 1. 実験者が置いた上書きファイル
            if (File.Exists(OverridePath))
            {
                string json = File.ReadAllText(OverridePath, Encoding.UTF8);
                Finish(json, OverridePath);
                yield break;
            }

            // 2. 同梱の既定ファイル
            string url = DefaultPath;
            if (!url.Contains("://")) url = "file://" + url;

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail($"既定の教示ファイルを読めません ({DefaultPath}): {request.error}");
                    yield break;
                }

                string json = request.downloadHandler.text;

                // 以後は実験者が編集できるよう、書き込み可能な場所へ複製しておく。
                TryWriteOverrideCopy(json);
                Finish(json, DefaultPath);
            }
        }

        private void TryWriteOverrideCopy(string json)
        {
            try
            {
                File.WriteAllText(
                    OverridePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                Debug.Log(
                    $"[{nameof(InstructionLoader)}] 教示ファイルを編集可能な場所へ複製しました: " +
                    OverridePath);
            }
            catch (System.Exception exception)
            {
                // 複製できなくても既定の文言で実験は走る。警告に留める。
                Debug.LogWarning(
                    $"[{nameof(InstructionLoader)}] 教示ファイルを複製できませんでした: {exception.Message}");
            }
        }

        private void Finish(string json, string path)
        {
            IsFinished = true;
            SourcePath = path;

            if (!InstructionSet.TryParse(json, out var parsed, out string error))
            {
                Fail($"{path}: {error}");
                return;
            }

            Set = parsed;
            Error = null;
            Debug.Log($"[{nameof(InstructionLoader)}] 教示文を読み込みました: {path}");
        }

        private void Fail(string message)
        {
            IsFinished = true;
            Set = null;
            Error = message;

            // 教示が無いまま実験を始めてはいけない。呼び出し側が進行を止める。
            Debug.LogError($"[{nameof(InstructionLoader)}] {message}");
        }
    }
}
