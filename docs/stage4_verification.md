# 段階4 の動作確認手順（誤差算出と CSV ログ）

対象：毎フレームの誤差量（§6.1）と CSV 出力（§6.2）。
条件割付・教示・試行フロー制御は段階5。

---

## 0. 確認いただいた判断

| 項目 | 決定 | 実装上の意味 |
|---|---|---|
| A 側の座標列 | **変換後のみ** | 誤差列と同じ空間になる。変換パラメータはヘッダに残すので生座標は復元可能 |
| 正規化分母 | **キャリブレーションの固定値 L_B** | 腕を伸ばして誤差を下げる抜け道を塞ぐ（§5.2 の趣旨） |
| Procrustes の回転 | **3 自由度の完全回転** | 軸角表現の角度。ピッチ・ロールのずれも拾う |
| 退化時の表現 | **値は出しつつフラグ列** | `proc_rot_defined` 列を追加（仕様書の列一覧への唯一の追加） |

---

## 1. §6.3 を守るためにしていないこと

アプリ内では **一切の閾値判定・二値化を行いません**。

- 一致判定なし
- 継続長・破綻回数・復帰時間の算出なし
- 最小内角の閾値判定なし（§7.2 の 10 度は後処理で決める）
- 低信頼フレームの除外なし（全フレーム記録し、信頼度列で後処理判断）

唯一のブール列 `proc_rot_defined` は「数値的に回転が一意に決まったか」だけを示し、
実験上の判断を含みません。幾何的な退化の閾値は `tri_min_angle_B` 列から
後処理で決めます。

---

## 2. 単体テスト（HMD 不要）

`Window > General > Test Runner` → EditMode → Run All。

### §8.3 が名指しで要求するテスト

仕様書 §8.3：「既知の変位（例：B の三角形を A から x 方向に 0.1 m ずらした状態）に対して、
頂点距離・Procrustes 成分が期待値を返すことのテスト」

| テスト | 期待値 |
|---|---|
| `KnownTranslation_ProducesExpectedVertexDistances` | 各頂点距離 0.1 m、総和 0.3 m、正規化 0.6（分母 0.5 m） |
| `KnownTranslation_ProducesPureTranslationInProcrustes` | 並進 0.1 m、回転 0 度、形状残差 0 |
| `KnownRotation_IsRecoveredAsAxisAngleMagnitude` | Unity の `Quaternion.Angle` と一致（4 通りの姿勢） |

回転テストは **形状残差が 0 になること** も確認しています。
Procrustes の回転行列の符号規約を取り違えると、回転角だけ正しくて残差が 0 に
ならないという気づきにくい壊れ方をするためです。

### その他

| テスト | 何を守っているか |
|---|---|
| `ShapeDifference_AppearsAsResidualNotRotation` | 剛体成分と形状残差の分解（§6.1-2） |
| **`NormalizationUsesGivenLengthNotFrameGeometry`** | **分母が毎フレーム値でないこと** |
| `ZeroNormalizationLength_LeavesNormalisedValuesAtZero` | 0 除算で NaN を CSV に出さない |
| `HeadDirectionResidual_IsAngleBetweenForwardVectors` | 頭部方向残差（§6.1-3） |
| `TriangleArea_MatchesAnalyticValue` / `MinInteriorAngle_MatchesAnalyticValue` | 解析値との一致（§6.1-4） |
| `FullyDegenerateTriangle_FlagsRotationAsNotWellDefined` | 退化時も値を出しフラグを下げる |
| **`ColumnHeader_MatchesSpecOrder`** | **§6.2 の列の並びそのもの** |
| `EveryRowHasSameColumnCountAsHeader` | 列数の一致 |
| `HeaderComments_ContainRequiredMetadata` | §6.2 が要求するヘッダ項目 |
| `HeaderComments_DocumentTheAppliedTransform` | A 側の生座標を復元できること |
| `RecenterFlag_IsPerFrameEventNotCumulative` | 発生フレームのみ 1 |
| **`NumberFormatting_IsLocaleIndependent`** | **ロケール依存の書式で CSV が壊れないこと** |

ロケールのテストは、スレッドのカルチャを `de-DE`（小数点がカンマ）に
切り替えて実行します。固定していなければ小数点がカンマになり、列数が増えて
CSV が壊れます。この環境のロケールが日本語であることを踏まえた予防です。

---

## 3. エディタ上で CSV を出す（HMD 不要）

段階3 の Playback シーンがそのまま使えます（`Trial Logger` が追加されています）。

### 手順

1. メニュー `Following Triangle > Build Playback Scene`（Play を停止した状態で）
2. `Mock Vertex Source` の `Animate` を off → Play → **Space**（B のキャリブレーション 3 秒）
3. **Space** → 試行開始。基準姿勢 3 秒 → Registration 確定
4. `Animate` を on にすると、自己三角形が動いて誤差が出る
5. 試行終了時に Console へ

```
試行ログを保存しました: .../logs/20260915-123456_B01_trial01_C3_take1.csv
  8100 行
```

> 時間短縮には `ExperimentSettings` の `Segment Seconds` を 2 に。
> 録画側も同じ設定で取り直す必要があります。

### 確認 12：CSV の中身

`Following Triangle > Open Logs Folder` でフォルダを開き、CSV をテキストエディタで確認。

- 先頭のコメント行に `# participant_id`, `# condition`, `# neck_offset_d_m`,
  `# body_scale_factor`, `# normalization_source: calibration_fixed`,
  `# a_columns_are: post_transform` があること
- 列名行が §6.2 の順序どおりであること
- `phase_marker` が `lead_in` → `segment_1` → … と切り替わること
- `recenter_flag` が全行 0 であること

