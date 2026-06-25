/*
 * SwitchController.ino
 * NX Macro Advanced — Arduino/Teensy 用 Switch コントローラーファームウェア
 *
 * 対応ボード:
 *   - Arduino Leonardo (ATmega32u4)
 *   - Arduino Pro Micro (ATmega32u4)
 *   - Teensy 3.x / 4.x (要 Teensyduino)
 *
 * 動作:
 *   PC から 115200bps でシリアル受信した 10バイトパケットを
 *   Nintendo Switch USB HID コントローラーレポートとして送信する
 *
 * パケット形式 (10バイト):
 *   [0]  0xAB      ヘッダー
 *   [1]  BTN_HIGH  左ボタン (L,ZL,UP,DOWN,LEFT,RIGHT等)
 *   [2]  BTN_MID   共有ボタン (Plus,Minus,Home,Capture等)
 *   [3]  BTN_LOW   右ボタン (A,B,X,Y,R,ZR等)
 *   [4]  DPAD      D-Pad 方向 (0-8)
 *   [5]  LX        左スティック X (0-255, 中央=128)
 *   [6]  LY        左スティック Y (0-255, 中央=128)
 *   [7]  RX        右スティック X
 *   [8]  RY        右スティック Y
 *   [9]  CHK       チェックサム (XOR of [0]-[8])
 *
 * ビルド手順:
 *   1. Arduino IDE を開く
 *   2. ツール→ボード→「Arduino Leonardo」を選択
 *   3. このファイルを開いて書き込む
 *   4. Switch の USB ポートに接続する
 *   5. PC のシリアルポートから NX Macro Advanced で接続する
 */

// ──────────────────────────────────────────────────────────────
//  USB HID レポートデスクリプタ (Nintendo Pro Controller)
// ──────────────────────────────────────────────────────────────

#include <HID.h>

// Pro Controller USB HID レポートデスクリプタ
static const uint8_t _hidReportDescriptor[] PROGMEM = {
  0x05, 0x01,        // Usage Page (Generic Desktop Ctrls)
  0x09, 0x05,        // Usage (Game Pad)
  0xA1, 0x01,        // Collection (Application)
  0x15, 0x00,        //   Logical Minimum (0)
  0x25, 0x01,        //   Logical Maximum (1)
  0x35, 0x00,        //   Physical Minimum (0)
  0x45, 0x01,        //   Physical Maximum (1)
  0x75, 0x01,        //   Report Size (1)
  0x95, 0x10,        //   Report Count (16)
  0x05, 0x09,        //   Usage Page (Button)
  0x19, 0x01,        //   Usage Minimum (0x01)
  0x29, 0x10,        //   Usage Maximum (0x10)
  0x81, 0x02,        //   Input (Data,Var,Abs)
  0x05, 0x01,        //   Usage Page (Generic Desktop)
  0x25, 0x07,        //   Logical Maximum (7)
  0x46, 0x3B, 0x01,  //   Physical Maximum (315)
  0x75, 0x04,        //   Report Size (4)
  0x95, 0x01,        //   Report Count (1)
  0x65, 0x14,        //   Unit
  0x09, 0x39,        //   Usage (Hat switch)
  0x81, 0x42,        //   Input (Data,Var,Abs,Null)
  0x65, 0x00,        //   Unit (None)
  0x95, 0x01,        //   Report Count (1)
  0x81, 0x01,        //   Input (Const,Array,Abs)
  0x26, 0xFF, 0x00,  //   Logical Maximum (255)
  0x46, 0xFF, 0x00,  //   Physical Maximum (255)
  0x09, 0x30,        //   Usage (X)
  0x09, 0x31,        //   Usage (Y)
  0x09, 0x32,        //   Usage (Z)
  0x09, 0x35,        //   Usage (Rz)
  0x75, 0x08,        //   Report Size (8)
  0x95, 0x04,        //   Report Count (4)
  0x81, 0x02,        //   Input (Data,Var,Abs)
  0xC0               // End Collection
};

