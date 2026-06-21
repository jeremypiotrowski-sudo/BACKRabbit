import dearpygui.dearpygui as dpg
import subprocess
from .main import AdbClient # Import the mock client for GUI testing

class CleanerGUI:
    """
    DearPyGui interface for guiding the user through the uninstallation process (L1, L2, L3).
    This class handles all user interaction and orchestrates the pipeline.
    """

    def __init__(self, adb_client: AdbClient):
        self.adb = adb_client
        # State tracking for progress
        self.pipeline_active = False

    def build_gui(self):
        """Sets up all GUI elements."""
        dpg.create_context()
        with dpg.window(label="Magisk Cleaner", width=800, height=600):
            # Status Label
            dpg.add_text("Device: N/A | Status: Ready to analyze.")

            # Device Connection/Detection Section (Level 1 Prep)
            with dpg.group(label="[STEP 1] Check Device & Prepare"):
                self.device_status = dpg.add_text("Status:", tag='dev_status')
                dpg.add_spacer(height=10)
                # Initial detection button/action
                dpg.add_button(label="Connect / Detect (ADB)", callback=self.detect_and_start)

            # Execution Logs and Progress
            with dpg.group(horizontal=True):
                dpg.add_text("Progress Log:")
                self.log_text = dpg.add_text("", multiline=True, width=-1, height=200)
                
            # Run Button (Main Execution Trigger)
            with dpg.group(label="[STEP 2] Execute Cleanup"):
                dpg.add_separator()
                self.run_button = dpg.add_button(label="RUN FULL CLEANUP PIPELINE", callback=self.execute_cleanup)

        # Setup a simple clean exit handler
        dpg.set_primary_window(dpg.last_item(), True)


    def set_log_message(self, message: str):
        """Updates the main log display."""
        dpg.set_value(self.log_text, message + "\n--- LOG UPDATED ---\n")

    def detect_and_start(self):
        """Simulates ADB connection and basic device checks."""
        self.set_log_message("Running initial ADB diagnostic check...")
        # In a real app: Execute adb devices | adb shell getprop ro.product.model
        success, log = self.adb.run_command(["shell", "getprop", "ro.product.model"])
        self.set_log_message(f"[DIAGNOSTICS] Device check complete. Simulation successful: {success}. Details:\n{log}")

    def execute_cleanup(self):
        """Triggers the full sequence of operations."""
        if self.pipeline_active:
            print("Pipeline already running.")
            return
        
        self.pipeline_active = True
        dpg.set_item_color(self.run_button, (200, 50, 0)) # Disable/Change color
        self.set_log_message("\n*** Initiating Core Cleanup Pipeline ***\nPlease wait for the system to complete all steps...")

        # EXECUTION FLOW: This calls the main orchestration function and handles output printing.
        success, log = run_full_cleaner_pipeline(self.adb)
        
        if success:
            dpg.set_item_color(self.run_button, (0, 150, 0)) # Green for success
            self.set_log_message("\n\n=============================================")
            self.set_log_message("             SUCCESSFUL CLEANUP COMPLETE         ")
            self.set_log_message("=============================================\n" + log)
        else:
            dpg.set_item_color(self.run_button, (150, 0, 0)) # Red for failure
            self.set_log_message("\n\n=============================================")
            self.set_log_message("             UNINSTALLATION FAILED FAILURE        ")
            self.set_log_message("=============================================\n" + log)

    def show(self):
        """Runs the GUI loop."""
        dpg.create_viewport(title='Magisk Cleaner', width=800, height=650)
        dpg.setup_dearpygui()
        self.build_gui()
        dpg.show_viewport()
        dpg.start_dearpygui()

# Placeholder for the main pipeline runner to integrate with GUI flow
def run_full_cleaner_pipeline(adb_client) -> Tuple[bool, str]:
    """A wrapper that calls the logic from main.py and keeps it self-contained."""
    from .main import run_full_cleaner_pipeline as _run_logic # Avoid circular dependency
    return _run_logic(adb_client)

if __name__ == "__main__":
    # Due to cyclical imports, we execute the main logic directly in this file for initial testing.
    gui = CleanerGUI(AdbClient())
    gui.build_gui()
    gui.show()
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
SamsungMagiskCleaner/ramdisk_editor.py
SamsungMagiskCleaner/main.py
SamsungMagiskCleaner/gui.py

# Visual Studio Code Open Tabs
SamsungMagiskCleaner/requirements.txt
SamsungMagiskCleaner/boot_image.py
SamsungMagiskCleaner/data_cleaner.py
SamsungMagiskCleaner/samsung_kernel_patcher.py
SamsungMagiskCleaner/fstab_restorer.py
SamsungMagiskCleaner/vbmeta_patcher.py
SamsungMagiskCleaner/selinux_helper.py
SamsungMagiskCleaner/ramdisk_editor.py
SamsungMagiskCleaner/main.py
SamsungMagiskCleaner/gui.py

# Current Time
6/15/2026, 2:02:29 AM (America/Los_Angeles, UTC-7:00)

# Context Window Usage
58,435 / 100K tokens used (59%)

# Current Mode
ACT MODE
</environment_details>