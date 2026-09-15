using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 録画された演者 A の三角形を、被験者 B の空間へ持ち込んで提示する
    /// （仕様書 §5.2 体格正規化, §5.3 Registration, §7.3 再生）。
    ///
    /// 使い方（試行 1 回ぶん）
    ///   1. LoadRecording         刺激を読み込む
    ///   2. BeginBaseline         基準姿勢フェーズの開始で呼ぶ
    ///   3. AddBaselineSample     基準姿勢のあいだ毎フレーム呼ぶ（B の三角形を蓄積）
    ///   4. CompleteRegistration  基準姿勢の終わりに 1 回だけ呼ぶ。ここで変換が確定する
    ///   5. Tick                  以降、毎フレーム経過時刻を渡す
    ///
    /// 変換は 4 で一度だけ決まり、以後 1 試行のあいだ変わらない。
    /// 毎フレーム解き直すと A が B を追いかけることになり、課題が逆転する。
    /// </summary>
    [DisallowMultipleComponent]
    public class StimulusPresenter : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("相手三角形 A の描画先。自己三角形と同じ TriangleView クラスを使い、" +
                 "同じ TriangleVisualConfig を与える (§4.2)。")]
        private TriangleView otherTriangleView;

        private RecordingPlayback playback;
        private readonly TriangleAccumulator baselineAccumulator = new TriangleAccumulator();

        /// <summary>確定した変換。試行ログのヘッダに書き出す（§6.2）。</summary>
        public StimulusTransform Transform { get; private set; } = StimulusTransform.Identity;

        public bool IsRegistered { get; private set; }

        /// <summary>Registration の当てはまり残差 RMS [m]。試行の質の指標。</summary>
        public float RegistrationResidualRms { get; private set; }

        /// <summary>Registration に使った A 側の基準三角形（変換前）。</summary>
        public Triangle ReferenceA { get; private set; }

        /// <summary>Registration に使った B 側の基準三角形。</summary>
        public Triangle BaselineB { get; private set; }

        /// <summary>平均に採用された基準姿勢サンプル数（両手が高信頼だったもの）。</summary>
        public int BaselineSampleCount => baselineAccumulator.SampleCount;

        /// <summary>
        /// 基準姿勢フェーズで受け取った総フレーム数。採用数との差が大きい試行は
        /// トラッキングが不安定だったということで、Registration の信頼性が落ちる。
        /// </summary>
        public int BaselineFrameCount { get; private set; }

        public RecordingFile Recording => playback?.Recording;

        public bool HasRecording => playback != null;

        /// <summary>変換後の直近サンプル。段階4 の誤差算出とログはここを読む。</summary>
        public BodyTriangleSample LatestTransformedSample { get; private set; }

        public bool HasTransformedSample { get; private set; }

        public void SetSettings(ExperimentSettings value) => settings = value;

        // ------------------------------------------------------------------

        public bool LoadRecording(RecordingFile recording, out string error)
        {
            if (settings == null)
            {
                error = "ExperimentSettings が未設定です。";
                return false;
            }

            if (recording == null || recording.frames.Count == 0)
            {
                error = "録画が空です。";
                return false;
            }

            if (!recording.metadata.valid)
            {
                // 無効な録画は読み込めるが、使ってはいけないことを明示する。
                // 黙って使うと、再センタリング後の座標系で取ったデータが混ざる（§1）。
                Debug.LogError(
                    $"[{nameof(StimulusPresenter)}] この録画には無効フラグが付いています: " +
                    $"{recording.metadata.invalidReason}");
            }

            playback = new RecordingPlayback(
                recording,
                settings.RecomputeNeckVertexOnPlayback,
                settings.NeckOffsetD);

            IsRegistered = false;
            HasTransformedSample = false;
            error = null;
            return true;
        }

        public void BeginBaseline()
        {
            baselineAccumulator.Reset();
            BaselineFrameCount = 0;
            IsRegistered = false;
            HasTransformedSample = false;
            playback?.Rewind();
            SetVisible(settings != null && settings.ShowOtherTriangleDuringBaseline);
        }

        /// <summary>
        /// 基準姿勢のあいだの B の三角形を蓄積する（§5.3-2）。
        ///
        /// 低信頼のフレームは捨てる。Registration は試行全体の座標対応を決めるので、
        /// 外挿された手の位置が混ざると、その試行の誤差すべてに定数バイアスが乗る（§7.1）。
        /// </summary>
        public bool AddBaselineSample(in BodyTriangleSample sample)
        {
            BaselineFrameCount++;

            if (!sample.leftHand.isTracked || !sample.rightHand.isTracked) return false;
            if (sample.leftHand.confidence != HandConfidence.High) return false;
            if (sample.rightHand.confidence != HandConfidence.High) return false;

            baselineAccumulator.Add(sample);
            return true;
        }

        /// <summary>
        /// 変換を確定する。基準姿勢フェーズの終わりに 1 回だけ呼ぶ。
        /// </summary>
        /// <param name="profileB">被験者 B のプロファイル（§5.1）。体格正規化に使う。</param>
        public bool CompleteRegistration(ParticipantProfile profileB, out string error)
        {
            if (playback == null)
            {
                error = "録画が読み込まれていません。";
                return false;
            }

            if (!baselineAccumulator.HasSamples)
            {
                error = "基準姿勢の有効サンプルがありません。" +
                        "両手が高信頼でトラッキングされている状態で静止してください。";
                return false;
            }

            if (profileB == null || !profileB.IsValid)
            {
                error = "被験者プロファイルが未確定です (§5.1)。";
                return false;
            }

            var calibrationA = playback.Recording.metadata.calibration;
            if (calibrationA == null || !calibrationA.IsValid)
            {
                error = "録画に演者のキャリブレーションが入っていません (§3.1)。";
                return false;
            }

            // A 側の基準姿勢の窓は「録画時に実際に演じられた長さ」を使う。
            // 現在の settings を使うと、録画後に区間長を変えた場合に窓がずれ、
            // A が動き出したあとのフレームを基準に混ぜてしまう。
            float baselineWindowA = playback.Recording.metadata.baselineHoldSeconds > 0f
                ? playback.Recording.metadata.baselineHoldSeconds
                : settings.BaselineHoldSeconds;

            ReferenceA = playback.BaselineAverage(baselineWindowA, out int referenceSamples);
            BaselineB = baselineAccumulator.Average();

            Transform = Registration.Solve(
                settings.RegistrationMethod, ReferenceA, BaselineB, calibrationA, profileB.calibration);

            RegistrationResidualRms = Registration.ResidualRms(Transform, ReferenceA, BaselineB);
            IsRegistered = true;

            Debug.Log(
                $"[{nameof(StimulusPresenter)}] Registration 確定 " +
                $"(method={settings.RegistrationMethod})\n" +
                $"  {Transform}\n" +
                $"  residual RMS = {RegistrationResidualRms * 1000f:F1} mm " +
                $"(A 基準 {referenceSamples} フレーム平均 / B 基準 {BaselineSampleCount} サンプル平均)\n" +
                $"  L_A = {calibrationA.characteristicLength:F3} m, " +
                $"L_B = {profileB.calibration.characteristicLength:F3} m");

            error = null;
            return true;
        }

        /// <summary>
        /// 経過時刻に対応する A のサンプルを取り出して描画する（§7.3）。
        /// </summary>
        /// <param name="elapsedSeconds">試行開始からの経過時刻 [s]。</param>
        public bool Tick(double elapsedSeconds, out BodyTriangleSample transformed)
        {
            if (!TrySampleTransformed(elapsedSeconds, out transformed)) return false;

            LatestTransformedSample = transformed;
            HasTransformedSample = true;

            if (otherTriangleView != null)
            {
                otherTriangleView.SetVertices(transformed.v0, transformed.v1, transformed.v2);
            }

            return true;
        }

        /// <summary>
        /// 指定時刻の A のサンプルを、変換を適用した状態で取り出す。描画は行わない。
        ///
        /// 基準姿勢フェーズのログを Registration 確定後に遡って作るために使う。
        /// 基準姿勢の最中はまだ変換が決まっていないので、その場では A 側の座標を出せない。
        /// </summary>
        public bool TrySampleTransformed(double elapsedSeconds, out BodyTriangleSample transformed)
        {
            transformed = default;

            if (playback == null || !IsRegistered) return false;

            transformed = Transform.Apply(playback.SampleAt(elapsedSeconds));
            return true;
        }

        /// <summary>頂点ごとの Registration 残差 [m]（x = V0, y = V1, z = V2）。</summary>
        public Vector3 RegistrationResidualPerVertex =>
            IsRegistered
                ? Registration.ResidualPerVertex(Transform, ReferenceA, BaselineB)
                : Vector3.zero;

        /// <summary>
        /// 描画パラメータを適用する。自己三角形と同一の <see cref="TriangleVisualConfig"/> を
        /// 渡すこと。<see cref="ConditionVisuals.For"/> は自他の区別を引数に取らないので、
        /// 同じ条件から作れば自動的に同一になる（§4.2, §4.3）。
        /// </summary>
        public void Configure(TriangleVisualConfig config)
        {
            if (otherTriangleView != null) otherTriangleView.Configure(config);
        }

        public void SetVisible(bool visible)
        {
            if (otherTriangleView != null) otherTriangleView.SetVisible(visible);
        }
    }
}
