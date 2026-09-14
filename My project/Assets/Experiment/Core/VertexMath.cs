using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>
    /// 3 頂点の幾何計算。シーン状態にも XR SDK にも依存しない純関数だけを置く。
    /// ここに閉じ込めることで、仕様書 §8.3 の単体テストが HMD なしで成立する。
    /// </summary>
    public static class VertexMath
    {
        /// <summary>
        /// 首頂点 V0（仕様書 §2.2）。CenterEye の位置を「ワールドの重力方向」へ d だけ下ろした点。
        ///
        /// 意図的に CenterEye の *位置のみ* を引数に取り、回転を受け取らない。
        /// 仕様書は頭部ローカル座標系の down を使うことを禁じている。頭部回転が頂点変位として
        /// 混入し、条件間の統制が崩れるためである。回転をシグネチャに含めないことで、
        /// その誤りを「コメントで戒める」のではなく「そもそも書けない」状態にしている。
        /// </summary>
        /// <param name="centerEyePosition">CenterEyeAnchor のワールド座標。</param>
        /// <param name="d">重力方向オフセット量 [m]。仕様書 §9 によりパイロットで確定する暫定値。</param>
        public static Vector3 NeckVertex(Vector3 centerEyePosition, float d)
        {
            return centerEyePosition + d * Vector3.down;
        }

        /// <summary>
        /// 三角形の代表寸法：首頂点から左右の手頂点までの距離の平均
        /// （仕様書 §5.1 の体格指標 L、§6.1 の正規化分母）。
        ///
        /// 注意：誤差の正規化にこの関数を毎フレーム適用してはならない。
        /// 仕様書 §5.2 の趣旨（被験者が腕を伸ばすだけで誤差を下げられてはならない）から、
        /// 正規化分母はキャリブレーション時に確定した固定値でなければならない。
        /// この関数はその固定値を「求める」ために使うものであり、実行時に再評価するものではない。
        /// </summary>
        public static float CharacteristicLength(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            return 0.5f * (Vector3.Distance(v1, v0) + Vector3.Distance(v2, v0));
        }
    }
}
