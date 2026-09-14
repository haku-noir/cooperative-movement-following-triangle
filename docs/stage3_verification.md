# 段階3 の動作確認手順（再生・Registration・体格正規化）

対象：録画の再生（§7.3）、位置合わせ（§5.3）、体格正規化（§5.2）、
被験者プロファイル（§5.1-3）。
誤差算出と CSV 出力は段階4、条件割付と試行フローは段階5。

---

## 0. 確認いただいた判断

| 項目 | 決定 |
|---|---|
| Registration の A 側基準 | **A の基準姿勢区間 3 s の平均** |
| 推定法 | **3 手法すべて実装し設定で切替**（既定は 3 頂点等重み最小二乗） |
| 基準姿勢中の相手三角形 | **表示しない** |

`ExperimentSettings` の追加項目：

- `Registration Method` … `ThreeVertexLeastSquares` / `NeckAnchored` / `HandsOnly`
- `Recompute Neck Vertex On Playback` … 既定 on（現在の `d` で V0 を引き直す）
- `Show Other Triangle During Baseline` … 既定 off

---

## 1. 単体テスト（HMD 不要）

`Window > General > Test Runner` → EditMode → Run All。

### Registration（`RegistrationTests`）

| テスト | 何を守っているか |
|---|---|
| `PureTranslation_IsRecoveredExactly` | 並進の復元 |
| `PureYaw_IsRecoveredExactly`（15/-40/90/179 度） | ヨーの復元 |
| `YawAndTranslation_AreRecoveredTogether` | 合成の復元 |
| **`PitchInBaseline_DoesNotProducePitchInTransform`** | **ピッチを含めないこと（注意点2）** |
| **`RollInBaseline_DoesNotProduceRollInTransform`** | **ロールを含めないこと（注意点2）** |
| `HeightDifference_DoesNotLeakIntoYaw` | 身長差が並進で吸収されヨーに漏れないこと |
| `BodyScale_IsRatioOfCharacteristicLengths` | s = L_B / L_A（§5.2） |
| **`ScalePivot_IsFixedSoTranslationIsAlsoScaled`** | **スケール中心が固定点であること（注意点3）** |
| `AllMethods_AgreeWhenExactFitExists` | 3 手法が完全一致可能な場合に一致すること |
| `NeckAnchored_LeavesZeroResidualAtNeck` | 手法の性質の違い |
| `LeastSquares_MinimisesTotalResidual` | 等重み法が残差二乗和を最小化すること |
| `ApplyRotation_AddsYawOnlyAndKeepsPitch` | A のピッチを保ちつつヨーだけ足すこと |

ピッチ・ロールのテストは「変換に含まれない」だけでなく
「**残差として残る**」ことも確認している。傾きを吸収してしまう実装だと
テストが落ちる。

### 再生（`RecordingPlaybackTests`）

| テスト | 何を守っているか |
|---|---|
| `SampleAt_IsRelativeToFirstFrameRegardlessOfAbsoluteTimestamp` | 絶対時刻の原点に依存しないこと |
| `SampleAt_InterpolatesPositionLinearly` | 位置の線形補間（§7.3） |
| `SampleAt_InterpolatesRotationBySlerp` | 回転の Slerp（§7.3） |
| **`PlaybackAtDifferentFrameRate_PreservesTimeline`** | **30 Hz 録画を 90 Hz で引いても時間軸が保たれること** |
| `SampleAt_IsOrderIndependent` | カーソル最適化が結果を変えないこと |
| `HandTrackingState_IsNotInterpolated` | 信頼度に中間値を作らないこと |
| `RecomputedNeckVertex_FollowsCurrentOffset` | V0 の二重保存が機能すること |
| `BaselineAverage_AveragesOverBaselineWindowOnly` | A 側基準が基準姿勢区間の平均であること |
| `BaselineAverage_SuppressesJitterBetterThanSingleFrame` | 平均が単一フレームより真値に近いこと |

---

## 2. エディタ上で再生を確認する（HMD 不要）

### 前提

