#!/bin/bash
# ============================================================
#  NX Macro Advanced — Raspberry Pi Zero USB Gadget セットアップ
#  Switch に Pro Controller として認識させる設定スクリプト
#
#  使用方法:
#    sudo bash gadget_setup.sh
#
#  前提条件:
#    /boot/config.txt に "dtoverlay=dwc2" を追加
#    /etc/modules  に "dwc2" と "libcomposite" を追加
#    → 追加後に再起動が必要
# ============================================================

set -e

GADGET_DIR="/sys/kernel/config/usb_gadget/nx_controller"

# 既存設定を削除
if [ -d "$GADGET_DIR" ]; then
  echo "" > "$GADGET_DIR/UDC" 2>/dev/null || true
  rm -rf "$GADGET_DIR"
fi

# ── ガジェット基本設定 ──
mkdir -p "$GADGET_DIR"
cd "$GADGET_DIR"

echo 0x057e > idVendor   # Nintendo
echo 0x2009 > idProduct  # Pro Controller
echo 0x0200 > bcdDevice
echo 0x0200 > bcdUSB

# ── 文字列デスクリプタ ──
mkdir -p strings/0x409
echo "NXMacroAdvanced"     > strings/0x409/manufacturer
echo "Pro Controller"      > strings/0x409/product
echo "000000000001"        > strings/0x409/serialnumber

# ── コンフィグレーション ──
mkdir -p configs/c.1/strings/0x409
echo "Controller"          > configs/c.1/strings/0x409/configuration
echo 500                   > configs/c.1/MaxPower

# ── HID 関数 ──
mkdir -p functions/hid.usb0
echo 0    > functions/hid.usb0/protocol   # None
echo 0    > functions/hid.usb0/subclass   # None
echo 8    > functions/hid.usb0/report_length

# HID レポートデスクリプタ (Pro Controller)
echo -ne '\x05\x01\x09\x05\xa1\x01\x15\x00\x25\x01\x35\x00\x45\x01\x75\x01\x95\x10\x05\x09\x19\x01\x29\x10\x81\x02\x05\x01\x25\x07\x46\x3b\x01\x75\x04\x95\x01\x65\x14\x09\x39\x81\x42\x65\x00\x95\x01\x81\x01\x26\xff\x00\x46\xff\x00\x09\x30\x09\x31\x09\x32\x09\x35\x75\x08\x95\x04\x81\x02\xc0' \
  > functions/hid.usb0/report_desc

# ── 関数をコンフィグにリンク ──
ln -s functions/hid.usb0 configs/c.1/

# ── UDC (USB Device Controller) に登録 ──
UDC=$(ls /sys/class/udc | head -1)
if [ -z "$UDC" ]; then
  echo "ERROR: UDC が見つかりません。dwc2 が有効になっているか確認してください"
  exit 1
fi
echo "$UDC" > UDC

echo "✅ USB HID ガジェット設定完了: /dev/hidg0"
echo "   次のコマンドでサーバーを起動: python3 nx_gadget_server.py"
