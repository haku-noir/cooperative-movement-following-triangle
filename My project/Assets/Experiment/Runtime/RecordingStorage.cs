using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 録画ファイルの保存と読み込み（仕様書 §3.1）。
    /// 保存先は Application.persistentDataPath/&lt;recordingsDirectoryName&gt;/。
    /// </summary>
    public static class RecordingStorage
    {
        public static string DirectoryFor(ExperimentSettings settings)
        {
            string name = settings != null ? settings.RecordingsDirectoryName : "recordings";
            return Path.Combine(Application.persistentDataPath, name);
        }

        /// <summary>
        /// ファイル名。録画 ID と日時から作る。実験者が Quest からファイルを取り出すときに
        /// 中身を開かずに見分けられることを優先する。
        /// </summary>
        public static string BuildFileName(RecordingFile file)
        {
            string performer = Sanitize(file.metadata.performerId, "performer");
            string id = Sanitize(file.metadata.recordingId, "rec");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string validity = file.metadata.valid ? "" : "_INVALID";

            return $"{stamp}_{performer}_{id}{validity}.json";
        }

        /// <summary>
        /// 録画を保存する。既存ファイルは上書きしない。
        /// 刺激の取り直しで前のテイクが消えるのは取り返しがつかないため。
        /// </summary>
        public static bool TrySave(
            RecordingFile file, ExperimentSettings settings, out string savedPath, out string error)
        {
            savedPath = null;

            if (file == null)
            {
                error = "録画がありません。";
                return false;
            }

            try
            {
                string directory = DirectoryFor(settings);
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, BuildFileName(file));
                path = MakeUnique(path);

                string json = RecordingSerializer.ToJson(
                    file, settings == null || settings.PrettyPrintRecordingJson);

                // UTF-8 BOM なしで書く。解析側（Python 等）が素直に読めるようにするため。
                File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                savedPath = path;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"保存に失敗しました: {exception.Message}";
                return false;
            }
        }

        public static bool TryLoad(string path, out RecordingFile file, out string error)
        {
            file = null;

            try
            {
                if (!File.Exists(path))
                {
                    error = $"ファイルが見つかりません: {path}";
                    return false;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                return RecordingSerializer.TryFromJson(json, out file, out error);
            }
            catch (Exception exception)
            {
                error = $"読み込みに失敗しました: {exception.Message}";
                return false;
            }
        }

        public static IReadOnlyList<string> ListRecordings(ExperimentSettings settings)
        {
            string directory = DirectoryFor(settings);
            if (!Directory.Exists(directory)) return Array.Empty<string>();

            var paths = Directory.GetFiles(directory, "*.json");
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        private static string MakeUnique(string path)
        {
            if (!File.Exists(path)) return path;

            string directory = Path.GetDirectoryName(path) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
                if (!File.Exists(candidate)) return candidate;
            }

            throw new IOException($"重複しないファイル名を作れません: {path}");
        }

        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            }

            return builder.ToString();
        }
    }
}
