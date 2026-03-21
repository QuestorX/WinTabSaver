using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace WinTabSaver
{
    /// <summary>
    /// Manages the Windows auto-start behaviour of WinTabSaver via the per-user
    /// registry run key:
    ///   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
    ///
    /// Using the registry (instead of a Startup-folder shortcut) has two advantages:
    ///   1. No file-system artefacts that break when the EXE is moved.
    ///   2. The entry is visible in Task Manager → Startup apps so users can review it.
    ///
    /// Only the current user is affected; no elevation is required.
    /// </summary>
    public static class AutostartManager
    {
        // Registry key path for per-user auto-start entries
        private const string RunKeyPath  = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Value name under which WinTabSaver registers itself
        private const string ValueName   = "WinTabSaver";

        // -- Public API ---------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> when a valid WinTabSaver auto-start entry exists in
        /// the current user's registry Run key.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key == null) return false;

                string? value = key.GetValue(ValueName) as string;

                // Treat the entry as valid only when it points to the current EXE.
                // This handles the case where the user moved the EXE after enabling.
                string exePath = GetExePath();
                return string.Equals(value, QuotePath(exePath),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutostartManager] IsEnabled error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enables auto-start by writing the current EXE path to the registry Run key.
        /// </summary>
        /// <returns><c>true</c> on success.</returns>
        public static bool Enable()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null)
                {
                    Debug.WriteLine("[AutostartManager] Enable: Run key not found.");
                    return false;
                }

                string value = QuotePath(GetExePath());
                key.SetValue(ValueName, value, RegistryValueKind.String);
                Debug.WriteLine($"[AutostartManager] Autostart enabled: {value}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutostartManager] Enable error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disables auto-start by removing the registry Run entry.
        /// Succeeds silently when no entry exists.
        /// </summary>
        /// <returns><c>true</c> on success.</returns>
        public static bool Disable()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return true; // nothing to remove

                // DeleteValue with throwOnMissingValue = false is safe even when the
                // entry does not exist.
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Debug.WriteLine("[AutostartManager] Autostart disabled.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutostartManager] Disable error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Toggles the auto-start state and returns the new state
        /// (<c>true</c> = enabled).
        /// </summary>
        public static bool Toggle()
        {
            if (IsEnabled())
            {
                Disable();
                return false;
            }
            else
            {
                Enable();
                return true;
            }
        }

        // -- Helpers ------------------------------------------------------------

        /// <summary>
        /// Returns the full path of the running EXE.
        /// Falls back to the assembly location when the process path is unavailable.
        /// </summary>
        private static string GetExePath()
        {
            // Process.GetCurrentProcess().MainModule?.FileName is the most reliable
            // source; it resolves the actual EXE even for single-file published builds.
            string? path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path)) return path!;

            // Fallback for environments where MainModule is restricted
            return Assembly.GetExecutingAssembly().Location;
        }

        /// <summary>Wraps a path in double quotes if it contains spaces.</summary>
        private static string QuotePath(string path) =>
            path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
