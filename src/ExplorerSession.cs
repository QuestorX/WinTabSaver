using System;
using System.Collections.Generic;

namespace WinTabSaver
{
    /// <summary>
    /// Persisted application settings stored alongside the session data.
    /// All options are saved in session.json so a single file contains both
    /// the Explorer state and the user's preferences.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// When <c>true</c>, the same directory path may be opened multiple times
        /// during a restore even if it is already visible in an Explorer window.
        /// Default: <c>false</c> (duplicate paths are skipped).
        /// </summary>
        public bool AllowDuplicatePaths { get; set; } = false;
    }

    /// <summary>
    /// Represents a saved session of all open Windows Explorer windows and their tabs,
    /// together with the application settings that were active at save time.
    /// </summary>
    [Serializable]
    public class ExplorerSession
    {
        /// <summary>The UTC timestamp when this session was saved.</summary>
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Application settings persisted with the session.
        /// Loaded on startup so preferences survive application restarts.
        /// </summary>
        public AppSettings Settings { get; set; } = new AppSettings();

        /// <summary>List of all Explorer windows captured in this session.</summary>
        public List<ExplorerWindowInfo> Windows { get; set; } = new List<ExplorerWindowInfo>();
    }

    /// <summary>
    /// Represents a single Explorer window with its open tabs/paths.
    /// </summary>
    [Serializable]
    public class ExplorerWindowInfo
    {
        /// <summary>
        /// Ordered list of folder paths open as tabs in this window.
        /// The first entry is the active (focused) tab.
        /// </summary>
        public List<string> Tabs { get; set; } = new List<string>();

        /// <summary>Window position: left edge in screen pixels.</summary>
        public int Left { get; set; }

        /// <summary>Window position: top edge in screen pixels.</summary>
        public int Top { get; set; }

        /// <summary>Window width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>Window height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>Whether this window was maximized.</summary>
        public bool IsMaximized { get; set; }
    }
}
