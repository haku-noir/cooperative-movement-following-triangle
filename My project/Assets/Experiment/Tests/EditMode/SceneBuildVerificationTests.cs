using System.Collections.Generic;
using FollowingTriangle.Editor;
using NUnit.Framework;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// 生成したシーンに配線漏れが無いかを調べる仕組みのテスト。
    ///
    /// この検査が必要な理由：
    /// EditorSceneManager.NewScene をまたいで持ち越したアセットのインスタンスを
    /// 配線すると、**代入は成功し、読み戻しても一致するのに、保存時だけ
    /// {fileID: 0} になる**。代入直後の検査では原理的に捕まえられず、
    /// 「生成は成功したのに Play すると参照が null」という形でしか表に出ない。
    ///
    /// 実際に 2 度発生している（段階1 の Sandbox シーン、段階6 の Quick Test シーン）。
    /// </summary>
    public class SceneBuildVerificationTests
    {
        private static readonly string[] WiredFields =
        {
            "settings", "vertexSourceBehaviour", "stimulusPresenter", "hud",
        };

        [Test]
        public void DetectsUnassignedAssetReference()
        {
            const string yaml = @"MonoBehaviour:
  m_EditorClassIdentifier: FollowingTriangle.Runtime.ExperimentDriver
  settings: {fileID: 0}
  vertexSourceBehaviour: {fileID: 1856913983}
";

            var offenders = SceneBuildUtility.FindUnassignedReferences(yaml, WiredFields);

            Assert.That(offenders, Is.EquivalentTo(new[] { "settings" }));
        }

        [Test]
        public void AcceptsProperlyWiredAssetReference()
        {
            const string yaml = @"MonoBehaviour:
  m_EditorClassIdentifier: FollowingTriangle.Runtime.ExperimentDriver
  settings: {fileID: 11400000, guid: 7e8c38335bbc75c4a92730b37f066abf, type: 2}
  vertexSourceBehaviour: {fileID: 1856913983}
  stimulusPresenter: {fileID: 1962717442}
  hud: {fileID: 1183924350}
";

            var offenders = SceneBuildUtility.FindUnassignedReferences(yaml, WiredFields);

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void DetectsEveryUnassignedField()
        {
            const string yaml = @"MonoBehaviour:
  settings: {fileID: 0}
  vertexSourceBehaviour: {fileID: 0}
  stimulusPresenter: {fileID: 1962717442}
  hud: {fileID: 0}
";

            var offenders = SceneBuildUtility.FindUnassignedReferences(yaml, WiredFields);

            Assert.That(offenders, Is.EquivalentTo(new[] { "settings", "vertexSourceBehaviour", "hud" }));
        }

        /// <summary>
        /// 配線していないフィールドは検査の対象外。
        /// 実機専用の参照（recenterMonitorBehaviour など）はエディタ用シーンでは
        /// 未設定のままが正しいので、それを誤検出してはいけない。
        /// </summary>
        [Test]
        public void IgnoresFieldsThatWereNeverWired()
        {
            const string yaml = @"MonoBehaviour:
  settings: {fileID: 11400000, guid: abc, type: 2}
  recenterMonitorBehaviour: {fileID: 0}
  xrRuntimeInfoBehaviour: {fileID: 0}
";

            var offenders = SceneBuildUtility.FindUnassignedReferences(yaml, WiredFields);

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void EmptyYamlProducesNoOffenders()
        {
            Assert.That(SceneBuildUtility.FindUnassignedReferences("", WiredFields), Is.Empty);
            Assert.That(SceneBuildUtility.FindUnassignedReferences(null, WiredFields), Is.Empty);
        }

        [Test]
        public void EmptyFieldListProducesNoOffenders()
        {
            const string yaml = "  settings: {fileID: 0}\n";

            Assert.That(
                SceneBuildUtility.FindUnassignedReferences(yaml, new List<string>()), Is.Empty);
        }
    }
}
