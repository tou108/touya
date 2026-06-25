#!/usr/bin/env python3
"""
NX Macro Advanced — Raspberry Pi Zero HID Gateway Server
==========================================================
PC から受信した 10 バイトパケットを /dev/hidg0 に転送し、
Switch に Pro Controller として HID レポートを送信します。

動作モード:
  A) シリアルモード : USB シリアル経由でPCから受信 (デフォルト)
  B) TCP モード     : Wi-Fi 経由でPCから受信
      python3 nx_gadget_server.py --tcp --port 5000

使用方法:
  sudo python3 nx_gadget_server.py
  sudo python3 nx_gadget_server.py --tcp --port 5000
  sudo python3 nx_gadget_server.py --serial /dev/ttyACM0
"""

import sys
import struct
import socket
import serial
import argparse
import threading
import time

# ── パケット定数 ──
PACKET_SIZE   = 10
PACKET_HEADER = 0xAB
HID_REPORT_SIZE = 8

# ── 中立状態レポート ──
NEUTRAL_REPORT = bytes([0x00, 0x00, 0x08, 0x80, 0x80, 0x80, 0x80, 0x00])

class HidGateway:
    """PC から受信したパケットを Switch に転送するゲートウェイ"""

    def __init__(self, hidg_path="/dev/hidg0"):
        self.hidg_path = hidg_path
        self._hidg     = None
        self._lock     = threading.Lock()

    def open(self):
        self._hidg = open(self.hidg_path, "wb")
        # 中立状態を送信
        self.send_neutral()
        print(f"✅ HID デバイス {self.hidg_path} 開いた")

    def close(self):
        self.send_neutral()
        if self._hidg:
            self._hidg.close()
            self._hidg = None

    def send_neutral(self):
        self._write_report(NEUTRAL_REPORT)

    def process_packet(self, data: bytes) -> bool:
        """10バイトパケットを受信してHIDレポートに変換・送信"""
        if len(data) < PACKET_SIZE:
            return False

        # ヘッダー確認
        if data[0] != PACKET_HEADER:
            return False

        # チェックサム検証
        chk = 0
        for b in data[:9]:
            chk ^= b
        if chk != data[9]:
            return False

        btn_h = data[1]  # 左ボタン
        btn_m = data[2]  # 共有ボタン
        btn_l = data[3]  # 右ボタン
        dpad  = data[4]  # Dパッド (0-8)
        lx    = data[5]  # 左スティック X
        ly    = data[6]  # 左スティック Y
        rx    = data[7]  # 右スティック X
        ry    = data[8]  # 右スティック Y

        # ボタンマッピング → HID ボタンビット
        buttons = 0
        if btn_l & 0x08: buttons |= (1 << 0)   # Y
        if btn_l & 0x04: buttons |= (1 << 1)   # X
        if btn_l & 0x02: buttons |= (1 << 2)   # B
        if btn_l & 0x01: buttons |= (1 << 3)   # A
        if btn_l & 0x40: buttons |= (1 << 6)   # R
        if btn_l & 0x80: buttons |= (1 << 7)   # ZR
        if btn_m & 0x01: buttons |= (1 << 8)   # Minus
        if btn_m & 0x02: buttons |= (1 << 9)   # Plus
        if btn_m & 0x04: buttons |= (1 << 10)  # RStick
        if btn_m & 0x08: buttons |= (1 << 11)  # LStick
        if btn_m & 0x10: buttons |= (1 << 12)  # Home
        if btn_m & 0x20: buttons |= (1 << 13)  # Capture
        if btn_h & 0x40: buttons |= (1 << 14)  # L
        if btn_h & 0x80: buttons |= (1 << 15)  # ZL

        # DPad
        hat = min(dpad, 8)

        # HID レポート (8バイト)
        report = struct.pack("HBBBBBB",
            buttons,   # 2 bytes buttons
            hat,       # 1 byte hat
            lx, ly,    # L stick
            rx, ry,    # R stick
            0          # padding
        )

        self._write_report(report)
        return True

    def _write_report(self, report: bytes):
        with self._lock:
            if self._hidg:
                try:
                    self._hidg.write(report)
                    self._hidg.flush()
                except Exception as e:
                    print(f"HID 書き込みエラー: {e}")


# ──────────────────────────────────────
#  受信モード: シリアル
# ──────────────────────────────────────

def serial_mode(gateway: HidGateway, port: str, baudrate: int):
    print(f"📡 シリアルモード: {port} @ {baudrate}bps")
    buf = bytearray()
    try:
        with serial.Serial(port, baudrate, timeout=1) as ser:
            print("✅ シリアル接続完了。PC からの接続を待機中...")
            while True:
                data = ser.read(64)
                if not data:
                    continue
                buf.extend(data)
                while len(buf) >= PACKET_SIZE:
                    # ヘッダー探索
                    idx = buf.find(PACKET_HEADER)
                    if idx < 0:
                        buf.clear()
                        break
                    if idx > 0:
                        del buf[:idx]
                    if len(buf) < PACKET_SIZE:
                        break
                    # パケット処理
                    packet = bytes(buf[:PACKET_SIZE])
                    del buf[:PACKET_SIZE]
                    gateway.process_packet(packet)
    except KeyboardInterrupt:
        pass


# ──────────────────────────────────────
#  受信モード: TCP
# ──────────────────────────────────────

def tcp_mode(gateway: HidGateway, host: str, port: int):
    print(f"🌐 TCP モード: {host}:{port} で待機中...")
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((host, port))
        srv.listen(1)
        print(f"✅ 待機中... PC から {host}:{port} に接続してください")
        try:
            while True:
                conn, addr = srv.accept()
                print(f"🔗 接続: {addr}")
                buf = bytearray()
                with conn:
                    while True:
                        try:
                            data = conn.recv(64)
                            if not data:
                                break
                            buf.extend(data)
                            while len(buf) >= PACKET_SIZE:
                                idx = buf.find(PACKET_HEADER)
                                if idx < 0: buf.clear(); break
                                if idx > 0: del buf[:idx]
                                if len(buf) < PACKET_SIZE: break
                                gateway.process_packet(bytes(buf[:PACKET_SIZE]))
                                del buf[:PACKET_SIZE]
                        except Exception as e:
                            print(f"受信エラー: {e}")
                            break
                print(f"🔌 切断: {addr}")
                gateway.send_neutral()
        except KeyboardInterrupt:
            pass


# ──────────────────────────────────────
#  メイン
# ──────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="NX Macro Advanced RPi Gateway")
    parser.add_argument("--tcp",    action="store_true",     help="TCP モードで起動")
    parser.add_argument("--host",   default="0.0.0.0",       help="TCP ホスト (デフォルト: 0.0.0.0)")
    parser.add_argument("--port",   type=int, default=5000,  help="TCP ポート (デフォルト: 5000)")
    parser.add_argument("--serial", default="/dev/ttyACM0",  help="シリアルポート")
    parser.add_argument("--baud",   type=int, default=115200,help="ボーレート")
    parser.add_argument("--hidg",   default="/dev/hidg0",    help="HIDガジェットデバイス")
    args = parser.parse_args()

    gateway = HidGateway(args.hidg)
    try:
        gateway.open()
        if args.tcp:
            tcp_mode(gateway, args.host, args.port)
        else:
            serial_mode(gateway, args.serial, args.baud)
    except Exception as e:
        print(f"エラー: {e}")
    finally:
        gateway.close()
        print("サーバー終了")

if __name__ == "__main__":
    main()
