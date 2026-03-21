using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace WinTabSaver
{
    /// <summary>
    /// Core application context that manages the system tray icon, the context menu,
    /// the auto-save timer, and Windows shutdown interception.
    /// </summary>
    public sealed class TrayApplicationContext : ApplicationContext
    {
        // -- Win32 message constants --------------------------------------------
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION      = 0x0016;

        // -- Auto-save interval -------------------------------------------------
        private const int AutoSaveIntervalMs = 30_000; // 30 seconds

        // -- Fields -------------------------------------------------------------

        private readonly NotifyIcon                  _notifyIcon;
        private readonly System.Windows.Forms.Timer  _autoSaveTimer;
        private readonly MessageWindow               _msgWindow;

        /// <summary>
        /// The "Allow Duplicate Paths on Restore" toggle menu item.
        /// Stored as a field so its Checked state can be updated at runtime.
        /// </summary>
        private ToolStripMenuItem _itemAllowDuplicates = null!;

        /// <summary>
        /// The "Start Automatically with Windows" toggle menu item.
        /// Stored as a field so its Checked state can be refreshed on menu open.
        /// </summary>
        private ToolStripMenuItem _itemAutostart = null!;

        private bool _disposed;
        private bool _sessionSavedOnExit; // guard against saving twice on exit

        // -- Constructor --------------------------------------------------------

        public TrayApplicationContext()
        {
            // Load persisted settings from the session file before building the menu
            SessionManager.LoadSession();

            var contextMenu = BuildContextMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon             = IconFactory.CreateAppIcon(),
                Text             = "WinTabSaver – Explorer Session Manager",
                Visible          = true,
                ContextMenuStrip = contextMenu
            };

            // Double-click on the tray icon shows the current session view
            _notifyIcon.DoubleClick += (_, __) => ShowSessionView();

            // -- Auto-save timer ------------------------------------------------
            _autoSaveTimer = new System.Windows.Forms.Timer { Interval = AutoSaveIntervalMs };
            _autoSaveTimer.Tick += (_, __) => AutoSave();
            _autoSaveTimer.Start();

            // -- Hidden message window for shutdown interception ----------------
            _msgWindow = new MessageWindow(this);

            // -- Restore session on startup (background thread) -----------------
            ThreadPool.QueueUserWorkItem(_ => RestoreSessionAsync());

            Debug.WriteLine("[TrayApp] WinTabSaver started.");
        }

        // -- Context menu -------------------------------------------------------

        /// <summary>Builds and returns the system tray context menu.</summary>
        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // -- Header (non-clickable label) -----------------------------------
            var header = new ToolStripLabel("WinTabSaver")
            {
                Font      = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(70, 130, 180)
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // -- Show current Explorer windows ----------------------------------
            var itemView = new ToolStripMenuItem("Show Open Explorer Windows…");
            itemView.Click += (_, __) => ShowSessionView();
            menu.Items.Add(itemView);

            menu.Items.Add(new ToolStripSeparator());

            // -- Session save / restore -----------------------------------------
            var itemSave = new ToolStripMenuItem("Save Session Now");
            itemSave.Click += (_, __) => ManualSave();
            menu.Items.Add(itemSave);

            var itemRestore = new ToolStripMenuItem("Restore Session");
            itemRestore.Click += (_, __) => ManualRestore();
            menu.Items.Add(itemRestore);

            menu.Items.Add(new ToolStripSeparator());

            // -- Options --------------------------------------------------------
            // NOTE: CheckOnClick toggles item.Checked automatically on every click.
            // We subscribe to Click (not CheckedChanged) so the handler only fires
            // on a real user interaction and never during initialisation when we
            // assign Checked below (CheckedChanged would fire immediately there).
            _itemAllowDuplicates = new ToolStripMenuItem("Allow Duplicate Paths on Restore")
            {
                CheckOnClick = true,
                ToolTipText  =
                    "When enabled: already-open Explorer paths are opened again during restore.\n" +
                    "When disabled (default): existing paths are skipped to prevent duplicates."
            };

            // Set the initial visual state BEFORE attaching the handler.
            _itemAllowDuplicates.Checked = SessionManager.Settings.AllowDuplicatePaths;

            // Only now attach – all subsequent clicks will update the setting.
            _itemAllowDuplicates.Click += OnAllowDuplicatesChanged;
            menu.Items.Add(_itemAllowDuplicates);

            // -- Autostart ------------------------------------------------------
            // Same pattern: set Checked before attaching Click so initialisation
            // does not trigger the handler.
            _itemAutostart = new ToolStripMenuItem("Start Automatically with Windows")
            {
                CheckOnClick = true,
                ToolTipText  =
                    "When enabled: WinTabSaver is launched automatically at Windows logon\n" +
                    "via the current user registry Run key (no elevation required)."
            };
            _itemAutostart.Checked = AutostartManager.IsEnabled();
            _itemAutostart.Click  += OnAutostartChanged;
            menu.Items.Add(_itemAutostart);

            // Refresh the autostart checkmark every time the menu opens,
            // in case the user changed the Run key externally (e.g. Task Manager).
            menu.Opening += (_, __) =>
                _itemAutostart.Checked = AutostartManager.IsEnabled();

            menu.Items.Add(new ToolStripSeparator());

            // -- Exit -----------------------------------------------------------
            var itemExit = new ToolStripMenuItem("Exit");
            itemExit.Click += (_, __) => ExitApplication();
            menu.Items.Add(itemExit);

            return menu;
        }

        // -- Menu action handlers -----------------------------------------------

        /// <summary>Opens the session view dialog showing all current Explorer windows.</summary>
        private void ShowSessionView()
        {
            using var form = new SessionViewForm();
            form.ShowDialog();
        }

        /// <summary>Saves the current session and shows a balloon notification.</summary>
        private void ManualSave()
        {
            bool ok = SessionManager.SaveCurrentSession();
            _notifyIcon.ShowBalloonTip(
                timeout : 3000,
                tipTitle: ok ? "Session Saved" : "Save Failed",
                tipText : ok
                    ? $"Explorer session saved to:\n{SessionManager.SessionFilePath}"
                    : "Could not save the session. Check the application log.",
                tipIcon : ok ? ToolTipIcon.Info : ToolTipIcon.Error);
        }

        /// <summary>
        /// Loads and restores the saved session, respecting the duplicate-paths setting.
        /// Runs on a background thread to avoid blocking the UI.
        /// </summary>
        private void ManualRestore()
        {
            var session = SessionManager.LoadSession();
            if (session == null)
            {
                _notifyIcon.ShowBalloonTip(
                    3000, "No Session Found",
                    "No saved session file was found.",
                    ToolTipIcon.Warning);
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int count = SessionManager.RestoreSession(session);
                string detail = SessionManager.Settings.AllowDuplicatePaths
                    ? $"{count} path(s) opened (duplicate paths allowed)."
                    : $"{count} path(s) opened. Already-open paths were skipped.";
                _notifyIcon.ShowBalloonTip(3000, "Session Restored", detail, ToolTipIcon.Info);
            });
        }

        /// <summary>Auto-save invoked by the timer tick.</summary>
        private void AutoSave()
        {
            bool ok = SessionManager.SaveCurrentSession();
            Debug.WriteLine($"[TrayApp] Auto-save at {DateTime.Now:HH:mm:ss} – ok: {ok}");
        }

        /// <summary>
        /// Restores the session on application startup (background thread).
        /// </summary>
        private void RestoreSessionAsync()
        {
            try
            {
                // Wait briefly so Explorer is fully initialised before enumeration
                Thread.Sleep(1500);

                var session = SessionManager.LoadSession();
                if (session == null) return;

                int count = SessionManager.RestoreSession(session);
                Debug.WriteLine($"[TrayApp] Startup restore: {count} path(s) opened.");

                if (count > 0)
                {
                    _notifyIcon.ShowBalloonTip(
                        4000, "Session Restored",
                        $"{count} Explorer path(s) restored from the last session.",
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayApp] RestoreSessionAsync error: {ex.Message}");
            }
        }

        // -- Options handler ----------------------------------------------------

        /// <summary>
        /// Fired when the "Allow Duplicate Paths on Restore" menu item is toggled.
        /// Updates the in-memory setting and immediately persists it to the session file.
        /// </summary>
        private void OnAllowDuplicatesChanged(object? sender, EventArgs e)
        {
            SessionManager.Settings.AllowDuplicatePaths = _itemAllowDuplicates.Checked;

            // Persist the changed setting immediately so it survives a restart
            // even if no further auto-save occurs before the next launch.
            SessionManager.SaveSettingsOnly();

            string state = _itemAllowDuplicates.Checked ? "enabled" : "disabled";
            Debug.WriteLine($"[TrayApp] AllowDuplicatePaths set to: {state}");

            _notifyIcon.ShowBalloonTip(
                2500,
                "Setting Updated",
                $"Allow Duplicate Paths on Restore: {state}.",
                ToolTipIcon.Info);
        }

        /// <summary>
        /// Fired when the "Start Automatically with Windows" menu item is clicked.
        /// Writes or removes the registry Run entry accordingly.
        /// </summary>
        private void OnAutostartChanged(object? sender, EventArgs e)
        {
            bool nowEnabled = _itemAutostart.Checked;   // CheckOnClick already toggled it
            bool ok = nowEnabled ? AutostartManager.Enable() : AutostartManager.Disable();

            if (!ok)
            {
                // Roll back the visual state if the registry operation failed
                _itemAutostart.Checked = !nowEnabled;
                _notifyIcon.ShowBalloonTip(
                    3000, "Autostart Error",
                    "Could not update the Windows autostart entry. Check application permissions.",
                    ToolTipIcon.Error);
                return;
            }

            string state = nowEnabled ? "enabled" : "disabled";
            Debug.WriteLine($"[TrayApp] Autostart set to: {state}");
            _notifyIcon.ShowBalloonTip(
                2500, "Autostart Updated",
                nowEnabled
                    ? "WinTabSaver will now start automatically at Windows logon."
                    : "WinTabSaver will no longer start automatically at Windows logon.",
                ToolTipIcon.Info);
        }

        // -- Shutdown / exit handling -------------------------------------------

        /// <summary>
        /// Called by the hidden message window when Windows sends WM_QUERYENDSESSION.
        /// Saves the session before the system shuts down.
        /// </summary>
        internal void OnWindowsShutdown()
        {
            SaveOnExit("Windows shutdown detected");
        }

        /// <summary>Saves the session and terminates the application.</summary>
        private void ExitApplication()
        {
            SaveOnExit("Manual exit");
            _autoSaveTimer.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        /// <summary>
        /// Single-shot save guard: ensures the session is written exactly once
        /// regardless of which exit path is taken.
        /// </summary>
        private void SaveOnExit(string reason)
        {
            if (_sessionSavedOnExit) return;
            _sessionSavedOnExit = true;

            Debug.WriteLine($"[TrayApp] Saving session on exit – reason: {reason}");
            SessionManager.SaveCurrentSession();
        }

        // -- IDisposable --------------------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                SaveOnExit("Dispose");
                _autoSaveTimer.Dispose();
                _notifyIcon.Dispose();
                _msgWindow.Dispose();
            }
            base.Dispose(disposing);
        }

        // -- Inner class: WM_QUERYENDSESSION / WM_ENDSESSION receiver ----------

        /// <summary>
        /// Invisible Win32 window that receives Windows shutdown messages and
        /// forwards them to <see cref="TrayApplicationContext"/>.
        /// </summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private readonly TrayApplicationContext _owner;
            private bool _disposed;

            public MessageWindow(TrayApplicationContext owner)
            {
                _owner = owner;
                var cp = new CreateParams
                {
                    Caption   = "WinTabSaverMessageWindow",
                    ClassName = null,
                    Style     = 0
                };
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_QUERYENDSESSION:
                        _owner.OnWindowsShutdown();
                        m.Result = new IntPtr(1); // TRUE = allow shutdown to proceed
                        return;

                    case WM_ENDSESSION:
                        if (m.WParam != IntPtr.Zero)
                            _owner.OnWindowsShutdown();
                        return;
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    if (Handle != IntPtr.Zero) DestroyHandle();
                }
            }
        }
    }
}
