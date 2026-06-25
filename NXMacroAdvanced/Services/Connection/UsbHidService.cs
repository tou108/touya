using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Protocol;

namespace NXMacroAdvanced.Services.Connection
{
    /// <summary>
    /// USB HID 接続サービス
    /// Arduino / Teensy を COM ポート経由でシリアル通信し、
    /// Switch に USB HID コントローラーとして認識させる
    ///
    /// ■ Arduino 側のスケッチは /Arduino/SwitchController.ino を使用すること
    /// ■ 対応ボード: Arduino Leonardo, Teensy 3.x/4.x, Pro Micro
    /// </summary>
    public class UsbHidService : IConnectionService
    {
        private SerialPort?  _port;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private readonly object  _lock   = new();
        private bool _disposed;

        public ConnectionStatus Status => _status;
        public string Description => _port != null
            ? $"USB HID — {_port.PortName} @ {_port.BaudRate}bps"
            : "USB HID — 未接続";

        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ConnectionErrorEventArgs>?         ErrorOccurred;

        // ─────────────────────────────────────────────────────────
        //  接続
        // ─────────────────────────────────────────────────────────

        public async Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
        {
            SetStatus(ConnectionStatus.Connecting, "接続中...");

            try
            {
                await Task.Run(() =>
                {
                    _port = new SerialPort(profile.ComPort, profile.BaudRate)
                    {
                        DataBits     = 8,
                        Parity       = Parity.None,
                        StopBits     = StopBits.One,
                        ReadTimeout  = 500,
                        WriteTimeout = 500,
                        DtrEnable    = false,
                        RtsEnable    = false,
                    };
                    _port.Open();
                }, ct);

                // Arduino がリセットから起動するまで少し待つ
                await Task.Delay(800, ct);

                // ハンドシェイク: Arduino に "HELLO" を送り "READY" を待つ
                bool handshook = await HandshakeAsync(ct);
                if (!handshook)
                {
                    SetStatus(ConnectionStatus.Error, "ハンドシェイク失敗");
                    _port?.Close();
                    return false;
                }

                SetStatus(ConnectionStatus.Connected, $"{profile.ComPort} に接続しました");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Error, ex.Message);
                ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("USB HID 接続エラー", ex));
                return false;
            }
        }

        private async Task<bool> HandshakeAsync(CancellationToken ct)
        {
            if (_port == null) return false;
            try
            {
                // ニュートラル状態を3回送信してArduinoを起動確認
                var neutral = ControllerState.Neutral.ToSerialPacket();
                for (int i = 0; i < 3; i++)
                {
                    _port.Write(neutral, 0, neutral.Length);
                    await Task.Delay(100, ct);
                }
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────
        //  切断
        // ─────────────────────────────────────────────────────────

        public async Task DisconnectAsync()
        {
            if (_port == null) return;
            try
            {
                // ニュートラル状態を送信してからポートを閉じる
                if (_port.IsOpen)
                {
                    var neutral = ControllerState.Neutral.ToSerialPacket();
                    _port.Write(neutral, 0, neutral.Length);
                    await Task.Delay(50);
                    _port.Close();
                }
            }
            catch { /* 切断時エラーは無視 */ }
            finally
            {
                _port.Dispose();
                _port = null;
                SetStatus(ConnectionStatus.Disconnected, "切断しました");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  状態送信
        // ─────────────────────────────────────────────────────────

        public Task<bool> SendStateAsync(ControllerState state, CancellationToken ct = default)
            => Task.FromResult(SendState(state));

        public bool SendState(ControllerState state)
        {
            if (_port == null || !_port.IsOpen || _status != ConnectionStatus.Connected)
                return false;

            lock (_lock)
            {
                try
                {
                    byte[] packet = SwitchProtocol.BuildSerialPacket(state);
                    _port.Write(packet, 0, packet.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("送信エラー", ex));
                    SetStatus(ConnectionStatus.Error, ex.Message);
                    return false;
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        //  ヘルパー
        // ─────────────────────────────────────────────────────────

        private void SetStatus(ConnectionStatus newStatus, string message = "")
        {
            var old = _status;
            _status = newStatus;
            if (old != newStatus)
                StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(old, newStatus, message));
        }

        /// <summary>
        /// システムで使用可能な COM ポート一覧を取得
        /// </summary>
        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
