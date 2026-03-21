#Requires -Version 5.1
<#
.SYNOPSIS
    Builds WinTabSaver and optionally installs it to the Windows Startup folder.

.DESCRIPTION
    1. Verifies that the .NET 8 SDK is installed.
    2. Runs "dotnet publish" in Release mode.
    3. Optionally creates a Windows Startup shortcut so the tray app runs at logon.
       Note: the shortcut is only a fallback. The preferred way to enable autostart
       is via the "Start Automatically with Windows" option in the tray context menu,
       which uses the registry Run key and requires no elevation.

.PARAMETER Install
    If specified, a shortcut is created in the current user Startup folder.

.EXAMPLE
    .\Build-And-Install.ps1
    .\Build-And-Install.ps1 -Install
#>

param(
    [switch]$Install
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Resolve the project root reliably regardless of how the script was invoked.
# $MyInvocation.MyCommand.Path can be empty when called via
# "powershell -File ..." from cmd.exe, so we fall back to $PSScriptRoot
# (available in PowerShell 3+) and then to the current directory.
# ---------------------------------------------------------------------------
if ($PSScriptRoot -and (Test-Path $PSScriptRoot)) {
    $projectDir = $PSScriptRoot
}
elseif ($MyInvocation.MyCommand.Path) {
    $projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
else {
    $projectDir = (Get-Location).Path
}

Write-Host "Project directory: $projectDir" -ForegroundColor DarkGray

$csproj = Join-Path $projectDir "src\WinTabSaver.csproj"

if (-not (Test-Path $csproj)) {
    Write-Error ("WinTabSaver.csproj not found in: $projectDir`n" +
                 "Make sure you run this script from the WinTabSaver project root.")
    exit 1
}

# ---------------------------------------------------------------------------
# Verify .NET SDK
# ---------------------------------------------------------------------------
try {
    $dotnetVersion = & dotnet --version 2>&1
    Write-Host ".NET SDK: $dotnetVersion" -ForegroundColor Cyan
}
catch {
    Write-Error ".NET SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

# ---------------------------------------------------------------------------
# Build / Publish
# ---------------------------------------------------------------------------
$publishDir = Join-Path $projectDir "publish"

Write-Host ""
Write-Host "Publishing WinTabSaver..." -ForegroundColor Yellow

& dotnet publish $csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $publishDir `
    /p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$exePath = Join-Path $publishDir "WinTabSaver.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Expected output not found: $exePath"
    exit 1
}

Write-Host ""
Write-Host "Build succeeded: $exePath" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Optional: create Startup-folder shortcut (-Install switch)
# ---------------------------------------------------------------------------
if ($Install) {
    $startupFolder = [System.IO.Path]::Combine(
        $env:APPDATA,
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup")

    $shortcutPath = Join-Path $startupFolder "WinTabSaver.lnk"

    $shell    = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath       = $exePath
    $shortcut.WorkingDirectory = $publishDir
    $shortcut.Description      = "WinTabSaver - Explorer Session Manager"
    $shortcut.Save()

    Write-Host "Startup shortcut created: $shortcutPath" -ForegroundColor Green
    Write-Host "WinTabSaver will now launch automatically at Windows logon."
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Done. Run the application with:" -ForegroundColor Cyan
Write-Host "    $exePath"
