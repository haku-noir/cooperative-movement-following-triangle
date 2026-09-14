# プラットフォーム構成と非推奨 API の監視メモ

最終更新：2026-09-15

このファイルは「何を選んだか」ではなく **「なぜ選んだか」と「いつ壊れるか」** を記録する。
Unity と Meta XR SDK は非推奨化のサイクルが速く、実験期間中にアップデートが必要になる可能性が高い。

---

## 1. 確定した構成

| 項目 | 選択 | 根拠 |
|---|---|---|
| Unity | 6000.3.24f1（Unity 6.3 LTS） | 仕様書 §1 は 6000.0 LTS または 2022.3 LTS を要求。6.3 LTS はその後継系列。 |
| レンダーパイプライン | URP 17.3.0 | テンプレート既定。Quest 向けは URP が前提。 |
| XR ローダー | Unity OpenXR Plugin 1.18.0 | 仕様書 §1 が OpenXR + Meta Quest feature group を指定。後述のとおり Oculus XR Plugin は非推奨。 |
| Meta XR SDK | `com.meta.xr.sdk.all` 205.0.0 | 仕様書 §1 の指定どおり。Unity 公式レジストリから解決可能（Unity 6000.0.66f2 以降を要求）。 |
| 入力 | Input System 1.20.0（`activeInputHandler: 1`） | 後述のとおり旧 Input Manager は Unity 6.3 で非推奨。 |
| テスト | Unity Test Framework 1.6.0（EditMode） | 仕様書 §8.3。 |

### `com.meta.xr.sdk.all` について

本実装が実際に使うのは `com.meta.xr.sdk.core`（OVRManager / OVRCameraRig / OVRSkeleton / OVRHand）だけで、
all-in-one が引き込む Voice / Platform / Interaction / MRUK / Haptics は一切参照しない。
仕様書 §1 の指定に従って `all` を入れているが、インポート時間と依存の面では `core` だけで足りる。
軽量化したくなった場合は `Packages/manifest.json` の `com.meta.xr.sdk.all` を
`com.meta.xr.sdk.core` に差し替えるだけでよい（本実装のコードは変更不要）。

---

## 2. 非推奨の監視リスト

### 2.1 旧 Input Manager（`UnityEngine.Input`）— Unity 6.3 で非推奨

Unity 6.3 LTS で旧 Input Manager が非推奨になり、将来のバージョンで削除される予定。
`Input.GetKey` / `Input.mousePosition` / `Input.GetMouseButtonDown` などは Input System へ移行が必要。

**本実装の対応**：プロジェクトの `activeInputHandler` は既に `1`（Input System のみ）。
`UnityEngine.Input` は一切使わない。区間マーカーのボタン入力（仕様書 §5.4、段階2）と
試行進行の入力（§5.4 教示フェーズ）は Input System 経由で実装する。

**注意点**：`OnMouseEnter` などの MonoBehaviour コールバックは Input System では動かない。
本実装では使っていないが、エディタ用のユーティリティを足すときは避けること。

### 2.2 Oculus XR Plugin（`com.unity.xr.oculus`）— Unity 6.5 で非推奨

`com.unity.xr.oculus` は Unity 6.5 以降で非推奨となり、本番利用は非推奨。
Meta も Unity OpenXR Plugin への移行を案内しており、Unity OpenXR Plugin は Unity 6 以降と
Meta XR SDK v74 以降を要求する。

**本実装の対応**：最初から Unity OpenXR Plugin を使い、`com.unity.xr.oculus` は導入しない。
確認済みの事実として、`com.meta.xr.sdk.core` 205.0.0 の依存は `com.unity.xr.hands` であり、
`com.unity.xr.oculus` には依存していない。つまり Meta XR SDK 205 系は既に OpenXR 路線に乗っている。

`OVR` 接頭辞のスクリプト・プレハブ・コンポーネントは Unity OpenXR Plugin と互換。
ただし `OVRManager` は Meta 拡張への強い依存があり、Meta 以外のプラットフォームでは動かない。
本実験は Quest 3 専用なのでこれは問題にならない。

### 2.3 将来の移行余地：`OVRSkeleton` → `com.unity.xr.hands`

仕様書 §2.3 は手の頂点取得に `OVRSkeleton.Bones` を指定している。
ベンダ中立な代替として `com.unity.xr.hands` の `XRHandSubsystem` があり、そちらの方が長期的には安全。

ただし **現時点で移行はしない**。理由は、仕様書 §6.1-5 と §7.1 が要求する
`HandConfidence`（Low / High）が Meta 固有の概念であり、`XRHandSubsystem` には
対応する「信頼度」の API がない（`XRHandJoint.trackingState` と
`XRHandSubsystem.updateSuccessFlags` は意味が異なる）。
低信頼区間を後処理で除外できることは本実験の測定妥当性に直結するため、
`OVRHand.HandConfidence` を捨てられない。

**移行に備えた構造**：手の頂点取得は `FollowingTriangle.Core` の `IVertexSource` 越しにしか
使われない。SDK 依存コードは `FollowingTriangle.Xr` アセンブリ 1 つに閉じているので、
将来 `XRHandSubsystem` へ移るとしても、差し替えるのはそのアセンブリだけで済む。