**Recorder シーンで録画を 1 本取っておくこと**（段階2 の手順）。
`recordings` フォルダの最新ファイルが自動で読み込まれる。

> 時間短縮のため `ExperimentSettings` の `Segment Seconds` を 2 に下げておくと、
> 録画も再生も 21 秒で通せる。段階4 に進む前に 20 へ戻すこと。

### シーンの生成

メニュー **`Following Triangle > Build Playback Scene`**（Play を停止した状態で）。
`Assets/Experiment/Scenes/Playback.unity` が生成される。

構成：

- `Self Triangle (B)` … 被験者側。`Mock Vertex Source` が駆動
- `Other Triangle (A, recorded)` … 録画側。`Stimulus Presenter` が駆動
- `Playback Verification Driver` … 段階3 専用のドライバ（段階5 で置き換わる）

### 手順

1. **`Mock Vertex Source` の `Animate` を off**
2. Play → **Space** → 被験者 B のキャリブレーション（3 秒静止）
   - Console に `プロファイル保存: .../profiles/MOCK-B.json` と `h=`, `L=` が出る
   - 2 回目以降は既存プロファイルを再利用する（`Reuse Existing Profile`）
3. **Space** → 試行開始。基準姿勢フェーズ（3 秒）
   - **この間、相手三角形は表示されない**
4. 3 秒後に Registration が確定し、相手三角形が現れる
   - Console に変換の内容が出る

```
Registration 確定 (method=ThreeVertexLeastSquares)
  scale=1.0000 pivot=(0.0, 1.5, 0.0) yaw=0.000deg translation=(0.0, 0.0, 0.0)
  residual RMS = 0.3 mm (A 基準 270 フレーム平均 / B 基準 268 サンプル平均)
  L_A = 0.400 m, L_B = 0.400 m
```

5. **`Animate` を on** にすると自己三角形が動き、相手三角形との差が見える

### 確認 8：Registration が効いていること

`Mock Vertex Source` の `HeadProxy` を Play 前に **Y 軸で 40 度回し、Z 方向に 1 m ずらす**。
その状態で手順 1〜4 を行うと、Registration が相手三角形をその位置・向きへ移す。

- Console の `yaw` が約 40 度、`translation` が約 (0, 0, 1) になること
- 基準姿勢の瞬間に、相手三角形と自己三角形がほぼ重なって見えること
- `residual RMS` が数 mm 以下であること

### 確認 9：ピッチ・ロールが入らないこと（注意点2）

`HeadProxy` を **X 軸（ピッチ）で 25 度傾けて** 同じことを行う。

- Console の変換にピッチ成分は現れない（`yaw` のみ）
- 相手三角形は**傾かない**。重なりが悪くなり `residual RMS` が大きく出る

これが正しい挙動。傾いて重なったら実装が §5.3 に違反している。

### 確認 10：体格正規化が固定値であること（注意点3）

1. `profiles/MOCK-B.json` を開き、`characteristicLength` を 2 倍の値に書き換える
2. `Reuse Existing Profile` を on のまま Play → Space
3. Console の `scale` が約 2.0 になり、相手三角形が 2 倍の大きさで提示される
4. **再生中に `LeftHandProxy` / `RightHandProxy` を動かして腕を伸ばしても、
   `scale` は変わらない**（Console に再計算のログが出ない）

実行時に追従してスケールが変わるなら、被験者が腕を伸ばすだけで誤差を
下げられることになり、§5.2 に違反している。

### 確認 11：推定法の切替

`ExperimentSettings` の `Registration Method` を切り替えて手順を繰り返す。
Console の `method=` が変わり、`residual RMS` と `yaw` が手法ごとに
わずかに異なることを確認する（完全に一致させられる配置では 3 手法とも同じ値になる）。

---

## 3. 実機での差し替え

段階2 と同じ。`MockVertexSource` → `OvrVertexSource`、
`KeyboardRecorderInput` → `OvrRecorderInput`、`XrRuntimeConfig` を追加。

被験者プロファイルは `Application.persistentDataPath/profiles/<被験者ID>.json`。
同一被験者の全試行で同じ L を使うため、実験中に消さないこと。
