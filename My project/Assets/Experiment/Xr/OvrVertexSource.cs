using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Xr
{
    /// <summary>
    /// 実機（Quest 3）から身体三角形を取得する（仕様書 §2.2, §2.3, §3.1）。
    ///
    /// Meta XR SDK に依存するコードはこのアセンブリだけに閉じている。
    /// 将来 OVRSkeleton から com.unity.xr.hands へ移行することになっても、
    /// 差し替えるのはこのファイルであり、測定の計算（Core）とテストには影響しない。
    /// 詳細は docs/platform_notes.md §2.3 を参照。
    /// </summary>
    [DisallowMultipleComponent]
    public class OvrVertexSource : MonoBehaviour, IVertexSource
    {
        [SerializeField] private ExperimentSettings settings;

        [Header("OVRCameraRig の参照")]
        [SerializeField]
        [Tooltip("OVRCameraRig/TrackingSpace/CenterEyeAnchor。位置のみを V0 の算出に使う。")]
        private Transform centerEyeAnchor;

        [Header("ハンドトラッキング")]
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private OVRSkeleton leftHandSkeleton;
        [SerializeField] private OVRHand rightHand;
        [SerializeField] private OVRSkeleton rightHandSkeleton;

        public bool IsAvailable =>
            settings != null && centerEyeAnchor != null
            && leftHandSkeleton != null && rightHandSkeleton != null;

        public bool TryGetSample(out BodyTriangleSample sample)
        {
            sample = default;
            if (!IsAvailable) return false;

            // 手の頂点が取れないフレームはサンプル自体を無効にする。
            // ここで直前値を保持したり外挿したりしてはならない。仕様書 §7.1 の趣旨は
            // 「低信頼区間を後処理で除外できるようにする」ことであり、
            // アプリ側で欠損を埋めると、その判断が後処理から見えなくなる。
            if (!TryGetHandVertex(leftHandSkeleton, out var leftVertex)) return false;
            if (!TryGetHandVertex(rightHandSkeleton, out var rightVertex)) return false;

            Vector3 headPosition = centerEyeAnchor.position;

            sample = new BodyTriangleSample
            {
                // 仕様書 §3.1：タイムスタンプは OVRPlugin.GetTimeInSeconds() を使う。
                // Time.time ではなくこちらを使うのは、トラッキングのサンプル時刻と
                // 同じ時間基準に乗せるため。
                timestampSeconds = OVRPlugin.GetTimeInSeconds(),

                headPosition = headPosition,
                headRotation = centerEyeAnchor.rotation,

                // 仕様書 §2.2：オフセットは重力方向固定。centerEyeAnchor.rotation は渡さない。
                // VertexMath.NeckVertex は回転を引数に取らないので、ここで誤って
                // 頭部ローカル down を使うことはできない。
                v0 = VertexMath.NeckVertex(headPosition, settings.NeckOffsetD),
                v1 = leftVertex,
                v2 = rightVertex,

                leftHand = ReadHandState(leftHand),
                rightHand = ReadHandState(rightHand),
            };

            return true;
        }

        private bool TryGetHandVertex(OVRSkeleton skeleton, out Vector3 position)
        {
            position = default;

            if (skeleton == null || !skeleton.IsInitialized || !skeleton.IsDataValid) return false;

            var boneId = ResolveBoneId(settings.HandVertexBone, skeleton.GetSkeletonType());
            var bones = skeleton.Bones;

            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone.Id != boneId) continue;
                if (bone.Transform == null) return false;

                // OVRBone.Transform はシーン階層に置かれているので .position はワールド座標。
                // 仕様書 §2.1 の要求どおり world 座標系のまま扱う。
                position = bone.Transform.position;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 仕様書 §2.3 は Hand_WristRoot / Hand_Middle1 という名前で頂点を指定しているが、
        /// Meta XR SDK 205 には骨格が 2 系統ある。
        ///
        ///   SkeletonType.HandLeft / HandRight     … 従来の OVR 骨格（Hand_* の名前）
        ///   SkeletonType.XRHandLeft / XRHandRight … OpenXR 骨格（XRHand_* の名前）
        ///
        /// 指している解剖学的位置は同じ（手首根元／中指近位指節骨の基部 = 中指 MCP）なので、
        /// どちらの骨格が構成されていても同じ頂点を取れるように両方を解決する。
        /// これは仕様の変更ではなく、SDK の名前の違いを吸収しているだけである。
        /// </summary>
        private static OVRSkeleton.BoneId ResolveBoneId(
            HandVertexBone vertexBone, OVRSkeleton.SkeletonType skeletonType)
        {
            bool isOpenXrSkeleton =
                skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
                skeletonType == OVRSkeleton.SkeletonType.XRHandRight;

            switch (vertexBone)
            {
                case HandVertexBone.WristRoot:
                    return isOpenXrSkeleton
                        ? OVRSkeleton.BoneId.XRHand_Wrist
                        : OVRSkeleton.BoneId.Hand_WristRoot;

                case HandVertexBone.MiddleMcp:
                default:
                    return isOpenXrSkeleton
                        ? OVRSkeleton.BoneId.XRHand_MiddleProximal
                        : OVRSkeleton.BoneId.Hand_Middle1;
            }
        }

        /// <summary>
        /// 仕様書 §6.1-5 / §7.1：IsTracked と HandConfidence を毎フレーム記録する。
        /// Quest のランタイムは手がカメラ視野外に出ても姿勢を外挿し続けるため、
        /// 「値が取れている」ことと「信頼できる」ことは別である。
        /// </summary>
        private static HandTrackingState ReadHandState(OVRHand hand)
        {
            if (hand == null) return HandTrackingState.Untracked;

            HandConfidence confidence;
            switch (hand.HandConfidence)
            {
                case OVRHand.TrackingConfidence.High:
                    confidence = HandConfidence.High;
                    break;
                case OVRHand.TrackingConfidence.Low:
                    confidence = HandConfidence.Low;
                    break;
                default:
                    confidence = HandConfidence.Unknown;
                    break;
            }

            return new HandTrackingState
            {
                isTracked = hand.IsTracked,
                confidence = confidence,
            };
        }
    }
}
