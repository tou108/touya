using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NXMacroAdvanced.Models
{
    /// <summary>
    /// マクロスクリプト全体（メタデータ + コマンドリスト）
    /// </summary>
    public class MacroScript
    {
        // ---- メタデータ ----
        [JsonProperty("name")]
        public string Name { get; set; } = "新規マクロ";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("author")]
        public string Author { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("created")]
        public DateTime Created { get; set; } = DateTime.Now;

        [JsonProperty("modified")]
        public DateTime Modified { get; set; } = DateTime.Now;

        [JsonProperty("targetFps")]
        public int TargetFps { get; set; } = 60;

        // ---- コマンドリスト ----
        [JsonProperty("commands")]
        public List<MacroCommand> Commands { get; set; } = new();

        // ---- ファイルパス (保存時) ----
        [JsonIgnore]
        public string? FilePath { get; set; }

        [JsonIgnore]
        public bool IsModified { get; set; } = false;

        /// <summary>
        /// 推定実行時間 (ms) を計算
        /// </summary>
        [JsonIgnore]
        public long EstimatedDurationMs => CalculateDuration(Commands);

        private long CalculateDuration(List<MacroCommand> commands)
        {
            long total = 0;
            foreach (var cmd in commands)
            {
                switch (cmd.Type)
                {
                    case MacroCommandType.Press:
                    case MacroCommandType.StickLeft:
                    case MacroCommandType.StickRight:
                    case MacroCommandType.DPad:
                        total += cmd.DurationMs;
                        break;
                    case MacroCommandType.Wait:
                        total += cmd.DurationMs;
                        break;
                    case MacroCommandType.WaitFrames:
                        total += (long)(cmd.Frames * (1000.0 / TargetFps));
                        break;
                    case MacroCommandType.LoopStart:
                        long inner = CalculateDuration(cmd.Children);
                        total += inner * (cmd.LoopCount == 0 ? 1 : cmd.LoopCount);
                        break;
                    case MacroCommandType.IfImageMatch:
                    case MacroCommandType.IfOcrContains:
                        total += CalculateDuration(cmd.Children);
                        total += CalculateDuration(cmd.ElseChildren);
                        break;
                }
            }
            return total;
        }

        // ─────────────────────────────────────────────────────────────
        //  JSON 保存/読み込み
        // ─────────────────────────────────────────────────────────────

        public void SaveAsJson(string path)
        {
            Modified = DateTime.Now;
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
            FilePath = path;
            IsModified = false;
        }

        public static MacroScript? LoadFromJson(string path)
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var script = JsonConvert.DeserializeObject<MacroScript>(json);
            if (script != null) script.FilePath = path;
            return script;
        }

        // ─────────────────────────────────────────────────────────────
        //  NX Macro Controller 互換テキスト形式
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// NX Macro Controller 互換 + 拡張テキスト形式でエクスポート
        /// </summary>
        public string ToScriptText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {Name}");
            if (!string.IsNullOrEmpty(Description))
                sb.AppendLine($"# {Description}");
            sb.AppendLine($"# 作成: {Created:yyyy/MM/dd}  FPS: {TargetFps}");
            sb.AppendLine();
            WriteCommands(sb, Commands, 0);
            return sb.ToString();
        }

        private void WriteCommands(System.Text.StringBuilder sb, List<MacroCommand> cmds, int indent)
        {
            string pad = new(' ', indent * 2);
            foreach (var cmd in cmds)
            {
                sb.AppendLine($"{pad}{cmd}");
                if (cmd.Children.Count > 0)
                    WriteCommands(sb, cmd.Children, indent + 1);
                if (cmd.ElseChildren.Count > 0)
                {
                    sb.AppendLine($"{pad}ELSE");
                    WriteCommands(sb, cmd.ElseChildren, indent + 1);
                }
            }
        }

        /// <summary>
        /// NX Macro Controller 互換テキスト形式からロード
        /// </summary>
        public static MacroScript FromScriptText(string text)
        {
            var script = new MacroScript();
            // パースはMacroInterpreterが担当するためここでは空スクリプトを返す
            script.Name = "インポートしたマクロ";
            return script;
        }
    }
}
