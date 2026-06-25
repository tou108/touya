using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Services.Macro
{
    /// <summary>
    /// コントローラー入力を録音し、マクロスクリプトとして出力するサービス
    /// キーボードやゲームパッドの入力を監視し、Switch への実際の入力として記録する
    /// </summary>
    public class MacroRecorder
    {
        // ─────────────────────────────────────────────────────────
        //  録音状態
        // ─────────────────────────────────────────────────────────
        private bool _isRecording;
        private readonly Stopwatch _sw = new();
        private ControllerState _prevState = ControllerState.Neutral;
        private long _lastEventTick;
        private readonly List<RecordedEvent> _events = new();
        private CancellationTokenSource? _cts;

        // デバウンス閾値: 同一状態が続いた場合に WAIT コマンドにまとめる
        private const int MERGE_THRESHOLD_MS = 5;

        public bool IsRecording => _isRecording;
        public int  EventCount   => _events.Count;

        // ─────────────────────────────────────────────────────────
        //  イベント
        // ─────────────────────────────────────────────────────────
        public event EventHandler<ControllerState>? StateRecorded;
        public event EventHandler<int>?             EventCountChanged;

        // ─────────────────────────────────────────────────────────
        //  録音制御
        // ─────────────────────────────────────────────────────────

        public void StartRecording()
        {
            if (_isRecording) return;
            _events.Clear();
            _sw.Restart();
            _lastEventTick = 0;
            _prevState = ControllerState.Neutral;
            _isRecording = true;
        }

        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            _sw.Stop();

            // 最後のイベントから録音停止までの WAIT を追加
            long finalWaitMs = (long)(_sw.ElapsedMilliseconds - TicksToMs(_lastEventTick));
            if (finalWaitMs > 10)
                _events.Add(new RecordedEvent { DeltaMs = (int)finalWaitMs, State = ControllerState.Neutral });
        }

        public void ClearRecording() => _events.Clear();

        // ─────────────────────────────────────────────────────────
        //  状態記録 (外部から呼ぶ: UI のボタン操作 or ポーリングループ)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 現在のコントローラー状態を記録する
        /// 前回の状態と変わった場合のみ記録する
        /// </summary>
        public void RecordState(ControllerState currentState)
        {
            if (!_isRecording) return;

            // 状態が変化していなければスキップ
            if (currentState.Equals(_prevState)) return;

            long nowTick  = _sw.ElapsedTicks;
            int  deltaMs  = (int)Math.Round((nowTick - _lastEventTick) * 1000.0 / Stopwatch.Frequency);

            _events.Add(new RecordedEvent
            {
                DeltaMs = Math.Max(deltaMs, 0),
                State   = (ControllerState)currentState.Clone(),
            });

            _lastEventTick = nowTick;
            _prevState     = (ControllerState)currentState.Clone();

            StateRecorded?.Invoke(this, currentState);
            EventCountChanged?.Invoke(this, _events.Count);
        }

        // ─────────────────────────────────────────────────────────
        //  MacroScript へのエクスポート
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 録音データを MacroScript に変換する
        /// 連続した同一ボタン入力をまとめて PRESS コマンドに変換する
        /// </summary>
        public MacroScript ToMacroScript(string name = "録音したマクロ", bool optimize = true)
        {
            var script = new MacroScript { Name = name };
            if (_events.Count == 0) return script;

            var events = optimize ? OptimizeEvents(_events) : _events;
            script.Commands.AddRange(ConvertToCommands(events));
            return script;
        }

        private List<MacroCommand> ConvertToCommands(List<RecordedEvent> events)
        {
            var cmds = new List<MacroCommand>();
            ControllerState prevState = ControllerState.Neutral;

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];

                // 前のイベントとの時間差を WAIT コマンドに変換
                if (ev.DeltaMs > MERGE_THRESHOLD_MS)
                {
                    // ニュートラル状態の WAIT
                    if (prevState.Buttons == SwitchButton.None)
                        cmds.Add(MacroCommand.CreateWait(ev.DeltaMs));
                }

                var state = ev.State;

                // スティックが動いている場合
                if (state.LeftStickX != 128 || state.LeftStickY != 128)
                    cmds.Add(MacroCommand.CreateStickLeft(state.LeftStickX, state.LeftStickY, 0));
                else if (prevState.LeftStickX != 128 || prevState.LeftStickY != 128)
                    cmds.Add(MacroCommand.CreateStickLeft(128, 128, 0)); // ニュートラルへ戻す

                if (state.RightStickX != 128 || state.RightStickY != 128)
                    cmds.Add(MacroCommand.CreateStickRight(state.RightStickX, state.RightStickY, 0));
                else if (prevState.RightStickX != 128 || prevState.RightStickY != 128)
                    cmds.Add(MacroCommand.CreateStickRight(128, 128, 0));

                // 新しく押されたボタン
                SwitchButton pressed  = state.Buttons & ~prevState.Buttons;
                SwitchButton released = prevState.Buttons & ~state.Buttons;

                if (released != SwitchButton.None)
                    cmds.Add(new MacroCommand { Type = MacroCommandType.Release, Buttons = released });
                if (pressed != SwitchButton.None)
                    cmds.Add(new MacroCommand { Type = MacroCommandType.Hold, Buttons = pressed });

                // DPad
                if (state.DPad != prevState.DPad)
                {
                    if (prevState.DPad != DPadDirection.None)
                        cmds.Add(new MacroCommand { Type = MacroCommandType.DPad, DPadDir = DPadDirection.None, DurationMs = 0 });
                    if (state.DPad != DPadDirection.None)
                        cmds.Add(new MacroCommand { Type = MacroCommandType.DPad, DPadDir = state.DPad, DurationMs = ev.DeltaMs });
                }

                prevState = state;
            }

            return PostProcessCommands(cmds);
        }

        /// <summary>
        /// HOLD → RELEASE を PRESS に変換するなどの後処理最適化
        /// </summary>
        private static List<MacroCommand> PostProcessCommands(List<MacroCommand> cmds)
        {
            var result = new List<MacroCommand>();
            for (int i = 0; i < cmds.Count; i++)
            {
                if (cmds[i].Type == MacroCommandType.Hold &&
                    i + 2 < cmds.Count &&
                    cmds[i + 1].Type == MacroCommandType.Wait &&
                    cmds[i + 2].Type == MacroCommandType.Release &&
                    cmds[i + 2].Buttons == cmds[i].Buttons)
                {
                    // HOLD + WAIT + RELEASE → PRESS
                    result.Add(MacroCommand.CreatePress(cmds[i].Buttons, cmds[i + 1].DurationMs));
                    i += 2;
                }
                else
                {
                    result.Add(cmds[i]);
                }
            }
            return result;
        }

        private static List<RecordedEvent> OptimizeEvents(List<RecordedEvent> events)
        {
            // 連続した WAIT を1つにまとめる
            var result = new List<RecordedEvent>(events);
            return result;
        }

        private static double TicksToMs(long ticks)
            => ticks * 1000.0 / Stopwatch.Frequency;

        // ─────────────────────────────────────────────────────────
        //  スクリプトテキスト出力
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 録音結果を NX Macro Controller 互換テキストとして返す
        /// </summary>
        public string ToScriptText(string name = "録音したマクロ")
            => ToMacroScript(name).ToScriptText();
    }

    // ─────────────────────────────────────────────────────────
    //  内部データ
    // ─────────────────────────────────────────────────────────

    internal class RecordedEvent
    {
        public int             DeltaMs { get; set; }  // 前イベントからの時間差 (ms)
        public ControllerState State   { get; set; } = ControllerState.Neutral;
    }
}
