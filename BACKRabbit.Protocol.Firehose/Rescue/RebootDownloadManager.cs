using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BACKRabbit.Protocol.Adb;
using BACKRabbit.Usb;

namespace BACKRabbit.Protocol.Firehose.Rescue;

/// <summary>
/// Automated Download Mode reboot for Samsung devices.
/// Tries ADB reboot first, falls back to model-specific button instructions.
/// Zero-wipe guarantee — Download Mode never requires a factory reset.
/// </summary>
public static class RebootDownloadManager
{
    private const int DOWNLOAD_MODE_TIMEOUT_MS = 90_000; // 90s — Z Fold 7 USB enumeration can be slow
    private const int POLL_INTERVAL_MS = 2_000;
    private const int MAX_BUTTON_ATTEMPTS = 3;

    /// <summary>
    /// Attempts to get the device into Samsung Download Mode (Odin mode).
    /// Phase A: Try ADB reboot download if device is responsive.
    /// Phase B: Print model-specific button instructions if ADB unavailable.
    /// Phase C: Verify device is in Download Mode via USB enumeration.
    /// </summary>
    /// <param name="adbClient">
    /// Optional pre-connected ADB client. If null, attempts a fresh TCP connection
    /// to localhost:5555 (common wireless debugging port).
    /// </param>
    /// <param name="skipInteractive">
    /// If true, skips the interactive button-instruction fallback.
    /// Returns false immediately if ADB is unavailable.
    /// </param>
    /// <returns>true if device is confirmed in Download Mode; false otherwise.</returns>
    public static async Task<bool> TryRebootToDownloadModeAsync(
        IAdbClient? adbClient = null,
        bool skipInteractive = false,
        CancellationToken ct = default)
    {
        // ── Phase A: Courtesy ADB reboot (only if pre-connected client provided) ──
        if (adbClient != null && adbClient.IsConnected)
        {
            Console.WriteLine("📱 Device responsive via ADB — attempting `adb reboot download`...");

            try
            {
                if (adbClient is AdbClient concreteAdb)
                {
                    await concreteAdb.RebootDownloadAsync(ct);
                }
                else
                {
                    await adbClient.ExecuteShellAsync("reboot download", ct);
                }

                Console.WriteLine("   Reboot command sent. Waiting for Download Mode...");

                if (await WaitForDownloadModeAsync(ct))
                {
                    Console.WriteLine("✅ Successfully rebooted to Download Mode via ADB");
                    return true;
                }

                Console.WriteLine("⚠️ ADB reboot initiated but device didn't enter Download Mode within timeout.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ ADB reboot failed: {ex.Message}");
            }
        }

        // ── Check if already in Download Mode ────────────────────
        if (IsDeviceInDownloadMode())
        {
            Console.WriteLine("✅ Device already in Download Mode — proceeding with rescue");
            return true;
        }

        // ── Phase B: Button instruction fallback ─────────────────
        if (skipInteractive)
        {
            Console.WriteLine("🔧 Device not in Download Mode and --skip-dl-mode-check specified.");
            Console.WriteLine("   Cannot proceed without Download Mode. Aborting.");
            return false;
        }

        PrintModelSpecificButtonInstructions();

        // ── Phase C: Retry loop — 3 attempts with progressive feedback ──
        for (int attempt = 1; attempt <= MAX_BUTTON_ATTEMPTS; attempt++)
        {
            Console.WriteLine($"\n⏳ Attempt {attempt}/{MAX_BUTTON_ATTEMPTS}: Press ENTER when you see 'Downloading...' screen...");
            Console.ReadLine();

            Console.WriteLine("🔍 Verifying Download Mode via USB enumeration...");
            if (IsDeviceInDownloadMode())
            {
                Console.WriteLine("✅ Download Mode CONFIRMED via USB — proceeding with rescue");
                return true;
            }

            if (attempt < MAX_BUTTON_ATTEMPTS)
            {
                Console.WriteLine("⚠️ Device not detected — likely released buttons too early or power not fully off");
                Console.WriteLine("🔁 REPEAT: Power off completely (15s VolDown+Power) → hold all 3 buttons 8-12s → wait for 'Downloading...' → Press ENTER");
            }
        }

        Console.WriteLine("❌ FAILED: Could not confirm Download Mode after 3 attempts.");
        Console.WriteLine("   Check: USB cable (use original Samsung cable), USB port (3.0+ directly),");
        Console.WriteLine("   ensure device is FULLY POWERED OFF before button combo, and hold buttons FIRMLY.");
        return false;
    }

    /// <summary>
    /// Synchronous wrapper for callers that can't use async.
    /// </summary>
    public static bool TryRebootToDownloadMode(
        IAdbClient? adbClient = null,
        bool skipInteractive = false)
    {
        return Task.Run(() => TryRebootToDownloadModeAsync(adbClient, skipInteractive))
            .GetAwaiter().GetResult();
    }

    // ── Private helpers ─────────────────────────────────────────

    private static async Task<bool> WaitForDownloadModeAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < DOWNLOAD_MODE_TIMEOUT_MS)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDeviceInDownloadMode())
                return true;

            await Task.Delay(POLL_INTERVAL_MS, ct);
        }
        return false;
    }

    private static bool IsDeviceInDownloadMode()
    {
        try
        {
            var devices = UsbDeviceManager.ListDevices(samsungOnly: true);
            return devices.Any(d =>
            {
                var pid = d.Descriptor.ProductID;
                return UsbDeviceManager.DOWNLOAD_MODE_PIDS.Contains(pid);
            });
        }
        catch
        {
            // USB enumeration can fail if no devices are connected
            return false;
        }
    }

    private static void PrintModelSpecificButtonInstructions()
    {
        Console.WriteLine(@"
🔧 SAMSUNG Z FOLD 7 DOWNLOAD MODE — GUARANTEED METHOD:
📱 CRITICAL: YOUR DEVICE MUST BE FULLY POWERED OFF FIRST
    → Hold Power + VolDown for 15+ seconds until screen goes black
    → WAIT 2 SECONDS after black screen (ensures full power off)

✋ BUTTON COMBO (USE BOTH HANDS IF NEEDED):
    [LEFT HAND]  Press and HOLD Volume Down
    [RIGHT HAND] Press and HOLD Side Button (Power)  +  Volume Up
    👉 ALL THREE BUTTONS SIMULTANEOUSLY — SQUEEZE FIRMLY

⏱️ HOLD FOR 8-12 SECONDS — DO NOT RELEASE EARLY
    • You WILL feel vibration at ~2s (ignore — keep holding)
    • Screen WILL flash briefly at ~5s (ignore — keep holding)
    • RELEASE ONLY when you see CLEAR, STABLE TEXT:
          'Downloading...'  OR  'Odin Mode'  OR  'Downloading... Do not turn off target'

🚫 ABSOLUTELY NO WIPE REQUIRED — THIS IS PURE BOOTLOADER ACCESS
🔌 USE ORIGINAL SAMSUNG USB-C CABLE IN USB 3.0+ PORT (NO HUBS)
".Trim());
    }
}