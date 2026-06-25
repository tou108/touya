using System;
using System.Threading;
using System.Threading.Tasks;
using NXMacroAdvanced.Models;

namespace NXMacroAdvanced.Services.Connection
{
    // ─────────────────────────────────────────────────────────────────────
    //  接続状態
    // ─────────────────────────────────────────────────────────────────────
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
    }

    // ─────────────────────────────────────────────────────────────────────
    //  接続サービスのインターフェース
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch への接続を抽象化するインターフェース
    /// USB HID / USB Gadget / Bluetooth の全実装が準拠する
    /// </summary>
    public interface IConnectionService : IDisposable
    {
        /// <summary>現在の接続状態</summary>
        ConnectionStatus Status { get; }

        /// <summary>接続状態が変わったときに発火</summary>
        event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;

        /// <summary>エラーが発生したときに発火</summary>
        event EventHandler<ConnectionErrorEventArgs>? ErrorOccurred;

        /// <summary>接続を開始する</summary>
        Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken ct = default);

        /// <summary>接続を切断する</summary>
        Task DisconnectAsync();

        /// <summary>コントローラー状態を送信する (最低 60fps)</summary>
        Task<bool> SendStateAsync(ControllerState state, CancellationToken ct = default);

        /// <summary>コントローラー状態を同期的に送信する</summary>
        bool SendState(ControllerState state);

        /// <summary>接続の説明（例: "COM3 @ 115200bps"）</summary>
        string Description { get; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  イベント引数
    // ─────────────────────────────────────────────────────────────────────

    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public ConnectionStatus OldStatus { get; }
        public ConnectionStatus NewStatus { get; }
        public string Message { get; }
        public ConnectionStatusChangedEventArgs(ConnectionStatus old, ConnectionStatus newS, string msg = "")
        {
            OldStatus = old; NewStatus = newS; Message = msg;
        }
    }

    public class ConnectionErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception? Exception { get; }
        public ConnectionErrorEventArgs(string msg, Exception? ex = null) { Message = msg; Exception = ex; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  接続マネージャー
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// アクティブな接続を管理するシングルトンマネージャー
    /// 接続タイプの切り替えとライフサイクルを制御する
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        private IConnectionService? _currentService;
        private ConnectionProfile?  _currentProfile;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        // ---- イベント ----
        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ConnectionErrorEventArgs>?         ErrorOccurred;

        public ConnectionStatus Status => _currentService?.Status ?? ConnectionStatus.Disconnected;
        public string Description     => _currentService?.Description ?? "未接続";

        /// <summary>
        /// 接続を開始する（タイプに応じたサービスを生成）
        /// </summary>
        public async Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
        {
            // 既存接続を切断
            await DisconnectAsync();

            _currentProfile = profile;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // タイプに応じたサービスを選択
            _currentService = profile.Type switch
            {
                ConnectionType.UsbHid    => new UsbHidService(),
                ConnectionType.UsbGadget => new UsbGadgetService(),
                ConnectionType.Bluetooth => new BluetoothService(),
                _ => throw new NotSupportedException($"未対応の接続タイプ: {profile.Type}")
            };

            // イベントを転送
            _currentService.StatusChanged  += OnStatusChanged;
            _currentService.ErrorOccurred  += OnErrorOccurred;

            try
            {
                bool ok = await _currentService.ConnectAsync(profile, _cts.Token);
                if (!ok)
                {
                    _currentService.Dispose();
                    _currentService = null;
                }
                return ok;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(this, new ConnectionErrorEventArgs("接続に失敗しました", ex));
                _currentService?.Dispose();
                _currentService = null;
                return false;
            }
        }

        /// <summary>
        /// 切断する
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_currentService == null) return;

            _cts?.Cancel();

            try   { await _currentService.DisconnectAsync(); }
            catch { /* 切断中のエラーは無視 */ }

            _currentService.StatusChanged -= OnStatusChanged;
            _currentService.ErrorOccurred -= OnErrorOccurred;
            _currentService.Dispose();
            _currentService = null;

            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// コントローラー状態を現在の接続に送信する
        /// </summary>
        public async Task<bool> SendStateAsync(ControllerState state, CancellationToken ct = default)
        {
            if (_currentService?.Status != ConnectionStatus.Connected) return false;
            try
            {
                return await _currentService.SendStateAsync(state, ct);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(this, new ConnectionErrorEventArgs("送信エラー", ex));
                return false;
            }
        }

        /// <summary>
        /// コントローラー状態を同期送信する（マクロエンジン内部用）
        /// </summary>
        public bool SendState(ControllerState state)
        {
            if (_currentService?.Status != ConnectionStatus.Connected) return false;
            try   { return _currentService.SendState(state); }
            catch { return false; }
        }

        private void OnStatusChanged(object? s, ConnectionStatusChangedEventArgs e)
            => StatusChanged?.Invoke(this, e);

        private void OnErrorOccurred(object? s, ConnectionErrorEventArgs e)
            => ErrorOccurred?.Invoke(this, e);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
