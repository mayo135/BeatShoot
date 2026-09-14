# BeatShoot

**心拍数で照準が揺れる狙撃ゲーム。**

スマートウォッチで測ったプレイヤーの実際の心拍数が、そのままゲーム内の照準の揺れになります。
落ち着けば当たる、焦れば当たらない。心拍を制御することがそのまま攻略になる射撃ゲームです。

---

## 特徴

- **実心拍連動** — BLE スマートウォッチの心拍を 1Hz で取得し、リアルタイムに照準へ反映
- **GGO 風バレットサークル** — 円＋十字のレティクルが心拍に合わせて漂い、円の大きさが命中精度を表す
- **物理ベースの揺れ** — 照準をバネ質点として扱い、心拍波形を「力」として加えることで自然な揺れを生成
- **部位別得点** — 頭部・胸部の同心円で 10 / 9 / 8 / 7 点を判定
- **実機なしでも動く** — ウォッチが無い環境では擬似心拍で動作。デモ中に切断しても自動でフォールバック

---

## 動作環境

| 項目 | バージョン |
|---|---|
| Unity | 6000.3.12f1 (Unity 6) |
| Render Pipeline | URP 17.3.0 |
| Input System | 1.19.0（新 Input System 必須） |
| Python（連携ツール） | 3.9 以上 |
| OS | macOS で動作確認済み |

心拍連携を使う場合のみ、標準の BLE Heart Rate Service (0x180D) に対応したスマートウォッチが必要です。
（動作確認機: P18）

---

## セットアップ

### 1. プロジェクトを開く

```bash
git clone <このリポジトリ>
```

Unity Hub から `BeatShoot` フォルダを開きます。

### 2. シーンを構築する

`Assets/Scenes/SampleScene.unity` を開き、メニューから順に実行します。

```
Tools > BeatShoot > シーンを自動セットアップ
Tools > BeatShoot > 射撃訓練場を作る
```

プレイヤー・カメラ・銃・的・射撃場が自動で配置され、スクリプトの参照も全て埋まります。
実行後に **Cmd+S でシーンを保存**してください。

> 足りないものだけを追加する実装なので、何度実行しても重複しません。Cmd+Z で元に戻せます。

### 3. 再生

再生ボタンを押せば遊べます。心拍はウォッチが無ければ擬似心拍で動作します。

---

## 操作

| 入力 | 動作 |
|---|---|
| `WASD` | 移動（歩行 4.0 m/s） |
| `Space` | ジャンプ |
| `Ctrl` / `C` | しゃがみ（押している間、1.8 m/s） |
| マウス | 視点 |
| **右クリック（長押し）** | **構える**（FOV 60→22 にズーム、照準の揺れが本来の大きさに） |
| **左クリック** | **発砲** |
| `Esc` | カーソル解放 |

画面左上に BPM・揺れ幅・バレットサークル半径・ウォッチ接続状況が表示されます。

---

## スマートウォッチ連携（任意）

BLE の心拍を Python で受け取り、UDP で Unity に転送します。

### 初回のみ

```bash
pip3 install bleak
```

macOS では「システム設定 → プライバシーとセキュリティ → Bluetooth」でターミナルを許可し、
ターミナルを **Cmd+Q で完全に終了してから**開き直してください。

### 1. ウォッチのアドレスを調べる

```bash
cd Tools
python3 hr_scan.py                    # 一覧表示
python3 hr_scan.py <アドレス>          # 中身を確認し、30 秒間 BPM を実測
```

`★ Heart Rate Service` が表示され BPM が流れれば対応機種です。

### 2. ブリッジを起動する（Unity 再生前）

```bash
python3 hr_bridge.py <アドレス>
```

ウォッチ本体で心拍計測を開始しておいてください（裏面の緑 LED が点灯）。

### 3. Unity を再生

画面左上が緑字の `WATCH: 接続中` になれば成功です。

| 表示 | 状態 |
|---|---|
| 緑 `WATCH: 接続中` | 実機の心拍で動作中 |
| 灰 `WATCH: 待機中` | ブリッジ未起動。擬似心拍で動作中 |
| 赤 `UDP 5005 を開けませんでした` | ポートが他プロセスに使用されている |

---

## ディレクトリ構成

```
Assets/
├── Editor/
│   ├── BeatShootSetup.cs          シーン自動セットアップ
│   └── ShootingRangeBuilder.cs    射撃訓練場の自動生成
├── Model/
│   ├── GunModel/                  Barrett M82 風ライフル (OBJ)
│   └── TargetModel/               人型シルエット標的 (OBJ)
├── Script/
│   ├── GunScript/
│   │   ├── HeartRateProvider.cs       心拍数の供給（擬似／実機）
│   │   ├── HeartRateUdpReceiver.cs    UDP 受信・実機への自動切替
│   │   ├── HeartbeatAimSway.cs        心拍 → 照準の揺れ（中核）
│   │   ├── GgoReticle.cs              円＋十字のレティクル描画
│   │   └── BulletTracer.cs            曳光弾
│   ├── PlayerScript/
│   │   └── SimplePlayerController.cs  歩き・ジャンプ・しゃがみ・視点
│   └── TargetScript/
│       ├── SniperShooter.cs           射撃・弾道・得点集計
│       └── TargetScoreZones.cs        部位別の得点判定
└── Scenes/SampleScene.unity

Tools/
├── hr_scan.py     BLE 機器の調査
├── hr_bridge.py   ウォッチ → Unity のブリッジ
└── hr_probe.py    UDP 受信の切り分け用

Spec/
└── SPEC.md        仕様書
```

---

## 主要パラメータ

難易度の調整は Inspector から行います。コードの変更は不要です。

### Main Camera → Heartbeat Aim Sway

| 項目 | 現在値 | 意味 |
|---|---|---|
| `Calm Bpm` | 70 | この BPM 以下なら揺れなし |
| `Hot Bpm` | 100 | この BPM で揺れが最大 |
| `Sway Calm` / `Sway Hot` | 0 / 2.2 | 揺れ幅[度] |
| `Damping` | 4.5 | 小さいほど尾を引いて大きく振れる |
| `Spread Calm` / `Spread Hot` | 0.06 / 0.85 | バレットサークル半径[度] |

### HeartRate → Heart Rate Provider

| 項目 | 現在値 | 意味 |
|---|---|---|
| `Mode` | Simulated | 実機接続時は自動で External に切り替わる |
| `Resting Bpm` | 60 | 擬似心拍の平常値 |
| `Shot Stress` | 16 | 1 発撃つごとに上がる BPM |

詳細は [Spec/SPEC.md](Spec/SPEC.md) を参照してください。

---

## トラブルシューティング

| 症状 | 原因と対処 |
|---|---|
| Add Component に出てこない | Console に赤エラーがある（1 つでも全スクリプトが停止します） |
| 撃っても得点が出ない | 的の `Target_Board` に MeshCollider が無い。セットアップを再実行 |
| ウォッチが繋がらない | スマホのアプリ（FitPro 等）を終了する。BLE は 1 台としか接続できません |
| `Bluetooth device is turned off` | ターミナルに Bluetooth 権限が無い。許可後 Cmd+Q で再起動 |
| UDP は届くが Unity に出ない | `hr_probe.py` で切り分け（Unity 停止中のみ実行可） |

---

## ライセンス

未定
