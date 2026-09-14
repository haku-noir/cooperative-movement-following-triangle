using System;
using System.IO;
using System.Text;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 被験者プロファイルの保存と読み込み（仕様書 §5.1-3）。
    /// 保存先は Application.persistentDataPath/profiles/&lt;participantId&gt;.json。
    ///
    /// 被験者 ID をファイル名にしているので、同じ被験者で実験を再開したとき
    /// 既存のキャリブレーションをそのまま読み直せる。取り直すと L が変わり、
    /// 条件間の正規化誤差が比較できなくなる。
    /// </summary>
    public static class ParticipantProfileStorage
    {
        private const string DirectoryName = "profiles";

        public static string Directory => Path.Combine(Application.persistentDataPath, DirectoryName);

        public static string PathFor(string participantId) =>
            Path.Combine(Directory, $"{Sanitize(participantId)}.json");

        public static bool Exists(string participantId) => File.Exists(PathFor(participantId));

        public static bool TrySave(ParticipantProfile profile, out string savedPath, out string error)
        {
            savedPath = null;

            if (profile == null || !profile.IsValid)
            {
                error = "プロファイルが不正です。";
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string path = PathFor(profile.participantId);

                File.WriteAllText(
                    path, JsonUtility.ToJson(profile, prettyPrint: true),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                savedPath = path;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"プロファイルの保存に失敗しました: {exception.Message}";
                return false;
            }
        }

        public static bool TryLoad(string participantId, out ParticipantProfile profile, out string error)
        {
            profile = null;

            try
            {
                string path = PathFor(participantId);
                if (!File.Exists(path))
                {
                    error = $"プロファイルがありません: {path}";
                    return false;
                }

                var parsed = JsonUtility.FromJson<ParticipantProfile>(
                    File.ReadAllText(path, Encoding.UTF8));

                if (parsed == null)
                {
                    error = "プロファイルを解釈できません。";
                    return false;
                }

                if (parsed.formatVersion != ParticipantProfile.CurrentFormatVersion)
                {
                    error = $"形式バージョンが {parsed.formatVersion} です " +
                            $"(このビルドが読めるのは {ParticipantProfile.CurrentFormatVersion})。";
                    return false;
                }

                if (!parsed.IsValid)
                {
                    error = "プロファイルの内容が不正です。";
                    return false;
                }

                profile = parsed;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"プロファイルの読み込みに失敗しました: {exception.Message}";
                return false;
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            }

            return builder.ToString();
        }
    }
}
