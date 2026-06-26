using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Protocol;

namespace NXMacroAdvanced.Services.Connection
{
    /// <summary>
    /// Bluetooth 無線接続サービス
    /// Nintendo Switch に Pro Controller として Bluetooth HID 接続する
    ///
    /// ■ 動作モード:
    ///   MODE A) ダイレクト接続 (推奨):
    ///     PC の Bluetooth アダプタを使用し、Switch に直接 Pro Controller として接続
    ///     ※ Windows では Classic BT HID Peripheral モードが制限されているため、
    ///       InTheHand.Net.Bluetooth + 管理者権限が必要
    ///
    ///   MODE B) 中継接続:
    ///     Raspberry Pi Zero W 上で動作する nx-controller-server に
    ///     Bluetooth Serial Profile (SPP) で接続し、Pi が Switch に転送する
    ///     ※ より安定して動作する推奨方法
    ///
    /// ■ NX Macro Controller との互換性:
    ///     NX Macro Controller が使用する Bluetooth 接続方式と同じプロトコルを実装
    /// </summary>
    public class BluetoothService : IConnectionService
    {
        private BluetoothClient?      _btClient;
        private System.IO.Stream?     _btStream;
        private BluetoothAddress?     _switchAddress;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private readonly object  _lock   = new();
        private bool _disposed;
        private byte _timerCounter;

        // SPP (Serial Port Profile) の UUID
        private static readonly Guid SerialPortServiceClassId
            = new("00001101-0000-1000-8000-00805f9b34fb");

        public ConnectionStatus Status => _status;
        public string Description => _status == ConnectionStatus.Connected
            ? $"Bluetooth — {_switchAddress}"
            : "Bluetooth — 未接続";

        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ConnectionErrorEventArgs>?         ErrorOccurred;

        // ─────────────────────────────────────────────────────────
        //  デバイススキャン (UI から呼び出す)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 周囲の Bluetooth デバイスをスキャンする
        /// </summary>
        public static async Task<List<BluetoothDeviceInfo>> ScanDevicesAsync(
            int timeoutSeconds = 10,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var result = new List<BluetoothDeviceInfo>();
                try
                {
                    using var client = new BluetoothClient();
                    var devices = client.DiscoverDevices(255);
                    result.AddRange(devices);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BT Scan Error: {ex.Message}");
                }
                return result;
            }, ct);
        }

        // ─────────────────────────────────────────────────────────
        //  接続
        // ─────────────────────────────────────────────────────────

        public async Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
        {
            SetStatus(ConnectionStatus.Connecting, "Bluetooth 接続中...");

            try
            {
                if (string.IsNullOrEmpty(profile.BluetoothAddress))
                {
                    // アドレス未設定の場合はスキャンして Nintendo デバイスを探す
                    var devices = await ScanDevicesAsync(10, ct);
                    var nintendo = devices.FirstOrDefault(d =>
                        d.DeviceName.Contains("Pro Controller", StringComparison.OrdinalIgnoreCase) ||
                        d.DeviceName.Contains("Nintendo", StringComparison.OrdinalIgnoreCase));

                    if (nintendo == null)
                    {
                        SetStatus(ConnectionStatus.Error, "Switch/Pro Controller が見つかりません");
                        return false;
                    }
                    _switchAddress = nintendo.DeviceAddress;
                }
                else
                {
                    _switchAddress = BluetoothAddress.Parse(profile.BluetoothAddress);
                }

                // Bluetooth SPP で接続 (中継モード)
                bool connected = await ConnectSppAsync(_switchAddress, ct);
                if (!connected) return false;

                SetStatus(ConnectionStatus.Connected, $"Bluetooth 接続: {_switchAddress}");

                // 初期化パケットを送信
                await SendInitPacketsAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Error, ex.Message);
                ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("Bluetooth 接続エラー", ex));
                return false;
            }
        }

        private async Task<bool> ConnectSppAsync(BluetoothAddress address, CancellationToken ct)
        {
            try
            {
                _btClient = new BluetoothClient();
                var ep = new BluetoothEndPoint(address, SerialPortServiceClassId);

                await Task.Run(() => _btClient.Connect(ep), ct);
                _btStream = _btClient.GetStream();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("SPP 接続失敗", ex));
                return false;
            }
        }

        /// <summary>
        /// Switch への初期化パケットを送信する
        /// </summary>
        private async Task SendInitPacketsAsync(CancellationToken ct)
        {
            if (_btStream == null) return;

            // ニュートラル状態を複数回送信して安定させる
            var neutral = ControllerState.Neutral;
            for (int i = 0; i < 5; i++)
            {
                SendState(neutral);
                await Task.Delay(16, ct); // 60fps
            }
        }

        // ─────────────────────────────────────────────────────────
        //  切断
        // ─────────────────────────────────────────────────────────

        public async Task DisconnectAsync()
        {
            try
            {
                SendState(ControllerState.Neutral);
                await Task.Delay(50);
                _btStream?.Close();
                _btClient?.Close();
            }
            catch { }
            finally
            {
                _btStream?.Dispose();
                _btClient?.Dispose();
                _btStream = null;
                _btClient = null;
                SetStatus(ConnectionStatus.Disconnected, "Bluetooth 切断しました");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  状態送信
        // ─────────────────────────────────────────────────────────

        public Task<bool> SendStateAsync(ControllerState state, CancellationToken ct = default)
            => Task.FromResult(SendState(state));

        public bool SendState(ControllerState state)
        {
            if (_status != ConnectionStatus.Connected || _btStream == null) return false;

            lock (_lock)
            {
                try
                {
                    // Bluetooth HID レポート (0x30 形式)
                    byte[] report = SwitchProtocol.BuildInputReport0x30(state, _timerCounter++);
                    _btStream.Write(report, 0, report.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("BT 送信エラー", ex));
                    SetStatus(ConnectionStatus.Error, ex.Message);
                    return false;
                }
            }
        }

        private void SetStatus(ConnectionStatus s, string msg = "")
        {
            var old = _status; _status = s;
            if (old != s) StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(old, s, msg));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
