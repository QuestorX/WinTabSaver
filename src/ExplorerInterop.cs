using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinTabSaver
{
    /// <summary>
    /// Reads currently open Windows Explorer windows and their tab paths.
    ///
    /// Strategy (applied in order, first success wins per window):
    ///   1. Shell.Application COM enumeration  – works on Win10 + Win11
    ///   2. Win32 EnumWindows fallback          – catches windows the COM layer misses
    ///      on Windows 10 (e.g. when explorer.exe runs in a different integrity level
    ///      or after a COM registration hiccup).
    ///
    /// Name-filter note:
    ///   item.Name returns the *localised* application name.  On Windows 10 this is
    ///   "Windows Explorer"; on Windows 11 it may be "File Explorer" or the
    ///   localised equivalent.  We therefore fall back to checking the process name
    ///   ("explorer") and window class ("CabinetWClass") instead of relying solely
    ///   on the display name string.
    /// </summary>
    public static class ExplorerInterop
    {
        // -- Win32 P/Invoke -----------------------------------------------------

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Top-level window class used by all File / Windows Explorer windows.</summary>
        private const string ExplorerWindowClass = "CabinetWClass";

        // -- Public API ---------------------------------------------------------

        /// <summary>
        /// Captures all currently open Explorer windows and their open paths.
        /// </summary>
        public static ExplorerSession CaptureCurrentSession()
        {
            var session = new ExplorerSession { SavedAt = DateTime.UtcNow };

            // Step 1: enumerate via Shell.Application COM (carries path info)
            var comWindows = EnumerateViaShellCom();

            // Step 2: collect all genuine Explorer HWNDs via Win32
            var explorerHwnds = GetAllExplorerHwnds();

            // Merge: use COM results first (they have path data), then add any
            // HWNDs the COM layer missed and try to resolve their path separately.
            var seenHwnds = new HashSet<IntPtr>();
            foreach (var kv in comWindows)
            {
                seenHwnds.Add(kv.Key);
                if (kv.Value.Tabs.Count > 0)
                    session.Windows.Add(kv.Value);
            }

            foreach (IntPtr hwnd in explorerHwnds)
            {
                if (seenHwnds.Contains(hwnd)) continue;

                var info = new ExplorerWindowInfo();
                CaptureWindowGeometry(hwnd, info);

                string? path = TryGetPathForHwnd(hwnd);
                if (!string.IsNullOrEmpty(path))
                    info.Tabs.Add(path!);

                if (info.Tabs.Count > 0)
                    session.Windows.Add(info);
            }

            Debug.WriteLine($"[ExplorerInterop] Captured {session.Windows.Count} window(s).");
            return session;
        }

        /// <summary>
        /// Returns all folder paths currently open in any Explorer window.
        /// </summary>
        public static HashSet<string> GetCurrentlyOpenPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in EnumerateViaShellCom())
                foreach (string p in kv.Value.Tabs)
                    paths.Add(p);

            // Also scan HWNDs the COM layer might have missed
            foreach (IntPtr hwnd in GetAllExplorerHwnds())
            {
                string? p = TryGetPathForHwnd(hwnd);
                if (!string.IsNullOrEmpty(p))
                    paths.Add(p!);
            }

            return paths;
        }

        // -- COM enumeration ----------------------------------------------------

        /// <summary>
        /// Enumerates open Explorer windows via Shell.Application COM.
        /// Returns a dictionary keyed by top-level HWND.
        /// </summary>
        private static Dictionary<IntPtr, ExplorerWindowInfo> EnumerateViaShellCom()
        {
            var result = new Dictionary<IntPtr, ExplorerWindowInfo>();

            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return result;

                object? shellObj = Activator.CreateInstance(shellType);
                if (shellObj == null) return result;

                dynamic shell = shellObj;
                try
                {
                    dynamic windows = shell.Windows();
                    int count = (int)windows.Count;
                    Debug.WriteLine($"[ExplorerInterop] Shell.Windows() count = {count}");

                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic? item = windows.Item(i);
                            if (item == null)
                            {
                                Debug.WriteLine($"[ExplorerInterop] Item {i} is null, skipping.");
                                continue;
                            }

                            // Resolve HWND first – validate via Win32 class name
                            // instead of trusting item.Name (which is localised and
                            // unreliable on Windows 10).
                            IntPtr hwnd = ResolveHwnd(item);
                            if (hwnd == IntPtr.Zero)
                            {
                                Debug.WriteLine($"[ExplorerInterop] Item {i}: HWND is zero, skipping.");
                                continue;
                            }

                            if (!IsExplorerHwnd(hwnd))
                            {
                                Debug.WriteLine($"[ExplorerInterop] Item {i}: HWND {hwnd} is not an Explorer window, skipping.");
                                continue;
                            }

                            string? path = GetLocationPath(item);
                            Debug.WriteLine($"[ExplorerInterop] Item {i}: HWND={hwnd}, path={path ?? "(null)"}");

                            if (string.IsNullOrEmpty(path)) continue;

                            if (!result.TryGetValue(hwnd, out ExplorerWindowInfo? info))
                            {
                                info = new ExplorerWindowInfo();
                                CaptureWindowGeometry(hwnd, info);
                                result[hwnd] = info;
                            }

                            if (!info.Tabs.Contains(path!))
                                info.Tabs.Add(path!);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ExplorerInterop] COM item {i} error: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExplorerInterop] EnumerateViaShellCom error: {ex.Message}");
            }

            return result;
        }

        // -- Win32 window enumeration fallback ----------------------------------

        /// <summary>
        /// Returns the HWNDs of all visible top-level Explorer (CabinetWClass) windows.
        /// </summary>
        private static List<IntPtr> GetAllExplorerHwnds()
        {
            var list = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd) && IsExplorerHwnd(hWnd))
                    list.Add(hWnd);
                return true; // continue enumeration
            }, IntPtr.Zero);
            Debug.WriteLine($"[ExplorerInterop] EnumWindows found {list.Count} Explorer HWND(s).");
            return list;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="hwnd"/> belongs to explorer.exe
        /// and has window class "CabinetWClass".
        /// </summary>
        private static bool IsExplorerHwnd(IntPtr hwnd)
        {
            // Check window class name
            var sb = new StringBuilder(256);
            if (GetClassName(hwnd, sb, sb.Capacity) == 0) return false;
            if (!sb.ToString().Equals(ExplorerWindowClass, StringComparison.OrdinalIgnoreCase))
                return false;

            // Check that the owning process is explorer.exe
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // -- Path resolution helpers --------------------------------------------

        /// <summary>
        /// Extracts the file-system path from a COM shell window item.
        /// Tries multiple properties so at least one works on every Windows version.
        /// </summary>
        private static string? GetLocationPath(dynamic item)
        {
            // Method 1: LocationURL  (file:///C:/path/to/folder)
            try
            {
                string? url = item.LocationURL as string;
                if (!string.IsNullOrWhiteSpace(url) &&
                    Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                {
                    string local = Uri.UnescapeDataString(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(local))
                        return NormalisePath(local);
                }
            }
            catch { /* fall through */ }

            // Method 2: Document.Folder.Self.Path
            try
            {
                string? path = item.Document?.Folder?.Self?.Path as string;
                if (!string.IsNullOrWhiteSpace(path))
                    return NormalisePath(path);
            }
            catch { /* fall through */ }

            // Method 3: Document.Folder.Self.Path via explicit cast chain
            try
            {
                object doc    = item.Document;
                object folder = ((dynamic)doc).Folder;
                object self   = ((dynamic)folder).Self;
                string? path  = ((dynamic)self).Path as string;
                if (!string.IsNullOrWhiteSpace(path))
                    return NormalisePath(path);
            }
            catch { /* fall through */ }

            return null;
        }

        /// <summary>
        /// Searches the Shell.Application window list for an item whose HWND matches
        /// <paramref name="targetHwnd"/> and returns its path.
        /// Used as a second-pass lookup for HWNDs found via EnumWindows.
        /// </summary>
        private static string? TryGetPathForHwnd(IntPtr targetHwnd)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                object? shellObj = Activator.CreateInstance(shellType);
                if (shellObj == null) return null;

                dynamic shell = shellObj;
                try
                {
                    dynamic windows = shell.Windows();
                    int count = (int)windows.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic? item = windows.Item(i);
                            if (item == null) continue;
                            if (ResolveHwnd(item) != targetHwnd) continue;
                            return GetLocationPath(item);
                        }
                        catch { /* skip */ }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>
        /// Safely reads item.HWND, handling both int and long returns from the COM
        /// dispatch layer (varies between Windows versions and process bitness).
        /// </summary>
        private static IntPtr ResolveHwnd(dynamic item)
        {
            try
            {
                object raw = item.HWND;
                return raw switch
                {
                    int    i => new IntPtr(i),
                    long   l => new IntPtr(l),
                    uint   u => new IntPtr((long)u),
                    _        => new IntPtr(Convert.ToInt64(raw))
                };
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Trims trailing backslashes and whitespace from a path.</summary>
        private static string NormalisePath(string path) =>
            path.TrimEnd('\\', '/').Trim();

        // -- Window geometry ----------------------------------------------------

        private static void CaptureWindowGeometry(IntPtr hwnd, ExplorerWindowInfo info)
        {
            try
            {
                if (GetWindowRect(hwnd, out RECT rect))
                {
                    info.Left   = rect.Left;
                    info.Top    = rect.Top;
                    info.Width  = rect.Right - rect.Left;
                    info.Height = rect.Bottom - rect.Top;
                }
                info.IsMaximized = IsZoomed(hwnd);
            }
            catch { /* geometry is optional */ }
        }

        // -- Open new Explorer window -------------------------------------------

        /// <summary>Opens a new Explorer window at <paramref name="path"/>.</summary>
        public static void OpenExplorerWindow(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExplorerInterop] OpenExplorerWindow error: {ex.Message}");
            }
        }
    }
}
