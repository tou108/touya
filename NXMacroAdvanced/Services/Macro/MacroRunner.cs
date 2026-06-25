using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Services.Connection;
using NXMacroAdvanced.Services.Imaging;

namespace NXMacroAdvanced.Services.Macro
{
    /// <summary>
    /// マクロの実行エンジン
    /// フレーム精密タイミングでコマンドを実行し、画像認識による条件分岐もサポートする
    /// </summary>
    public class MacroRunner
    {
        // ─────────────────────────────────────────────────────────
        //  依存サービス
        // ─────────────────────────────────────────────────────────
        private readonly ConnectionManager       _conn;
        private readonly ImageRecognitionService _imageService;
        private readonly OcrService              _ocrService;
        private readonly TimingService           _timing = new();

        // ─────────────────────────────────────────────────────────
        //  状態
        // ─────────────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private readonly SemaphoreSlim _pauseSem = new(1, 1);

        public bool  IsRunning { get; private set; }
        public bool  IsPaused  => _isPaused;
        public int   CurrentLine { get; private set; }
        public long  ElapsedMs   => (long)_timing.ElapsedMs;

        // ─────────────────────────────────────────────────────────
        //  イベント
        // ─────────────────────────────────────────────────────────
        public event EventHandler<MacroProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>?                 LogMessage;
        public event EventHandler?                         Completed;
        public event EventHandler<string>?                 ErrorOccurred;

        // ─────────────────────────────────────────────────────────
        //  コンストラクタ
        // ─────────────────────────────────────────────────────────
        public MacroRunner(ConnectionManager conn,
                           ImageRecognitionService imageService,
                           OcrService ocrService)
        {
            _conn         = conn;
            _imageService = imageService;
            _ocrService   = ocrService;
        }

        // ─────────────────────────────────────────────────────────
        //  実行制御
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// マクロスクリプトを非同期実行する
        /// </summary>
        public async Task RunAsync(MacroScript script, CancellationToken externalCt = default)
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _timing.Restart();

