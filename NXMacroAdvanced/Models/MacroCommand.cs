using System.Collections.Generic;
using Newtonsoft.Json;

namespace NXMacroAdvanced.Models
{
    /// <summary>
    /// マクロコマンドの種別
    /// </summary>
    public enum MacroCommandType
    {
        // ---- 基本入力 ----
        Press,          // ボタンを一定時間押す
        Hold,           // ボタンを押し続ける
        Release,        // ボタンを離す
        Wait,           // 待機 (ms)
        WaitFrames,     // フレーム数待機 (1f=16.667ms @ 60fps)

        // ---- スティック ----
        StickLeft,      // 左スティック移動
        StickRight,     // 右スティック移動

        // ---- 十字キー ----
        DPad,           // 十字キー入力

        // ---- 制御フロー ----
        LoopStart,      // ループ開始 (count=0で無限)
        LoopEnd,        // ループ終了

        // ---- 条件分岐 ----
        IfImageMatch,   // 画像マッチ条件
        IfOcrContains,  // OCRテキスト条件
        ElseIf,         // else if
        Else,           // else
        EndIf,          // if終了

        // ---- 画像操作 ----
        WaitForImage,   // 画像が出現するまで待機
        CaptureScreen,  // スクリーンショット保存

        // ---- 特殊 ----
        Comment,        // コメント (実行なし)
        Label,          // ジャンプラベル
        GoTo,           // ラベルへジャンプ
    }

    /// <summary>
    /// 1つのマクロコマンドを表すクラス
    /// </summary>
    public class MacroCommand
    {
        [JsonProperty("type")]
        public MacroCommandType Type { get; set; }

        // ---- ボタン系 ----
        [JsonProperty("buttons")]
        public SwitchButton Buttons { get; set; } = SwitchButton.None;

        [JsonProperty("dpad")]
        public DPadDirection DPadDir { get; set; } = DPadDirection.None;

        // ---- 時間 ----
        [JsonProperty("durationMs")]
        public int DurationMs { get; set; } = 100;

        [JsonProperty("frames")]
        public int Frames { get; set; } = 1;

        // ---- スティック (0-255, 中央=128) ----
        [JsonProperty("stickX")]
        public byte StickX { get; set; } = 128;

        [JsonProperty("stickY")]
        public byte StickY { get; set; } = 128;

        // ---- ループ ----
        [JsonProperty("loopCount")]
        public int LoopCount { get; set; } = 1;  // 0=無限

        // ---- 画像認識 ----
        [JsonProperty("imagePath")]
        public string ImagePath { get; set; } = "";

        [JsonProperty("confidence")]
        public double Confidence { get; set; } = 0.9;

        [JsonProperty("timeoutMs")]
        public int TimeoutMs { get; set; } = 10000;

        [JsonProperty("region")]
        public ImageRegion? Region { get; set; }

        // ---- OCR ----
        [JsonProperty("ocrText")]
        public string OcrText { get; set; } = "";

        [JsonProperty("ocrRegion")]
        public ImageRegion? OcrRegion { get; set; }

        // ---- ラベル/GOTO ----
        [JsonProperty("label")]
        public string Label { get; set; } = "";

        // ---- コメント ----
        [JsonProperty("comment")]
        public string Comment { get; set; } = "";

        // ---- ネスト (ループ・条件ブロック内のコマンド) ----
        [JsonProperty("children")]
        public List<MacroCommand> Children { get; set; } = new();

        // ---- else if / else ブロック ----
        [JsonProperty("elseChildren")]
        public List<MacroCommand> ElseChildren { get; set; } = new();

        // ---- 行番号 (エラー表示用) ----
        [JsonIgnore]
        public int LineNumber { get; set; }

        /// <summary>
        /// ファクトリ: ボタン押下コマンド
        /// </summary>
        public static MacroCommand CreatePress(SwitchButton btn, int durationMs = 100) =>
            new() { Type = MacroCommandType.Press, Buttons = btn, DurationMs = durationMs };

