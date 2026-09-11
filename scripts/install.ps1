<#
.SYNOPSIS
    Installs PitWall.exe for the current user and adds it to the Start menu (and optionally the desktop).

.DESCRIPTION
    Copies the built executable to %LOCALAPPDATA%\Programs\PitWall and creates shortcuts.
    No administrator rights, no registry changes. Settings and the sealed F1 TV token stay in
    %LOCALAPPDATA%\PitWall and survive reinstalling.

.EXAMPLE
    ./scripts/install.ps1                 # Start menu shortcut
    ./scripts/install.ps1 -Desktop        # plus a desktop shortcut
    ./scripts/install.ps1 -Uninstall      # remove the program and shortcuts (keeps settings)
#>
[CmdletBinding()]
param(
    [string] $Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\PitWall.exe'),
    [switch] $Desktop,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PitWall'
$target = Join-Path $installDir 'PitWall.exe'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PitWall.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PitWall.lnk'

if (Get-Process PitWall -ErrorAction SilentlyContinue) {
    throw 'PitWall is running. Exit it from its tray icon first.'
}

if ($Uninstall) {
    foreach ($path in $startMenu, $desktopLink) {
        if (Test-Path $path) { Remove-Item $path -Confirm:$false }
    }
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Confirm:$false }
    Write-Host 'PitWall removed. Settings remain in' (Join-Path $env:LOCALAPPDATA 'PitWall') -ForegroundColor Green
    return
}

if (-not (Test-Path $Source)) {
    throw "No build found at $Source. Run build.cmd first."
}

New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item $Source $target -Force

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string] $Path) {
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $target
    $link.WorkingDirectory = $installDir
    $link.IconLocation = "$target,0"
    $link.Description = 'PitWall: Formula 1 race-weekend command centre'
    $link.Save()
}

New-Shortcut $startMenu
if ($Desktop) { New-Shortcut $desktopLink }

Write-Host "Installed $target" -ForegroundColor Green
Write-Host 'Start it from the Start menu: type "PitWall".'
