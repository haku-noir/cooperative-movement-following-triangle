using System;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 録画された演者 A の座標を、被験者 B の空間へ移す変換（仕様書 §5.2 + §5.3）。
    ///
    /// 適用順は必ず「体格正規化 → 剛体変換」。
    ///
    ///     p' = R_yaw * ( pivot + s * (p - pivot) ) + t
    ///
    /// 各項の意味と、そう決めた理由：
    ///
    /// s（スケール, §5.2）
    ///     s = L_B / L_A。キャリブレーション時に確定した固定値であり、実行時の自由度ではない。
    ///     自由度として残すと、被験者が腕を伸ばすだけで誤差を下げられてしまう。
    ///
    /// pivot（スケールの中心, §5.2）
    ///     Registration 基準フレームにおける A の首頂点。**毎フレームの V0_A ではない**。
    ///     固定点にすることで全身が一様な相似変換を受け、A の頭部並進振幅も s 倍される。
    ///     毎フレームの V0_A を中心にすると手だけがスケールされ、
    ///     区間2（並進のみ）の刺激振幅が体格補正を受けないまま残ってしまう。
    ///
    /// R_yaw（回転, §5.3）
    ///     ワールド Y 軸まわりの回転のみ。ピッチ・ロールは含めない。
    ///     重力方向は A と B で共有されているため、傾ける理由がない。
    ///     傾けてしまうと V0 の重力方向固定（§2.2）が実質的に壊れる。
    ///
    /// t（並進, §5.3）
    ///     3 次元。A と B の身長差・立ち位置の差はここで吸収する。
    ///     回転中心は t に吸収されるので、別途「回転中心」を持たない。
    /// </summary>
    [Serializable]
    public struct StimulusTransform
    {
        /// <summary>体格正規化のスケール係数 s = L_B / L_A（§5.2）。</summary>
        public float scale;

        /// <summary>スケールの中心。Registration 基準フレームの A の首頂点（§5.2）。</summary>
        public Vector3 scalePivot;

        /// <summary>ヨー角 [度]。ワールド Y 軸まわり（§5.3）。</summary>
        public float yawDegrees;

        /// <summary>並進 [m]（§5.3）。</summary>
        public Vector3 translation;

        public static StimulusTransform Identity => new StimulusTransform
        {
            scale = 1f,
            scalePivot = Vector3.zero,
            yawDegrees = 0f,
            translation = Vector3.zero,
        };

        /// <summary>ヨー回転だけを取り出したクォータニオン。</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, yawDegrees, 0f);

        /// <summary>点を A の空間から B の空間へ移す。</summary>
        public Vector3 Apply(Vector3 point)
        {
            Vector3 scaled = scalePivot + scale * (point - scalePivot);
            return YawRotation * scaled + translation;
        }

        /// <summary>
        /// 方向ベクトルを移す。スケールも並進も向きを変えないので、効くのはヨーだけ。
        /// 頭部方向残差（§6.1-3）で A の前方ベクトルを B の空間に持ち込むのに使う。
        /// </summary>
        public Vector3 ApplyDirection(Vector3 direction) => YawRotation * direction;

        /// <summary>
        /// 姿勢を移す。ヨーだけを左から掛ける。
        /// A のピッチ・ロールはそのまま残る。これは意図した挙動で、
        /// 「A がうなずいた」情報を B の空間でも保つため。
        /// </summary>
        public Quaternion ApplyRotation(Quaternion rotation) => YawRotation * rotation;

        /// <summary>録画 1 フレームぶんをまとめて変換する。</summary>
        public BodyTriangleSample Apply(in BodyTriangleSample sample)
        {
            return new BodyTriangleSample
            {
                timestampSeconds = sample.timestampSeconds,
                headPosition = Apply(sample.headPosition),
                headRotation = ApplyRotation(sample.headRotation),
                v0 = Apply(sample.v0),
                v1 = Apply(sample.v1),
                v2 = Apply(sample.v2),
                leftHand = sample.leftHand,
                rightHand = sample.rightHand,
            };
        }

        public override string ToString() =>
            $"scale={scale:F4} pivot={scalePivot} yaw={yawDegrees:F3}deg translation={translation}";
    }
}
