using System;
using System.Collections.Generic;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>条件ごとの教示文（仕様書 §0.2）。</summary>
    [Serializable]
    public class ConditionInstruction
    {
        public string conditionId = "";
        public string text = "";
    }

    /// <summary>
    /// 教示文一式（仕様書 §7.4）。
    ///
    /// **文言はコードに一切埋め込まない。** 外部 JSON から読み込む。
    /// C1 と C2 の差は教示文だけなので、文言は実験操作そのものである。
    /// コードに書くと、実験者が文言を調整するたびに再ビルドが必要になり、
    /// 「どのビルドでどの文言を使ったか」の追跡も難しくなる。
    /// </summary>
    [Serializable]
    public class InstructionSet
    {
        public int formatVersion = CurrentFormatVersion;

        public const int CurrentFormatVersion = 1;

        public List<ConditionInstruction> conditions = new List<ConditionInstruction>();

        [Tooltip("基準姿勢フェーズ中の表示 (§5.3-1)。")]
        public string baselinePrompt = "";

        [Tooltip("教示フェーズで、開始待ちであることを示す表示 (§5.4)。")]
        public string readyPrompt = "";

        [Tooltip("試行終了時の表示。")]
        public string trialCompletePrompt = "";

        [Tooltip("全試行終了時の表示。")]
        public string sessionCompletePrompt = "";

        public string TextFor(Condition condition)
        {
            string id = condition.ToString();

            foreach (var entry in conditions)
            {
                if (string.Equals(entry.conditionId, id, StringComparison.Ordinal)) return entry.text;
            }

            return "";
        }

        /// <summary>
        /// 教示ファイルとして成立しているかを検証する。
        /// 不備のまま本番を走らせると、被験者に出す文言が空だったり
        /// C1 と C2 が同じだったりしたことに、データを取り終えてから気づくことになる。
        /// </summary>
        public bool Validate(out string error)
        {
            if (formatVersion != CurrentFormatVersion)
            {
                error = $"形式バージョンが {formatVersion} です " +
                        $"(このビルドが読めるのは {CurrentFormatVersion})。";
                return false;
            }

            foreach (Condition condition in Enum.GetValues(typeof(Condition)))
            {
                if (string.IsNullOrWhiteSpace(TextFor(condition)))
                {
                    error = $"条件 {condition} の教示文がありません。";
                    return false;
                }
            }

            // C1 と C2 は表示が完全に同一で、差は教示文だけ（仕様書 §0.2）。
            // 文言まで同じなら 2 つは同一条件であり、実験操作が存在しないことになる。
            if (string.Equals(TextFor(Condition.C1), TextFor(Condition.C2), StringComparison.Ordinal))
            {
                error = "C1 と C2 の教示文が同一です。両条件の差は教示文のみ（§0.2）なので、" +
                        "これでは実験操作が存在しないことになります。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(baselinePrompt))
            {
                error = "baselinePrompt がありません。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(readyPrompt))
            {
                error = "readyPrompt がありません。";
                return false;
            }

            error = null;
            return true;
        }

        public static bool TryParse(string json, out InstructionSet set, out string error)
        {
            set = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "教示ファイルが空です。";
                return false;
            }

            InstructionSet parsed;
            try
            {
                parsed = JsonUtility.FromJson<InstructionSet>(json);
            }
            catch (Exception exception)
            {
                error = $"教示ファイルを解釈できません: {exception.Message}";
                return false;
            }

            if (parsed == null)
            {
                error = "教示ファイルを解釈できません (null)。";
                return false;
            }

            if (!parsed.Validate(out error)) return false;

            set = parsed;
            return true;
        }
    }
}
