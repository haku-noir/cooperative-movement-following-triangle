using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>手の頂点として使うボーン（仕様書 §2.3）。</summary>
    public enum HandVertexBone
    {
        /// <summary>手首（OVRSkeleton.BoneId.Hand_WristRoot 相当）。</summary>
        WristRoot = 0,

        /// <summary>中指 MCP（OVRSkeleton.BoneId.Hand_Middle1 相当）。指の開閉で動きにくいため既定。</summary>
        MiddleMcp = 1,
    }

    /// <summary>
    /// 実験の設定値を一箇所に集めた ScriptableObject。
    ///
    /// 仕様書 §9 の「パイロットで確定する事項」はすべてここに置き、コードに定数を埋め込まない。
    /// 再現性のため、ここの値は試行ごとに CSV ヘッダへ書き出される（段階4で実装）。
    /// </summary>
    [CreateAssetMenu(
        fileName = "ExperimentSettings",
        menuName = "Following Triangle/Experiment Settings",
        order = 0)]
    public class ExperimentSettings : ScriptableObject
    {
        // ------------------------------------------------------------------
        // §2 頂点定義
        // ------------------------------------------------------------------
        [Header("頂点定義 (仕様書 §2)")]
        [SerializeField]
        [Tooltip("首頂点 V0 の重力方向オフセット d [m]。暫定値 0.15、パイロットで確定する (§9)。")]
        private float neckOffsetD = 0.15f;

        [SerializeField]
        [Tooltip("手の頂点に使うボーン。既定は中指 MCP（指の開閉で頂点が動きにくいため, §2.3）。")]
        private HandVertexBone handVertexBone = HandVertexBone.MiddleMcp;

        // ------------------------------------------------------------------
        // §4 提示仕様 / §9 パイロットで確定
        // ------------------------------------------------------------------
        [Header("描画 (仕様書 §4, §9)")]
        [SerializeField]
        [Tooltip("頂点球の直径 [m]。暫定 0.03 (§4.1)。自己・相手で共通。")]
        private float vertexSphereDiameter = 0.03f;

        [SerializeField]
        [Tooltip("辺の線幅 [m]。暫定 0.01 (§4.3)。C3 でのみ使用、自己・相手で共通。")]
        private float edgeLineWidth = 0.01f;

        [SerializeField]
        [Tooltip("頂点球の色。仕様書 §10-1 が未確定のためプレースホルダ。背景との視認性は要確認。")]
        private Color vertexColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        [SerializeField]
        [Tooltip("辺の色。実験1では自他同色 (§4.2)。仕様書 §10-1 が未確定のためプレースホルダ。")]
        private Color edgeColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        // ------------------------------------------------------------------
        // §1 技術要件
        // ------------------------------------------------------------------
        [Header("XR ランタイム (仕様書 §1)")]
        [SerializeField]
        [Tooltip("要求するディスプレイリフレッシュレート [Hz]。起動時に設定し、実効値を検証してログに残す。")]
        private float targetDisplayFrequencyHz = 90f;

        // ------------------------------------------------------------------
        // §5.4 試行の構成
        // ------------------------------------------------------------------
        [Header("試行タイミング (仕様書 §5.4, §9)")]
        [SerializeField]
        [Tooltip("基準姿勢の保持時間 [s]。この間に Registration を行う (§5.3)。")]
        private float baselineHoldSeconds = 3f;

        [SerializeField]
        [Tooltip("導入区間の長さ [s]。解析から除外される。")]
        private float leadInSeconds = 10f;

        [SerializeField]
        [Tooltip("1 区間の長さ [s]。暫定 20 (§9)。")]
        private float segmentSeconds = 20f;

        [SerializeField]
        [Tooltip("区間数。仕様書 §5.4 では 4（複合・並進のみ・手のみ・複合）。")]
        private int segmentCount = 4;

        // ------------------------------------------------------------------
        // §0.3 スコープ外
        // ------------------------------------------------------------------
        [Header("実験2の要因 (仕様書 §0.3：本実装では未実装のパラメータ枠)")]
        [SerializeField]
        private Experiment2Parameters experiment2 = new Experiment2Parameters();

        public float NeckOffsetD => neckOffsetD;
        public HandVertexBone HandVertexBone => handVertexBone;
        public float VertexSphereDiameter => vertexSphereDiameter;
        public float EdgeLineWidth => edgeLineWidth;
        public Color VertexColor => vertexColor;
        public Color EdgeColor => edgeColor;
        public float TargetDisplayFrequencyHz => targetDisplayFrequencyHz;
        public float BaselineHoldSeconds => baselineHoldSeconds;
        public float LeadInSeconds => leadInSeconds;
        public float SegmentSeconds => segmentSeconds;
        public int SegmentCount => segmentCount;
        public Experiment2Parameters Experiment2 => experiment2;

        /// <summary>仕様書 §5.4 の 1 試行の総時間 [s]（教示フェーズは被験者ペースなので含まない）。</summary>
        public float TrialDurationSeconds =>
            baselineHoldSeconds + leadInSeconds + segmentSeconds * segmentCount;

        private void OnValidate()
        {
            neckOffsetD = Mathf.Max(0f, neckOffsetD);
            vertexSphereDiameter = Mathf.Max(0.001f, vertexSphereDiameter);
            edgeLineWidth = Mathf.Max(0.001f, edgeLineWidth);
            targetDisplayFrequencyHz = Mathf.Max(1f, targetDisplayFrequencyHz);
            baselineHoldSeconds = Mathf.Max(0f, baselineHoldSeconds);
            leadInSeconds = Mathf.Max(0f, leadInSeconds);
            segmentSeconds = Mathf.Max(0f, segmentSeconds);
            segmentCount = Mathf.Max(0, segmentCount);

            experiment2.WarnIfEnabled(name);
        }
    }

    /// <summary>
    /// 仕様書 §0.3 が実験2の要因として挙げた項目。
    /// 「パラメータとしてインスペクタに公開し、後から変更可能にしておくこと」との指示に従って
    /// 枠だけを用意してあるが、実験1の実装ではどれも参照されない。
    /// 誤って有効化したまま本番を走らせないよう、true にすると OnValidate が警告を出す。
    /// </summary>
    [Serializable]
    public class Experiment2Parameters
    {
        [Tooltip("頂点オフセット量 d を試行内で変動させる。実験1では固定値のため未実装。")]
        public bool varyNeckOffset = false;

        [Tooltip("三角形を半透明面で塗りつぶす。未実装。")]
        public bool fillTriangle = false;

        [Tooltip("自己と相手を色で区別する。実験1では自他同色 (§4.2)。未実装。")]
        public bool distinguishSelfAndOtherByColor = false;

        [Tooltip("双方向リアルタイム通信。実験1は録画再生の一方向のみ。未実装。")]
        public bool bidirectionalRealtime = false;

        [Tooltip("パススルー映像を使う。実験1は完全な VR 空間内 (§0.3, §4.4)。未実装。")]
        public bool usePassthrough = false;

        public bool AnyEnabled =>
            varyNeckOffset || fillTriangle || distinguishSelfAndOtherByColor
            || bidirectionalRealtime || usePassthrough;

        public void WarnIfEnabled(string assetName)
        {
            if (!AnyEnabled) return;

            Debug.LogWarning(
                $"[{assetName}] 実験2の要因が有効化されていますが、実験1の実装では参照されません " +
                "(仕様書 §0.3)。本番前に無効へ戻してください。");
        }
    }
}
