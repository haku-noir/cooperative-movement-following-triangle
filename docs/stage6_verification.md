# 段階6 の動作確認手順（エディタ上の検証手段）

対象：仕様書 §8 の 3 項目。

| §8 の項目 | 状態 |
|---|---|
| §8.1 モック再生モード | 段階1〜5 で実装済み（`MockVertexSource` + Experiment シーン） |
| §8.2 合成録画生成機能 | **段階6 で実装** |
| §8.3 誤差算出の単体テスト | 段階4 で実装済み（本段階でパイプライン通しの検証を追加） |

---

## 1. 合成録画の生成（§8.2）

メニュー **`Following Triangle > Generate Synthetic Stimuli (take1-3)`**

既知の正弦波運動から刺激 3 本（`take1` / `take2` / `take3`）を生成し、
`recordings` フォルダに保存します。**実機で演者を撮らなくても実験モードを
最後まで通せます。**

### 生成される運動は §5.4 の区間構成を再現する

| 区間 | 運動 |
|---|---|
| 基準姿勢 | 完全に静止（Registration の基準になるため） |
| 導入 | 複合（頭部並進 + 手） |
| 区間1 | 複合 |
| 区間2 | 並進のみ（手は身体に対して静止し、身体と一緒に並進する） |
| 区間3 | 手のみ（頭部は静止） |
| 区間4 | 複合 |

区間の切れ目で位置が飛ばないよう、各区間の運動には両端で 0 になる包絡（sin²）を
掛けています。周波数と区間長の関係に制約を設けずに連続性が保てます。

### 刺激 3 本の違い

位相と周波数だけを変えています。**振幅は変えていません。**
刺激間で難易度が変わると、条件効果と交絡するためです。

### 実機の録画と同じ形式

合成録画は実機の録画とまったく同じ JSON 形式で、`StimulusCatalog` からも
通常の刺激として解決されます。特別扱いが必要だと、それを使った検証が
本番の経路を通らなくなるためです。

メタデータの `notes` に「実機の記録ではない」旨が入り、
`vertexSourceKind` は `SyntheticMotion`、`performerId` は `SYNTHETIC` になります。
**本番データと取り違えないでください。**

---

## 2. パイプライン全体の解析的な自己検査

メニュー **`Following Triangle > Run Pipeline Self-Check`**

合成録画 → 再生 → 体格正規化 → Registration → 誤差算出 を本番と同じ順序で通し、
結果が閉形式の期待値と一致するかを確認して Console に出します。

```
[Pipeline Self-Check]
  刺激: 8371 フレーム, 基準姿勢 270 フレーム平均
  L = 0.4472 m
  Registration (ThreeVertexLeastSquares): scale=1.000000 yaw=0.0000 deg 残差 RMS=0.0000 mm
  既知のずれ: (0.10, 0.00, 0.00) (|offset| = 0.1000 m)
  dist_V0=0.100000  dist_V1=0.100000  dist_V2=0.100000
  dist_sum=0.300000  dist_sum_norm=0.670820
  proc_trans=0.100000  proc_rot_deg=0.0000  proc_residual=0.000000
    [OK] Registration が恒等: 実測 0.000000 / 期待 0.000000
    [OK] dist_V0 = |offset|: 実測 0.100000 / 期待 0.100000
    ...
  すべて期待値と一致しました。
```

個々の関数の単体テストが通っていても、それらを繋いだ経路が正しいとは限りません。
この自己検査は、実験者が「今のビルドで」確認できるようにしたものです。
同じ内容が `PipelineAnalyticTests` として単体テストにも入っています。

---

## 3. 単体テスト（HMD 不要）

`Window > General > Test Runner` → EditMode → Run All。

### 合成録画（`SyntheticRecordingTests`）

| テスト | 何を守っているか |
|---|---|
| `BaselinePhase_IsPerfectlyStatic` | 基準姿勢が完全静止であること（Registration の前提） |
| **`Segment2_TranslatesBodyWithHandsFixedRelativeToNeck`** | **区間2 が「並進のみ」であること** |
| **`Segment3_MovesHandsWithStaticHead`** | **区間3 が「手のみ」であること** |
| `Segment1AndSegment4_MoveBothHeadAndHands` | 区間1・4 が複合であること |
| `PhaseBoundaries_AreContinuous` | 区間の切れ目で位置が飛ばないこと |
| **`HeadRotation_DoesNotAffectNeckVertex`** | **§2.2 が生成された録画でも保たれること** |
| `GeneratedRecording_CarriesAnalyticCalibration` | キャリブレーションが解析値と一致 |
| `GeneratedRecording_SurvivesJsonRoundTrip` | 実機の録画と同じ経路で扱えること |
| `GeneratedRecording_WorksWithNonZeroStartTimestamp` | 絶対時刻の原点が 0 でなくても動くこと |

