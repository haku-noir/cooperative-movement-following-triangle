using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 被験者 B の代わりに、既知の運動で三角形を動かす頂点供給元（仕様書 §8.2）。
    ///
    /// 合成録画と **同じ運動モデル** を使うので、オフセットと遅れを 0 にすれば
    /// A と B の三角形は完全に重なる。そこから既知の量だけずらせば、
    /// CSV に出る誤差がその値と一致するはずである。
    /// 「0.1 m ずらしたら dist_V0 が 0.100000 になる」という確認が
    /// 本番のパイプラインをそのまま通して行える。
    ///
    /// <see cref="MockVertexSource"/> との違いは、運動が合成録画と同期していること。
    /// 手で動かして見る用途にはモックのほうが向く。
    /// </summary>
    [DisallowMultipleComponent]
    public class SyntheticVertexSource : MonoBehaviour, IVertexSource
    {
        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("合成録画と同じパラメータにすること。違うと A と B が別の運動になる。")]
        private SyntheticMotionParameters parameters = SyntheticMotionParameters.Default;

        [Header("既知のずれ（実行中に変更可）")]
        [SerializeField]
        [Tooltip("A に対する位置のずれ [m]。ここに入れた値が、そのまま各頂点距離として " +
                 "CSV に出るはず。0 なら完全に重なる。")]
        private Vector3 positionOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("A に対する時間の遅れ [s]。追従の遅れを模擬する。")]
        private float lagSeconds = 0f;

        [Header("時刻")]
        [SerializeField]
        [Tooltip("起動からの経過時刻を録画の経過時刻とみなす。" +
                 "実験フローの開始タイミングと厳密に合わせる必要がある場合は " +
                 "ResetClock() を呼ぶ。")]
        private bool useTimeSinceStart = true;

        private SyntheticMotion motion;
        private double clockOrigin;

        public Vector3 PositionOffset
        {
            get => positionOffset;
            set => positionOffset = value;
        }

        public float LagSeconds
        {
            get => lagSeconds;
            set => lagSeconds = value;
        }

        public bool IsAvailable => settings != null;

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError($"[{nameof(SyntheticVertexSource)}] ExperimentSettings が未設定です。", this);
                return;
            }

            motion = new SyntheticMotion(parameters, RecordingSchedule.FromSettings(settings));
            ResetClock();
        }

        /// <summary>時刻の原点を今に合わせる。試行の開始に合わせたいときに呼ぶ。</summary>
        public void ResetClock() => clockOrigin = Time.timeAsDouble;

        public bool TryGetSample(out BodyTriangleSample sample)
        {
            sample = default;
            if (motion == null) return false;

            double now = Time.timeAsDouble;
            double elapsed = useTimeSinceStart ? now - clockOrigin : now;

            // 遅れは「A の少し前の時刻を参照する」ことで作る。
            double referenceTime = System.Math.Max(0.0, elapsed - lagSeconds);

            sample = motion.Sample(referenceTime, now);

            if (positionOffset == Vector3.zero) return true;

            sample.headPosition += positionOffset;
            sample.v0 += positionOffset;
            sample.v1 += positionOffset;
            sample.v2 += positionOffset;
            return true;
        }
    }
}
