"""
BACKRabbit — Magisk Residue Cleanup Script
Run AFTER flashing flash_root.tar via Odin.
Phone must be booted with Magisk root active.
"""
import subprocess
import sys
import time

def run_adb(serial, cmd, timeout=30):
    """Run adb command and return output."""
    full_cmd = f'platform-tools\\adb.exe -s {serial} {cmd}'
    result = subprocess.run(full_cmd, shell=True, capture_output=True, text=True, timeout=timeout)
    return result.stdout + result.stderr

def main():
    if len(sys.argv) < 2:
        print("Usage: python cleanup_script.py <ip:port>")
        print("Example: python cleanup_script.py 100.67.154.12:43215")
        sys.exit(1)
    
    serial = sys.argv[1]
    print(f"🐰 BACKRabbit Cleanup — Target: {serial}")
    print("=" * 60)
    
    # Step 1: Verify connection
    print("\n[1/6] Verifying ADB connection...")
    out = run_adb(serial, "devices")
    if serial in out and "device" in out:
        print(f"  ✅ Connected: {serial}")
    else:
        print(f"  ❌ Connection failed. Output:\n{out}")
        print("  Make sure Wireless Debugging is ON and port is correct.")
        sys.exit(1)
    
    # Step 2: Check root
    print("\n[2/6] Checking root access...")
    out = run_adb(serial, 'shell "su -c id"')
    if "uid=0" in out:
        print(f"  ✅ Root confirmed: {out.strip()}")
    else:
        print(f"  ❌ Root not available. Output:\n{out}")
        print("  Did you flash flash_root.tar? Is Magisk active?")
        sys.exit(1)
    
    # Step 3: Check current /data/adb state
    print("\n[3/6] Checking /data/adb/ state...")
    out = run_adb(serial, 'shell "su -c \'ls -la /data/adb/ 2>&1\'"')
    print(f"  Current state:\n{out}")
    
    # Step 4: Clean /data/adb/
    print("\n[4/6] Removing /data/adb/...")
    cmds = [
        'su -c "rm -rf /data/adb"',
        'su -c "rm -rf /data/adb/modules"',
        'su -c "rm -rf /data/adb/post-fs-data.d"',
        'su -c "rm -rf /data/adb/service.d"',
        'su -c "rm -rf /data/adb/magisk"',
        'su -c "rm -f /data/adb/magisk.db"',
        'su -c "rm -f /data/adb/magiskboot"',
    ]
    for cmd in cmds:
        out = run_adb(serial, f'shell "{cmd}"')
        if "error" in out.lower() or "permission denied" in out.lower():
            print(f"  ⚠️  {cmd}: {out.strip()}")
        else:
            print(f"  ✅ {cmd}")
    
    # Step 5: Restore SELinux contexts
    print("\n[5/6] Restoring SELinux contexts on /data...")
    out = run_adb(serial, 'shell "su -c \'restorecon -R /data\'"', timeout=120)
    if "error" in out.lower():
        print(f"  ⚠️  restorecon: {out.strip()}")
    else:
        print(f"  ✅ restorecon completed")
    
    # Step 6: Verify cleanup
    print("\n[6/6] Verifying cleanup...")
    out = run_adb(serial, 'shell "su -c \'test -d /data/adb && echo EXISTS || echo CLEAN\'"')
    if "CLEAN" in out:
        print(f"  ✅ /data/adb/ is GONE — cleanup successful!")
    elif "EXISTS" in out:
        print(f"  ❌ /data/adb/ still exists! Manual check needed.")
        out2 = run_adb(serial, 'shell "su -c \'ls -la /data/adb/ 2>&1\'"')
        print(f"  Remaining contents:\n{out2}")
    else:
        print(f"  ⚠️  Unexpected: {out.strip()}")
    
    # Check for module-systemized apps
    print("\n  Checking for Magisk-module packages...")
    out = run_adb(serial, 'shell "pm list packages -s 2>&1 | grep -iE \'magisk|xposed|lsposed|riru|zygisk\'"')
    if out.strip():
        print(f"  ⚠️  Found systemized module packages:\n{out}")
        print("  To remove: pm uninstall -k --user 0 <package> (as root)")
    else:
        print(f"  ✅ No Magisk-related system packages found")
    
    print("\n" + "=" * 60)
    print("🐰 Cleanup complete!")
    print("\nNEXT STEP (you):")
    print("1. Boot to Download Mode")
    print("2. Odin flash: BL(stock) + flash_stock.tar(AP) + CP(stock) + CSC(stock)")
    print("3. Phone boots stock, clean, Knox 0x0")

if __name__ == "__main__":
    main()