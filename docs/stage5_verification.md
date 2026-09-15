# 段階5 の動作確認手順（条件切替・ラテン方格割付・試行フロー）

対象：モード選択（§3）、教示提示（§5.4, §7.4）、割付（§5.5）、試行フロー制御（§5.4）。

---

## 0. 確認いただいた判断

| 項目 | 決定 |
|---|---|
| 割付設計 | **グレコ・ラテン方格（3 名 1 周期）** |
| 被験者 ID → 割付行 | **ID 末尾の連番**（番号が取れない ID はエラーで停止） |
| 再センタリング時 | **中断せず続行、フラグのみ** |
| 教示の提示 | **HMD 内に表示** |

---

## 1. 検証用ドライバを廃止しました

段階3・4 で使っていた `PlaybackVerificationDriver` と `Playback.unity` を削除し、
本番の `ExperimentDriver` に一本化しました。

**検証したコードと本番のコードが違うと、検証の意味がなくなる**ためです。
エディタと実機で差し替わるのは以下の 2 つだけです。

| エディタ | 実機 |
|---|---|
| `MockVertexSource` | `OvrVertexSource` |
| `KeyboardRecorderInput` | `OvrRecorderInput` |

段階3・4 の手順書にある `Build Playback Scene` は
**`Build Experiment Scene`** に読み替えてください。

---

## 2. 割付設計（§5.5）

3 次のグレコ・ラテン方格。被験者 3 名で次の 3 つが同時に成立します。

1. 各条件が各提示位置にちょうど 1 回ずつ（順序効果の均衡）
2. 各刺激が各提示位置にちょうど 1 回ずつ（刺激の順序効果の均衡）
3. 9 通りの（条件, 刺激）の組がちょうど 1 回ずつ（条件-刺激交絡の除去）

構成式（r = 割付行 0..2、p = 提示位置 0..2）：

```
条件番号 = (r + p)     mod 3
刺激番号 = (r + 2 * p) mod 3
```

p の係数が 3 を法として異なるため、(条件, 刺激) から (r, p) が一意に逆算でき、
直交性が保証されます。

メニュー **`Following Triangle > Print Counterbalance Table`** で割付表を
Console に出せます。実験前に紙で確認してください。

> **被験者数は 3 の倍数にしてください。** 3 の倍数でないところで打ち切ると
> 均衡が崩れます。被験者 4 は被験者 1 と同じ割付になります。

---

## 3. 単体テスト（HMD 不要）

`Window > General > Test Runner` → EditMode → Run All。

| テスト | 何を守っているか |
|---|---|
| `EveryConditionAppearsOnceAtEveryPosition` | 均衡1：順序効果と条件効果が交絡しないこと |
| `EveryStimulusAppearsOnceAtEveryPosition` | 均衡2：刺激の難易度差が提示位置と交絡しないこと |
| **`EveryConditionStimulusPairAppearsExactlyOnceInOneCycle`** | **均衡3：グレコ・ラテン方格の直交性** |
| `AssignmentRepeatsEveryThreeParticipants` | 3 名 1 周期であること |
| `AssignmentIsDeterministic` | 乱数を使っていないこと（中断・再開で割付が変わらない） |
| `ParticipantNumber_IsTakenFromTrailingDigits` | B01 → 1, P007 → 7 |
| `ParticipantNumber_RejectsIdsWithoutUsableNumber` | 番号が取れない ID で先へ進まないこと |
| **`IdenticalC1AndC2Text_IsRejected`** | **C1 と C2 の文言が同じなら実験操作が存在しない** |
| `ShippedInstructionFile_IsValid` | 同梱の教示ファイルがそのまま検証を通ること |
| `DefaultInstanceHasNoEmbeddedText` | 教示文をコードに埋め込んでいないこと（§7.4） |

---

## 4. 教示文の差し替え（§7.4）

文言は **コードに 1 文字も持っていません**。読み込み順は次のとおりです。

1. `Application.persistentDataPath/instructions.json`
   実験者が差し替える場所。再ビルド不要
2. `Assets/StreamingAssets/instructions.json`
   リポジトリに入っている既定。1 が無ければこちらを読み、同時に 1 へ複製する

初回起動後は 1 が存在するので、そちらを編集すれば反映されます。
Quest 実機では adb で取り出して編集し、押し戻してください。

```
adb pull /sdcard/Android/data/<パッケージ名>/files/instructions.json
adb push instructions.json /sdcard/Android/data/<パッケージ名>/files/
```

