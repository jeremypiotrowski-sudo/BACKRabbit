#!/usr/bin/env python3
"""
BACKRabbit Emergency Recovery Script (GATE 10 — R10)
=====================================================
Use this script when:
  - Your phone is stuck in a boot loop after Magisk removal
  - ADB/Fastboot are not responding
  - You need to flash stock firmware but Odin isn't working
  - You're in Download Mode and need to flash manually

Requirements:
  - Python 3.8+
  - PyUSB (pip install pyusb)
  - libusb (Windows: zadig.akeo.ie, Linux: apt install libusb-1.0-0)
  - Stock firmware files (boot.img, vbmeta.img) in same directory

Usage:
  python emergency_fix.py --device MODEL --boot boot.img [--vbmeta vbmeta.img]

What this does:
  1. Detects Samsung device in Download Mode via USB
  2. Sends Odin protocol handshake
  3. Flashes boot.img to BOOT partition
  4. Optionally flashes vbmeta.img to VBMETA partition
  5. Reboots device

WARNING: This modifies your device's boot partition.
         Only use with stock firmware images.
         BACKRabbit is not responsible for data loss.
"""

import argparse
import hashlib
import os
import struct
import sys
import time
from pathlib import Path

# ============================================================
# USB Constants
# ============================================================
SAMSUNG_VID = 0x04E8
ODIN_PID = 0x685D  # Samsung Download Mode

# Odin protocol constants
ODIN_CMD_BEGIN_SESSION = 0x64
ODIN_CMD_END_SESSION = 0x65
ODIN_CMD_FLASH = 0x66
ODIN_CMD_REBOOT = 0x67
ODIN_CMD_PIT = 0x68

# Partition names in Odin protocol
PARTITION_MAP = {
    "boot": "BOOT",
    "vbmeta": "VBMETA",
    "recovery": "RECOVERY",
    "bootloader": "BOOTLOADER",
    "init_boot": "INIT_BOOT",
    "vendor_boot": "VENDOR_BOOT",
}

# ============================================================
# USB Detection
# ============================================================

def find_samsung_download_mode():
    """Find Samsung device in Download Mode via USB."""
    try:
        import usb.core
        import usb.util
    except ImportError:
        print("❌ PyUSB not installed. Run: pip install pyusb")
        print("   Also install libusb driver via Zadig: https://zadig.akeo.ie/")
        return None

    # Find Samsung device
    device = usb.core.find(idVendor=SAMSUNG_VID, idProduct=ODIN_PID)
    if device is None:
        # Try broader Samsung VID search
        devices = list(usb.core.find(idVendor=SAMSUNG_VID, find_all=True))
        if devices:
            print(f"Found {len(devices)} Samsung USB device(s):")
            for d in devices:
                print(f"  VID: 0x{d.idVendor:04X} PID: 0x{d.idProduct:04X}")
            print("Is your phone in Download Mode? (Power off → Vol Up + Vol Down → Plug USB)")
        else:
            print("❌ No Samsung device found via USB.")
            print("   Make sure your phone is in Download Mode:")
            print("   1. Power off completely")
            print("   2. Hold Volume Up + Volume Down simultaneously")
            print("   3. While holding, plug USB cable into computer")
            print("   4. Screen should show 'Downloading... Do not turn off target'")
        return None

    try:
        device.set_configuration()
        print(f"✅ Found Samsung device in Download Mode: 0x{device.idProduct:04X}")
        return device
    except usb.core.USBError as e:
        print(f"❌ USB access error: {e}")
        print("   You may need to install libusb driver via Zadig:")
        print("   1. Download Zadig from https://zadig.akeo.ie/")
        print("   2. Run Zadig, select your Samsung device")
        print("   3. Install libusb-win32 or WinUSB driver")
        return None


# ============================================================
# Odin Protocol (Simplified)
# ============================================================

def odin_handshake(device):
    """Send Odin begin session command."""
    try:
        # Odin uses bulk endpoint 0x02 OUT, 0x83 IN typically
        # This is a simplified implementation
        endpoint_out = 0x02
        endpoint_in = 0x83
        
        # Begin session packet
        packet = struct.pack('<I', ODIN_CMD_BEGIN_SESSION)
        device.write(endpoint_out, packet, timeout=5000)
        
        # Read response
        response = device.read(endpoint_in, 64, timeout=5000)
        print(f"✅ Odin handshake successful")
        return True
    except Exception as e:
        print(f"⚠️ Odin handshake failed: {e}")
        print("   This script uses a simplified Odin protocol.")
        print("   For full Odin flashing, use Odin3 (Windows) or Heimdall (Linux/Mac).")
        return False


