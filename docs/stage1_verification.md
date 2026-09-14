# 段階1 の動作確認手順

対象：頂点取得（V0/V1/V2）と自己三角形の描画まで。
記録・再生・Registration・誤差算出・条件フローは段階2 以降。

---

## 0. 前提

- Unity 6000.3.24f1 でプロジェクト `My project` を開く
- **Android Build Support が未インストールだと Meta XR SDK のエディタコードがコンパイルエラーになる**
  （`AndroidExternalToolsSettings` が見つからない）。Unity Hub からモジュール追加が必要。
  追加するまでは、後述の「Meta XR SDK を外した状態」でのみ検証できる。

---

## 1. 単体テスト（HMD 不要・Android モジュール不要）

仕様書 §8.3 の土台。測定の正しさに直結する計算だけを検証する。

### エディタ UI から

`Window > General > Test Runner` → `EditMode` タブ → `Run All`

### コマンドラインから

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe" `
  -batchmode -nographics -silent-crashes `
  -projectPath "C:\Users\maedalab\cooperative-movement-following-triangle\My project" `
  -runTests -testPlatform EditMode `
  -testResults "$env:TEMP\test-results.xml" `
  -logFile "$env:TEMP\unity-tests.log"
```

Unity Editor を開いたままだとプロジェクトがロックされ、batchmode は即座に終了する（return code 1）。
コマンドラインで回すときは Editor を閉じること。

### 確認すべき内容

| テスト | 何を守っているか |
|---|---|
| `NeckVertex_IsCenterEyeOffsetAlongWorldDown` | V0 の定義そのもの（仕様書 §2.2） |
| `NeckVertex_IsInvariantToHeadRotation` | 頭部姿勢が V0 に影響しないこと |
| `ForbiddenLocalDown_MatchesUnderPureYaw_ButDivergesUnderPitchOrRoll` | **ヨーだけでは実装ミスを検出できない**という事実 |
| `ForbiddenLocalDownImplementation_ShiftsVertexBeyondSphereDiameter` | 実装ミス時の混入量が頂点球より大きいこと |
| `C1AndC2_ProduceIdenticalVisualConfig` | C1 と C2 の表示が完全に同一であること |
| `OnlyEdgeVisibilityDiffersAcrossConditions` | 条件間で変わるのが辺の有無だけであること |
| `SelfAndOther_CannotDifferByConstruction` | 自己と相手の見た目が構造上ずれないこと |
| `TrialDuration_MatchesSpecTable` | 3 + 10 + 20×4 = 93 s（仕様書 §5.4） |
| `Experiment2Parameters_AreDisabledByDefault` | スコープ外項目が既定で無効（§0.3） |

---

## 2. エディタ上の目視確認（HMD 不要）

### シーンの生成

メニュー `Following Triangle > Build Sandbox Scene` を実行する。
`Assets/Experiment/Scenes/Sandbox.unity` と `Assets/Experiment/Settings/ExperimentSettings.asset`
が生成され、シーンが開く。

シーンには以下だけが置かれる（装飾は無し）。

- `Editor Preview Camera` … エディタで形を見るためのカメラ。実機の視点ではない
- `Background Room` … 仕様書 §4.4 の背景。床グリッド＋壁の規則模様、Unlit、影なし
- `Mock Vertex Source` … 頭部と両手のダミー Transform
- `Self Triangle` … 三角形の描画
- `Self Triangle Presenter` … 上記を毎フレーム結線する

### 確認 1：V0 が頭部回転に追従しないこと（最重要）

1. Play する。`Mock Vertex Source` の `animate` が on なら、頭部ダミーが
   **ヨーとピッチの両方** を振りながら左右に並進する
2. Hierarchy で `Self Triangle > V0` を選択し、Inspector の Transform position を見る
3. `HeadProxy` の rotation が変化しても、`V0` の position は
   `HeadProxy.position + (0, -0.15, 0)` のままであること

さらに確実な確認：

1. `Mock Vertex Source` の `animate` を off にする
2. Scene ビューで `HeadProxy` を選択し、回転ギズモでピッチ方向（X 軸まわり）に大きく回す
3. **`V0` の球が 1 mm も動かないこと**。動いたら実装が仕様書 §2.2 に違反している

> ピッチ・ロールで確認すること。ヨー（Y 軸まわり）だけでは、
> 誤った実装でも球が動かないため検証にならない。

### 確認 2：d を変えると V0 が真下に動くこと

`Assets/Experiment/Settings/ExperimentSettings.asset` の `Neck Offset D` を
0.15 → 0.30 に変える。Play 中でも即座に反映される。
`V0` が真下へ 0.15 m 移動し、水平方向には動かないこと。

### 確認 3：C1 と C2 の表示が同一で、C3 だけ辺が出ること

Play 中に `Self Triangle Presenter` の `Condition` を切り替える。

- `C1` → 球 3 個のみ
- `C2` → **C1 とまったく同じ見た目**（辺が出ない、球のサイズ・色も変わらない）
- `C3` → 球 3 個 + 辺 3 本

C1 と C2 で少しでも見た目が変われば実装の誤り。
（この同一性はテスト `C1AndC2_ProduceIdenticalVisualConfig` でも固定してある）

### 確認 4：背景が静止していること

Play 中、`Background Room` の Transform が一切変化しないこと。
三角形だけが動き、部屋は動かない（仕様書 §4.4）。

---

## 3. 実機での確認（Android Build Support 導入後）

段階1 の実機側コードは `Assets/Experiment/Xr/` にあるが、
シーンへの OVRCameraRig 配置と XR Plug-in Management の設定はまだ行っていない。
Android モジュール導入後に実施する。

- `OvrVertexSource` … CenterEyeAnchor と OVRSkeleton / OVRHand から頂点を取得
- `XrRuntimeConfig` … 90 Hz の設定と**実効値の検証**、床原点の検証、再センタリング検出
