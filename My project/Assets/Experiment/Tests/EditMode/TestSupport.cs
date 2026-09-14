using FollowingTriangle.Core;
using UnityEditor;
using UnityEngine;

namespace FollowingTriangle.Tests
{
    /// <summary>
    /// テスト用の合成サンプル生成。
    ///
    /// 仕様書 §8.2 が求める「既知の正弦波運動からの合成録画生成」は段階6 の課題であり、
    /// ここにあるのはテストを書くための最小限の下ごしらえ。段階6 で本実装を Core へ移す。
    /// </summary>
    public static class SyntheticSamples
    {
        public static readonly Vector3 HeadRest = new Vector3(0f, 1.60f, 0f);

        /// <summary>首頂点から 0.40 m の位置に左右の手を置く静止姿勢。</summary>
        public static BodyTriangleSample StaticPose(
            double timestamp, float neckOffsetD = 0.15f, float armLength = 0.40f,
            HandConfidence confidence = HandConfidence.High)
        {
            Vector3 neck = VertexMath.NeckVertex(HeadRest, neckOffsetD);

            return new BodyTriangleSample
            {
                timestampSeconds = timestamp,
                headPosition = HeadRest,
                headRotation = Quaternion.identity,
                v0 = neck,
                v1 = neck + new Vector3(-armLength, 0f, 0f),
                v2 = neck + new Vector3(armLength, 0f, 0f),
                leftHand = new HandTrackingState { isTracked = true, confidence = confidence },
                rightHand = new HandTrackingState { isTracked = true, confidence = confidence },
            };
        }

        /// <summary>頭部と手が既知の正弦波で動くサンプル。</summary>
        public static BodyTriangleSample Moving(
            double timestamp, float neckOffsetD = 0.15f, float armLength = 0.40f,
            float frequencyHz = 0.25f, float headAmplitude = 0.15f, float handAmplitude = 0.20f)
        {
            float phase = 2f * Mathf.PI * frequencyHz * (float)timestamp;
            Vector3 head = HeadRest + new Vector3(headAmplitude * Mathf.Sin(phase), 0f, 0f);
            Vector3 neck = VertexMath.NeckVertex(head, neckOffsetD);
            float lift = handAmplitude * Mathf.Cos(phase);

            return new BodyTriangleSample
            {
                timestampSeconds = timestamp,
                headPosition = head,

                // 頭部は回すが、V0 はこの回転の影響を受けない（§2.2）。
                headRotation = Quaternion.Euler(10f * Mathf.Cos(phase), 30f * Mathf.Sin(phase), 0f),
                v0 = neck,
                v1 = neck + new Vector3(-armLength, lift, 0f),
                v2 = neck + new Vector3(armLength, -lift, 0f),
                leftHand = new HandTrackingState { isTracked = true, confidence = HandConfidence.High },
                rightHand = new HandTrackingState { isTracked = true, confidence = HandConfidence.High },
            };
        }
    }

    /// <summary>
    /// ExperimentSettings の private [SerializeField] をテストから書き換えるための道具。
    /// 本番コードにテスト専用のセッターを生やさずに済ませるため、
    /// インスペクタと同じ経路（SerializedObject）で値を入れる。
    /// </summary>
    public static class SettingsEditing
    {
        public static ExperimentSettings CreateDefault()
        {
            return ScriptableObject.CreateInstance<ExperimentSettings>();
        }

        public static void SetEnum(ExperimentSettings settings, string fieldName, int value)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty(fieldName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(ExperimentSettings settings, string fieldName, float value)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty(fieldName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(ExperimentSettings settings, string fieldName, int value)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty(fieldName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
