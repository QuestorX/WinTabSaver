# WinTabSaver

> **Reliable Windows Explorer session manager for your system tray.**  
> Automatically saves and restores all open Explorer windows and tabs – so you never lose your working context after a reboot.

---

## Why WinTabSaver?

Windows includes a built-in option to reopen Explorer windows after a restart
(*Folder Options → General → "Restore previous folder windows at sign-in"*).
In practice this feature has been **unreliable across multiple Windows versions**:

- It regularly fails to restore windows after a forced reboot or Windows Update.
- It does not save or restore **tabs** (introduced in Windows 11 22H2).
- It provides no visibility into what will be restored, no manual trigger, and no
  protection against shutdown-without-warning scenarios.

**WinTabSaver** solves all of these problems with a lightweight tray application
that gives you full, transparent control over your Explorer sessions.

---

## Features

| # | Feature | Detail |
|---|---|---|
| 1 | **Auto-Save** | Session saved automatically every **30 seconds** |
| 2 | **Save on Exit** | Session saved when the app exits gracefully |
| 3 | **Shutdown Interception** | `WM_QUERYENDSESSION` / `WM_ENDSESSION` are caught so the session is written **before Windows powers off**, even on forced shutdowns |
| 4 | **Startup Restore** | Last saved session is restored automatically when WinTabSaver launches |
| 5 | **Duplicate Prevention** | Already-open paths are never re-opened during restore (configurable) |
| 6 | **Allow Duplicate Paths** | Toggle to allow opening the same path multiple times if needed |
| 7 | **Manual Save** | *Save Session Now* in the tray context menu |
| 8 | **Manual Restore** | *Restore Session* in the tray context menu |
| 9 | **Session Viewer** | *Show Open Explorer Windows* lists every open window and its tabs live |
| 10 | **Persistent Settings** | All preferences are stored in `session.json` and survive restarts |
| 11 | **Single Instance** | A named mutex prevents running more than one instance |
| 12 | **No External Dependencies** | No NuGet packages; icon is generated at runtime via GDI+ |

---

## Requirements

- **Windows 10 / 11** (64-bit)
- **.NET 8 Runtime** (Desktop) – download at <https://dotnet.microsoft.com/download/dotnet/8.0>
- .NET 8 **SDK** is required only if you want to build from source

---

## Installation

### Option A – Download a release

1. Go to the [Releases](../../releases) page and download `WinTabSaver.exe`.
2. Place it anywhere you like (e.g. `C:\Tools\WinTabSaver\`).
3. Run it – it will appear in the system tray immediately.

### Option B – Build from source

```cmd
git clone https://github.com/YOUR_USERNAME/WinTabSaver.git
cd WinTabSaver
```

Then run the build script. If your PowerShell execution policy blocks unsigned
scripts, use the `Bypass` form (no permanent system changes required):

```cmd
# Build only:
powershell -ExecutionPolicy Bypass -File ".\Build-And-Install.ps1"

# Build + create a Startup-folder shortcut:
powershell -ExecutionPolicy Bypass -File ".\Build-And-Install.ps1" -Install
```

If you have already adjusted your execution policy (`Set-ExecutionPolicy RemoteSigned`),
you can call the script directly from PowerShell:

```powershell
.\Build-And-Install.ps1          # build only
.\Build-And-Install.ps1 -Install # build + add to Windows Startup
```

Or build manually without the script:

```cmd
dotnet publish -c Release -r win-x64 --self-contained false -o publish\ /p:PublishSingleFile=true
```

### Auto-start with Windows (manual)

Create a shortcut to `WinTabSaver.exe` in your Startup folder:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
```

Press `Win + R`, type `shell:startup`, and drop the shortcut there.

---

## Usage

After launching, **WinTabSaver sits in the system tray** and requires no
interaction. Right-click the icon to access all features:

### Context Menu

| Menu Item | Action |
|---|---|
| **Show Open Explorer Windows…** | Live view of all open Explorer windows and their tabs |
| **Save Session Now** | Immediately save the current session; a balloon tip confirms the result |
| **Restore Session** | Open all saved paths that are not currently visible |
| **Allow Duplicate Paths on Restore** | ✔ Toggle – see *Settings* below |
| **Exit** | Save the session and quit |

> **Tip:** Double-clicking the tray icon also opens the session viewer.

---

## Settings

All settings are stored inside `session.json` alongside the Explorer window data.
They are loaded automatically on startup and survive application restarts.

### Allow Duplicate Paths on Restore

| State | Behaviour |
|---|---|
| **Off** *(default)* | During restore, paths that are already open in an Explorer window are **skipped**. This prevents clutter when WinTabSaver is used together with the native Windows restore feature. |
| **On** | Every path in the saved session is opened, even if a window for that path is already visible. Useful when you deliberately work with the same directory in multiple windows. |

Toggle this option from the tray context menu. The change takes effect immediately
and is persisted to disk right away.

> **Note:** Even with *Allow Duplicate Paths* **on**, a single restore pass will
> never open the exact same path **twice** – within one restore operation each
> path is only opened once.

---

## Session File

```
%APPDATA%\WinTabSaver\session.json
```

The file is human-readable JSON. Example:

```json
{
  "SavedAt": "2025-06-01T14:30:00Z",
  "Settings": {
    "AllowDuplicatePaths": false
  },
  "Windows": [
    {
      "Tabs": [
        "C:\\Users\\Alice\\Documents",
        "C:\\Projects\\MyApp"
      ],
      "Left": 100,
      "Top": 80,
      "Width": 1024,
      "Height": 768,
      "IsMaximized": false
    }
  ]
}
```

---

## Architecture

```
WinTabSaver/
├── Build-And-Install.ps1      PowerShell build & Startup-shortcut helper
├── README.md
└── src/
    ├── WinTabSaver.csproj         .NET 8 WinForms project file
    ├── Program.cs                 Entry point, single-instance mutex
    ├── TrayApplicationContext.cs  Tray icon, context menu, auto-save timer,
    │                              settings toggle, Windows shutdown interception
    ├── ExplorerInterop.cs         Shell.Application COM enumeration of open
    │                              Explorer windows and tabs; opens new windows
    ├── SessionManager.cs          JSON serialize/deserialize; in-memory settings
    │                              singleton; restore logic with duplicate filtering
    ├── SessionViewForm.cs         WinForms dialog – TreeView of the current session
    ├── IconFactory.cs             Programmatic GDI+ icon (folder + clock overlay)
    └── ExplorerSession.cs         Data model: ExplorerSession, ExplorerWindowInfo,
                                   AppSettings
```

---

## Notes on Tab Support

Windows 11 22H2 introduced native tabs in File Explorer. The Shell Automation
COM object (`Shell.Application`) enumerates **each tab as a separate shell
window** sharing the same top-level HWND. WinTabSaver groups them by HWND so
all tabs of one window are saved together.

On restore each path is opened as a new Explorer window, which works on all
supported Windows versions. True programmatic tab creation (injecting a path
directly as a new tab into an existing window) requires undocumented COM
interfaces that differ between Windows releases.

---

## Contributing

Pull requests and issues are welcome. Please open an issue before submitting a
large change so we can discuss the approach first.

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-improvement`
3. Commit your changes with clear messages
4. Open a pull request against `main`

---

## License

[MIT](LICENSE) – free to use, modify, and distribute.
