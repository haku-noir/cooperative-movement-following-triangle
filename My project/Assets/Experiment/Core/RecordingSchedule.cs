using System;
using System.Collections.Generic;
using UnityEngine;

namespace FollowingTriangle.Core
{
    /// <summary>区間マーカーの打ち方（仕様書 §5.4）。</summary>
    public enum SegmentMarkerMode
    {
        /// <summary>
        /// 設定した区間長で自動的に境界を打ち、演者 A には次の区間を予告表示する。
        /// 区間長が録画間で完全に揃うため、刺激 3 本の比較が清潔になる。
        /// </summary>
        TimeBased = 0,

        /// <summary>
        /// 実験者がコントローラのボタンで区切る。演技の実態に合うが区間長がばらつく。
        /// 演者 A の手はハンドトラッキングを使うため、コントローラは別の人が持つ必要がある。
        /// </summary>
        Controller = 1,
    }

    /// <summary>録画スケジュール上の 1 フェーズ。</summary>
    public readonly struct SchedulePhase
    {
        /// <summary>ログ・JSON に書く機械可読な識別子。CSV の phase_marker 列にもこれを使う。</summary>
        public readonly string Id;

        /// <summary>演者 A へのガイド表示に使う日本語。</summary>
        public readonly string Instruction;

        /// <summary>録画開始からの予定開始時刻 [s]。</summary>
        public readonly float StartSeconds;

        public readonly float DurationSeconds;

        /// <summary>解析対象か。基準姿勢と導入は false（仕様書 §5.4）。</summary>
        public readonly bool IncludedInAnalysis;

        public float EndSeconds => StartSeconds + DurationSeconds;

        public SchedulePhase(
            string id, string instruction, float startSeconds, float durationSeconds,
            bool includedInAnalysis)
        {
            Id = id;
            Instruction = instruction;
            StartSeconds = startSeconds;
            DurationSeconds = durationSeconds;
            IncludedInAnalysis = includedInAnalysis;
        }

        public override string ToString() =>
            $"{Id} [{StartSeconds:F1}-{EndSeconds:F1}s] analysis={IncludedInAnalysis}";
    }

    /// <summary>
    /// 仕様書 §5.4 の試行構成を、設定値から組み立てたスケジュール。
    ///
    /// | フェーズ   | 時間 | 内容                                    |
    /// | 基準姿勢   |  3 s | Registration（実験モード）／初期姿勢（記録モード）|
    /// | 導入       | 10 s | 追従開始。解析から除外                  |
    /// | 区間1      | 20 s | 頭部並進と手を同時                      |
    /// | 区間2      | 20 s | 手を静止させ身体だけ並進                |
    /// | 区間3      | 20 s | 頭部を静止させ手だけ                    |
    /// | 区間4      | 20 s | 区間1と同種                             |
    ///
    /// 記録モードと実験モードで同一のスケジュールを使う。両者がずれると、
    /// 録画の区間境界と実験ログのマーカーが対応しなくなる。
    /// </summary>
    public sealed class RecordingSchedule
    {
        /// <summary>
        /// 仕様書 §5.4 の区間内容。区間数が 4 以外に設定された場合はここを流用できないので、
        /// 汎用ラベルにフォールバックする（パイロットでの試行錯誤を想定した逃げ道であり、
        /// 本番では 4 区間で運用する）。
        /// </summary>
        private static readonly string[] SpecSegmentInstructions =
        {
            "区間1：頭部を並進させながら、同時に手も動かしてください",
            "区間2：手は止めたまま、身体だけを並進させてください",
            "区間3：頭部は止めたまま、手だけを動かしてください",
            "区間4：頭部を並進させながら、同時に手も動かしてください",
        };

        public const string BaselinePhaseId = "baseline";
        public const string LeadInPhaseId = "lead_in";

        private readonly SchedulePhase[] phases;

        public IReadOnlyList<SchedulePhase> Phases => phases;

        public float TotalSeconds { get; }

        private RecordingSchedule(SchedulePhase[] phases, float totalSeconds)
        {
            this.phases = phases;
            TotalSeconds = totalSeconds;
        }

        public static RecordingSchedule FromSettings(ExperimentSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var built = new List<SchedulePhase>(settings.SegmentCount + 2);
            float cursor = 0f;

            built.Add(new SchedulePhase(
                BaselinePhaseId,
                "基準姿勢：両手を前方に開いて静止してください",
                cursor, settings.BaselineHoldSeconds, includedInAnalysis: false));
            cursor += settings.BaselineHoldSeconds;

            built.Add(new SchedulePhase(
                LeadInPhaseId,
                "導入：動きはじめてください（この区間は解析に使いません）",
                cursor, settings.LeadInSeconds, includedInAnalysis: false));
            cursor += settings.LeadInSeconds;

            for (int i = 0; i < settings.SegmentCount; i++)
            {
                string instruction = i < SpecSegmentInstructions.Length
                    ? SpecSegmentInstructions[i]
                    : $"区間{i + 1}";

                built.Add(new SchedulePhase(
                    $"segment_{i + 1}", instruction,
                    cursor, settings.SegmentSeconds, includedInAnalysis: true));
                cursor += settings.SegmentSeconds;
            }

            return new RecordingSchedule(built.ToArray(), cursor);
        }

        /// <summary>
        /// 経過時刻に対応するフェーズの添字。範囲外なら -1。
        /// 境界は [start, end) とする。20.0 s ちょうどは次の区間の先頭フレーム。
        /// </summary>
        public int PhaseIndexAt(float elapsedSeconds)
        {
            if (elapsedSeconds < 0f) return -1;

            for (int i = 0; i < phases.Length; i++)
            {
                if (elapsedSeconds < phases[i].EndSeconds) return i;
            }

            return -1;
        }

        public bool TryGetPhaseAt(float elapsedSeconds, out SchedulePhase phase)
        {
            int index = PhaseIndexAt(elapsedSeconds);
            if (index < 0)
            {
                phase = default;
                return false;
            }

            phase = phases[index];
            return true;
        }

        /// <summary>フェーズ境界（各フェーズの開始時刻）。区間マーカーの予定位置に相当する。</summary>
        public float[] BoundarySeconds()
        {
            var boundaries = new float[phases.Length];
            for (int i = 0; i < phases.Length; i++) boundaries[i] = phases[i].StartSeconds;
            return boundaries;
        }

        /// <summary>設定値がスケジュールとして成立しているか。記録開始前の検証に使う。</summary>
        public bool Validate(out string error)
        {
            if (phases.Length == 0)
            {
                error = "フェーズが 1 つもありません。";
                return false;
            }

            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].DurationSeconds <= 0f)
                {
                    error = $"フェーズ {phases[i].Id} の長さが 0 以下です。";
                    return false;
                }
            }

            if (!Mathf.Approximately(phases[phases.Length - 1].EndSeconds, TotalSeconds))
            {
                error = "フェーズの合計と総時間が一致しません。";
                return false;
            }

            error = null;
            return true;
        }
    }
}