Python で読む場合：

```python
import pandas as pd
df = pd.read_csv(path, comment='#')
print(df.columns.tolist())
print(df.groupby('phase_marker')['dist_sum_norm'].describe())
```

ヘッダのメタデータを読むには：

```python
meta = {}
with open(path, encoding='utf-8') as f:
    for line in f:
        if not line.startswith('#'):
            break
        key, _, value = line[2:].partition(': ')
        meta[key.strip()] = value.strip()
```

### 確認 13：誤差が意味を持つこと

`Animate` を on にした状態で試行を走らせ、CSV を確認します。

- `dist_sum` が 0 より大きく、時間とともに変動すること
- `dist_sum_norm` が `dist_sum / normalization_length_m` と一致すること
- `proc_residual` が `dist_sum` より小さいこと
  （剛体成分を除いた残りなので、必ず小さくなる）
- `tri_min_angle_B` が 20〜30 度前後で安定していること

### 確認 14：正規化分母が固定であること

試行中に `LeftHandProxy` / `RightHandProxy` を大きく動かして腕を広げても、
CSV ヘッダの `normalization_length_m` は変わりません。
`dist_sum_norm` は常に同じ分母で割られます。

毎フレーム分母だと、腕を広げるだけで `dist_sum_norm` が下がる抜け道になります。

---

## 4. 実機での差し替え

段階2・3 と同じ。加えて `XrRuntimeConfig` を
`Playback Verification Driver` の `recenterMonitorBehaviour` と
`xrRuntimeInfoBehaviour` に割り当てると、

- 再センタリングが `recenter_flag` 列とヘッダの `recenter_count` に載る（§1）
- SDK バージョンと実効リフレッシュレートがヘッダに載る（§6.2）

CSV の保存先は `Application.persistentDataPath/logs/`。

---

## 5. 出力される CSV は試行ごとに 2 本

| ファイル | 内容 | 区間 |
|---|---|---|
| `..._C3_take1.csv` | 追従課題のログ | 導入区間の頭から試行終了まで |
| `..._C3_take1_registration.csv` | 基準姿勢フェーズのログ | 試行開始から基準姿勢の終わりまで |

### なぜ別ファイルなのか

基準姿勢は追従課題ではありません。この区間では A の三角形がまだ見えておらず、
算出される「誤差」は追従成績ではなく **Registration の当てはまり具合** を表す量です。
同じファイルに混ぜると、後処理で取り違えて課題成績に数えてしまう余地が残ります。

### 列構成は試行ログと完全に同一

同じ読み込みコードで扱えるほうが解析側の間違いが減るため、列は 1 つも変えていません。
区別はファイル名の `_registration` と、ヘッダの `# log_kind:` で行います。

```python
import pandas as pd

def load(path):
    meta = {}
    with open(path, encoding='utf-8') as f:
        for line in f:
            if not line.startswith('#'):
                break
            k, _, v = line[2:].partition(': ')
            meta[k.strip()] = v.strip()
    return meta, pd.read_csv(path, comment='#')

meta, df = load(path)
assert meta['log_kind'] in ('trial', 'registration')
```

### 「平均に採用されたか」は別列を持たない

Registration の平均に入るのは両手が高信頼だったフレームだけですが、
それは `hand_L_tracked` / `hand_L_conf` / `hand_R_tracked` / `hand_R_conf` から
厳密に導出できます。冗長な列は足していません。

```python
accepted = (df.hand_L_tracked == 1) & (df.hand_R_tracked == 1) \
         & (df.hand_L_conf == 1) & (df.hand_R_conf == 1)
```

採用数と総フレーム数はヘッダにも入ります
（`# baseline_accepted_count`, `# baseline_frame_count`）。
差が大きい試行はトラッキングが不安定で、Registration の信頼性が落ちています。

### Registration ログのヘッダにある診断情報

```
# log_kind: registration
# registration_method: ThreeVertexLeastSquares
# registration_residual_rms_m: 0.003100
# registration_residual_per_vertex_m: 0.002800 0.003800 0.002500
# registration_reference_A_v0: 0.000000 1.450000 0.000000
# registration_baseline_B_v0: 0.010000 1.500000 0.000000
# baseline_frame_count: 270
# baseline_accepted_count: 262
```

頂点ごとの残差を出しているのは、RMS だけではどの頂点で合っていないのかが
分からないためです。推定法によって残差の配分が変わる（首頂点固定法なら V0 が 0）ので、
手法の選択が妥当だったかを後から評価できます。

### 確認 15：基準姿勢ログの中身

試行を 1 本走らせると Console に 2 行出ます。

```
基準姿勢ログを保存しました (270 行, うち平均採用 270 件): .../..._registration.csv
試行ログを保存しました (8100 行): .../....csv
```

`_registration.csv` を開いて確認する点：

- `# log_kind: registration`
- 全行の `phase_marker` が `baseline`
- `t` が 0 付近から `baseline_hold_s` まで
- `dist_sum` が小さい値で安定していること（Registration が効いていれば数 mm 台）
- `# registration_residual_per_vertex_m` の 3 値が近いこと
  （等重み最小二乗の場合。首頂点固定法なら 1 つ目が 0）

### 再センタリングの扱い

基準姿勢中に再センタリングが起きると、蓄積した B の三角形が途中で別の座標系のものに
入れ替わります。この場合 Registration ログのヘッダが

```
# trial_valid: false
# invalid_reason: 基準姿勢中に再センタリングが 1 回発生しました (§1)。この Registration は無効です。
```

となり、ファイル名にも `_INVALID` が付きます。
