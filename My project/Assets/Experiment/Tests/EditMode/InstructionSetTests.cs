using System.IO;
using FollowingTriangle.Core;
using NUnit.Framework;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 教示文（仕様書 §0.2, §7.4）。
    ///
    /// C1 と C2 の差は教示文だけなので、文言は実験操作そのものである。
    /// 読み込みに失敗したり文言が空だったりしたまま実験を始めると、
    /// 条件操作が存在しない状態でデータを取ることになる。
    /// </summary>
    public class InstructionSetTests
    {
        private const string ValidJson = @"{
  ""formatVersion"": 1,
  ""conditions"": [
    { ""conditionId"": ""C1"", ""text"": ""3つの点を合わせてください"" },
    { ""conditionId"": ""C2"", ""text"": ""3つの点を三角形と見なし、その三角形を合わせてください"" },
    { ""conditionId"": ""C3"", ""text"": ""三角形を合わせてください"" }
  ],
  ""baselinePrompt"": ""両手を前方に開いて静止してください"",
  ""readyPrompt"": ""準備ができたら実験者にお知らせください"",
  ""trialCompletePrompt"": ""この試行は終了です"",
  ""sessionCompletePrompt"": ""すべての試行が終了しました""
}";

        [Test]
        public void ValidJson_Parses()
        {
            Assert.That(InstructionSet.TryParse(ValidJson, out var set, out string error), Is.True, error);

            Assert.That(set.TextFor(Condition.C1), Is.EqualTo("3つの点を合わせてください"));
            Assert.That(set.TextFor(Condition.C3), Is.EqualTo("三角形を合わせてください"));
            Assert.That(set.baselinePrompt, Is.Not.Empty);
        }

        [Test]
        public void MissingConditionText_IsRejected()
        {
            string json = ValidJson.Replace(@"""text"": ""三角形を合わせてください""", @"""text"": """"");

            Assert.That(InstructionSet.TryParse(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("C3"));
        }

        /// <summary>
        /// C1 と C2 の文言が同じなら、2 つは同一条件であり実験操作が存在しない。
        /// 仕様書 §0.2 の C1 → C2 の比較（教示の効果）が測れなくなる。
        /// </summary>
        [Test]
        public void IdenticalC1AndC2Text_IsRejected()
        {
            string json = ValidJson.Replace(
                @"""text"": ""3つの点を三角形と見なし、その三角形を合わせてください""",
                @"""text"": ""3つの点を合わせてください""");

            Assert.That(InstructionSet.TryParse(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("C1 と C2"));
        }

        [Test]
        public void UnknownFormatVersion_IsRejected()
        {
            string json = ValidJson.Replace(@"""formatVersion"": 1", @"""formatVersion"": 99");

            Assert.That(InstructionSet.TryParse(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("形式バージョン"));
        }

        [Test]
        public void EmptyJson_IsRejected()
        {
            Assert.That(InstructionSet.TryParse("", out _, out _), Is.False);
            Assert.That(InstructionSet.TryParse("   ", out _, out _), Is.False);
        }

        [Test]
        public void MissingBaselinePrompt_IsRejected()
        {
            string json = ValidJson.Replace(
                @"""baselinePrompt"": ""両手を前方に開いて静止してください""",
                @"""baselinePrompt"": """"");

            Assert.That(InstructionSet.TryParse(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("baselinePrompt"));
        }

        /// <summary>
        /// リポジトリに入っている既定の教示ファイルが、そのまま検証を通ること。
        /// 実機に持っていってから「教示が読めません」で止まらないようにする。
        /// </summary>
        [Test]
        public void ShippedInstructionFile_IsValid()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "instructions.json");

            Assert.That(File.Exists(path), Is.True, $"既定の教示ファイルがありません: {path}");

            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);

            Assert.That(InstructionSet.TryParse(json, out var set, out string error), Is.True, error);

            // 仕様書 §0.2 の 3 条件がすべて入っていること。
            foreach (Condition condition in System.Enum.GetValues(typeof(Condition)))
            {
                Assert.That(set.TextFor(condition), Is.Not.Empty, condition.ToString());
            }
        }

        /// <summary>
        /// 教示文はコードに埋め込まない（§7.4）。
        /// InstructionSet の既定値は空であり、外部ファイルを読まない限り
        /// 検証を通らないことを確認する。
        /// </summary>
        [Test]
        public void DefaultInstanceHasNoEmbeddedText()
        {
            var empty = new InstructionSet();

            Assert.That(empty.TextFor(Condition.C1), Is.Empty);
            Assert.That(empty.TextFor(Condition.C2), Is.Empty);
            Assert.That(empty.TextFor(Condition.C3), Is.Empty);
            Assert.That(empty.Validate(out _), Is.False);
        }
    }
}
