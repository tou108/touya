using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Services.Macro
{
    /// <summary>
    /// マクロスクリプトテキストをパースして MacroCommand リストに変換する
    ///
    /// ■ 対応構文:
    ///   NX Macro Controller 互換:
    ///     A 100          → Aボタンを100ms押す
    ///     B 200          → Bボタンを200ms押す
    ///     WAIT 500       → 500ms待機
    ///
    ///   拡張構文:
    ///     PRESS A 100    → 上記と同じ（明示的形式）
    ///     HOLD A         → Aを押し続ける
    ///     RELEASE A      → Aを離す
    ///     STICK L 0 128 500  → 左スティック X=0, Y=128, 500ms
    ///     STICK R 255 128 1000
    ///     DPAD UP 100    → 十字キー上 100ms
    ///     WAIT_FRAMES 3  → 3フレーム待機 (@60fps)
    ///     LOOP 10        → ループ開始 (0=無限)
    ///     END_LOOP       → ループ終了
    ///     IF IMAGE_MATCH "file.png" 0.9
    ///     ELIF IMAGE_MATCH "file2.png" 0.8
    ///     ELSE
    ///     END_IF
    ///     IF OCR "100,200,300,50" CONTAINS "OK"
    ///     WAIT_IMAGE "file.png" 30000
    ///     CAPTURE_SCREEN "out.png"
    ///     # コメント
    ///     LABEL myLabel
    ///     GOTO myLabel
    /// </summary>
    public class MacroInterpreter
    {
        private static readonly Regex RxButton
            = new(@"^(?<btn>[A-Z0-9_+]+)\s+(?<dur>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxPress
            = new(@"^PRESS\s+(?<btn>[A-Z0-9_+]+)\s+(?<dur>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxHold
            = new(@"^HOLD\s+(?<btn>[A-Z0-9_+]+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxRelease
            = new(@"^RELEASE\s+(?<btn>[A-Z0-9_+]+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxWait
            = new(@"^WAIT\s+(?<ms>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxWaitFrames
            = new(@"^WAIT_FRAMES?\s+(?<f>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxStick
            = new(@"^STICK\s+(?<side>[LR])\s+(?<x>\d+)\s+(?<y>\d+)(?:\s+(?<dur>\d+))?$", RegexOptions.IgnoreCase);
        private static readonly Regex RxDPad
            = new(@"^DPAD\s+(?<dir>[A-Z]+)\s+(?<dur>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxLoop
            = new(@"^LOOP\s+(?<n>\d+|INF)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxIfImage
            = new(@"^IF\s+IMAGE_MATCH\s+""(?<path>[^""]+)""\s+(?<conf>[\d.]+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxIfOcr
            = new(@"^IF\s+OCR\s+""(?<region>[^""]*)""\s+CONTAINS\s+""(?<text>[^""]*)""$", RegexOptions.IgnoreCase);
        private static readonly Regex RxWaitImage
            = new(@"^WAIT_IMAGE\s+""(?<path>[^""]+)""\s+(?<timeout>\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxCapture
            = new(@"^CAPTURE_SCREEN(?:\s+""(?<path>[^""]+)"")?$", RegexOptions.IgnoreCase);
        private static readonly Regex RxLabel
            = new(@"^LABEL\s+(?<name>\w+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxGoto
            = new(@"^GOTO\s+(?<name>\w+)$", RegexOptions.IgnoreCase);

        // ─────────────────────────────────────────────────────────
        //  パースエラー
        // ─────────────────────────────────────────────────────────

        public class ParseError
        {
            public int    Line    { get; }
            public string Message { get; }
            public string Source  { get; }
            public ParseError(int line, string msg, string src) { Line = line; Message = msg; Source = src; }
            public override string ToString() => $"行 {Line}: {Message}  ({Source})";
        }

        // ─────────────────────────────────────────────────────────
        //  パースエントリポイント
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// スクリプトテキストをパースして MacroCommand リストと ParseError リストを返す
        /// </summary>
        public static (List<MacroCommand> Commands, List<ParseError> Errors)
            Parse(string scriptText)
        {
            var errors  = new List<ParseError>();
            var lines   = scriptText.Replace("\r\n", "\n").Split('\n');
            var tokens  = new List<(int LineNo, string Text)>();

            // コメント除去・空行スキップ
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Contains('#')) line = line[..line.IndexOf('#')].Trim();
                if (!string.IsNullOrEmpty(line))
                    tokens.Add((i + 1, line));
            }

            var pos      = 0;
            var commands = ParseBlock(tokens, ref pos, errors, "LOOP_END_EOF");
            return (commands, errors);
        }

        private static List<MacroCommand> ParseBlock(
            List<(int LineNo, string Text)> tokens,
            ref int pos,
            List<ParseError> errors,
            string terminator)
        {
            var result = new List<MacroCommand>();

            while (pos < tokens.Count)
            {
                var (lineNo, text) = tokens[pos];
                string upper = text.ToUpperInvariant();

                // ---- ブロック終端 ----
                if (upper == "END_LOOP" && terminator == "END_LOOP") { pos++; return result; }
                if (upper == "END_IF"   && terminator == "END_IF")   { pos++; return result; }
                if ((upper == "ELSE" || upper.StartsWith("ELIF")) && terminator == "END_IF")
                    return result;  // else/elif は親が処理

                // ---- LOOP ----
                Match mLoop = RxLoop.Match(text);
                if (mLoop.Success)
                {
                    pos++;
                    int count = mLoop.Groups["n"].Value.ToUpperInvariant() == "INF"
                        ? 0 : int.Parse(mLoop.Groups["n"].Value);
                    var body = ParseBlock(tokens, ref pos, errors, "END_LOOP");
                    var cmd  = new MacroCommand
                    {
                        Type = MacroCommandType.LoopStart,
                        LoopCount = count,
                        Children  = body,
                        LineNumber = lineNo,
                    };
                    result.Add(cmd);
                    continue;
                }

                // ---- IF IMAGE_MATCH ----
                Match mIfImg = RxIfImage.Match(text);
                if (mIfImg.Success)
                {
                    pos++;
                    var ifCmd = new MacroCommand
                    {
                        Type       = MacroCommandType.IfImageMatch,
                        ImagePath  = mIfImg.Groups["path"].Value,
                        Confidence = double.Parse(mIfImg.Groups["conf"].Value),
                        LineNumber = lineNo,
                    };
                    ifCmd.Children = ParseBlock(tokens, ref pos, errors, "END_IF");
                    // else/elif の処理
                    ifCmd.ElseChildren = ParseElse(tokens, ref pos, errors);
                    result.Add(ifCmd);
                    continue;
                }

                // ---- IF OCR CONTAINS ----
                Match mIfOcr = RxIfOcr.Match(text);
                if (mIfOcr.Success)
                {
                    pos++;
                    var ifCmd = new MacroCommand
                    {
                        Type      = MacroCommandType.IfOcrContains,
                        OcrText   = mIfOcr.Groups["text"].Value,
                        LineNumber = lineNo,
                    };
                    if (ImageRegion.TryParse(mIfOcr.Groups["region"].Value, out var region))
                        ifCmd.OcrRegion = region;
                    ifCmd.Children     = ParseBlock(tokens, ref pos, errors, "END_IF");
                    ifCmd.ElseChildren = ParseElse(tokens, ref pos, errors);
                    result.Add(ifCmd);
                    continue;
                }

                // ---- 単一コマンド ----
                var single = ParseSingleCommand(lineNo, text, errors);
                if (single != null) result.Add(single);
                pos++;
            }

            return result;
        }

        private static List<MacroCommand> ParseElse(
            List<(int LineNo, string Text)> tokens,
            ref int pos,
            List<ParseError> errors)
        {
            if (pos >= tokens.Count) return new();
            string upper = tokens[pos].Text.ToUpperInvariant();

            if (upper == "ELSE")
            {
                pos++;
                var body = ParseBlock(tokens, ref pos, errors, "END_IF");
                // END_IF を消費
                if (pos < tokens.Count && tokens[pos].Text.ToUpperInvariant() == "END_IF")
                    pos++;
                return body;
            }
            // elif は再帰処理 (ElseChildren に新しい IF を入れる)
            if (upper.StartsWith("ELIF"))
            {
                var elifText = tokens[pos].Text["ELIF".Length..].Trim();
                tokens[pos] = (tokens[pos].LineNo, "IF " + elifText);
                return ParseBlock(tokens, ref pos, errors, "END_IF");
            }
            if (upper == "END_IF") { pos++; }
            return new();
        }

        private static MacroCommand? ParseSingleCommand(int lineNo, string text, List<ParseError> errors)
        {
            // WAIT
            Match m = RxWait.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.Wait, DurationMs = int.Parse(m.Groups["ms"].Value), LineNumber = lineNo };

            // WAIT_FRAMES
            m = RxWaitFrames.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.WaitFrames, Frames = int.Parse(m.Groups["f"].Value), LineNumber = lineNo };

            // PRESS
            m = RxPress.Match(text);
            if (m.Success)
                return TryParseButton(m.Groups["btn"].Value, out var btn, lineNo, errors)
                    ? new MacroCommand { Type = MacroCommandType.Press, Buttons = btn, DurationMs = int.Parse(m.Groups["dur"].Value), LineNumber = lineNo }
                    : null;

            // HOLD
            m = RxHold.Match(text);
            if (m.Success)
                return TryParseButton(m.Groups["btn"].Value, out var btn, lineNo, errors)
                    ? new MacroCommand { Type = MacroCommandType.Hold, Buttons = btn, LineNumber = lineNo }
                    : null;

            // RELEASE
            m = RxRelease.Match(text);
            if (m.Success)
                return TryParseButton(m.Groups["btn"].Value, out var btn, lineNo, errors)
                    ? new MacroCommand { Type = MacroCommandType.Release, Buttons = btn, LineNumber = lineNo }
                    : null;

            // STICK
            m = RxStick.Match(text);
            if (m.Success)
            {
                bool isLeft = m.Groups["side"].Value.ToUpperInvariant() == "L";
                return new MacroCommand
                {
                    Type      = isLeft ? MacroCommandType.StickLeft : MacroCommandType.StickRight,
                    StickX    = byte.Parse(m.Groups["x"].Value),
                    StickY    = byte.Parse(m.Groups["y"].Value),
                    DurationMs = m.Groups["dur"].Success ? int.Parse(m.Groups["dur"].Value) : 0,
                    LineNumber = lineNo,
                };
            }

            // DPAD
            m = RxDPad.Match(text);
            if (m.Success && SwitchButtonHelper.TryParseDPad(m.Groups["dir"].Value, out var dpad))
                return new MacroCommand { Type = MacroCommandType.DPad, DPadDir = dpad, DurationMs = int.Parse(m.Groups["dur"].Value), LineNumber = lineNo };

            // WAIT_IMAGE
            m = RxWaitImage.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.WaitForImage, ImagePath = m.Groups["path"].Value, TimeoutMs = int.Parse(m.Groups["timeout"].Value), LineNumber = lineNo };

            // CAPTURE_SCREEN
            m = RxCapture.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.CaptureScreen, ImagePath = m.Groups["path"].Value, LineNumber = lineNo };

            // LABEL
            m = RxLabel.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.Label, Label = m.Groups["name"].Value, LineNumber = lineNo };

            // GOTO
            m = RxGoto.Match(text);
            if (m.Success)
                return new MacroCommand { Type = MacroCommandType.GoTo, Label = m.Groups["name"].Value, LineNumber = lineNo };

            // NX Macro Controller 互換: "BUTTON DUR" 形式
            m = RxButton.Match(text);
            if (m.Success && TryParseButton(m.Groups["btn"].Value, out var b2, lineNo, errors))
                return new MacroCommand { Type = MacroCommandType.Press, Buttons = b2, DurationMs = int.Parse(m.Groups["dur"].Value), LineNumber = lineNo };

            // 不明なコマンド
            errors.Add(new ParseError(lineNo, $"不明なコマンド: '{text}'", text));
            return null;
        }

        private static bool TryParseButton(string token, out SwitchButton btn, int lineNo, List<ParseError> errors)
        {
            btn = SwitchButton.None;
            // 複合ボタン (A+B のような形式)
            foreach (var part in token.Split('+'))
            {
                if (!SwitchButtonHelper.TryParse(part.Trim(), out var b))
                {
                    errors.Add(new ParseError(lineNo, $"不明なボタン: '{part}'", token));
                    return false;
                }
                btn |= b;
            }
            return true;
        }
    }
}
