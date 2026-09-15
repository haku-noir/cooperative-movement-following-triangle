using System.Collections.Generic;
using System.IO;
using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 刺激番号と録画ファイルの対応（仕様書 §5.5：3 条件 × 3 録画刺激）。
    ///
    /// 対応はファイル名ではなく録画メタデータの recordingId で決める。
    /// ファイル名は日時を含むため撮り直すたびに変わるが、recordingId は
    /// 実験者が意図して付けた識別子なので安定している。
    /// </summary>
    [DisallowMultipleComponent]
    public class StimulusCatalog : MonoBehaviour
    {
        [SerializeField] private ExperimentSettings settings;

        [SerializeField]
        [Tooltip("刺激の recordingId を提示順とは無関係に 3 つ並べる。" +
                 "ここでの並び順が刺激番号 1, 2, 3 になる。")]
        private string[] stimulusIds = { "take1", "take2", "take3" };

        private readonly Dictionary<string, string> pathById = new Dictionary<string, string>();
        private bool scanned;

        public int DeclaredCount => stimulusIds != null ? stimulusIds.Length : 0;

        /// <summary>
        /// 宣言した刺激がすべて揃っているか。
        /// 揃っていない状態で実験を走らせるとカウンターバランスが成立しないため、
        /// この値は CSV ヘッダにも書き出す。
        /// </summary>
        public bool IsComplete { get; private set; }

        public string LastScanSummary { get; private set; } = "";

        public void SetSettings(ExperimentSettings value) => settings = value;

        /// <summary>
        /// 録画フォルダを走査して recordingId とファイルの対応を作る。
        /// セッション開始時に 1 回呼ぶ。
        /// </summary>
        public void Scan()
        {
            pathById.Clear();
            scanned = true;

            var found = new List<string>();
            var missing = new List<string>();
            var invalid = new List<string>();

            foreach (string path in RecordingStorage.ListRecordings(settings))
            {
                if (!RecordingStorage.TryLoad(path, out var recording, out _)) continue;

                string id = recording.metadata.recordingId;
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (!recording.metadata.valid)
                {
                    // 無効フラグ付きの録画は刺激として使えない（§1）。
                    // 黙って採用すると、再センタリング後の座標系で取った運動を刺激にしてしまう。
                    invalid.Add($"{id} ({Path.GetFileName(path)}): {recording.metadata.invalidReason}");
                    continue;
                }

                // 同じ recordingId が複数あれば、新しいほう（一覧の後ろ）で上書きする。
                pathById[id] = path;
            }

            foreach (string id in stimulusIds ?? new string[0])
            {
                if (pathById.ContainsKey(id)) found.Add(id);
                else missing.Add(id);
            }

            IsComplete = missing.Count == 0 && DeclaredCount == GraecoLatinSquare.Order;

            LastScanSummary =
                $"見つかった刺激 {found.Count}/{DeclaredCount}" +
                (missing.Count > 0 ? $"、不足: {string.Join(", ", missing)}" : "") +
                (invalid.Count > 0 ? $"、無効フラグ付きで除外: {string.Join(" / ", invalid)}" : "");

            if (IsComplete) Debug.Log($"[{nameof(StimulusCatalog)}] {LastScanSummary}");
            else Debug.LogError($"[{nameof(StimulusCatalog)}] {LastScanSummary}");
        }

        /// <summary>
        /// 刺激番号（0 始まり）から録画を読み込む。
        /// </summary>
        public bool TryLoad(int stimulusIndex, out RecordingFile recording, out string stimulusId,
            out string path, out string error)
        {
            recording = null;
            stimulusId = null;
            path = null;

            if (!scanned) Scan();

            if (stimulusIds == null || stimulusIds.Length == 0)
            {
                error = "刺激 ID が 1 つも設定されていません。";
                return false;
            }

            // 刺激が 3 本揃っていない場合でも、エディタ検証のために循環させて動かす。
            // ただし IsComplete が false のまま本番データを取ってはならない。
            // その状態は CSV ヘッダの stimulus_catalog_complete に残る。
            stimulusId = stimulusIds[stimulusIndex % stimulusIds.Length];

            if (!pathById.TryGetValue(stimulusId, out path))
            {
                // 宣言した ID が見つからないときは、走査で見つかった何かで代用する。
                // これも検証用の逃げ道であり、IsComplete は false のまま。
                foreach (var pair in pathById)
                {
                    stimulusId = pair.Key;
                    path = pair.Value;
                    break;
                }

                if (path == null)
                {
                    error = $"録画が 1 つもありません: {RecordingStorage.DirectoryFor(settings)}";
                    return false;
                }
            }

            return RecordingStorage.TryLoad(path, out recording, out error);
        }
    }
}