### パイプライン通し（`PipelineAnalyticTests`）

| テスト | 閉形式の期待値 |
|---|---|
| `IdenticalPerformerAndParticipant_YieldIdentityTransform` | 同体格・同姿勢なら Registration は恒等 |
| **`PerfectFollowing_ProducesZeroErrorThroughoutTheTrial`** | **完全追従なら全区間で誤差 0** |
| **`KnownOffsetDuringTrial_MatchesClosedForm`** | **既知のずれがそのまま各頂点距離になる（3 通り）** |
| `LargerParticipant_StillYieldsZeroErrorWhenFollowingCorrectly` | 体格 1.25 倍でも正しく追従すれば誤差 0 |
| **`ConstantLag_ProducesErrorEqualToPerformerDisplacement`** | **遅れの誤差 = その間に A が動いた距離** |

最後のテストは区間設計が誤差の内訳に現れることも確認しています。

- 区間3（手のみ）では首頂点が静止しているので、遅れても `dist_V0` は 0 のまま
- 区間2（並進のみ）では 3 頂点が同じだけずれるので、純粋な平行移動として現れ
  形状残差は出ない

`PerfectFollowing_ProducesZeroErrorThroughoutTheTrial` は、
パイプラインのどこかに定数バイアスが入っていれば必ず落ちます。

---

## 4. 実機なしで実験モードを最後まで通す

段階5 の手順に、刺激の用意が不要になった版です。

```
Following Triangle > Generate Synthetic Stimuli (take1-3)
Following Triangle > Build Experiment Scene
Following Triangle > Build Boot Scene
Following Triangle > Register Scenes In Build Settings
```

`Boot.unity` を Play すれば、録画を 1 本も撮っていない状態から
3 試行のセッションを通せます。`Stimulus Catalog` の `IsComplete` も true になります。

### 確認 20：既知のずれが CSV に出ること

`Experiment.unity` の `Mock Vertex Source` を **`SyntheticVertexSource`** に
差し替えると、被験者 B が合成録画とまったく同じ運動をします。

1. `Experiment Driver` の `vertexSourceBehaviour` を `SyntheticVertexSource` に変更
2. `SyntheticVertexSource` の `parameters` を、生成した刺激と同じにする
   （既定パラメータのままなら一致します）
3. `Position Offset` を `(0, 0, 0)` にして Play
   → 2 つの三角形が**完全に重なる**
4. 試行の途中で `Position Offset` を `(0.1, 0, 0)` に変更
   → 三角形が 0.1 m 離れる
5. 試行終了後の CSV で、変更後の行の `dist_V0` / `dist_V1` / `dist_V2` が
   **`0.100000`** になっていること

これが本番のパイプラインをそのまま通した、誤差算出の end-to-end 確認になります。

> `SyntheticVertexSource` は時刻を `Time.timeAsDouble` で進めるため、
> 試行の開始と厳密には同期しません。手順 3 の「完全に重なる」は
> 試行開始のタイミングが合ったときの話です。
> 厳密な同期が必要な検証は `PipelineAnalyticTests` が行っています。

---

## 5. 仕様書 §11 の完成条件に対する状態

| 完成条件 | 状態 |
|---|---|
| 記録モードで A の運動を録画し JSON として保存できる | 実装済み（段階2） |
| 記録モードで区間マーカーを打てる | 実装済み（時間ベース / コントローラ両方） |
| 実験モードでキャリブレーション・体格正規化・Registration が動作する | 実装済み（段階3） |
| 3 条件が切り替えられ、C1 と C2 の表示が完全に同一である | 実装済み・テストで固定（段階1, 5） |
| 被験者 ID 入力によりラテン方格の割付が決定される | 実装済み（段階5、グレコ・ラテン方格） |
| §6.2 の CSV が仕様どおり出力される | 実装済み・列順をテストで固定（段階4） |
| §8 のモック再生・合成録画生成・単体テストが動作する | 実装済み（段階6） |
| 再センタリング検出とログ記録が動作する | 実装済み（段階1, 4） |

**すべてエディタ上での確認です。実機での確認は未実施です。**
実機で必要な作業は各段階の手順書の「実機での差し替え」節にまとめてあります。
