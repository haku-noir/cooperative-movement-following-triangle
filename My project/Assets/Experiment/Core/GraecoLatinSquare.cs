using System;
using System.Text;

namespace FollowingTriangle.Core
{
    /// <summary>1 試行の割付（仕様書 §5.5）。</summary>
    public readonly struct TrialAssignment
    {
        /// <summary>セッション内の提示順（0 始まり）。</summary>
        public readonly int Position;

        public readonly Condition Condition;

        /// <summary>刺激の番号（0 始まり）。実際のファイルへの対応は StimulusCatalog が持つ。</summary>
        public readonly int StimulusIndex;

        public TrialAssignment(int position, Condition condition, int stimulusIndex)
        {
            Position = position;
            Condition = condition;
            StimulusIndex = stimulusIndex;
        }

        public override string ToString() =>
            $"#{Position + 1}: {Condition} x stimulus{StimulusIndex + 1}";
    }

    /// <summary>
    /// 3 次のグレコ・ラテン方格による割付（仕様書 §5.5）。
    ///
    /// 仕様書は「条件順序と条件-刺激対応の両方をカウンターバランス」を求めているが、
    /// 単一の 3x3 ラテン方格では両方を同時に均衡できない。直交する 2 つのラテン方格を
    /// 重ねたグレコ・ラテン方格を使うと、被験者 3 名で次の 3 つが同時に成立する。
    ///
    ///   1. 各条件が各提示位置にちょうど 1 回ずつ現れる（順序効果の均衡）
    ///   2. 各刺激が各提示位置にちょうど 1 回ずつ現れる（刺激の順序効果の均衡）
    ///   3. 9 通りの（条件, 刺激）の組がちょうど 1 回ずつ現れる（条件-刺激交絡の除去）
    ///
    /// 構成は次の式で与える。r は割付行（0..2）、p は提示位置（0..2）。
    ///
    ///     条件番号 = (r + p)     mod 3
    ///     刺激番号 = (r + 2 * p) mod 3
    ///
    /// 2 つの式の p の係数が 3 を法として互いに異なるため、
    /// (条件番号, 刺激番号) から (r, p) が一意に逆算でき、直交性が保証される。
    ///
    /// 被験者数は 3 の倍数にすること。3 の倍数でないところで打ち切ると均衡が崩れる。
    /// </summary>
    public static class GraecoLatinSquare
    {
        public const int Order = 3;

        /// <summary>
        /// 割付行。被験者番号（1 始まり）から決まる。
        /// </summary>
        public static int RowFor(int participantNumber)
        {
            if (participantNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(participantNumber), participantNumber, "被験者番号は 1 以上である必要があります。");
            }

            return (participantNumber - 1) % Order;
        }

        /// <summary>
        /// 被験者番号から試行の並びを決める。仕様書 §5.5 の
        /// 「被験者IDを入力すると割付順が決定される」に対応する。
        ///
        /// 同じ被験者番号なら必ず同じ並びが返る。実験を中断して再開しても
        /// 割付が変わらないよう、乱数も実行時の状態も使わない。
        /// </summary>
        public static TrialAssignment[] For(int participantNumber)
        {
            int row = RowFor(participantNumber);
            var assignments = new TrialAssignment[Order];

            for (int position = 0; position < Order; position++)
            {
                int conditionIndex = (row + position) % Order;
                int stimulusIndex = (row + 2 * position) % Order;

                assignments[position] = new TrialAssignment(
                    position, ConditionAt(conditionIndex), stimulusIndex);
            }

            return assignments;
        }

        /// <summary>条件番号（0..2）から条件へ。C1 = 1 始まりの enum に合わせる。</summary>
        public static Condition ConditionAt(int index) => (Condition)(index + 1);

        /// <summary>
        /// 割付表を人間が読める形にする。実験前に紙で確認するため。
        /// </summary>
        public static string DescribeCycle()
        {
            var builder = new StringBuilder();
            builder.AppendLine("グレコ・ラテン方格による割付（3 名 1 周期）");
            builder.AppendLine("被験者番号  1試行目      2試行目      3試行目");

            for (int participant = 1; participant <= Order; participant++)
            {
                builder.Append($"{participant,8}    ");

                foreach (var assignment in For(participant))
                {
                    builder.Append($"{assignment.Condition}/S{assignment.StimulusIndex + 1}      ");
                }

                builder.AppendLine();
            }

            builder.AppendLine("被験者 4 以降は 1 に戻る。被験者数は 3 の倍数にすること。");
            return builder.ToString();
        }
    }

    /// <summary>
    /// 被験者 ID から割付に使う被験者番号を取り出す。
    ///
    /// ID 末尾の連番を 1 始まりの通し番号として使う（B01 → 1, B12 → 12）。
    /// 番号が取れない ID はエラーにして先へ進まない。
    /// ハッシュ等で無理に番号を作ると割付が実質ランダムになり、
    /// 仕様書 §5.5 が要求するカウンターバランスが成立しなくなる。
    /// </summary>
    public static class ParticipantNumber
    {
        public static bool TryParse(string participantId, out int number, out string error)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(participantId))
            {
                error = "被験者 ID が空です。";
                return false;
            }

            int end = participantId.Length;
            int start = end;
            while (start > 0 && char.IsDigit(participantId[start - 1])) start--;

            if (start == end)
            {
                error = $"被験者 ID \"{participantId}\" の末尾に数字がありません。" +
                        "割付は ID 末尾の連番で決まるため、B01 のような形式にしてください。";
                return false;
            }

            string digits = participantId.Substring(start, end - start);
            if (!int.TryParse(digits, out int parsed) || parsed < 1)
            {
                error = $"被験者 ID \"{participantId}\" から 1 以上の番号を取り出せません。";
                return false;
            }

            number = parsed;
            error = null;
            return true;
        }
    }
}
