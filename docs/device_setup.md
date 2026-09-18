# Meta Quest 実機で実行する手順

エディタでの動作確認（`docs/quick_test.md`）が済んでいることを前提とします。

---

## 0. 全体の流れ

| | 作業 | 自動化 |
|---|---|---|
| 1 | Quest 側の開発者設定 | 手動（あなた） |
| 2 | プロジェクト設定を Quest 向けにする | **メニュー 1 つ** |
| 3 | ビルドターゲットを Android に切替 | 手動（Unity の仕様上、自動化しない） |
| 4 | XR Plug-in Management で OpenXR 有効化 | 手動（プラットフォーム切替後） |
| 5 | 実機用シーンの生成 | **メニュー 1 つ** |
| 6 | APK をビルドして転送 | 手動 |

---

## 1. Quest 側の準備（初回のみ）

1. スマートフォンの **Meta Horizon アプリ** で対象の Quest を選び、
   **開発者モード**を有効にする（Meta の開発者アカウント登録が必要）
2. Quest を USB で PC に接続
3. Quest を装着し、表示される **「USB デバッグを許可しますか」** を許可

確認：

```
adb devices
```

`device` と表示されればつながっています。`unauthorized` なら手順 3 をやり直してください。

> `adb` は Unity の Android モジュールに同梱されています。
> `C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`

---

## 2. プロジェクト設定（メニュー 1 つ）

**`Following Triangle > Device (Quest) > Configure Project For Quest`**

以下がまとめて適用され、結果が Console に検証付きで出ます。

| 設定 | 値 | 根拠 |
|---|---|---|
| Hand Tracking Support | `ControllersAndHands` | 仕様書 §1 |
| Android アーキテクチャ | ARM64 | Quest の要件 |
| スクリプティングバックエンド | IL2CPP | 同上 |
| minSdkVersion | 32 | Horizon OS の要件 |
| Color Space | Linear | Quest 向け推奨 |

> **Hand Tracking Support がいちばん重要です。**
> `ControllersOnly` のままだと `OVRSkeleton` が骨格データを返さず、
> **V1/V2（両手の頂点）が取れません。** しかもエディタ上のモック検証では
> この問題が一切現れないため、実機で初めて発覚します。

いつでも **`Verify Quest Configuration`** で現在の状態を確認できます。

---

## 3. ビルドターゲットを Android に切替（手動）

`File > Build Profiles` → **Android** → `Switch Platform`

初回は全アセットの再インポートが走るため数分かかります。

> これは自動化していません。プラットフォーム切替はプロジェクト全体の
> 再インポートを伴う重い操作で、意図せず走ると作業中の状態を壊すためです。

---

## 4. XR Plug-in Management（手動・初回のみ）

`Edit > Project Settings > XR Plug-in Management` → **Android タブ** → **OpenXR** にチェック

続いて `XR Plug-in Management > OpenXR` → **Android タブ** で、

- `Interaction Profiles` に **Oculus Touch Controller Profile** が入っていること
- `OpenXR Feature Groups` の **Meta Quest** が有効であること

を確認します（Meta XR SDK が自動で入れている場合が多いです）。

`Verify Quest Configuration` で `§1 OpenXR ローダー資産` が OK になれば完了です。

---

## 5. 実機用シーンの生成（メニュー 1 つ）

**`Following Triangle > Device (Quest) > Build Device Scenes`**

以下の 2 シーンが生成され、Build Settings に登録されます。

```
Assets/Experiment/Scenes/ExperimentDevice.unity
Assets/Experiment/Scenes/RecorderDevice.unity
```

### エディタ用シーンとの違いは 2 箇所だけ

| | エディタ | 実機 |
|---|---|---|
| 頂点供給元 | `MockVertexSource` | `OvrVertexSource` |
| 入力 | `KeyboardRecorderInput` | `OvrRecorderInput` |

これに `OVRCameraRig` と `XrRuntimeConfig` が加わります。
**`ExperimentDriver` 以下のロジックはエディタとまったく同じものが走ります。**

### 自動で行われる設定

- `OVRManager` のトラッキング原点を **Floor Level** に（仕様書 §1）
  眼高 h の計測（§5.1-1）が CenterEye のワールド Y に依存するため必須
- 両手に `OVRHandPrefab` を配置し、Hand / Skeleton / Mesh の種別を左右それぞれに設定
- `XrRuntimeConfig` を `ExperimentDriver` の再センタリング監視と
  ランタイム情報の両方に配線（§1, §6.2）