            try
            {
                Log($"▶ '{script.Name}' 実行開始");
                var state = ControllerState.Neutral;
                var labels = BuildLabelMap(script.Commands);
                await ExecuteBlockAsync(script.Commands, state, labels, _cts.Token);
                Log($"✅ '{script.Name}' 完了");
                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                Log("⏹ マクロを停止しました");
                _conn.SendState(ControllerState.Neutral); // 安全のため全解放
            }
            catch (Exception ex)
            {
                Log($"❌ エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
                _conn.SendState(ControllerState.Neutral);
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Stop()  => _cts?.Cancel();
        public void Pause() { _isPaused = true;  Log("⏸ 一時停止"); }
        public void Resume(){ _isPaused = false; Log("▶ 再開"); }

        // ─────────────────────────────────────────────────────────
        //  コマンドブロック実行
        // ─────────────────────────────────────────────────────────

        private async Task ExecuteBlockAsync(
            List<MacroCommand> commands,
            ControllerState    state,
            Dictionary<string, int> labels,
            CancellationToken  ct)
        {
            int i = 0;
            while (i < commands.Count)
            {
                ct.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(ct);

                var cmd = commands[i];
                CurrentLine = cmd.LineNumber;
                ProgressChanged?.Invoke(this, new MacroProgressEventArgs(i, commands.Count, cmd));

                switch (cmd.Type)
                {
                    // ─── 基本ボタン ───
                    case MacroCommandType.Press:
                        await ExecutePressAsync(state, cmd.Buttons, DPadDirection.None, cmd.DurationMs, ct);
                        break;

                    case MacroCommandType.Hold:
                        state.Press(cmd.Buttons);
                        _conn.SendState(state);
                        break;

                    case MacroCommandType.Release:
                        state.Release(cmd.Buttons);
                        _conn.SendState(state);
                        break;

                    // ─── 待機 ───
                    case MacroCommandType.Wait:
                        await WaitSendingNeutralAsync(state, cmd.DurationMs, ct);
                        break;

                    case MacroCommandType.WaitFrames:
                        int waitMs = (int)Math.Round(cmd.Frames * TimingService.MsPerFrame60);
                        await WaitSendingNeutralAsync(state, waitMs, ct);
                        break;

                    // ─── スティック ───
                    case MacroCommandType.StickLeft:
                        state.LeftStickX = cmd.StickX;
                        state.LeftStickY = cmd.StickY;
                        _conn.SendState(state);
                        if (cmd.DurationMs > 0)
                        {
                            await TimingService.PreciseSleepAsync(cmd.DurationMs, ct);
                            state.LeftStickX = 128; state.LeftStickY = 128;
                            _conn.SendState(state);
                        }
                        break;

                    case MacroCommandType.StickRight:
                        state.RightStickX = cmd.StickX;
                        state.RightStickY = cmd.StickY;
                        _conn.SendState(state);
                        if (cmd.DurationMs > 0)
                        {
                            await TimingService.PreciseSleepAsync(cmd.DurationMs, ct);
                            state.RightStickX = 128; state.RightStickY = 128;
                            _conn.SendState(state);
                        }
                        break;

                    // ─── 十字キー ───
                    case MacroCommandType.DPad:
                        state.DPad = cmd.DPadDir;
                        _conn.SendState(state);
                        await TimingService.PreciseSleepAsync(cmd.DurationMs, ct);
                        state.DPad = DPadDirection.None;
                        _conn.SendState(state);
                        break;

                    // ─── ループ ───
                    case MacroCommandType.LoopStart:
                        if (cmd.LoopCount == 0) // 無限ループ
                        {
                            while (!ct.IsCancellationRequested)
                                await ExecuteBlockAsync(cmd.Children, state, labels, ct);
                        }
                        else
                        {
                            for (int n = 0; n < cmd.LoopCount && !ct.IsCancellationRequested; n++)
                            {
                                Log($"  ループ {n + 1}/{cmd.LoopCount}");
                                await ExecuteBlockAsync(cmd.Children, state, labels, ct);
                            }
                        }
                        break;

                    // ─── 条件分岐: 画像認識 ───
                    case MacroCommandType.IfImageMatch:
                        bool matched = await _imageService.MatchAsync(cmd.ImagePath, cmd.Confidence, cmd.Region, ct);
                        Log($"  IMAGE_MATCH '{cmd.ImagePath}': {(matched ? "✓" : "✗")}");
                        await ExecuteBlockAsync(matched ? cmd.Children : cmd.ElseChildren, state, labels, ct);
                        break;

                    // ─── 条件分岐: OCR ───
                    case MacroCommandType.IfOcrContains:
                        string ocrText = await _ocrService.RecognizeAsync(cmd.OcrRegion, ct);
                        bool contains  = ocrText.Contains(cmd.OcrText, StringComparison.OrdinalIgnoreCase);
                        Log($"  OCR テキスト='{ocrText}' CONTAINS '{cmd.OcrText}': {(contains ? "✓" : "✗")}");
                        await ExecuteBlockAsync(contains ? cmd.Children : cmd.ElseChildren, state, labels, ct);
                        break;

                    // ─── 画像待機 ───
                    case MacroCommandType.WaitForImage:
                        Log($"  WAIT_IMAGE '{cmd.ImagePath}'...");
                        await _imageService.WaitForMatchAsync(cmd.ImagePath, cmd.Confidence, cmd.TimeoutMs, cmd.Region, ct);
                        break;

                    // ─── スクリーンショット ───
                    case MacroCommandType.CaptureScreen:
                        string capPath = string.IsNullOrEmpty(cmd.ImagePath)
                            ? $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                            : cmd.ImagePath;
                        await _imageService.SaveScreenshotAsync(capPath, ct);
                        Log($"  📷 保存: {capPath}");
                        break;

                    // ─── GOTO ───
                    case MacroCommandType.GoTo:
                        if (labels.TryGetValue(cmd.Label, out int targetIdx))
                            i = targetIdx;
                        break;
                }

                i++;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ヘルパー
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// ボタンを押して指定時間後に離す (60fps ループで送信し続ける)
        /// </summary>
        private async Task ExecutePressAsync(ControllerState state, SwitchButton btn,
            DPadDirection dpad, int durationMs, CancellationToken ct)
        {
            state.Press(btn);
            if (dpad != DPadDirection.None) state.DPad = dpad;

            long endTick = System.Diagnostics.Stopwatch.GetTimestamp()
                         + (long)(durationMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);

            _timing.ResetFrameTimer();
            while (System.Diagnostics.Stopwatch.GetTimestamp() < endTick && !ct.IsCancellationRequested)
            {
                _conn.SendState(state);
                _timing.WaitForNextFrame60();
            }

            state.Release(btn);
            if (dpad != DPadDirection.None) state.DPad = DPadDirection.None;
            _conn.SendState(state);
        }

        /// <summary>
        /// 指定時間だけニュートラル状態を 60fps で送信しながら待機する
        /// </summary>
        private async Task WaitSendingNeutralAsync(ControllerState state, int durationMs, CancellationToken ct)
        {
            long endTick = System.Diagnostics.Stopwatch.GetTimestamp()
                         + (long)(durationMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);

            _timing.ResetFrameTimer();
            while (System.Diagnostics.Stopwatch.GetTimestamp() < endTick && !ct.IsCancellationRequested)
            {
                _conn.SendState(state);
                _timing.WaitForNextFrame60();
            }
        }

        private async Task WaitIfPausedAsync(CancellationToken ct)
        {
            while (_isPaused && !ct.IsCancellationRequested)
                await Task.Delay(50, ct);
        }

        private static Dictionary<string, int> BuildLabelMap(List<MacroCommand> cmds)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cmds.Count; i++)
                if (cmds[i].Type == MacroCommandType.Label)
                    map[cmds[i].Label] = i;
            return map;
        }

        private void Log(string msg)
        {
            LogMessage?.Invoke(this, msg);
            System.Diagnostics.Debug.WriteLine($"[MacroRunner] {msg}");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  イベント引数
    // ─────────────────────────────────────────────────────────
    public class MacroProgressEventArgs : EventArgs
    {
        public int          CurrentIndex { get; }
        public int          TotalCount   { get; }
        public MacroCommand CurrentCommand { get; }
        public double       ProgressPct => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100 : 0;
        public MacroProgressEventArgs(int cur, int total, MacroCommand cmd)
        { CurrentIndex = cur; TotalCount = total; CurrentCommand = cmd; }
    }
}