### 2.4 その他

| API | 状態 | 本実装 |
|---|---|---|
| `Object.FindObjectOfType` / `FindObjectsOfType` | Unity 2023 で非推奨（`FindFirstObjectByType` / `FindObjectsByType`） | 使用しない（シーン参照は全てインスペクタ配線） |
| ビルトインレンダーパイプライン | URP / HDRP へ移行済み | URP を使用。シェーダは `Universal Render Pipeline/Unlit` |
| `XRDevice.refreshRate` | 非推奨（`XRDisplaySubsystem`） | リフレッシュレートは `OVRManager.display` を使う（仕様書 §1 の指定） |

---

## 3. アセンブリ構成と、その理由

```
FollowingTriangle.Core          … 純粋な型と幾何計算。XR SDK に一切依存しない
FollowingTriangle.Runtime       … MonoBehaviour（描画・モック入力・背景）。Core のみ参照
FollowingTriangle.Xr            … Meta XR SDK 依存のコードだけを隔離（段階1 後半で追加）
FollowingTriangle.Editor        … エディタツール
FollowingTriangle.Tests.EditMode… 単体テスト。Core のみ参照
```

この分割には 2 つの目的がある。

1. **実機なしで検証できるようにする**（仕様書 §8）。
   測定の正しさを決める計算（頂点定義、Registration、誤差算出、Procrustes）はすべて `Core` にあり、
   HMD も SDK もなしにテストできる。実装者が実機で毎回確認できない以上、
   これを後から足すのは現実的に不可能なので、段階1 の時点で構造として入れてある。

2. **SDK の非推奨化を 1 アセンブリに閉じ込める**。
   2.2 / 2.3 のような移行が起きたとき、書き換えるファイルが `FollowingTriangle.Xr` に限られる。

---

## 4. 未解決（実機ビルド前に必要）

### 4.1 Android Build Support が未インストール

`C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Data\PlaybackEngines\` に
`windowsstandalonesupport` しか無い。Unity Hub から Android Build Support
（OpenJDK / Android SDK & NDK Tools 含む）を追加する必要がある。

これはビルド時だけの問題ではない。**エディタのコンパイルが通らない**。

```
Library\PackageCache\com.meta.xr.sdk.core@.../Editor/RuntimeOptimizer/PerformanceInsight/CaptureTool.cs(43,28):
error CS0103: The name 'AndroidExternalToolsSettings' does not exist in the current context
```

Meta XR SDK 自身のエディタツールが Android モジュール由来の型を参照しているため。
本実装のアセンブリ（FollowingTriangle.*）は 5 つとも独立してコンパイルに成功することを
確認済みだが、エディタのドメイン全体がエラー状態になるとテストランナーなどが不安定になる。

### 4.2 XR Plug-in Management のローダーが未登録

仕様書 §1 は「XR Plugin Management：OpenXR、Meta Quest feature group を有効化」を要求するが、
2026-09-15 時点で **未充足**。`Assets/XR/XRGeneralSettingsPerBuildTarget.asset` の状態：

- `Standalone Providers` の `m_Loaders` が **空**（OpenXR ローダーが登録されていない）
- **Android のエントリが存在しない**（Android プラットフォームが未インストールのため）

一方 `Assets/XR/Settings/OpenXR Package Settings.asset` では OpenXR の機能自体は有効化済み
（`MetaXRFeature`, `MetaXRFoveationFeature`, `OculusTouchControllerProximityProfile` が
Standalone / Android 両方で `m_enabled: 1`）。つまり「機能は有効だがローダーが動かない」状態。

Android モジュール導入後に `Edit > Project Settings > XR Plug-in Management` で
Android タブの OpenXR にチェックを入れ、生成される設定アセットをコミットすること。

### 4.3 Meta XR SDK の自動セットアップが変更したプロジェクト設定

SDK のインポート時に以下が自動変更された。いずれも本実験の測定には影響しないが、
差分の由来が分かるよう記録しておく。

| ファイル | 変更内容 |
|---|---|
| `ProjectSettings/AudioManager.asset` | スペーシャライザを `Meta XR Audio` に設定。本実験は音声を使わない |
| `ProjectSettings/TagManager.asset` | Meta の UI キット用タグ（`QDSUI*`）を 16 個追加 |
| `ProjectSettings/EditorBuildSettings.asset` | XR Management / OpenXR 設定アセットへの参照を追加 |
| `ProjectSettings/ProjectSettings.asset` | `AndroidPreferredInstallLocation` を変更 |
| `Assets/Settings/Mobile_RPAsset.asset` | URP アセットのバージョンを 12 → 13 に更新（Unity 側の移行） |
| `Assets/Resources/*` | Meta XR の各種ランタイム設定アセットを生成 |
| `Assets/Oculus/OculusProjectConfig.asset` | Meta のプロジェクト設定。ハンドトラッキング設定はここに入る |

`OculusProjectConfig.asset` は仕様書 §1 の
`Hand Tracking Support = Controllers And Hands` を設定する場所でもある。
Android プラットフォーム有効化後に設定すること。
