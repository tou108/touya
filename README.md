# NX Macro Advanced 🎮
> Nintendo Switch 全自動マクロツール — Windows専用

NX Macro Controller の全機能 + 大幅拡張した Switch 自動化ツールです。

---

## ✨ 機能一覧

| 機能 | 説明 |
|------|------|
| 🔌 USB HID 接続 | Arduino/Teensy 経由。最も安定した有線接続 |
| 🔌 USB Gadget 接続 | Raspberry Pi Zero 経由。高機能・安定 |
| 📡 Bluetooth 無線接続 | ワイヤレス接続。NX Macro Controller 互換 |
| 🎮 バーチャルコントローラー | 画面上のボタンで手動入力 |
| 📝 マクロテキストエディター | NX Macro Controller 互換 + 拡張スクリプト |
| 🧩 ビジュアルスクリプトエディター | ノードをドラッグ&ドロップでフロー作成 |
| ⏺ マクロ録音 | 手動操作を記録してマクロに変換 |
| ⏰ スケジューラー | 時刻・間隔・曜日指定で自動実行 |
| 👁 画像認識/OCR | 画面を見て条件分岐（OpenCV + Tesseract） |
| ⏱ フレーム精密タイミング | 60fps基準の高精度制御（±0.5ms以下） |

---

## 🛠 ビルド手順