// HID レポート構造体 (8バイト)
typedef struct {
  uint16_t buttons;   // ボタン (bit マップ)
  uint8_t  hat;       // ハット (0=Up, 1=UpRight, 2=Right ... 8=None)
  uint8_t  lx;        // 左スティック X
  uint8_t  ly;        // 左スティック Y
  uint8_t  rx;        // 右スティック X
  uint8_t  ry;        // 右スティック Y
  uint8_t  _pad;      // パディング
} SwitchReport;

// ──────────────────────────────────────────────────────────────
//  グローバル変数
// ──────────────────────────────────────────────────────────────

SwitchReport report;
uint8_t      packetBuf[10];
uint8_t      packetIdx = 0;

// ──────────────────────────────────────────────────────────────
//  setup / loop
// ──────────────────────────────────────────────────────────────

void setup() {
  // HID デスクリプタ登録
  static HIDSubDescriptor node(_hidReportDescriptor, sizeof(_hidReportDescriptor));
  HID().AppendDescriptor(&node);

  Serial.begin(115200);

  // ニュートラル状態で初期化
  memset(&report, 0, sizeof(report));
  report.hat = 8;
  report.lx  = 128;
  report.ly  = 128;
  report.rx  = 128;
  report.ry  = 128;

  // Switch に最初のレポートを送信
  HID().SendReport(0, &report, sizeof(report));
  delay(100);
}

void loop() {
  // シリアルからパケット受信
  while (Serial.available() > 0) {
    uint8_t b = Serial.read();

    if (packetIdx == 0) {
      if (b == 0xAB) packetBuf[packetIdx++] = b;
      continue;
    }

    packetBuf[packetIdx++] = b;

    if (packetIdx >= 10) {
      packetIdx = 0;

      // チェックサム検証
      uint8_t chk = 0;
      for (int i = 0; i < 9; i++) chk ^= packetBuf[i];
      if (chk != packetBuf[9]) continue; // チェックサムエラー

      // パケット展開
      uint8_t btnH   = packetBuf[1];
      uint8_t btnM   = packetBuf[2];
      uint8_t btnL   = packetBuf[3];
      uint8_t dpad   = packetBuf[4];
      uint8_t lx     = packetBuf[5];
      uint8_t ly     = packetBuf[6];
      uint8_t rx     = packetBuf[7];
      uint8_t ry     = packetBuf[8];

      // ボタンマッピング (NX Macro Advanced → Switch HID)
      uint16_t buttons = 0;
      // 右ボタン (btnL)
      if (btnL & 0x08) buttons |= (1 << 0);  // Y
      if (btnL & 0x04) buttons |= (1 << 1);  // X
      if (btnL & 0x02) buttons |= (1 << 2);  // B
      if (btnL & 0x01) buttons |= (1 << 3);  // A
      if (btnL & 0x40) buttons |= (1 << 6);  // R
      if (btnL & 0x80) buttons |= (1 << 7);  // ZR
      // 共有ボタン (btnM)
      if (btnM & 0x01) buttons |= (1 << 8);  // Minus
      if (btnM & 0x02) buttons |= (1 << 9);  // Plus
      if (btnM & 0x04) buttons |= (1 << 10); // RStick
      if (btnM & 0x08) buttons |= (1 << 11); // LStick
      if (btnM & 0x10) buttons |= (1 << 12); // Home
      if (btnM & 0x20) buttons |= (1 << 13); // Capture
      // 左ボタン (btnH)
      if (btnH & 0x40) buttons |= (1 << 14); // L
      if (btnH & 0x80) buttons |= (1 << 15); // ZL

      // レポート更新
      report.buttons = buttons;
      report.hat     = (dpad < 8) ? dpad : 8; // 8=None
      report.lx      = lx;
      report.ly      = ly;
      report.rx      = rx;
      report.ry      = ry;

      // Switch に HID レポート送信
      HID().SendReport(0, &report, sizeof(report));
    }
  }

  // 16ms ごとに現在の状態を再送 (60fps)
  static unsigned long lastSend = 0;
  if (millis() - lastSend >= 16) {
    HID().SendReport(0, &report, sizeof(report));
    lastSend = millis();
  }
}
