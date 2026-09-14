# 段階2 の動作確認手順（記録モード）

対象：録画と JSON 保存、キャリブレーション、区間マーカー。
再生・Registration・体格正規化・誤差算出は段階3 以降。

---

## 0. 事前準備（1 回だけ）

記録モードの HUD は TextMeshPro を使う。TMP の必須リソースが未導入だと文字が出ない。

メニュー **`Following Triangle > Import TMP Essential Resources`** を実行する。
`Assets/TextMesh Pro/Resources/TMP Settings.asset` ほかが生成されれば完了。既に入っていれば何もしない。

> batchmode から入れる場合は `-quit` を付けないこと。
> `AssetDatabase.ImportPackage` は非同期なので、`-quit` だと取り込み前に Editor が落ちる。
> ```
> Unity.exe -batchmode -nographics -projectPath <path> \
>   -executeMethod FollowingTriangle.Editor.TmpEssentialResourcesInstaller.InstallAndExit
> ```

---

## 1. 単体テスト（HMD 不要）

`Window > General > Test Runner` → EditMode → Run All。

段階2 で追加したもの：

| テスト | 何を守っているか |
|---|---|
| `RecordingScheduleTests.Schedule_MatchesSpecTable` | 3 + 10 + 20×4 の各境界（§5.4） |
| `OnlySegments_AreIncludedInAnalysis` | 基準姿勢と導入が解析対象外であること |
| `PhaseBoundaries_AreHalfOpenIntervals` | 境界が [start, end) であること |
| `CalibrationAccumulatorTests.StaticPose_YieldsExactArmLength` | L の算出（§5.1） |
| `EyeHeight_IsWorldYOfCenterEye` | h の算出が Floor Level 前提であること |
| `BodyScaleFactor_IsRatioOfCharacteristicLengths` | s = L_B / L_A（§5.2） |
| `Calibration_RejectsLowConfidenceSamples` | 外挿された手をキャリブレーションに混ぜないこと（§7.1） |
| `TimeBasedMarkers_AreEmittedAtEveryPhaseBoundary` | 区間マーカーが全境界に打たれること |
| `TimeBasedMarkers_SurviveDroppedFrames` | フレーム落ちでマーカーを打ち漏らさないこと |
| `ManualMarkers_AssignPhasesInOrder` | コントローラ方式のマーカー割当 |
| `Recenter_MarksRecordingInvalid` | 再センタリング録画に無効フラグ（§1） |
| `JsonRoundTrip_PreservesFramesMarkersAndMetadata` | 保存形式の往復（§3.1） |
| `Deserialize_RejectsUnknownFormatVersion` | 将来形式を黙って読まないこと |
| `Deserialize_RejectsNonMonotonicTimestamps` | 再生時の補間が破綻する録画を弾く（§7.3） |
| `RecordedFrame_CanRecomputeNeckVertexWithDifferentOffset` | V0 の二重保存が機能すること |

---

## 2. エディタ上で録画を 1 本通す（HMD 不要）

### シーンの生成

メニュー **`Following Triangle > Build Recorder Scene`**（Play を停止した状態で）。
`Assets/Experiment/Scenes/Recorder.unity` が生成される。

構成：

- `Mock Vertex Source` … 頭部と両手のダミー Transform（`HeadProxy` / `LeftHandProxy` / `RightHandProxy`）
- `Recorder Input (Keyboard)` … Space = 確定 / M = 区間マーカー / Esc = 中断
- `Performer Triangle` … 演者 A に見せる自分の三角形
- `Recorder HUD` … ガイド文と警告
- `Recorder Controller` … 進行役
- `Background Room`, `Editor Preview Camera`

### 手順

1. **`Mock Vertex Source` の `Animate` を off にする**（静止姿勢を作るため）
2. Play する。HUD に「確定キーでキャリブレーションを開始します」
3. **Space** を押す → 「基準姿勢を保持してください」＋残り秒数
4. 3 秒待つ → 「キャリブレーション完了 眼高 1.60 m / 体格指標 0.40 m」
   Console にも `h=`, `L_left=`, `L_right=`, `L=`, `d/h=` が出る
5. **`Animate` を on に戻す**（演技の代わり）
6. **Space** → 録画開始。HUD が区間ごとの指示に切り替わる
   - 0–3 s 基準姿勢 / 3–13 s 導入 / 13–33 s 区間1 / 33–53 s 区間2 / 53–73 s 区間3 / 73–93 s 区間4
7. 93 秒後に自動停止し、「保存しました <ファイル名>」

> 全部待つのが長い場合は、`ExperimentSettings` の `Segment Seconds` を 2 に下げると
> 合計 21 秒で通せる。確認後に 20 へ戻すこと。

### 確認 5：保存された JSON の中身

メニュー **`Following Triangle > Inspect Latest Recording`** を実行。Console に要約が出る。

```
frames=8371 duration=93.00s meanFps=90.0 maxInterval=11.1ms
d=0.150m d/h=0.0938 bone=MiddleMcp
calibration: h=1.600m L=0.400m (sd=0.0mm, n=270)
markers=6 mode=TimeBased recenter=0
区間マーカー:
  baseline     t=  0.000s (予定   0.00s, 差   0.000s) analysis=False
  lead_in      t=  3.000s (予定   3.00s, 差   0.000s) analysis=False
  segment_1    t= 13.011s (予定  13.00s, 差   0.011s) analysis=True
  ...
```

見るべき点：

- `markers=6`（フェーズ数と一致）
- 各マーカーの「差」が 1 フレーム（約 0.011 s）以内
- `analysis` が baseline / lead_in のみ False
- `calibration` の `L` が期待値、`sd` が小さいこと
- `recenter=0`
- `valid=true`（false なら理由が併記される）

ファイル本体は **`Following Triangle > Open Recordings Folder`** で開ける。

### 確認 6：コントローラ方式の区間マーカー

1. `ExperimentSettings` の `Segment Marker Mode` を `Controller` に変更
2. Recorder シーンを Play し、録画中に **M** キーを 6 回、区間の切れ目で押す
3. 押した回数が 6 に満たないと、保存時に無効フラグが付き理由が表示される

### 確認 7：トラッキング警告

`ExperimentSettings` の `Show Performer Tracking Warning` が on の状態で、
`MockVertexSource` は常に高信頼を返すため警告は出ない。
実機では手が視野外に出ると「右手のトラッキングが不安定です」が出る。

---

## 3. 実機で使うときの差し替え

Recorder シーンはエディタ検証用。実機では 2 つを差し替える。

| エディタ | 実機 |
|---|---|
| `MockVertexSource` | `OvrVertexSource`（CenterEyeAnchor / OVRSkeleton / OVRHand を配線） |
| `KeyboardRecorderInput` | `OvrRecorderInput` |

加えて `XrRuntimeConfig` をシーンに置き、`RecorderController` の
`recenterMonitorBehaviour` と `xrRuntimeInfoBehaviour` に割り当てる。
これで再センタリング検出（§1）と、SDK バージョン・実効リフレッシュレートの
メタデータ記録（§6.2）が有効になる。

> `OvrRecorderInput` は **実験者が持つコントローラ** を前提としている。
> 演者 A の手頂点はハンドトラッキングから取る（§2.3）ため、
> A 自身がコントローラを握ると骨格が取得できなくなる。

保存先は `Application.persistentDataPath/recordings/`。
Quest からは adb で取り出す。