**実際に提示した文言は CSV ヘッダの `# instruction_text` に残ります。**
教示ファイルを差し替えても、どの試行でどの文言だったかが追えます。

教示ファイルが読めない、または検証に通らない場合、実験は開始せず停止します。
教示が出ないなら条件操作そのものが存在しないためです。

---

## 5. エディタ上で 1 セッション通す（HMD 不要）

### 前提

Recorder シーンで **録画を 3 本** 取り、それぞれ `Recording Id` を
`take1` / `take2` / `take3` にしておきます。

> 1 本しか無くても動きます。その場合 `Stimulus Catalog` が
> `IsComplete = false` になり、Console にエラーが出て、
> CSV ヘッダに `# stimulus_catalog_complete: false` が残ります。
> **この状態で取ったデータはカウンターバランスが成立していません。**

### シーンの生成

```
Following Triangle > Build Experiment Scene
Following Triangle > Build Boot Scene
Following Triangle > Register Scenes In Build Settings
```

最後の 1 つは、モード選択からのシーン遷移（§3）に必要です。

### 手順

`Boot.unity` を開いて Play します。

1. **モード選択**（§3）
   M キーで「実験モード / 記録モード」を切替、Space で決定
2. **被験者番号の選択**
   M キーで番号を進め、Space で決定。割付行が表示される
3. **キャリブレーション**（§5.1）
   `Mock Vertex Source` の `Animate` を off にして 3 秒静止
   （既存プロファイルがあれば自動で再利用され、この手順は飛ばされる）
4. **試行 1：教示表示**（被験者ペース）
   条件に応じた教示文が出る。Space で次へ
5. **基準姿勢 3 秒** → Registration 確定
6. **追従 90 秒**（`Animate` を on に）
   **残り時間は表示されません**（§10-3）
7. 試行終了 → Space → 試行 2 → 試行 3 → セッション終了

### 確認 16：割付が効いていること

Console に出る割付を確認します。

```
被験者 B01 (番号 1) の割付: #1: C1 x stimulus1 / #2: C2 x stimulus3 / #3: C3 x stimulus2
```

被験者番号を 1 → 2 → 3 と変えて、`Print Counterbalance Table` の表と
一致することを確認してください。

### 確認 17：C1 と C2 で表示が変わらないこと

被験者 B01 の試行 1（C1）と試行 2（C2）で、

- **教示文だけが変わる**
- 頂点球の大きさ・色・位置の見え方は一切変わらない
- どちらも辺は出ない

C3 の試行だけ辺が出ます。

### 確認 18：進行状況が出ないこと（§10-3）

追従中、HUD に残り時間やフェーズ名が出ないこと。
`ExperimentSettings` の `Show Progress To Participant` を on にすると出ますが、
**本番では off のまま**にしてください。残り時間が見えると被験者の注意配分が
課題から時間へ移ります。

### 確認 19：出力ファイル

1 セッションで CSV が 6 本出ます（試行 3 本 × 2 種類）。

```
..._B01_trial01_C1_take1.csv
..._B01_trial01_C1_take1_registration.csv
..._B01_trial02_C2_take3.csv
..._B01_trial02_C2_take3_registration.csv
..._B01_trial03_C3_take2.csv
..._B01_trial03_C3_take2_registration.csv
```

ヘッダに追加された項目：

```
# participant_number: 1
# assignment_row: 0
# assignment_design: graeco_latin_square_order3
# stimulus_catalog_complete: true
# instruction_text: 3つの点を合わせてください
# instruction_source: .../instructions.json
```

---

## 6. 実機での差し替え

Experiment シーンと Recorder シーンで、次の 2 つを置き換えます。

| エディタ | 実機 |
|---|---|
| `Mock Vertex Source`（`MockVertexSource`） | `OvrVertexSource`（CenterEyeAnchor / OVRSkeleton / OVRHand を配線） |
| `Session Input (Keyboard)`（`KeyboardRecorderInput`） | `OvrRecorderInput` |

加えて `XrRuntimeConfig` を置き、`ExperimentDriver` の
`recenterMonitorBehaviour` と `xrRuntimeInfoBehaviour` に割り当てます。

> `OvrRecorderInput` は **実験者が持つコントローラ** を前提としています。
> 被験者 B の手はハンドトラッキングで頂点を取る（§2.3）ため、
> B がコントローラを握ると骨格が取得できなくなります。
