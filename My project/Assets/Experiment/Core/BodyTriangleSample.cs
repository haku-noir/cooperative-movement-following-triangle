using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// ある時刻における一人ぶんの身体三角形。記録モードの 1 フレーム、実験モードの被験者 B の
    /// 1 フレーム、再生された演者 A の 1 フレームが、いずれもこの型で表現される。
    ///
    /// 座標はすべてワールド座標（仕様書 §2.1：重力固定・床原点）。
    /// </summary>
    [Serializable]
    public struct BodyTriangleSample
    {
        /// <summary>仕様書 §3.1 に従い OVRPlugin.GetTimeInSeconds() 由来の絶対時刻 [s]。</summary>
        public double timestampSeconds;

        /// <summary>
        /// CenterEyeAnchor の姿勢。CSV の headA_* / headB_* 列（§6.2）と
        /// 頭部方向残差（§6.1-3）に使う。V0 の算出には position のみを使い、rotation は使わない。
        /// </summary>
        public Vector3 headPosition;

        public Quaternion headRotation;

        /// <summary>V0：首頂点（§2.2）。重力方向固定オフセットで生成される。</summary>
        public Vector3 v0;

        /// <summary>V1：左手頂点（§2.2, §2.3）。</summary>
        public Vector3 v1;

        /// <summary>V2：右手頂点（§2.2, §2.3）。</summary>
        public Vector3 v2;

        public HandTrackingState leftHand;
        public HandTrackingState rightHand;
    }
}
