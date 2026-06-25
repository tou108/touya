using System;

namespace NXMacroAdvanced.Models
{
    /// <summary>
    /// Nintendo Switch Pro Controller の完全な状態を表すクラス
    /// </summary>
    public class ControllerState : ICloneable
    {
        // ---- ボタン ----
        public SwitchButton Buttons { get; set; } = SwitchButton.None;

        // ---- 十字キー ----
        public DPadDirection DPad { get; set; } = DPadDirection.None;

        // ---- 左スティック (0-255, 中央=128) ----
        public byte LeftStickX  { get; set; } = 128;
        public byte LeftStickY  { get; set; } = 128;

        // ---- 右スティック (0-255, 中央=128) ----
        public byte RightStickX { get; set; } = 128;
        public byte RightStickY { get; set; } = 128;

        /// <summary>
        /// ニュートラル（全入力なし）状態を作成
        /// </summary>
        public static ControllerState Neutral => new();

        /// <summary>
        /// ボタンが押されているか確認
        /// </summary>
        public bool IsPressed(SwitchButton button) => (Buttons & button) != 0;

        /// <summary>
        /// ボタンを押す
        /// </summary>
        public void Press(SwitchButton button)   => Buttons |= button;

        /// <summary>
        /// ボタンを離す
        /// </summary>
        public void Release(SwitchButton button) => Buttons &= ~button;

        /// <summary>
        /// 左スティックを -1.0〜1.0 で設定 (0,0=中央)
        /// </summary>
        public void SetLeftStick(double x, double y)
        {
            LeftStickX = NormalizeStick(x);
            LeftStickY = NormalizeStick(y);
        }

        /// <summary>
        /// 右スティックを -1.0〜1.0 で設定 (0,0=中央)
        /// </summary>
        public void SetRightStick(double x, double y)
        {
            RightStickX = NormalizeStick(x);
            RightStickY = NormalizeStick(y);
        }

        private static byte NormalizeStick(double value)
        {
            value = Math.Clamp(value, -1.0, 1.0);
            return (byte)Math.Round((value + 1.0) * 127.5);
        }

        /// <summary>
        /// 送信用バイト配列に変換 (Arduino/Raspberry Pi シリアル送信用)
        /// フォーマット: [0xAB][BTN_H][BTN_M][BTN_L][DPAD][LX][LY][RX][RY][CHK]
        /// </summary>
        public byte[] ToSerialPacket()
        {
            uint raw = (uint)Buttons;
            byte btnHigh   = (byte)((raw >> 16) & 0xFF); // 左ボタン
            byte btnMiddle = (byte)((raw >>  8) & 0xFF); // 共有ボタン
            byte btnLow    = (byte)( raw        & 0xFF); // 右ボタン
            byte dpad      = (byte)DPad;

            byte[] packet = new byte[10];
            packet[0] = 0xAB;          // ヘッダー
            packet[1] = btnHigh;
            packet[2] = btnMiddle;
            packet[3] = btnLow;
            packet[4] = dpad;
            packet[5] = LeftStickX;
            packet[6] = LeftStickY;
            packet[7] = RightStickX;
            packet[8] = RightStickY;
            // チェックサム: XOR
            byte chk = 0;
            for (int i = 0; i < 9; i++) chk ^= packet[i];
            packet[9] = chk;
            return packet;
        }

        /// <summary>
        /// NX Macro Controller 互換のテキスト行を生成
        /// </summary>
        public string ToNxMacroLine(int durationMs)
        {
            if (Buttons == SwitchButton.None && DPad == DPadDirection.None)
                return $"WAIT {durationMs}";

            var parts = new System.Collections.Generic.List<string>();
            foreach (SwitchButton btn in Enum.GetValues<SwitchButton>())
            {
                if (btn != SwitchButton.None && IsPressed(btn))
                    parts.Add(SwitchButtonHelper.GetDisplayName(btn));
            }
            if (DPad != DPadDirection.None)
                parts.Add($"DPAD_{DPad.ToString().ToUpper()}");

            return $"{string.Join("+", parts)} {durationMs}";
        }

        /// <summary>
        /// 2つの状態を比較
        /// </summary>
        public bool Equals(ControllerState? other)
        {
            if (other is null) return false;
            return Buttons     == other.Buttons     &&
                   DPad        == other.DPad        &&
                   LeftStickX  == other.LeftStickX  &&
                   LeftStickY  == other.LeftStickY  &&
                   RightStickX == other.RightStickX &&
                   RightStickY == other.RightStickY;
        }

        /// <summary>
        /// 深いコピー
        /// </summary>
        public object Clone() => new ControllerState
        {
            Buttons     = Buttons,
            DPad        = DPad,
            LeftStickX  = LeftStickX,
            LeftStickY  = LeftStickY,
            RightStickX = RightStickX,
            RightStickY = RightStickY,
        };
    }
}