- **手のメッシュ描画を無効化**（次節）

### 手は描画しません

仕様書 §4 が提示すると定めているのは頂点球と（C3 では）辺、そして背景だけです。
手が見えていると、三角形を介さずに相手の手と自分の手を直接見比べられてしまい、
**三角形提示の効果を測るという実験の目的が成立しません。**

骨格データ（`OVRSkeleton` / `OVRHand`）は必要なので、描画だけを止めています
（`SkinnedMeshRenderer` / `OVRMeshRenderer` / `OVRSkeletonRenderer` を無効化）。

> これは仕様書に明示されていない判断です。手を見せる運用にしたい場合は
> ご指示ください。

---

## 6. ビルドと転送

`File > Build Profiles` → **Android** → `Build And Run`

Quest がつながっていれば、ビルド後に自動でインストールされ起動します。

APK だけ作る場合は `Build` を選び、あとから：

```
adb install -r <出力した.apk>
```

### 起動後

`Boot` シーンのモード選択が出ます（§3）。コントローラで操作します。

| ボタン | 動作 |
|---|---|
| **A / X** | 決定 |
| 人差し指トリガー | 選択を進める / 区間マーカー |
| **B / Y** | 中断 |

> **コントローラは実験者が持ってください。**
> 被験者 B と演者 A の手はハンドトラッキングで頂点を取る（§2.3）ため、
> 本人がコントローラを握ると骨格が取得できなくなります。

---

## 7. 実機で最初に確認すること

### 7-1. 90 Hz が適用されているか（§1）

起動直後の Console（`adb logcat` または Unity の Device Console）に
`XrRuntimeConfig` のログが出ます。

```
[XrRuntimeConfig] リフレッシュレート 90 Hz を適用しました。
```

適用できなかった場合はエラーとして出て、利用可能な値も併記されます。
実効値は CSV ヘッダの `# display_frequency_effective_hz` にも残ります。

### 7-2. V0 が頭部回転に追従しないこと（§2.2）

**ピッチ方向（うなずき）で確認してください。**
ヨー（左右を見回す）だけでは、実装が誤っていても差が出ません。
重力方向はヨー回転の不動軸だからです。

うなずいたときに首の球が前後に動いたら、実装が §2.2 に違反しています。

### 7-3. ハンドトラッキングの実効範囲（§7.1, §9）

これがパイロットの主目的です。記録モードで演者 A を撮りながら、
`hand_L_conf` / `hand_R_conf` が High を保てる運動範囲を測ってください。

記録モードには**低信頼になったら演者に警告する機能**があるので、
収録中にその場で気づけます。

### 7-4. 頂点球の視認性（§10-1）

背景グリッドに埋もれないか、明るすぎないかを確認してください。
`ExperimentSettings` の `Vertex Color` / `Edge Color` で変更します。

現在はシェーダを **Unlit** にしてあります（照明由来の陰影が奥行き手がかりとして
混入するのを避けるため）。その結果として球は平坦に見えます。

---

## 8. データの取り出し

Quest 上の保存先：

```
/sdcard/Android/data/<パッケージ名>/files/
  recordings/    録画 JSON
  profiles/      被験者プロファイル
  logs/          試行 CSV
```

```
adb pull /sdcard/Android/data/<パッケージ名>/files/logs ./logs
adb pull /sdcard/Android/data/<パッケージ名>/files/recordings ./recordings
```

パッケージ名は `Project Settings > Player > Identification > Package Name` です。

### 教示文の差し替え（§7.4）

同じ場所の `instructions.json` を取り出して編集し、押し戻せば反映されます。
再ビルドは不要です。

```
adb pull /sdcard/Android/data/<パッケージ名>/files/instructions.json
adb push instructions.json /sdcard/Android/data/<パッケージ名>/files/
```

---

## 9. 実機に持っていく前のチェックリスト

- [ ] `Verify Quest Configuration` がすべて OK
- [ ] `Build Device Scenes` を実行済み
- [ ] 刺激 3 本（`take1` / `take2` / `take3`）を**実機の記録モードで**撮影済み
      （合成刺激は `performerId = SYNTHETIC`。本番には使わない）
- [ ] `Print Counterbalance Table` で割付表を印刷
- [ ] 被験者数を 3 の倍数で計画
- [ ] `ExperimentSettings` の `Segment Seconds` が 20 に戻っている
      （動作テストで変更した場合。専用アセットを使っていれば影響なし）
