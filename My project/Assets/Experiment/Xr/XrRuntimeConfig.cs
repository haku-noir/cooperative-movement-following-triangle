using System;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Xr
{
    /// <summary>
    /// 仕様書 §1 の技術要件のうち、実行時に設定・検証すべきものを扱う。
    ///
    ///   * リフレッシュレートを 90 Hz に設定し、**実際に適用されたか**を読み戻して保持する
    ///   * トラッキング原点が Floor Level であることを検証する
    ///   * 試行中の再センタリングを検出して記録する
    ///
    /// いずれも「設定して終わり」にせず検証まで行う。要求どおりに設定できなかったことに
    /// 気づかないまま本番データを取ってしまうと、後から救済できないため。
    ///
    /// ここで集めた値は段階4 で CSV ヘッダとログ列に書き出される。
    /// 現段階では保持と Console 出力までを行う。
    /// </summary>
    [DisallowMultipleComponent]
    public class XrRuntimeConfig : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        /// <summary>要求したリフレッシュレート [Hz]。</summary>
        public float RequestedDisplayFrequencyHz { get; private set; }

        /// <summary>設定後に読み戻した実効リフレッシュレート [Hz]。CSV ヘッダに書き出す（§6.2）。</summary>
        public float EffectiveDisplayFrequencyHz { get; private set; }

        /// <summary>要求値が実際に適用されたか。false ならその試行のデータは要注意。</summary>
        public bool DisplayFrequencyApplied { get; private set; }

        /// <summary>トラッキング原点が Floor Level だったか（仕様書 §1）。</summary>
        public bool TrackingOriginIsFloorLevel { get; private set; }

        /// <summary>この試行中に再センタリングが発生した回数（仕様書 §1）。</summary>
        public int RecenterCount { get; private set; }

        /// <summary>
        /// 直近の再センタリング発生時刻 [s]（OVRPlugin.GetTimeInSeconds() 基準）。
        /// 一度も起きていなければ NaN。
        /// </summary>
        public double LastRecenterTimestamp { get; private set; } = double.NaN;

        /// <summary>
        /// この試行が再センタリングで汚染されたか。仕様書 §1 の「無効フラグ」に対応する。
        ///
        /// 未確認事項：仕様書は「当該試行を無効フラグ付きで保存する」とだけ書いており、
        /// 試行を中断するのか最後まで続行するのかを定めていない。
        /// 現状は中断せずフラグだけを立てる実装にしてある（被験者の課題文脈を壊さないため）。
        /// 段階5 の試行フロー実装時に確認する。
        /// </summary>
        public bool RecenterDetected => RecenterCount > 0;

        /// <summary>再センタリング発生を購読したい側（段階4 のロガー）向け。引数は発生時刻 [s]。</summary>
        public event Action<double> Recentered;

        /// <summary>SDK バージョン文字列。CSV ヘッダに書き出す（§6.2）。</summary>
        public string SdkVersion { get; private set; } = "unknown";

        private bool subscribed;

        private void Start()
        {
            CaptureSdkVersion();
            ApplyAndVerifyDisplayFrequency();
            VerifyTrackingOrigin();
            SubscribeRecenterEvent();
        }

        private void OnDestroy() => UnsubscribeRecenterEvent();

        /// <summary>試行の開始時に呼び、再センタリングの記録を初期化する。</summary>
        public void ResetPerTrialState()
        {
            RecenterCount = 0;
            LastRecenterTimestamp = double.NaN;
        }

        private void CaptureSdkVersion()
        {
            try
            {
                SdkVersion = OVRPlugin.wrapperVersion != null
                    ? OVRPlugin.wrapperVersion.ToString()
                    : "unknown";
            }
            catch (Exception exception)
            {
                SdkVersion = "unknown";
                Debug.LogWarning($"[{nameof(XrRuntimeConfig)}] SDK バージョンを取得できませんでした: {exception.Message}");
            }
        }

        private void ApplyAndVerifyDisplayFrequency()
        {
            RequestedDisplayFrequencyHz = settings != null ? settings.TargetDisplayFrequencyHz : 90f;

            var display = OVRManager.display;
            if (display == null)
            {
                DisplayFrequencyApplied = false;
                EffectiveDisplayFrequencyHz = float.NaN;
                Debug.LogWarning(
                    $"[{nameof(XrRuntimeConfig)}] OVRManager.display が未初期化です。" +
                    "リフレッシュレートを設定できません（エディタで HMD 未接続の場合は想定内）。");
                return;
            }

            display.displayFrequency = RequestedDisplayFrequencyHz;

            // 設定しただけでは適用されたか分からないので読み戻す。
            EffectiveDisplayFrequencyHz = display.displayFrequency;
            DisplayFrequencyApplied =
                Mathf.Abs(EffectiveDisplayFrequencyHz - RequestedDisplayFrequencyHz) < 0.5f;

            if (DisplayFrequencyApplied)
            {
                Debug.Log(
                    $"[{nameof(XrRuntimeConfig)}] リフレッシュレート {EffectiveDisplayFrequencyHz} Hz を適用しました。");
            }
            else
            {
                string available = string.Join(", ", display.displayFrequenciesAvailable);
                Debug.LogError(
                    $"[{nameof(XrRuntimeConfig)}] リフレッシュレートを {RequestedDisplayFrequencyHz} Hz に " +
                    $"設定できませんでした（実効値 {EffectiveDisplayFrequencyHz} Hz）。" +
                    $"この端末で利用可能な値: [{available}]。このまま本番データを取らないこと。");
            }
        }

        private void VerifyTrackingOrigin()
        {
            var manager = OVRManager.instance;
            if (manager == null)
            {
                TrackingOriginIsFloorLevel = false;
                Debug.LogWarning($"[{nameof(XrRuntimeConfig)}] OVRManager が見つかりません。");
                return;
            }

            TrackingOriginIsFloorLevel =
                manager.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel;

            if (!TrackingOriginIsFloorLevel)
            {
                // 自動で直さない。床原点でない状態で取れたデータは、
                // 仕様書 §2.1 の座標系前提が崩れているため信用できない。
                Debug.LogError(
                    $"[{nameof(XrRuntimeConfig)}] トラッキング原点が {manager.trackingOriginType} です。" +
                    "仕様書 §1 は Floor Level を要求しています。OVRManager の設定を修正してください。");
            }
        }

        private void SubscribeRecenterEvent()
        {
            if (subscribed || OVRManager.display == null) return;

            OVRManager.display.RecenteredPose += OnRecenteredPose;
            subscribed = true;
        }

        private void UnsubscribeRecenterEvent()
        {
            if (!subscribed || OVRManager.display == null) return;

            OVRManager.display.RecenteredPose -= OnRecenteredPose;
            subscribed = false;
        }

        /// <summary>
        /// 仕様書 §1：試行中の再センタリングは禁止。発生した場合はログに記録し、
        /// 当該試行を無効フラグ付きで保存する。
        ///
        /// 再センタリングが起きるとワールド原点そのものが動くため、
        /// Registration で確定した A の三角形の位置と B の座標系の対応が壊れる。
        /// 発生後のフレームの誤差値は意味を持たない。
        /// </summary>
        private void OnRecenteredPose()
        {
            RecenterCount++;
            LastRecenterTimestamp = OVRPlugin.GetTimeInSeconds();

            Debug.LogError(
                $"[{nameof(XrRuntimeConfig)}] 再センタリングを検出しました " +
                $"(通算 {RecenterCount} 回目, t = {LastRecenterTimestamp:F3} s)。" +
                "この試行は無効フラグ付きで保存されます。");

            Recentered?.Invoke(LastRecenterTimestamp);
        }
    }
}
