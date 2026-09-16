# 動作テスト（実機なし・約 1 分）

実験としてではなく「一通り動くか」を確かめるための短縮構成です。

本番設定では 1 セッション 5 分以上（93 s × 3 試行）かかるので、
**1 試行 16 s・3 試行で約 48 s** の専用構成を用意しています。

---

## 本番には一切触れません

| | 本番 | 動作テスト |
|---|---|---|
| 設定 | `ExperimentSettings.asset` | `ExperimentSettings.QuickTest.asset` |
| シーン | `Experiment.unity` | `ExperimentQuickTest.unity` |
| 刺激の保存先 | `recordings/` | `recordings-quicktest/` |
| ログの保存先 | `logs/` | `logs-quicktest/` |
| 刺激 ID | `take1` / `take2` / `take3` | `qt1` / `qt2` / `qt3` |
| 被験者 ID | `B01`… | `QT01`… |

短い区間長で取ったデータが本番データに混ざると、後から見分けるには
CSV ヘッダの `segment_s` を見るしかなくなります。物理的に分けてあります。

**走るコードは本番とまったく同じです。** `ExperimentDriver` も `TrialLogger` も
同じものが動き、違うのは与える設定値だけです。

---

## 手順

### 1. 準備（1 回だけ、数秒）

メニュー **`Following Triangle > Quick Test > Set Up Quick Test`**

これだけで以下がすべて行われます。

- 短縮設定アセットの作成（基準姿勢 2 s / 導入 2 s / 区間 3 s × 4）
- 合成刺激 3 本の生成（`qt1` / `qt2` / `qt3`）
- 専用シーンの生成と配線
- Build Settings への登録

Console に保存先が出ます。

> Play モードを停止した状態で実行してください。
> `EditorSceneManager.NewScene` は Play 中に使えません。

### 2. 実行（約 1 分）

`Assets/Experiment/Scenes/ExperimentQuickTest.unity` を開いて **Play**。

キー操作は 3 つだけです。

| キー | 動作 |
|---|---|
| **Space** | 決定・次へ |
| **M** | 選択を進める（被験者番号など） |
| **Esc** | 中断 |

流れ：

1. **被験者番号の選択** — `QT01` と割付行が表示される → **Space**
2. **キャリブレーション 2 秒** — `Mock Vertex Source` の `Animate` を **off** にして待つ
   - 2 回目以降は既存プロファイルが再利用され、この手順は飛びます
3. **試行 1：教示表示** — 条件に応じた教示文 → **Space**
4. **基準姿勢 2 秒** — 相手三角形は出ません（Registration 中のため）
5. **追従 14 秒** — 相手三角形が現れる。`Animate` を **on** にすると自己三角形が動きます
6. 試行終了 → **Space** → 試行 2 → 試行 3 → セッション終了

合計 3 試行、キー操作を含めて 1 分程度です。

### 3. 結果の確認

メニュー **`Following Triangle > Quick Test > Open Quick Test Logs Folder`**

CSV が 6 本（試行 3 本 × 2 種類）出ているはずです。

```
..._QT01_trial01_C1_qt1.csv
..._QT01_trial01_C1_qt1_registration.csv
..._QT01_trial02_C2_qt3.csv
..._QT01_trial02_C2_qt3_registration.csv
..._QT01_trial03_C3_qt2.csv
..._QT01_trial03_C3_qt2_registration.csv
```

### 4. 後片付け

メニュー **`Following Triangle > Quick Test > Delete Quick Test Data`**

`recordings-quicktest/` と `logs-quicktest/` を消します。本番フォルダには触れません。

---

## 見るとよい点

| 見る場所 | 期待 |
|---|---|
| 試行 1（C1）と試行 2（C2）の教示 | **文言だけが変わり、球の見え方は変わらない** |
| 試行 3（C3） | 辺が 3 本出る |
| 追従中の HUD | 残り時間が出ない（§10-3） |
| Console の割付 | `#1: C1 x stimulus1 / #2: C2 x stimulus3 / #3: C3 x stimulus2` |
| CSV ヘッダ | `# segment_s: 3.000`（本番と区別できる） |
| CSV ヘッダ | `# stimulus_catalog_complete: true` |
| CSV ヘッダ | `# instruction_text:` に実際の文言 |

---

## 何を確認できて、何を確認できないか

### 確認できること

- 試行フロー全体が最後まで通ること
- 割付（グレコ・ラテン方格）が効いていること
- 教示文が外部ファイルから読まれていること
- Registration が収束すること
- CSV が 2 種類とも正しい形式で出ること
- C1 / C2 / C3 の表示の違い

### 確認できないこと

**実機でしか分からないことは、これでは分かりません。**

- ハンドトラッキングの精度と実効範囲（§7.1）
- 90 Hz が実際に適用されるか（§1）
- HMD 内での頂点球の視認性（§10-1）
- 自他が重なった際の見え方（§10-2）
- 被験者にとっての課題の難易度

また、頂点の供給元が `MockVertexSource`（正弦波で動くダミー）なので、
**ここで出る誤差の値そのものには意味がありません。**
誤差算出が正しいかどうかは
`Following Triangle > Run Pipeline Self-Check` と単体テストで確認してください。
