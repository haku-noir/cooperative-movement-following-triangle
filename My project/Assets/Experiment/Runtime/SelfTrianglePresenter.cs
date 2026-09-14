using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 自己三角形（被験者 B 自身）を毎フレーム更新して描画する（仕様書 §4.1）。
    /// 段階1 の成果物はここまで。相手三角形の再生・Registration・誤差算出は段階2 以降。
    ///
    /// 頂点供給元は <see cref="IVertexSource"/> 越しにしか触らないので、実機（OVR）でも
    /// エディタのモックでも同じ経路が走る。
    /// </summary>
    [DisallowMultipleComponent]
    public class SelfTrianglePresenter : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("IVertexSource を実装した MonoBehaviour。実機では OvrVertexSource、" +
                 "エディタ検証では MockVertexSource。")]
        private MonoBehaviour vertexSourceBehaviour;

        [SerializeField] private TriangleView selfTriangleView;

        [Header("条件 (段階5 で試行フローに置き換わる。現状は目視確認用)")]
        [SerializeField]
        [Tooltip("実行中に切り替えると即座に反映される。C1 と C2 で表示が変わらないことを目視確認できる。")]
        private Condition condition = Condition.C1;

        private IVertexSource vertexSource;
        private Condition appliedCondition;
        private bool configured;

        /// <summary>直近フレームのサンプル。段階2 以降の記録・誤差算出はここを読む。</summary>
        public BodyTriangleSample LatestSample { get; private set; }

        public bool HasSample { get; private set; }

        public Condition Condition
        {
            get => condition;
            set => condition = value;
        }

        private void Awake()
        {
            vertexSource = vertexSourceBehaviour as IVertexSource;

            if (vertexSource == null)
            {
                Debug.LogError(
                    $"[{nameof(SelfTrianglePresenter)}] vertexSourceBehaviour が IVertexSource を " +
                    "実装していません。自己三角形は描画されません。", this);
                enabled = false;
                return;
            }

            if (settings == null)
            {
                Debug.LogError(
                    $"[{nameof(SelfTrianglePresenter)}] ExperimentSettings が未設定です。", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!configured || condition != appliedCondition)
            {
                ApplyCondition();
            }

            if (!vertexSource.TryGetSample(out var sample))
            {
                HasSample = false;
                selfTriangleView.SetVisible(false);
                return;
            }

            LatestSample = sample;
            HasSample = true;

            selfTriangleView.SetVisible(true);
            selfTriangleView.SetVertices(sample.v0, sample.v1, sample.v2);
        }

        private void ApplyCondition()
        {
            // 条件から描画パラメータへの変換は ConditionVisuals 一箇所に閉じている。
            // ここには条件ごとの分岐を書かない。
            selfTriangleView.Configure(ConditionVisuals.For(condition, settings));
            appliedCondition = condition;
            configured = true;
        }
    }
}
