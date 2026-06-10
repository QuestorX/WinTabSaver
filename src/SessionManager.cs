using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace WinTabSaver
{
    /// <summary>
    /// Handles serialization and deserialization of <see cref="ExplorerSession"/>
    /// objects to/from a JSON file stored in the user's application-data folder.
    /// Also manages the in-memory <see cref="AppSettings"/> instance that is
    /// persisted as part of every session save.
    /// </summary>
    public static class SessionManager
    {
        // -- Configuration ------------------------------------------------------

        /// <summary>Sub-folder name inside %APPDATA%.</summary>
        private const string AppFolderName = "WinTabSaver";

        /// <summary>Session file name.</summary>
        private const string SessionFileName = "session.json";

        /// <summary>Full path to the session file.</summary>
        public static string SessionFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            SessionFileName);

        // -- In-memory settings (loaded from file on first access) --------------

        /// <summary>
        /// The active application settings. Initialised with defaults; overwritten
        /// when a session file is loaded. Persisted on every session save.
        /// </summary>
        public static AppSettings Settings { get; private set; } = new AppSettings();

        // -- JSON options -------------------------------------------------------

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Only escape characters strictly required by the JSON spec (" and \).
            // JavaScriptEncoder.Create(UnicodeRanges.All) still escapes HTML-unsafe
            // characters such as & -> \u0026, which corrupts paths like "One & Two".
            // UnsafeRelaxedJsonEscaping writes all characters as-is except " and \,
            // so Umlauts, &, and other non-ASCII chars are stored correctly.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // -- Public API ---------------------------------------------------------

        /// <summary>
        /// Captures the current Explorer state together with the current
        /// <see cref="Settings"/> and writes everything to the session file.
        /// </summary>
        /// <returns><c>true</c> if saved successfully.</returns>
        public static bool SaveCurrentSession()
        {
            try
            {
                ExplorerSession session = ExplorerInterop.CaptureCurrentSession();

                // Embed the current settings so they survive a restart
                session.Settings = Settings;

                return WriteSession(session);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] SaveCurrentSession error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes a pre-built <see cref="ExplorerSession"/> to disk atomically.
        /// </summary>
        public static bool WriteSession(ExplorerSession session)
        {
            try
            {
                string dir = Path.GetDirectoryName(SessionFilePath)!;
                Directory.CreateDirectory(dir);

                string json     = JsonSerializer.Serialize(session, JsonOptions);
                string tempPath = SessionFilePath + ".tmp";

                File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                File.Move(tempPath, SessionFilePath, overwrite: true);

                Debug.WriteLine($"[SessionManager] Session saved: {session.Windows.Count} window(s).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] WriteSession error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads the session file from disk and restores <see cref="Settings"/>
        /// from the embedded settings block.
        /// </summary>
        /// <returns>The deserialized session, or <c>null</c> if unavailable.</returns>
        public static ExplorerSession? LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                {
                    Debug.WriteLine("[SessionManager] No session file found.");
                    return null;
                }

                string json    = File.ReadAllText(SessionFilePath, System.Text.Encoding.UTF8);
                var    session = JsonSerializer.Deserialize<ExplorerSession>(json, JsonOptions);

                if (session != null)
                {
                    // Restore persisted settings into the in-memory singleton
                    Settings = session.Settings ?? new AppSettings();
                    Debug.WriteLine($"[SessionManager] Settings loaded – AllowDuplicatePaths: {Settings.AllowDuplicatePaths}");
                }

                Debug.WriteLine($"[SessionManager] Session loaded: {session?.Windows.Count ?? 0} window(s).");
                return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] LoadSession error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Persists only the current <see cref="Settings"/> without changing the
        /// saved Explorer window list and without overwriting the in-memory
        /// <see cref="Settings"/> singleton (unlike <see cref="LoadSession"/>).
        /// </summary>
        public static void SaveSettingsOnly()
        {
            try
            {
                // Read the raw JSON to preserve the existing window list, then patch
                // only the Settings block. We deliberately do NOT call LoadSession()
                // here because that would overwrite the in-memory Settings singleton
                // with the old on-disk value – undoing the change the user just made.
                ExplorerSession session = LoadSessionRaw() ?? new ExplorerSession();
                session.Settings = Settings;
                WriteSession(session);
                Debug.WriteLine($"[SessionManager] Settings saved – AllowDuplicatePaths: {Settings.AllowDuplicatePaths}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] SaveSettingsOnly error: {ex.Message}");
            }
        }

        /// <summary>
        /// Deserializes the session file from disk without touching the in-memory
        /// <see cref="Settings"/> singleton. Used internally by
        /// <see cref="SaveSettingsOnly"/> to avoid clobbering a just-changed setting.
        /// </summary>
        private static ExplorerSession? LoadSessionRaw()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return null;
                string json = File.ReadAllText(SessionFilePath, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize<ExplorerSession>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] LoadSessionRaw error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Restores a saved session window by window, preserving tab grouping.
        ///
        /// For each saved window:
        ///   - The first tab is opened as a new Explorer window.
        ///   - Additional tabs are injected via keyboard automation (Ctrl+T + Alt+D)
        ///     so they appear as real tabs in the same window (Windows 11 22H2+).
        ///   - On Windows 10 (no tab support) each path opens as a separate window.
        ///
        /// Whether already-open paths are skipped is controlled by
        /// <see cref="AppSettings.AllowDuplicatePaths"/>.
        /// </summary>
        /// <param name="session">The session to restore.</param>
        /// <returns>Total number of new Explorer paths opened.</returns>
        public static int RestoreSession(ExplorerSession session)
        {
            int opened = 0;

            try
            {
                // Collect currently open paths for duplicate filtering
                var alreadyOpen = ExplorerInterop.GetCurrentlyOpenPaths();

                foreach (var window in session.Windows)
                {
                    // Build a filtered copy of this window's tabs, honouring the
                    // duplicate-paths setting and removing missing paths.
                    var windowToRestore = new ExplorerWindowInfo
                    {
                        Left        = window.Left,
                        Top         = window.Top,
                        Width       = window.Width,
                        Height      = window.Height,
                        IsMaximized = window.IsMaximized
                    };

                    foreach (string tabPath in window.Tabs)
                    {
                        if (!System.IO.Directory.Exists(tabPath))
                        {
                            Debug.WriteLine($"[SessionManager] Skipping missing path: {tabPath}");
                            continue;
                        }

                        // Honour the duplicate-paths setting:
                        // – AllowDuplicatePaths = false  →  skip paths already open
                        // – AllowDuplicatePaths = true   →  open regardless
                        if (!Settings.AllowDuplicatePaths && alreadyOpen.Contains(tabPath))
                        {
                            Debug.WriteLine($"[SessionManager] Path already open, skipping: {tabPath}");
                            continue;
                        }

                        windowToRestore.Tabs.Add(tabPath);
                    }

                    if (windowToRestore.Tabs.Count == 0) continue;

                    // Restore this window, injecting tabs where supported
                    int count = ExplorerInterop.RestoreWindow(windowToRestore, alreadyOpen);
                    opened += count;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SessionManager] RestoreSession error: {ex.Message}");
            }

            return opened;
        }
    }
}