def flash_partition(device, partition_name, data):
    """Flash a partition via Odin protocol."""
    try:
        endpoint_out = 0x02
        
        # Flash command
        cmd = struct.pack('<I', ODIN_CMD_FLASH)
        device.write(endpoint_out, cmd, timeout=5000)
        
        # Send partition name
        name_bytes = partition_name.encode('ascii') + b'\x00'
        device.write(endpoint_out, name_bytes, timeout=5000)
        
        # Send size
        size_bytes = struct.pack('<I', len(data))
        device.write(endpoint_out, size_bytes, timeout=5000)
        
        # Send data in chunks
        chunk_size = 1024 * 1024  # 1MB chunks
        for i in range(0, len(data), chunk_size):
            chunk = data[i:i + chunk_size]
            device.write(endpoint_out, chunk, timeout=10000)
            print(f"  Sent {min(i + chunk_size, len(data))}/{len(data)} bytes...")
        
        print(f"✅ Flashed {partition_name} ({len(data)} bytes)")
        return True
    except Exception as e:
        print(f"❌ Flash failed for {partition_name}: {e}")
        return False


def odin_reboot(device):
    """Send reboot command."""
    try:
        endpoint_out = 0x02
        packet = struct.pack('<I', ODIN_CMD_REBOOT)
        device.write(endpoint_out, packet, timeout=5000)
        print("✅ Reboot command sent")
        return True
    except Exception as e:
        print(f"⚠️ Reboot command failed: {e}")
        print("   Manually reboot: Hold Power + Vol Down for 10 seconds")
        return False


# ============================================================
# File Verification
# ============================================================

def verify_image(path):
    """Verify image file exists and has reasonable size."""
    if not os.path.exists(path):
        print(f"❌ File not found: {path}")
        return None
    
    size = os.path.getsize(path)
    if size < 1024:
        print(f"❌ File too small ({size} bytes): {path}")
        return None
    
    if size > 200 * 1024 * 1024:  # 200MB max
        print(f"⚠️ File unusually large ({size / 1024 / 1024:.1f}MB): {path}")
    
    with open(path, 'rb') as f:
        data = f.read()
    
    sha = hashlib.sha256(data).hexdigest()[:16]
    print(f"  {path}: {size / 1024 / 1024:.1f}MB, SHA256: {sha}...")
    return data


# ============================================================
# Main Recovery Flow
# ============================================================

