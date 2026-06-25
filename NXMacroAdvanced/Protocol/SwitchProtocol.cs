using System;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Protocol
{
    /// <summary>
    /// Nintendo Switch Pro Controller の HID プロトコル定数・パケット構築クラス
    /// Switch の USB/Bluetooth プロトコルを実装する
    /// </summary>
    public static class SwitchProtocol
    {
        // ─────────────────────────────────────────────────────────
        //  USB HID レポートID
        // ─────────────────────────────────────────────────────────
        public const byte USB_REPORT_ID_INPUT       = 0x30; // 標準入力レポート
        public const byte USB_REPORT_ID_SUBCMD      = 0x01; // サブコマンドレポート
        public const byte USB_REPORT_ID_NFC_IR      = 0x31; // NFC/IRレポート
        public const byte USB_REPORT_ID_MCU         = 0x21; // MCU更新レポート

        // ─────────────────────────────────────────────────────────
        //  サブコマンド番号
        // ─────────────────────────────────────────────────────────
        public const byte SUBCMD_GET_DEVICE_INFO    = 0x02;
        public const byte SUBCMD_SET_INPUT_REPORT   = 0x03; // 入力レポートモード設定
        public const byte SUBCMD_SET_HCI_STATE      = 0x06; // スリープ/接続制御
        public const byte SUBCMD_SET_SHIPMENT_MODE  = 0x08;
        public const byte SUBCMD_SPI_FLASH_READ     = 0x10;
        public const byte SUBCMD_SET_PLAYER_LIGHTS  = 0x30; // プレイヤーLED設定
        public const byte SUBCMD_SET_HOME_LIGHT     = 0x38;
        public const byte SUBCMD_ENABLE_IMU         = 0x40; // ジャイロ/加速度センサー
        public const byte SUBCMD_ENABLE_RUMBLE      = 0x48; // 振動
        public const byte SUBCMD_GET_REGULATED_VOLT = 0x50;

        // ─────────────────────────────────────────────────────────
        //  USB HID コマンド (USB接続時)
        // ─────────────────────────────────────────────────────────
        public const byte USB_CMD_CONNECT           = 0x02;
        public const byte USB_CMD_HANDSHAKE          = 0x02;
        public const byte USB_CMD_HIGHSPEED          = 0x03;
        public const byte USB_CMD_FORCE_USB          = 0x04;
        public const byte USB_CMD_CLEAR_USB          = 0x05;
        public const byte USB_CMD_RESET_MCU          = 0x06;

        // ─────────────────────────────────────────────────────────
        //  デバイス情報 (Pro Controller として偽装)
        // ─────────────────────────────────────────────────────────
        public const ushort VID = 0x057E;  // Nintendo
        public const ushort PID = 0x2009;  // Pro Controller

        public static readonly byte[] MacAddress = new byte[]
        {
            0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF  // ※実際の使用時は変更すること
        };

        // ─────────────────────────────────────────────────────────
        //  標準入力レポート 0x30 の構築 (48バイト)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 標準入力レポート (Report 0x30) を構築する
        /// Switch がコントローラーから受信する形式
        /// </summary>
        public static byte[] BuildInputReport0x30(ControllerState state, byte timer = 0)
        {
            byte[] report = new byte[49]; // Report ID(1) + データ(48)
            report[0] = USB_REPORT_ID_INPUT; // 0x30

            // Byte 1: タイマー (毎回インクリメント)
            report[1] = timer;

            // Byte 2: バッテリーレベル (上4bit) | 接続情報 (下4bit)
            // 0x8E = USB接続, バッテリーフル
            report[2] = 0x8E;

            // Bytes 3-5: ボタン状態
            EncodeButtons(state, report, 3);

            // Bytes 6-8: 左スティック (12bit × 2 = 3bytes)
            EncodeStick(state.LeftStickX,  state.LeftStickY,  report, 6);

            // Bytes 9-11: 右スティック (12bit × 2 = 3bytes)
            EncodeStick(state.RightStickX, state.RightStickY, report, 9);

            // Byte 12: 振動レポートコード
            report[12] = 0x00;

            // Bytes 13-48: IMU データ (ゼロ埋め, 必要に応じて有効化)
            // ... (残りはゼロ)

            return report;
        }

        /// <summary>
        /// サブコマンドへの応答レポート (Report 0x21) を構築
        /// </summary>
        public static byte[] BuildSubcmdReply(byte subcmd, ControllerState state, byte[] replyData, byte timer = 0)
        {
            byte[] report = new byte[49];
            report[0] = USB_REPORT_ID_MCU; // 0x21

            report[1] = timer;
            report[2] = 0x8E; // バッテリー+接続
            EncodeButtons(state, report, 3);
            EncodeStick(state.LeftStickX,  state.LeftStickY,  report, 6);
            EncodeStick(state.RightStickX, state.RightStickY, report, 9);
            report[12] = 0x00;

            // Byte 13: ACK (0x80 = OK)
            report[13] = 0x80;
            // Byte 14: 応答するサブコマンド番号
            report[14] = subcmd;

            // Byte 15〜: 応答データ
            if (replyData != null)
            {
                int copyLen = Math.Min(replyData.Length, report.Length - 15);
                Array.Copy(replyData, 0, report, 15, copyLen);
            }

            return report;
        }

        // ─────────────────────────────────────────────────────────
        //  ボタン/スティックエンコード (Switch HIDフォーマット)
        // ─────────────────────────────────────────────────────────

        private static void EncodeButtons(ControllerState state, byte[] buf, int offset)
        {
            // Byte 0 (Right side buttons): Y X B A SR(right) SL(right) R ZR
            byte r0 = 0;
            if (state.IsPressed(SwitchButton.Y))  r0 |= 0x01;
            if (state.IsPressed(SwitchButton.X))  r0 |= 0x02;
            if (state.IsPressed(SwitchButton.B))  r0 |= 0x04;
            if (state.IsPressed(SwitchButton.A))  r0 |= 0x08;
            if (state.IsPressed(SwitchButton.R))  r0 |= 0x40;
            if (state.IsPressed(SwitchButton.ZR)) r0 |= 0x80;
            buf[offset] = r0;

            // Byte 1 (Shared buttons): Minus Plus RStick LStick Home Capture
            byte r1 = 0;
            if (state.IsPressed(SwitchButton.Minus))   r1 |= 0x01;
            if (state.IsPressed(SwitchButton.Plus))    r1 |= 0x02;
            if (state.IsPressed(SwitchButton.RStick))  r1 |= 0x04;
            if (state.IsPressed(SwitchButton.LStick))  r1 |= 0x08;
            if (state.IsPressed(SwitchButton.Home))    r1 |= 0x10;
            if (state.IsPressed(SwitchButton.Capture)) r1 |= 0x20;
            buf[offset + 1] = r1;

            // Byte 2 (Left side buttons): Down Up Right Left SR(left) SL(left) L ZL + DPad
            byte r2 = 0;
            // DPad encoding
            r2 |= state.DPad switch
            {
                DPadDirection.Up        => 0x02,
                DPadDirection.UpRight   => 0x06,
                DPadDirection.Right     => 0x04,
                DPadDirection.DownRight => 0x0C,
                DPadDirection.Down      => 0x08,
                DPadDirection.DownLeft  => 0x09,
                DPadDirection.Left      => 0x01,
                DPadDirection.UpLeft    => 0x03,
                _                       => 0x00
            };
            if (state.IsPressed(SwitchButton.L))  r2 |= 0x40;
            if (state.IsPressed(SwitchButton.ZL)) r2 |= 0x80;
            buf[offset + 2] = r2;
        }

        /// <summary>
        /// スティック値 (0-255) を Switch HID フォーマットの 12bit 値にエンコード
        /// 3バイトで2軸分 (各12bit) を格納
        /// </summary>
        private static void EncodeStick(byte x, byte y, byte[] buf, int offset)
        {
            // 8bit → 12bit 変換 (0-255 → 0-4095)
            ushort sx = (ushort)(x << 4);
            ushort sy = (ushort)(y << 4);

            // リトルエンディアンで3バイトに格納
            buf[offset]     = (byte)(sx & 0xFF);
            buf[offset + 1] = (byte)(((sx >> 8) & 0x0F) | ((sy & 0x0F) << 4));
            buf[offset + 2] = (byte)((sy >> 4) & 0xFF);
        }

        // ─────────────────────────────────────────────────────────
        //  Arduino シリアル用 コンパクトパケット
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Arduino/Teensy シリアル通信用パケット (10バイト)
        /// Arduino 側でこのパケットを受信し、USB HID レポートに変換する
        /// </summary>
        public static byte[] BuildSerialPacket(ControllerState state)
        {
            return state.ToSerialPacket();
        }

        // ─────────────────────────────────────────────────────────
        //  Bluetooth HID サービス記述
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Switch が Pro Controller に期待する HID デスクリプタ
        /// (Bluetooth でコントローラーを偽装する際に送信)
        /// </summary>
        public static readonly byte[] HidDescriptor = new byte[]
        {
            0x05, 0x01,         // Usage Page (Generic Desktop)
            0x09, 0x05,         // Usage (Game Pad)
            0xA1, 0x01,         // Collection (Application)
              0x15, 0x00,       //   Logical Minimum (0)
              0x25, 0x01,       //   Logical Maximum (1)
              0x35, 0x00,       //   Physical Minimum (0)
              0x45, 0x01,       //   Physical Maximum (1)
              0x75, 0x01,       //   Report Size (1)
              0x95, 0x10,       //   Report Count (16) - ボタン16個
              0x05, 0x09,       //   Usage Page (Button)
              0x19, 0x01,       //   Usage Minimum (Button 1)
              0x29, 0x10,       //   Usage Maximum (Button 16)
              0x81, 0x02,       //   Input (Data,Var,Abs)
              0x05, 0x01,       //   Usage Page (Generic Desktop)
              0x25, 0x07,       //   Logical Maximum (7)
              0x46, 0x3B, 0x01, //   Physical Maximum (315)
              0x75, 0x04,       //   Report Size (4)
              0x95, 0x01,       //   Report Count (1)
              0x65, 0x14,       //   Unit (System: English Rotation, Length: Centimeter)
              0x09, 0x39,       //   Usage (Hat Switch)
              0x81, 0x42,       //   Input (Data,Var,Abs,Null)
              0x65, 0x00,       //   Unit (None)
              0x95, 0x01,       //   Report Count (1)
              0x81, 0x01,       //   Input (Const,Array,Abs)
              0x26, 0xFF, 0x00, //   Logical Maximum (255)
              0x46, 0xFF, 0x00, //   Physical Maximum (255)
              0x09, 0x30,       //   Usage (X)
              0x09, 0x31,       //   Usage (Y)
              0x09, 0x32,       //   Usage (Z)
              0x09, 0x35,       //   Usage (Rz)
              0x75, 0x08,       //   Report Size (8)
              0x95, 0x04,       //   Report Count (4)
              0x81, 0x02,       //   Input (Data,Var,Abs)
            0xC0                // End Collection
        };

        /// <summary>
        /// デバイス情報サブコマンドへの応答データを生成
        /// </summary>
        public static byte[] GetDeviceInfoResponse()
        {
            return new byte[]
            {
                0x04, 0x00,                          // ファームウェアバージョン
                0x03,                                // デバイスタイプ (Pro Controller)
                0x02,                                // 不明
                MacAddress[0], MacAddress[1], MacAddress[2],
                MacAddress[3], MacAddress[4], MacAddress[5],
                0x01,                                // カラー設定の有無
                0x01,                                // バッテリーの有無
            };
        }
    }
}