### 必要なもの
- **Windows 10/11** (x64)
- **[.NET 8 SDK](https://dotnet.microsoft.com/download)** (必須)
- **[Visual Studio 2022](https://visualstudio.microsoft.com/ja/)** または dotnet CLI

### ビルド
```bat
build.bat
```
→ `dist\NXMacroAdvanced.exe` が生成されます。

---

## 🔌 接続方法の選び方

```
┌─────────────────────────────────────────────────────┐
│ 安定性・精度を重視する場合                             │
│   → USB HID 接続 (Arduino/Teensy)                   │
│                                                     │
│ 高機能かつ有線で接続したい場合                          │
│   → USB Gadget (Raspberry Pi Zero)                  │
│                                                     │
│ ワイヤレスで使いたい場合                               │
│   → Bluetooth 無線接続                               │
└─────────────────────────────────────────────────────┘
```

---

## 🔌 USB HID 接続 (Arduino/Teensy)

### 必要なもの
- Arduino Leonardo / Pro Micro (ATmega32u4) または Teensy 3.x/4.x
- USB ケーブル × 2 (Switch 用 + PC 用)

### セットアップ
1. `Arduino/SwitchController.ino` を Arduino IDE で開く
2. ボードを「Arduino Leonardo」に選択
3. Switch の USB ポートに Arduino を接続
4. PC と Arduino も USB 接続してスケッチを書き込む
5. ツール → 接続設定 → COM ポートを選択して「接続する」

---

## 🔌 USB Gadget 接続 (Raspberry Pi Zero)

### 必要なもの
- Raspberry Pi Zero W / 2W
- USB ケーブル

### RPi Zero セットアップ
```bash
# /boot/config.txt に追加
dtoverlay=dwc2

# /etc/modules に追加
dwc2
libcomposite

# 再起動後
sudo bash RPi_Setup/gadget_setup.sh

# サーバー起動 (毎回または自動起動に設定)
sudo python3 RPi_Setup/nx_gadget_server.py
# Wi-Fi 経由の場合:
sudo python3 RPi_Setup/nx_gadget_server.py --tcp --port 5000
```

---

## 📡 Bluetooth 無線接続

1. Switch ホーム画面 → **コントローラーとセンサー** → **コントローラーの登録**
2. ツールの「接続設定」→ Bluetooth 選択 → **スキャン** をクリック
3. リストに Switch が表示されたら選択 → **接続する**

---

## 📝 マクロスクリプト構文リファレンス

```
# ── 基本入力 ──────────────────────────────────────────
A 100              # NX Macro Controller 互換: Aを100ms押す
PRESS A 100        # 同上（明示的形式）
PRESS A+B 200      # 複数ボタン同時押し
HOLD ZL            # ZL を押し続ける
RELEASE ZL         # ZL を離す
WAIT 500           # 500ms 待機
WAIT_FRAMES 3      # 3フレーム待機（60fps）

# ── スティック ──────────────────────────────────────
STICK L 0 128 500  # 左スティック X=0, Y=128 を 500ms
STICK R 255 128 1000  # 右スティック
# X/Y は 0〜255（中央=128）

# ── 十字キー ──────────────────────────────────────
DPAD UP 100        # 十字キー上を 100ms
DPAD DOWN 200
DPAD LEFT 150
DPAD RIGHT 150

# ── ループ ──────────────────────────────────────────
LOOP 10            # 10回繰り返し
  PRESS A 100
  WAIT 200
END_LOOP

LOOP 0             # 0 = 無限ループ
  PRESS A 100
  WAIT 300
END_LOOP

# ── 条件分岐: 画像認識 ──────────────────────────────
IF IMAGE_MATCH "templates/battle.png" 0.9
  PRESS A 100
ELIF IMAGE_MATCH "templates/menu.png" 0.85
  PRESS B 100
ELSE
  WAIT 500
END_IF

# ── 条件分岐: OCR テキスト認識 ──────────────────────
IF OCR "100,200,400,80" CONTAINS "OK"
  PRESS A 100
ELSE
  PRESS B 100
END_IF

# ── 画像待機 ────────────────────────────────────────
WAIT_IMAGE "templates/loading_done.png" 30000
# 最大30000ms (30秒) 待機

# ── スクリーンショット ──────────────────────────────
CAPTURE_SCREEN "screenshot.png"

# ── ラベル / GOTO ────────────────────────────────────
LABEL main_loop
PRESS A 100
WAIT 200
GOTO main_loop

# ── コメント ─────────────────────────────────────────
# これはコメントです
```

---

## 👁 画像認識・OCR のセットアップ

### テンプレート画像の作り方
1. Switch 画面をキャプチャボードで PC に映す
2. ツールの「画像認識」→「スクリーンショット保存」で取得
3. 認識したいUI部分をペイント等でトリミング・保存
4. `IF IMAGE_MATCH "保存したファイル.png" 0.9` で使用

### OCR（日本語テキスト認識）
1. [tessdata](https://github.com/tesseract-ocr/tessdata) から `jpn.traineddata` をダウンロード
2. `dist/tessdata/` フォルダに配置
3. ツールの「画像認識」→「OCR テスト」で動作確認

---

## ⚙ 動作環境

| 項目 | 要件 |
|------|------|
| OS | Windows 10 / 11 (x64) |
| ランタイム | .NET 8 (ビルド済み exe は不要) |
| メモリ | 推奨 4GB 以上 |
| キャプチャボード | 画像認識機能使用時に推奨 |

---

## 📁 ファイル構成

```
NXMacroAdvanced/
├── dist/
│   ├── NXMacroAdvanced.exe     ← 実行ファイル
│   └── tessdata/               ← OCR データ置き場
├── Arduino/
│   └── SwitchController.ino   ← Arduino スケッチ
├── RPi_Setup/
│   ├── gadget_setup.sh         ← RPi ガジェット設定
│   └── nx_gadget_server.py    ← RPi ゲートウェイサーバー
├── NXMacroAdvanced/            ← C# ソースコード
├── build.bat                   ← ビルドスクリプト
└── README.md                   ← このファイル
```

---

## 🐛 トラブルシューティング

| 症状 | 対処法 |
|------|--------|
| COM ポートが表示されない | デバイスマネージャーで Arduino/Ch340 ドライバを確認 |
| Switch がコントローラーを認識しない | USB 接続でペアリング済みコントローラーが残っている場合は削除 |
| Bluetooth スキャンで見つからない | Switch をペアリングモードにしてからスキャン |
| 画像認識が当たらない | 解像度・トリミングサイズを調整、閾値を下げる（0.8程度から試す） |
| OCR が空白になる | tessdata フォルダと jpn.traineddata を確認 |

---

## 📄 ライセンス
MIT License — 自由に改変・再配布可能です。
YouTube 配布・紹介動画での使用も歓迎します！
