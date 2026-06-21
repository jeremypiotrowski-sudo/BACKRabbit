import subprocess
import os
from typing import List, Dict, Any

class DataCleaner:
    """Handles the cleanup of Magisk artifacts in /data/adb/. Requires root access."""

    @staticmethod
    def __init__(self, adb_client):
        self.adb = adb_client # Assume an initialized ADB client object is passed

    def run_cleanup(self, force_remount: bool = True) -> Tuple[bool, str]:
        """
        Executes the full cleanup routine for /data/adb/.
        Returns (success: bool, log: str).
        """
        log = []
        success = False

        # 1. Forced Remount Attempt (Critical for read-only attacker scenario)
        if force_remount:
            print("Attempting to remount /data as writeable...")
            # The 'mount -o rw,remount' command is the standard attempt
            cmd = ["adb", "shell", "su", "-c", "mount -o rw,remount /data"]
            try:
                result = subprocess.run(cmd, capture_output=True, text=True, check=True)
                log.append("Successfully remounted /data as read/write.")
                print(f"--- Remount Success Log ---\n{result.stdout}")
            except subprocess.CalledProcessError as e:
                log.append(f"Failed to remount /data. Error: {e.stderr}")
                # This is expected if the attacker used advanced security mechanisms (e.g., kernel-level locks)
            except FileNotFoundError:
                 log.append("ERROR: ADB command not found. Is the device connected and authorized?")

        # 2. Cleaning /data/adb directories/files
        cleanup_dirs = [
            "/data/adb/modules", 
            "/data/adb/magisk*", 
            "/data/adb/post-fs-data.d/", 
            "/data/adb/service.d/", 
            "/data/adb/magisk.db"
        ]
        
        print("\nCleaning /data/adb directories...")

        for directory in cleanup_dirs:
            # Use 'rm -rf' via su -c for deletion, as we assume root context is active or required.
            cmd = ["adb", "shell", "su", "-c", f"rm -rf {directory}"]
            try:
                result = subprocess.run(cmd, capture_output=True, text=True, check=False) # Check=False since deletion might fail safely
                if "No such file or directory" in result.stderr or "File not found" in result.stdout:
                    log.append(f"[SUCCESS] {directory}: Directory/file already clean or non-existent.")
                else:
                     log.append(f"[INFO] {directory} cleanup command run (Output logged if failure occurred).")
            except subprocess.CalledProcessError as e:
                 log.append(f"[WARNING] Failed to clean {directory}. Error: {e.stderr}")

        success = True # Assume success unless a critical ADB issue happens later

        return success, "\\n".join(log)
</final_file_content>

IMPORTANT: For any future changes to this file, use the final_file_content shown above as your reference. This content reflects the current state of the file, including any auto-formatting (e.g., if you used single quotes but the formatter converted them to double quotes). Always base your SEARCH/REPLACE operations on this final version to ensure accuracy.


<environment_details>
# Visual Studio Code Visible Files
SamsungMagiskCleaner/boot_image.py
SamsungMagiskCleaner/data_cleaner.py

# Visual Studio Code Open Tabs
SamsungMagiskCleaner/requirements.txt
SamsungMagiskCleaner/boot_image.py
SamsungMagiskCleaner/data_cleaner.py

# Current Time
6/15/2026, 1:56:49 AM (America/Los_Angeles, UTC-7:00)

# Context Window Usage
44,381 / 100K tokens used (45%)

# Current Mode
ACT MODE
</environment_details>