        /// <summary>
        /// ファクトリ: 待機コマンド
        /// </summary>
        public static MacroCommand CreateWait(int durationMs) =>
            new() { Type = MacroCommandType.Wait, DurationMs = durationMs };

        /// <summary>
        /// ファクトリ: フレーム待機コマンド
        /// </summary>
        public static MacroCommand CreateWaitFrames(int frames) =>
            new() { Type = MacroCommandType.WaitFrames, Frames = frames };

        /// <summary>
        /// ファクトリ: 左スティックコマンド
        /// </summary>
        public static MacroCommand CreateStickLeft(byte x, byte y, int durationMs = 0) =>
            new() { Type = MacroCommandType.StickLeft, StickX = x, StickY = y, DurationMs = durationMs };

        /// <summary>
        /// ファクトリ: 右スティックコマンド
        /// </summary>
        public static MacroCommand CreateStickRight(byte x, byte y, int durationMs = 0) =>
            new() { Type = MacroCommandType.StickRight, StickX = x, StickY = y, DurationMs = durationMs };

        /// <summary>
        /// NX Macro Controller 形式の行表現
        /// </summary>
        public override string ToString() => Type switch
        {
            MacroCommandType.Press      => $"PRESS {Buttons} {DurationMs}",
            MacroCommandType.Hold       => $"HOLD {Buttons}",
            MacroCommandType.Release    => $"RELEASE {Buttons}",
            MacroCommandType.Wait       => $"WAIT {DurationMs}",
            MacroCommandType.WaitFrames => $"WAIT_FRAMES {Frames}",
            MacroCommandType.StickLeft  => $"STICK L {StickX} {StickY} {DurationMs}",
            MacroCommandType.StickRight => $"STICK R {StickX} {StickY} {DurationMs}",
            MacroCommandType.DPad       => $"DPAD {DPadDir} {DurationMs}",
            MacroCommandType.LoopStart  => $"LOOP {(LoopCount == 0 ? "INF" : LoopCount.ToString())}",
            MacroCommandType.LoopEnd    => "END_LOOP",
            MacroCommandType.IfImageMatch  => $"IF IMAGE_MATCH \"{ImagePath}\" {Confidence:F2}",
            MacroCommandType.IfOcrContains => $"IF OCR \"{(OcrRegion != null ? OcrRegion.ToString() : "")}\" CONTAINS \"{OcrText}\"",
            MacroCommandType.Else          => "ELSE",
            MacroCommandType.EndIf         => "END_IF",
            MacroCommandType.WaitForImage  => $"WAIT_IMAGE \"{ImagePath}\" {TimeoutMs}",
            MacroCommandType.CaptureScreen => $"CAPTURE_SCREEN \"{ImagePath}\"",
            MacroCommandType.Comment       => $"# {Comment}",
            MacroCommandType.Label         => $"LABEL {Label}",
            MacroCommandType.GoTo          => $"GOTO {Label}",
            _                              => Type.ToString()
        };
    }

    /// <summary>
    /// 画面内の矩形領域
    /// </summary>
    public class ImageRegion
    {
        [JsonProperty("x")]     public int X      { get; set; }
        [JsonProperty("y")]     public int Y      { get; set; }
        [JsonProperty("w")]     public int Width  { get; set; }
        [JsonProperty("h")]     public int Height { get; set; }

        public ImageRegion() { }
        public ImageRegion(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }

        public override string ToString() => $"{X},{Y},{Width},{Height}";

        public static bool TryParse(string s, out ImageRegion? region)
        {
            region = null;
            var parts = s.Split(',');
            if (parts.Length == 4 &&
                int.TryParse(parts[0], out int x) &&
                int.TryParse(parts[1], out int y) &&
                int.TryParse(parts[2], out int w) &&
                int.TryParse(parts[3], out int h))
            {
                region = new ImageRegion(x, y, w, h);
                return true;
            }
            return false;
        }
    }
}
