using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NXMacroAdvanced.Models;
using NXMacroAdvanced.Protocol;

namespace NXMacroAdvanced.Services.Connection
{
    /// <summary>
    /// USB Gadget 接続サービス
    /// Raspberry Pi Zero W / 2W を USB Gadget (HID) として動作させ、
    /// PC から Serial または TCP で命令を送信する
    ///
    /// ■ RPi Zero 側の設定:
    ///   - /boot/config.txt に "dtoverlay=dwc2" を追加
    ///   - /etc/modules に "dwc2", "libcomposite" を追加
    ///   - HIDガジェット設定スクリプト (gadget_setup.sh) を起動時に実行
    ///   - Python受信スクリプト (nx_gadget_server.py) を常駐させる
    ///
    /// ■ 接続方法:
    ///   A) USB シリアル: RPi Zero を USB で PC に接続し /dev/ttyACM0 or COMx で通信
    ///   B) TCP/IP: RPi Zero の Wi-Fi または USB ネットワーク (RNDIS) 経由で TCP 接続
    /// </summary>
    public class UsbGadgetService : IConnectionService
    {
        private SerialPort?  _serialPort;
        private TcpClient?   _tcpClient;
        private NetworkStream? _tcpStream;
        private bool         _useTcp;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private readonly object  _lock   = new();
        private bool _disposed;

        public ConnectionStatus Status => _status;
        public string Description
        {
            get
            {
                if (_status != ConnectionStatus.Connected) return "USB Gadget — 未接続";
                return _useTcp
                    ? $"USB Gadget (TCP) — {_tcpClient?.Client.RemoteEndPoint}"
                    : $"USB Gadget (Serial) — {_serialPort?.PortName}";
            }
        }

        public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ConnectionErrorEventArgs>?         ErrorOccurred;

        // ─────────────────────────────────────────────────────────
        //  接続
        // ─────────────────────────────────────────────────────────

        public async Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
        {
            SetStatus(ConnectionStatus.Connecting, "接続中...");
            _useTcp = profile.GadgetUseTcp;

            try
            {
                if (_useTcp)
                    return await ConnectTcpAsync(profile, ct);
                else
                    return await ConnectSerialAsync(profile, ct);
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Error, ex.Message);
                ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("USB Gadget 接続エラー", ex));
                return false;
            }
        }

        private async Task<bool> ConnectSerialAsync(ConnectionProfile profile, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                _serialPort = new SerialPort(profile.GadgetComPort, profile.BaudRate)
                {
                    DataBits    = 8,
                    Parity      = Parity.None,
                    StopBits    = StopBits.One,
                    ReadTimeout = 1000, WriteTimeout = 1000,
                };
                _serialPort.Open();
            }, ct);

            await Task.Delay(500, ct);
            SetStatus(ConnectionStatus.Connected, $"{profile.GadgetComPort} に接続しました");
            return true;
        }

        private async Task<bool> ConnectTcpAsync(ConnectionProfile profile, CancellationToken ct)
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(profile.GadgetTcpIp, profile.GadgetTcpPort, ct);
            _tcpStream = _tcpClient.GetStream();
            SetStatus(ConnectionStatus.Connected, $"{profile.GadgetTcpIp}:{profile.GadgetTcpPort} に接続しました");
            return true;
        }

        // ─────────────────────────────────────────────────────────
        //  切断
        // ─────────────────────────────────────────────────────────

        public async Task DisconnectAsync()
        {
            try
            {
                // ニュートラル状態を送信
                SendState(ControllerState.Neutral);
                await Task.Delay(50);

                _tcpStream?.Close();
                _tcpClient?.Close();
                _serialPort?.Close();
            }
            catch { }
            finally
            {
                _tcpStream?.Dispose();
                _tcpClient?.Dispose();
                _serialPort?.Dispose();
                _tcpStream   = null;
                _tcpClient   = null;
                _serialPort  = null;
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
            if (_status != ConnectionStatus.Connected) return false;

            lock (_lock)
            {
                try
                {
                    byte[] packet = SwitchProtocol.BuildSerialPacket(state);

                    if (_useTcp && _tcpStream != null)
                    {
                        _tcpStream.Write(packet, 0, packet.Length);
                    }
                    else if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.Write(packet, 0, packet.Length);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs("Gadget 送信エラー", ex));
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
