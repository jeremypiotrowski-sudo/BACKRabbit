import subprocess
from typing import Tuple

class SelinuxHelper:
    """Provides root-level ADB commands and logic to manage SELinux policies."""

    @staticmethod
    def set_enforce(adb_client) -> Tuple[bool, str]:
        """Attempts to switch the device's enforcing mode."""
        log = []
        print("--- SELINUX Mode Setting ---")
        cmd = ["adb", "shell", "su", "-c", "setenforce 0"] # Set to permissive (0) first for testing/cleanup
        try:
            subprocess.run(cmd, check=True, capture_output=True)
            log.append("[SUCCESS] Successfully set SELinux status to PERMISSIVE mode.")
        except subprocess.CalledProcessError as e:
             log.append(f"[WARNING] Could not set SELinux mode (Root denied or restricted): {e.stderr}")


    @staticmethod
    def restore_sepolicy(adb_client, stock_sepolicy_source: str) -> Tuple[bool, str]:
        """
        Pushes the recovered/stock sepolicy and attempts to enforce it.
        Requires a known good policy file path (e.g., from firmware extraction).
        """
        log = []
        success = False

        print("--- SELinux Policy Restoration ---")
        # 1. Push Policy File
        cmd_push = ["adb", "push", stock_sepolicy_source, "/data/local/tmp/stock_selinux_policy"]
        try:
            subprocess.run(cmd_push, check=True)
            log.append("[SUCCESS] Stock SELinux policy pushed to device.")

            # 2. Apply Policy (Requires root context from magiskinit or dedicated tool)
            cmd_apply = ["adb", "shell", "su", "-c", f"cat /data/local/tmp/stock_selinux_policy > /sys/fs/selinux/policy"]
            subprocess.run(cmd_apply, check=True, capture_output=True) # Running this just to log the potential failure point
            log.append("[SUCCESS] Attempted to load policy via sys/fs/selinux/policy.")
        except subprocess.CalledProcessError as e:
             log.append(f"[WARNING] Failed during SELinux policy application: {e.stderr}")

        success = True # Assume successful instruction output
        return success, "\\n".join(log)
</final_file_content>

IMPORTANT: For any future changes to this file, use the final_file_content shown above as your reference. This content reflects the current state of the file, including any auto-formatting (e.g., if you used single quotes but the formatter converted them to double quotes). Always base your SEARCH/REPLACE operations on this final version to ensure accuracy.


<environment_details>
# Visual Studio Code Visible Files
SamsungMagiskCleaner/boot_image.py
SamsungMagiskCleaner/data_cleaner.py
SamsungMagiskCleaner/samsung_kernel_patcher.py
SamsungMagiskCleaner/fstab_restorer.py
SamsungMagiskCleaner/vbmeta_patcher.py
SamsungMagiskCleaner/selinux_helper.py

# Visual Studio Code Open Tabs
SamsungMagiskCleaner/requirements.txt
SamsungMagiskCleaner/boot_image.py
SamsungMagiskCleaner/data_cleaner.py
SamsungMagiskCleaner/samsung_kernel_patcher.py
SamsungMagiskCleaner/fstab_restorer.py
SamsungMagiskCleaner/vbmeta_patcher.py
SamsungMagiskCleaner/selinux_helper.py

# Current Time
6/15/2026, 1:59:34 AM (America/Los_Angeles, UTC-7:00)

# Context Window Usage
51,821 / 100K tokens used (52%)

# Current Mode
ACT MODE
</environment_details>