using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Services.Connection;
using NXMacroAdvanced.Services.Imaging;
using NXMacroAdvanced.Services.Macro;

namespace NXMacroAdvanced.ViewModels
{
    // ─────────────────────────────────────────────────────────────────────
    //  ベース ViewModel
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 全 ViewModel の基底クラス
    /// CommunityToolkit.Mvvm の ObservableObject を継承
    /// </summary>
    public abstract class BaseViewModel : ObservableObject
    {
        protected void RunOnUI(Action action)
            => Application.Current?.Dispatcher?.Invoke(action);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  メイン ViewModel
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// メインウィンドウの ViewModel
    /// 全サービスのライフサイクルと子 ViewModel を管理する
    /// </summary>
    public partial class MainViewModel : BaseViewModel, IDisposable
    {
        // ─── サービス ───
        public ConnectionManager       ConnectionManager { get; }
        public MacroRunner             MacroRunner       { get; }
        public MacroRecorder           MacroRecorder     { get; }
        public MacroScheduler          MacroScheduler    { get; }
        public ScreenCaptureService    CaptureService    { get; }
        public ImageRecognitionService ImageService      { get; }
        public OcrService              OcrService        { get; }

        // ─── 子 ViewModel ───
        public ConnectionViewModel      ConnectionVM      { get; }
        public ControllerViewModel      ControllerVM      { get; }
        public MacroEditorViewModel     MacroEditorVM     { get; }
        public RecorderViewModel        RecorderVM        { get; }
        public SchedulerViewModel       SchedulerVM       { get; }
        public ImageRecognitionViewModel ImageRecognitionVM { get; }

        // ─── UI 状態 ───
        [ObservableProperty] private int _selectedTabIndex;
        [ObservableProperty] private string _statusMessage = "未接続";
        [ObservableProperty] private bool   _isConnected;
        [ObservableProperty] private bool   _isRunning;
        [ObservableProperty] private string _connectionDescription = "未接続";
        [ObservableProperty] private double _macroProgress;

        // ─── ログ ───
        public ObservableCollection<string> Logs { get; } = new();
        private const int MAX_LOG = 500;

        public MainViewModel()
        {
            // ─ サービス初期化 ─
            CaptureService    = new ScreenCaptureService();
            ImageService      = new ImageRecognitionService(CaptureService);
            OcrService        = new OcrService(CaptureService);
            ConnectionManager = new ConnectionManager();
            MacroRunner       = new MacroRunner(ConnectionManager, ImageService, OcrService);
            MacroRecorder     = new MacroRecorder();
            MacroScheduler    = new MacroScheduler(MacroRunner);

            // ─ 子 ViewModel 初期化 ─
            ConnectionVM       = new ConnectionViewModel(ConnectionManager);
            ControllerVM       = new ControllerViewModel(ConnectionManager);
            MacroEditorVM      = new MacroEditorViewModel(MacroRunner, ConnectionManager);
            RecorderVM         = new RecorderViewModel(MacroRecorder, ConnectionManager);
            SchedulerVM        = new SchedulerViewModel(MacroScheduler);
            ImageRecognitionVM = new ImageRecognitionViewModel(ImageService, OcrService, CaptureService);

            // ─ イベント購読 ─
            ConnectionManager.StatusChanged  += OnConnectionStatusChanged;
            ConnectionManager.ErrorOccurred  += OnConnectionError;
            MacroRunner.ProgressChanged      += OnMacroProgress;
            MacroRunner.LogMessage           += (_, msg) => AddLog(msg);
            MacroRunner.Completed            += OnMacroCompleted;
            MacroScheduler.LogMessage        += (_, msg) => AddLog(msg);

            // スケジューラー開始
            MacroScheduler.Start();
        }

        // ─────────────────────────────────────────────────────────
        //  イベントハンドラー
        // ─────────────────────────────────────────────────────────

        private void OnConnectionStatusChanged(object? s, ConnectionStatusChangedEventArgs e)
        {
            RunOnUI(() =>
            {
                IsConnected           = e.NewStatus == ConnectionStatus.Connected;
                ConnectionDescription = ConnectionManager.Description;
                StatusMessage = e.NewStatus switch
                {
                    ConnectionStatus.Connected    => $"✅ 接続中: {ConnectionManager.Description}",
                    ConnectionStatus.Connecting   => "🔄 接続中...",
                    ConnectionStatus.Disconnected => "⭕ 未接続",
                    ConnectionStatus.Error        => $"❌ エラー: {e.Message}",
                    _ => StatusMessage
                };
                if (!string.IsNullOrEmpty(e.Message))
                    AddLog(e.Message);
            });
        }

        private void OnConnectionError(object? s, ConnectionErrorEventArgs e)
        {
            RunOnUI(() =>
            {
                AddLog($"❌ {e.Message}");
                if (e.Exception != null)
                    AddLog($"   詳細: {e.Exception.Message}");
            });
        }

        private void OnMacroProgress(object? s, MacroProgressEventArgs e)
        {
            RunOnUI(() =>
            {
                MacroProgress = e.ProgressPct;
                IsRunning     = MacroRunner.IsRunning;
            });
        }

        private void OnMacroCompleted(object? s, EventArgs e)
        {
            RunOnUI(() =>
            {
                MacroProgress = 100;
                IsRunning     = false;
                StatusMessage = "✅ マクロ完了";
            });
        }

        // ─────────────────────────────────────────────────────────
        //  コマンド
        // ─────────────────────────────────────────────────────────

        [RelayCommand]
        private void StopMacro() => MacroRunner.Stop();

        [RelayCommand]
        private void PauseMacro()
        {
            if (MacroRunner.IsPaused) MacroRunner.Resume();
            else                      MacroRunner.Pause();
        }

        [RelayCommand]
        private async Task DisconnectAsync()
        {
            await ConnectionManager.DisconnectAsync();
            StatusMessage = "⭕ 切断しました";
        }

        // ─────────────────────────────────────────────────────────
        //  ログ管理
        // ─────────────────────────────────────────────────────────

        public void AddLog(string message)
        {
            RunOnUI(() =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                while (Logs.Count > MAX_LOG)
                    Logs.RemoveAt(Logs.Count - 1);
            });
        }


        [RelayCommand]
        private void SelectTab(string indexStr)
        {
            if (int.TryParse(indexStr, out int idx))
                SelectedTabIndex = idx;
        }

        [RelayCommand]
        private void ClearLog() => Logs.Clear();

        // ─────────────────────────────────────────────────────────
        //  破棄
        // ─────────────────────────────────────────────────────────

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MacroScheduler.Stop();
            MacroScheduler.Dispose();
            ConnectionManager.Dispose();
            ImageService.Dispose();
            OcrService.Dispose();
            CaptureService.Dispose();
        }
    }
}
// ─── SelectTabCommand を MainViewModel に追加 ───
// （ファイル末尾の } の前に挿入済み）
