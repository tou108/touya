using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Services.Connection;
using NXMacroAdvanced.Services.Imaging;
using NXMacroAdvanced.Services.Macro;

namespace NXMacroAdvanced.ViewModels
{
    // ═══ 接続 ViewModel ═══════════════════════════════════════════════════

    public partial class ConnectionViewModel : BaseViewModel
    {
        private readonly ConnectionManager _conn;

        [ObservableProperty] private ConnectionType _selectedType = ConnectionType.UsbHid;
        [ObservableProperty] private string  _comPort      = "COM3";
        [ObservableProperty] private int     _baudRate     = 115200;
        [ObservableProperty] private string  _gadgetComPort  = "COM4";
        [ObservableProperty] private string  _gadgetTcpIp    = "192.168.7.1";
        [ObservableProperty] private int     _gadgetTcpPort  = 5000;
        [ObservableProperty] private bool    _gadgetUseTcp   = false;
        [ObservableProperty] private string  _btAddress    = "";
        [ObservableProperty] private bool    _isConnecting;
        [ObservableProperty] private string  _statusText   = "未接続";
        [ObservableProperty] private bool    _isConnected;

        public ObservableCollection<string> AvailablePorts { get; } = new();
        public ObservableCollection<string> BtDevices      { get; } = new();

        public ConnectionViewModel(ConnectionManager conn)
        {
            _conn = conn;
            _conn.StatusChanged += (s, e) => Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsConnected  = e.NewStatus == ConnectionStatus.Connected;
                IsConnecting = e.NewStatus == ConnectionStatus.Connecting;
                StatusText   = e.Message;
            });
            RefreshPortsCommand.Execute(null);
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var p in UsbHidService.GetAvailablePorts()) AvailablePorts.Add(p);
            if (AvailablePorts.Any() && !AvailablePorts.Contains(ComPort))
                ComPort = AvailablePorts.First();
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            IsConnecting = true;
            var profile = new ConnectionProfile
            {
                Type = SelectedType, ComPort = ComPort, BaudRate = BaudRate,
                GadgetComPort = GadgetComPort, GadgetTcpIp = GadgetTcpIp,
                GadgetTcpPort = GadgetTcpPort, GadgetUseTcp = GadgetUseTcp,
                BluetoothAddress = BtAddress,
            };
            await _conn.ConnectAsync(profile);
            IsConnecting = false;
        }

        [RelayCommand]
        private async Task DisconnectAsync() => await _conn.DisconnectAsync();

        [RelayCommand]
        private async Task ScanBluetoothAsync()
        {
            BtDevices.Clear();
            BtDevices.Add("スキャン中...");
            var devices = await BluetoothService.ScanDevicesAsync();
            BtDevices.Clear();
            foreach (var d in devices)
                BtDevices.Add($"{d.DeviceName} ({d.DeviceAddress})");
            if (!BtDevices.Any()) BtDevices.Add("デバイスが見つかりませんでした");
        }
    }

    // ═══ コントローラー ViewModel ═════════════════════════════════════════

    public partial class ControllerViewModel : BaseViewModel
    {
        private readonly ConnectionManager _conn;
        private readonly ControllerState   _state = new();

        [ObservableProperty] private bool _btnA, _btnB, _btnX, _btnY;
        [ObservableProperty] private bool _btnL, _btnR, _btnZL, _btnZR;
        [ObservableProperty] private bool _btnPlus, _btnMinus, _btnHome, _btnCapture;
        [ObservableProperty] private bool _btnLStick, _btnRStick;
        [ObservableProperty] private bool _dpadUp, _dpadDown, _dpadLeft, _dpadRight;
        [ObservableProperty] private double _lStickX = 0.5, _lStickY = 0.5;
        [ObservableProperty] private double _rStickX = 0.5, _rStickY = 0.5;

        public ControllerViewModel(ConnectionManager conn) => _conn = conn;

        [RelayCommand]
        private void ToggleButton(string? btn)
        {
            if (btn == null) return;
            if (!SwitchButtonHelper.TryParse(btn, out var sb)) return;
            if (_state.IsPressed(sb)) _state.Release(sb); else _state.Press(sb);
            UpdateBoolProps();
            _conn.SendState(_state);
        }

        [RelayCommand]
        private void ToggleDPad(string? dir)
        {
            if (dir == null) return;
            SwitchButtonHelper.TryParseDPad(dir, out var d);
            _state.DPad = _state.DPad == d ? DPadDirection.None : d;
            UpdateDPadProps();
            _conn.SendState(_state);
        }

        [RelayCommand]
        private void CenterLStick() { LStickX = 0.5; LStickY = 0.5; }

        [RelayCommand]
        private void CenterRStick() { RStickX = 0.5; RStickY = 0.5; }

        [RelayCommand]
        private async Task QuickMacroAsync(string? macro)
        {
            if (macro == null || !_conn.Status.Equals(ConnectionStatus.Connected)) return;
            switch (macro)
            {
                case "A10":
                    for (int i = 0; i < 10; i++)
                    {
                        _state.Press(SwitchButton.A); _conn.SendState(_state);
                        await Task.Delay(100);
                        _state.Release(SwitchButton.A); _conn.SendState(_state);
                        await Task.Delay(100);
                    }
                    break;
                case "BA":
                    _state.Press(SwitchButton.B); _conn.SendState(_state);
                    await Task.Delay(80);
                    _state.Release(SwitchButton.B);
                    _state.Press(SwitchButton.A); _conn.SendState(_state);
                    await Task.Delay(80);
                    _state.Release(SwitchButton.A); _conn.SendState(_state);
                    break;
                case "StickUp":
                    _state.LeftStickY = 0; _conn.SendState(_state);
                    await Task.Delay(500);
                    _state.LeftStickY = 128; _conn.SendState(_state);
                    break;
            }
        }

        [RelayCommand]
        private void NeutralAll()
        {
            _state.Buttons = SwitchButton.None;
            _state.DPad = DPadDirection.None;
            _state.LeftStickX = _state.LeftStickY = 128;
            _state.RightStickX = _state.RightStickY = 128;
            UpdateBoolProps(); UpdateDPadProps();
            LStickX = LStickY = RStickX = RStickY = 0.5;
            _conn.SendState(_state);
        }

        private void UpdateBoolProps()
        {
            BtnA = _state.IsPressed(SwitchButton.A); BtnB = _state.IsPressed(SwitchButton.B);
            BtnX = _state.IsPressed(SwitchButton.X); BtnY = _state.IsPressed(SwitchButton.Y);
            BtnL = _state.IsPressed(SwitchButton.L); BtnR = _state.IsPressed(SwitchButton.R);
            BtnZL = _state.IsPressed(SwitchButton.ZL); BtnZR = _state.IsPressed(SwitchButton.ZR);
            BtnPlus = _state.IsPressed(SwitchButton.Plus); BtnMinus = _state.IsPressed(SwitchButton.Minus);
            BtnHome = _state.IsPressed(SwitchButton.Home); BtnCapture = _state.IsPressed(SwitchButton.Capture);
            BtnLStick = _state.IsPressed(SwitchButton.LStick); BtnRStick = _state.IsPressed(SwitchButton.RStick);
        }

        private void UpdateDPadProps()
        {
            DpadUp    = _state.DPad == DPadDirection.Up    || _state.DPad == DPadDirection.UpLeft   || _state.DPad == DPadDirection.UpRight;
            DpadDown  = _state.DPad == DPadDirection.Down  || _state.DPad == DPadDirection.DownLeft || _state.DPad == DPadDirection.DownRight;
            DpadLeft  = _state.DPad == DPadDirection.Left  || _state.DPad == DPadDirection.UpLeft   || _state.DPad == DPadDirection.DownLeft;
            DpadRight = _state.DPad == DPadDirection.Right || _state.DPad == DPadDirection.UpRight  || _state.DPad == DPadDirection.DownRight;
        }

        partial void OnLStickXChanged(double value) { _state.LeftStickX  = (byte)(value * 255); _conn.SendState(_state); }
        partial void OnLStickYChanged(double value) { _state.LeftStickY  = (byte)(value * 255); _conn.SendState(_state); }
        partial void OnRStickXChanged(double value) { _state.RightStickX = (byte)(value * 255); _conn.SendState(_state); }
        partial void OnRStickYChanged(double value) { _state.RightStickY = (byte)(value * 255); _conn.SendState(_state); }
    }

    // ═══ マクロエディター ViewModel ═══════════════════════════════════════

    public partial class MacroEditorViewModel : BaseViewModel
    {
        private readonly MacroRunner       _runner;
        private readonly ConnectionManager _conn;
        private CancellationTokenSource?   _cts;

        [ObservableProperty] private string _scriptText    = "# NX Macro Advanced スクリプト\n# ─────────────────────────\n# 基本構文:\n#   PRESS A 100      → Aを100ms押す\n#   WAIT 500         → 500ms待機\n#   LOOP 5           → 5回繰り返し\n#   END_LOOP\n#   STICK L 128 0 500 → 左スティック上に500ms\n#   DPAD UP 100      → 十字キー上\n#   IF IMAGE_MATCH \"file.png\" 0.9\n#   END_IF\n# ─────────────────────────\n\nWAIT 1000\nPRESS A 100\nWAIT 500\n";
        [ObservableProperty] private string _errorMessages = "";
        [ObservableProperty] private bool   _isRunning;
        [ObservableProperty] private bool   _isPaused;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _currentFile   = "";
        [ObservableProperty] private bool   _isModified;
        [ObservableProperty] private int    _loopCount     = 1;
        [ObservableProperty] private bool   _infiniteLoop;

        public ObservableCollection<string> RunLog { get; } = new();

        public MacroEditorViewModel(MacroRunner runner, ConnectionManager conn)
        {
            _runner = runner; _conn = conn;
            _runner.ProgressChanged += (s, e) => Application.Current?.Dispatcher?.Invoke(() =>
            {
                Progress = e.ProgressPct; IsRunning = _runner.IsRunning;
            });
            _runner.LogMessage += msg => Application.Current?.Dispatcher?.Invoke(() =>
            {
                RunLog.Insert(0, msg);
                while (RunLog.Count > 300) RunLog.RemoveAt(RunLog.Count - 1);
            });
            _runner.Completed += (s, e) => Application.Current?.Dispatcher?.Invoke(() =>
            { IsRunning = false; Progress = 100; });
        }

        [RelayCommand]
        private async Task RunAsync()
        {
            if (IsRunning) return;
            ErrorMessages = ""; RunLog.Clear();
            var (cmds, errors) = MacroInterpreter.Parse(ScriptText);
            if (errors.Count > 0) { ErrorMessages = string.Join("\n", errors); return; }
            var script = new MacroScript { Name = "エディターマクロ", Commands = cmds };
            _cts = new CancellationTokenSource();
            IsRunning = true;
            int runCount = InfiniteLoop ? int.MaxValue : Math.Max(1, LoopCount);
            for (int i = 0; i < runCount && !_cts.IsCancellationRequested; i++)
            {
                if (runCount > 1) RunLog.Insert(0, $"ループ {i + 1}/{(InfiniteLoop ? "∞" : runCount.ToString())}");
                await _runner.RunAsync(script, _cts.Token);
            }
            IsRunning = false;
        }

        [RelayCommand] private void Stop()  { _cts?.Cancel(); _runner.Stop(); }
        [RelayCommand] private void Pause() { if (IsPaused) _runner.Resume(); else _runner.Pause(); IsPaused = !IsPaused; }
        [RelayCommand] private void NewFile()    { ScriptText = "# 新規マクロ\n\nWAIT 1000\n"; CurrentFile = ""; IsModified = false; }
        [RelayCommand] private void OpenFile()
        {
            var dlg = new OpenFileDialog { Filter = "マクロファイル|*.nmx;*.txt|全ファイル|*.*" };
            if (dlg.ShowDialog() != true) return;
            ScriptText = File.ReadAllText(dlg.FileName); CurrentFile = dlg.FileName; IsModified = false;
        }
        [RelayCommand] private void SaveFile()
        {
            if (string.IsNullOrEmpty(CurrentFile)) { SaveFileAs(); return; }
            File.WriteAllText(CurrentFile, ScriptText); IsModified = false;
        }
        [RelayCommand] private void SaveFileAs()
        {
            var dlg = new SaveFileDialog { Filter = "マクロファイル|*.nmx|テキスト|*.txt", DefaultExt = ".nmx" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, ScriptText); CurrentFile = dlg.FileName; IsModified = false;
        }
        [RelayCommand] private void Validate()
        {
            var (_, errors) = MacroInterpreter.Parse(ScriptText);
            ErrorMessages = errors.Count == 0 ? "✅ 構文エラーなし" : string.Join("\n", errors);
        }
        partial void OnScriptTextChanged(string value) => IsModified = true;
    }

    // ═══ 録音 ViewModel ═══════════════════════════════════════════════════

    public partial class RecorderViewModel : BaseViewModel
    {
        private readonly MacroRecorder     _recorder;
        private readonly ConnectionManager _conn;

        [ObservableProperty] private bool   _isRecording;
        [ObservableProperty] private int    _eventCount;
        [ObservableProperty] private string _recordingName  = "録音マクロ";
        [ObservableProperty] private string _exportedScript = "";

        public RecorderViewModel(MacroRecorder recorder, ConnectionManager conn)
        {
            _recorder = recorder; _conn = conn;
            _recorder.EventCountChanged += (s, n) =>
                Application.Current?.Dispatcher?.Invoke(() => EventCount = n);
        }

        [RelayCommand] private void StartRecording() { _recorder.StartRecording(); IsRecording = true; }
        [RelayCommand] private void StopRecording()
        {
            _recorder.StopRecording(); IsRecording = false;
            ExportedScript = _recorder.ToScriptText(RecordingName);
        }
        [RelayCommand] private void ClearRecording() { _recorder.ClearRecording(); EventCount = 0; ExportedScript = ""; }
        [RelayCommand] private void ExportScript()
        {
            if (string.IsNullOrEmpty(ExportedScript)) return;
            var dlg = new SaveFileDialog { Filter = "マクロファイル|*.nmx|テキスト|*.txt", DefaultExt = ".nmx" };
            if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, ExportedScript);
        }
        public void RecordCurrentState(ControllerState state) => _recorder.RecordState(state);
    }

    // ═══ スケジューラー ViewModel ══════════════════════════════════════════

    public partial class SchedulerViewModel : BaseViewModel
    {
        private readonly MacroScheduler _scheduler;
        public ObservableCollection<ScheduleEntry> Entries => _scheduler.Entries;

        [ObservableProperty] private ScheduleEntry? _selectedEntry;
        [ObservableProperty] private bool _isSchedulerRunning = true;

        public SchedulerViewModel(MacroScheduler scheduler) { _scheduler = scheduler; }

        [RelayCommand] private void AddEntry()
        {
            var e = new ScheduleEntry { Name = "新しいスケジュール", ScheduledTime = DateTime.Now.AddMinutes(5) };
            _scheduler.AddEntry(e); SelectedEntry = e;
        }
        [RelayCommand] private void RemoveEntry()
        {
            if (SelectedEntry != null) _scheduler.RemoveEntry(SelectedEntry);
        }
        [RelayCommand] private void SelectMacroFile()
        {
            if (SelectedEntry == null) return;
            var dlg = new OpenFileDialog { Filter = "マクロ|*.nmx;*.json" };
            if (dlg.ShowDialog() == true) { SelectedEntry.MacroFilePath = dlg.FileName; OnPropertyChanged(nameof(SelectedEntry)); }
        }
        [RelayCommand] private void SaveSchedule() => _scheduler.SaveEntries();
    }

    // ═══ 画像認識 ViewModel ══════════════════════════════════════════════

    public partial class ImageRecognitionViewModel : BaseViewModel
    {
        private readonly ImageRecognitionService _imgService;
        private readonly OcrService              _ocrService;
        private readonly ScreenCaptureService    _capture;

        [ObservableProperty] private string _templatePath       = "";
        [ObservableProperty] private double _threshold          = 0.9;
        [ObservableProperty] private string _testResult         = "";
        [ObservableProperty] private string _ocrResult          = "";
        [ObservableProperty] private string _ocrRegionText      = "0,0,400,100";
        [ObservableProperty] private string _screenshotPath     = "";
        [ObservableProperty] private int    _captureDeviceIndex = 0;
        [ObservableProperty] private bool   _captureDeviceOpen;

        public ImageRecognitionViewModel(ImageRecognitionService img, OcrService ocr, ScreenCaptureService cap)
        { _imgService = img; _ocrService = ocr; _capture = cap; }

        [RelayCommand] private void SelectTemplate()
        {
            var dlg = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.bmp" };
            if (dlg.ShowDialog() == true) TemplatePath = dlg.FileName;
        }
        [RelayCommand] private async Task TestMatchAsync()
        {
            TestResult = "テスト中...";
            double conf = await _imgService.GetMatchResultAsync(TemplatePath);
            TestResult = $"信頼度: {conf:P1}   {(conf >= Threshold ? "✅ 一致" : "❌ 不一致")}";
        }
        [RelayCommand] private async Task RunOcrAsync()
        {
            OcrResult = "認識中...";
            ImageRegion.TryParse(OcrRegionText, out var region);
            OcrResult = await _ocrService.RecognizeAsync(region);
            if (string.IsNullOrWhiteSpace(OcrResult)) OcrResult = "（テキストを認識できませんでした）";
        }
        [RelayCommand] private async Task CaptureScreenshotAsync()
        {
            string path = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await _imgService.SaveScreenshotAsync(path);
            ScreenshotPath = path; TestResult = $"📷 保存: {path}";
        }
        [RelayCommand] private void OpenCaptureDevice()
        {
            CaptureDeviceOpen = _capture.OpenCaptureDevice(CaptureDeviceIndex);
            TestResult = CaptureDeviceOpen ? "✅ キャプチャデバイス接続成功" : "❌ デバイス接続失敗（番号を確認してください）";
        }
    }
}