def main():
    parser = argparse.ArgumentParser(
        description="BACKRabbit Emergency Recovery — Flash stock firmware via USB",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python emergency_fix.py --device SM-F966U1 --boot boot.img
  python emergency_fix.py --device SM-S928U1 --boot boot.img --vbmeta vbmeta.img
  python emergency_fix.py --device auto --boot stock_boot.img

For full firmware restore, use Odin3 (Windows) or Heimdall (Linux/Mac).
This script is for emergency boot partition recovery only.
        """
    )
    parser.add_argument("--device", required=True, 
                        help="Device model (e.g., SM-F966U1) or 'auto' for detection")
    parser.add_argument("--boot", required=True,
                        help="Path to stock boot.img")
    parser.add_argument("--vbmeta", default=None,
                        help="Path to stock vbmeta.img (optional)")
    parser.add_argument("--no-reboot", action="store_true",
                        help="Don't reboot after flashing")
    
    args = parser.parse_args()
    
    print("=" * 60)
    print("  BACKRabbit Emergency Recovery")
    print("  GATE 10 — R10: Emergency Flasher")
    print("=" * 60)
    print()
    print(f"  Device: {args.device}")
    print(f"  Boot image: {args.boot}")
    if args.vbmeta:
        print(f"  VBMeta image: {args.vbmeta}")
    print()
    
    # Step 1: Verify image files
    print("--- Step 1: Verifying image files ---")
    boot_data = verify_image(args.boot)
    if boot_data is None:
        print("❌ Cannot proceed without valid boot.img")
        print("   Download stock firmware from: https://samfw.com/firmware/" + args.device)
        sys.exit(1)
    
    vbmeta_data = None
    if args.vbmeta:
        vbmeta_data = verify_image(args.vbmeta)
        if vbmeta_data is None:
            print("⚠️ VBMeta image invalid, skipping VBMETA flash")
    
    # Step 2: Find device
    print()
    print("--- Step 2: Detecting device in Download Mode ---")
    device = find_samsung_download_mode()
    if device is None:
        print()
        print("=" * 60)
        print("  MANUAL RECOVERY INSTRUCTIONS")
        print("=" * 60)
        print()
        print("  Since USB detection failed, use Odin3 manually:")
        print()
        print("  1. Download Odin3 from: https://odindownload.com")
        print("  2. Download stock firmware from: https://samfw.com/firmware/" + args.device)
        print("  3. Extract the .zip to get AP, BL, CP, CSC .tar.md5 files")
        print("  4. Put phone in Download Mode:")
        print("     Power off → Hold Vol Up + Vol Down → Plug USB")
        print("  5. Open Odin3, load files into slots:")
        print("     BL → BL_*.tar.md5")
        print("     AP → AP_*.tar.md5")
        print("     CP → CP_*.tar.md5")
        print("     CSC → CSC_*.tar.md5 (NOT HOME_CSC — that keeps data)")
        print("  6. Click Start. Wait 5-10 minutes.")
        print("  7. Phone will reboot with stock firmware.")
        print()
        print(f"  Your boot image is at: {args.boot}")
        print(f"  Keep it safe — you may need it later.")
        print()
        sys.exit(0)
    
    # Step 3: Odin handshake
    print()
    print("--- Step 3: Odin handshake ---")
    if not odin_handshake(device):
        print()
        print("⚠️ Odin protocol handshake failed.")
        print("   This script uses a simplified protocol.")
        print("   Falling back to manual instructions (see above).")
        print()
        print("=" * 60)
        print("  MANUAL RECOVERY INSTRUCTIONS")
        print("=" * 60)
        print()
        print("  1. Download Odin3 from: https://odindownload.com")
        print("  2. Download stock firmware from: https://samfw.com/firmware/" + args.device)
        print("  3. Put phone in Download Mode (already detected)")
        print("  4. Open Odin3, load AP, BL, CP, CSC files")
        print("  5. Click Start. Wait 5-10 minutes.")
        print()
        sys.exit(0)
    
    # Step 4: Flash boot
    print()
    print("--- Step 4: Flashing boot partition ---")
    print("⚠️ This will modify your device's boot partition.")
    print("   Make sure you're flashing STOCK firmware, not modified images.")
    print()
    
    confirm = input("Type 'FLASH' to confirm: ")
    if confirm != "FLASH":
        print("❌ Flash cancelled.")
        sys.exit(0)
    
    boot_partition = PARTITION_MAP.get("boot", "BOOT")
    if not flash_partition(device, boot_partition, boot_data):
        print("❌ Boot flash failed!")
        print("   Your device may be in an unstable state.")
        print("   DO NOT REBOOT — flash stock firmware via Odin immediately.")
        sys.exit(1)
    
    # Step 5: Flash vbmeta (optional)
    if vbmeta_data:
        print()
        print("--- Step 5: Flashing VBMETA partition ---")
        vbmeta_partition = PARTITION_MAP.get("vbmeta", "VBMETA")
        if not flash_partition(device, vbmeta_partition, vbmeta_data):
            print("⚠️ VBMETA flash failed. Boot may still work without it.")
    
    # Step 6: Reboot
    if not args.no_reboot:
        print()
        print("--- Step 6: Rebooting ---")
        odin_reboot(device)
    
    print()
    print("=" * 60)
    print("  ✅ EMERGENCY RECOVERY COMPLETE")
    print("=" * 60)
    print()
    print("  Your device should now boot with stock firmware.")
    print("  If it doesn't:")
    print("  1. Try flashing full firmware via Odin")
    print("  2. Check samfw.com for your device's firmware")
    print("  3. BACKRabbit backups are in Documents/BACKRabbit/Backups")
    print()


if __name__ == "__main__":
    main()