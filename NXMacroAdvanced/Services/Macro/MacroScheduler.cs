using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Services.Macro
{
    /// <summary>
    /// マクロスケジューラー
    /// 指定した時刻・間隔・曜日にマクロを自動実行するサービス
    /// </summary>
    public class MacroScheduler : IDisposable
    {
        private readonly MacroRunner _runner;
        private readonly string      _settingsPath;
        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private bool _disposed;

        // ─────────────────────────────────────────────────────────
        //  スケジュールリスト (UI にバインド可能)
        // ─────────────────────────────────────────────────────────
        public ObservableCollection<ScheduleEntry> Entries { get; } = new();

        // ─────────────────────────────────────────────────────────
        //  イベント
        // ─────────────────────────────────────────────────────────
        public event EventHandler<ScheduleEntry>? EntryExecuting;
        public event EventHandler<ScheduleEntry>? EntryCompleted;
        public event EventHandler<(ScheduleEntry, Exception)>? EntryFailed;
        public event EventHandler<string>?         LogMessage;

        public MacroScheduler(MacroRunner runner, string settingsPath = "scheduler.json")
        {
            _runner       = runner;
            _settingsPath = settingsPath;
            LoadEntries();
        }

        // ─────────────────────────────────────────────────────────
        //  スケジューラー起動/停止
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// スケジューラーワーカーを開始する
        /// </summary>
        public void Start()
        {
            if (_workerTask != null && !_workerTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token), _cts.Token);
            Log("⏰ スケジューラー開始");
        }

        /// <summary>
        /// スケジューラーワーカーを停止する
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _workerTask?.Wait(3000);
            Log("⏹ スケジューラー停止");
        }

        // ─────────────────────────────────────────────────────────
        //  ワーカーループ
        // ─────────────────────────────────────────────────────────

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 次に実行すべきエントリを探す
                    var now = DateTime.Now;
                    foreach (var entry in Entries.ToList())
                    {
                        if (!entry.IsEnabled) continue;
                        if (entry.NextRunTime == null)
                            entry.NextRunTime = entry.CalculateNextRun(now);

                        if (entry.NextRunTime.HasValue && entry.NextRunTime.Value <= now)
                        {
                            await ExecuteEntryAsync(entry, ct);
                        }
                    }

                    await Task.Delay(1000, ct); // 1秒ごとにチェック
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log($"スケジューラーエラー: {ex.Message}"); }
            }
        }

        private async Task ExecuteEntryAsync(ScheduleEntry entry, CancellationToken ct)
        {
            if (!File.Exists(entry.MacroFilePath))
            {
                Log($"⚠ マクロファイルが見つかりません: {entry.MacroFilePath}");
                entry.NextRunTime = entry.CalculateNextRun(DateTime.Now);
                return;
            }

            var script = MacroScript.LoadFromJson(entry.MacroFilePath);
            if (script == null)
            {
                Log($"⚠ マクロ読み込み失敗: {entry.MacroFilePath}");
                return;
            }

            Log($"▶ スケジュール実行: '{entry.Name}' ('{script.Name}')");
            EntryExecuting?.Invoke(this, entry);

            try
            {
                entry.RunCount++;
                entry.LastRunTime  = DateTime.Now;
                entry.NextRunTime  = entry.CalculateNextRun(DateTime.Now);

                await _runner.RunAsync(script, ct);

                Log($"✅ スケジュール完了: '{entry.Name}'");
                EntryCompleted?.Invoke(this, entry);
                SaveEntries();
            }
            catch (Exception ex)
            {
                Log($"❌ スケジュール失敗: '{entry.Name}' — {ex.Message}");
                EntryFailed?.Invoke(this, (entry, ex));
            }
        }

        // ─────────────────────────────────────────────────────────
        //  CRUD
        // ─────────────────────────────────────────────────────────

        public void AddEntry(ScheduleEntry entry)
        {
            entry.NextRunTime = entry.CalculateNextRun(DateTime.Now);
            Entries.Add(entry);
            SaveEntries();
        }

        public void UpdateEntry(ScheduleEntry entry)
        {
            entry.NextRunTime = entry.CalculateNextRun(DateTime.Now);
            SaveEntries();
        }

        public void RemoveEntry(ScheduleEntry entry)
        {
            Entries.Remove(entry);
            SaveEntries();
        }

        // ─────────────────────────────────────────────────────────
        //  永続化
        // ─────────────────────────────────────────────────────────

        public void SaveEntries()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Entries.ToList(), Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex) { Log($"保存エラー: {ex.Message}"); }
        }

        private void LoadEntries()
        {
            if (!File.Exists(_settingsPath)) return;
            try
            {
                string json = File.ReadAllText(_settingsPath);
                var list = JsonConvert.DeserializeObject<List<ScheduleEntry>>(json);
                if (list == null) return;
                foreach (var e in list)
                {
                    e.NextRunTime = e.CalculateNextRun(DateTime.Now);
                    Entries.Add(e);
                }
            }
            catch (Exception ex) { Log($"読み込みエラー: {ex.Message}"); }
        }

        private void Log(string msg) => LogMessage?.Invoke(this, msg);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
