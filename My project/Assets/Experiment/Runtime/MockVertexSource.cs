using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// HMD なしでエディタ上の検証を行うための頂点供給元（仕様書 §8.1 の土台）。
    ///
    /// 頭部と両手を表すダミー Transform を持ち、そこから実機と同じ計算経路で V0/V1/V2 を作る。
    /// V0 の生成には実機と同一の <see cref="VertexMath.NeckVertex"/> を使うので、
    /// 「頭部を回しても V0 が動かない」ことをエディタの Scene ビューで直接確認できる。
    ///
    /// 注意：タイムスタンプは <see cref="Time.timeAsDouble"/> を使う。実機では仕様書 §3.1 に従い
    /// OVRPlugin.GetTimeInSeconds() を使うため、記録モードでこのソースを使ってはならない。
    /// </summary>
    [DisallowMultipleComponent]
    public class MockVertexSource : MonoBehaviour, IVertexSource
    {
        [SerializeField]
        private ExperimentSettings settings;

        [Header("ダミー Transform (未設定なら Awake で自動生成)")]
        [SerializeField] private Transform headProxy;
        [SerializeField] private Transform leftHandProxy;
        [SerializeField] private Transform rightHandProxy;

        [Header("自動運動 (検証用。Scene ビューで手動操作する場合は off)")]
        [SerializeField]
        [Tooltip("正弦波でダミー Transform を動かす。頭部ヨー回転も含むので、V0 が回転に" +
                 "追従していないことをそのまま観察できる。")]
        private bool animate = true;

        [SerializeField] private float cycleFrequencyHz = 0.25f;
        [SerializeField] private float headTranslationAmplitude = 0.15f;
        [SerializeField] private float headYawAmplitudeDegrees = 40f;
        [SerializeField] private float headPitchAmplitudeDegrees = 20f;
        [SerializeField] private float handAmplitude = 0.20f;

        private static readonly Vector3 DefaultHeadPosition = new Vector3(0f, 1.60f, 0f);
        private static readonly Vector3 DefaultLeftHandPosition = new Vector3(-0.30f, 1.20f, 0.35f);
        private static readonly Vector3 DefaultRightHandPosition = new Vector3(0.30f, 1.20f, 0.35f);

        public ExperimentSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        public Transform HeadProxy => headProxy;
        public Transform LeftHandProxy => leftHandProxy;
        public Transform RightHandProxy => rightHandProxy;

        public bool IsAvailable => settings != null && headProxy != null
                                   && leftHandProxy != null && rightHandProxy != null;

        private void Awake()
        {
            headProxy = EnsureProxy(headProxy, "HeadProxy", DefaultHeadPosition);
            leftHandProxy = EnsureProxy(leftHandProxy, "LeftHandProxy", DefaultLeftHandPosition);
            rightHandProxy = EnsureProxy(rightHandProxy, "RightHandProxy", DefaultRightHandPosition);
        }

        private void Update()
        {
            if (!animate) return;

            float phase = 2f * Mathf.PI * cycleFrequencyHz * Time.time;
            float s = Mathf.Sin(phase);
            float c = Mathf.Cos(phase);

            headProxy.position = DefaultHeadPosition + new Vector3(headTranslationAmplitude * s, 0f, 0f);

            // 頭部はヨーとピッチの両方を振る。V0 がこの回転の影響を受けないことが確認点。
            headProxy.rotation = Quaternion.Euler(
                headPitchAmplitudeDegrees * c,
                headYawAmplitudeDegrees * s,
                0f);

            leftHandProxy.position = DefaultLeftHandPosition + new Vector3(0f, handAmplitude * c, 0f);
            rightHandProxy.position = DefaultRightHandPosition + new Vector3(0f, -handAmplitude * c, 0f);
        }

        public bool TryGetSample(out BodyTriangleSample sample)
        {
            if (!IsAvailable)
            {
                sample = default;
                return false;
            }

            Vector3 headPosition = headProxy.position;

            sample = new BodyTriangleSample
            {
                timestampSeconds = Time.timeAsDouble,
                headPosition = headPosition,
                headRotation = headProxy.rotation,

                // 実機と同一の経路。頭部の rotation は渡さない（仕様書 §2.2）。
                v0 = VertexMath.NeckVertex(headPosition, settings.NeckOffsetD),
                v1 = leftHandProxy.position,
                v2 = rightHandProxy.position,

                leftHand = new HandTrackingState
                {
                    isTracked = true,
                    confidence = HandConfidence.High,
                },
                rightHand = new HandTrackingState
                {
                    isTracked = true,
                    confidence = HandConfidence.High,
                },
            };

            return true;
        }

        private Transform EnsureProxy(Transform existing, string proxyName, Vector3 defaultPosition)
        {
            if (existing != null) return existing;

            var created = new GameObject(proxyName).transform;
            created.SetParent(transform, worldPositionStays: false);
            created.localPosition = defaultPosition;
            return created;
        }
    }
}
