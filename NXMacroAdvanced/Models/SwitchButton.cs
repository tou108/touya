using System;

namespace NXMacroAdvanced.Models
{
    /// <summary>
    /// Nintendo Switch Pro Controller の全ボタン定義
    /// </summary>
    [Flags]
    public enum SwitchButton : uint
    {
        None      = 0,

        // ---- 右ボタン ----
        Y         = 1 << 0,
        X         = 1 << 1,
        B         = 1 << 2,
        A         = 1 << 3,
        R_SR      = 1 << 4,   // 右Joy-Con SRボタン
        R_SL      = 1 << 5,   // 右Joy-Con SLボタン
        R         = 1 << 6,
        ZR        = 1 << 7,

        // ---- 共有ボタン ----
        Minus     = 1 << 8,
        Plus      = 1 << 9,
        RStick    = 1 << 10,  // 右スティック押し込み
        LStick    = 1 << 11,  // 左スティック押し込み
        Home      = 1 << 12,
        Capture   = 1 << 13,

        // ---- 左ボタン ----
        Down      = 1 << 16,
        Up        = 1 << 17,
        Right     = 1 << 18,
        Left      = 1 << 19,
        L_SR      = 1 << 20,  // 左Joy-Con SRボタン
        L_SL      = 1 << 21,  // 左Joy-Con SLボタン
        L         = 1 << 22,
        ZL        = 1 << 23,
    }

    /// <summary>
    /// 十字キー方向 (D-Pad)
    /// </summary>
    public enum DPadDirection : byte
    {
        Up        = 0,
        UpRight   = 1,
        Right     = 2,
        DownRight = 3,
        Down      = 4,
        DownLeft  = 5,
        Left      = 6,
        UpLeft    = 7,
        None      = 8,
    }

    /// <summary>
    /// ボタン名からSwitchButton列挙型へのマッピングヘルパー
    /// </summary>
    public static class SwitchButtonHelper
    {
        /// <summary>
        /// 文字列名からSwitchButtonを取得 (NX Macro Controller互換)
        /// </summary>
        public static bool TryParse(string name, out SwitchButton button)
        {
            button = SwitchButton.None;
            return name.ToUpperInvariant() switch
            {
                "A"         => SetAndReturn(ref button, SwitchButton.A),
                "B"         => SetAndReturn(ref button, SwitchButton.B),
                "X"         => SetAndReturn(ref button, SwitchButton.X),
                "Y"         => SetAndReturn(ref button, SwitchButton.Y),
                "L"         => SetAndReturn(ref button, SwitchButton.L),
                "R"         => SetAndReturn(ref button, SwitchButton.R),
                "ZL"        => SetAndReturn(ref button, SwitchButton.ZL),
                "ZR"        => SetAndReturn(ref button, SwitchButton.ZR),
                "PLUS"  or "+" => SetAndReturn(ref button, SwitchButton.Plus),
                "MINUS" or "-" => SetAndReturn(ref button, SwitchButton.Minus),
                "HOME"      => SetAndReturn(ref button, SwitchButton.Home),
                "CAPTURE"   => SetAndReturn(ref button, SwitchButton.Capture),
                "LSTICK"    => SetAndReturn(ref button, SwitchButton.LStick),
                "RSTICK"    => SetAndReturn(ref button, SwitchButton.RStick),
                "UP"        => SetAndReturn(ref button, SwitchButton.Up),
                "DOWN"      => SetAndReturn(ref button, SwitchButton.Down),
                "LEFT"      => SetAndReturn(ref button, SwitchButton.Left),
                "RIGHT"     => SetAndReturn(ref button, SwitchButton.Right),
                _           => false
            };
        }

        private static bool SetAndReturn(ref SwitchButton button, SwitchButton value)
        {
            button = value;
            return true;
        }

        /// <summary>
        /// DPad文字列のパース
        /// </summary>
        public static bool TryParseDPad(string direction, out DPadDirection dpad)
        {
            dpad = DPadDirection.None;
            return direction.ToUpperInvariant() switch
            {
                "UP"        => SetDPad(ref dpad, DPadDirection.Up),
                "DOWN"      => SetDPad(ref dpad, DPadDirection.Down),
                "LEFT"      => SetDPad(ref dpad, DPadDirection.Left),
                "RIGHT"     => SetDPad(ref dpad, DPadDirection.Right),
                "UPLEFT"    => SetDPad(ref dpad, DPadDirection.UpLeft),
                "UPRIGHT"   => SetDPad(ref dpad, DPadDirection.UpRight),
                "DOWNLEFT"  => SetDPad(ref dpad, DPadDirection.DownLeft),
                "DOWNRIGHT" => SetDPad(ref dpad, DPadDirection.DownRight),
                _           => false
            };
        }

        private static bool SetDPad(ref DPadDirection dpad, DPadDirection value)
        {
            dpad = value;
            return true;
        }

        /// <summary>
        /// ボタン表示名の取得
        /// </summary>
        public static string GetDisplayName(SwitchButton button) => button switch
        {
            SwitchButton.A       => "A",
            SwitchButton.B       => "B",
            SwitchButton.X       => "X",
            SwitchButton.Y       => "Y",
            SwitchButton.L       => "L",
            SwitchButton.R       => "R",
            SwitchButton.ZL      => "ZL",
            SwitchButton.ZR      => "ZR",
            SwitchButton.Plus    => "+",
            SwitchButton.Minus   => "−",
            SwitchButton.Home    => "HOME",
            SwitchButton.Capture => "CAP",
            SwitchButton.LStick  => "LS",
            SwitchButton.RStick  => "RS",
            SwitchButton.Up      => "↑",
            SwitchButton.Down    => "↓",
            SwitchButton.Left    => "←",
            SwitchButton.Right   => "→",
            _                    => button.ToString()
        };
    }
}
