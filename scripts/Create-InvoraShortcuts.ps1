[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallDirectory)
$ErrorActionPreference = 'Stop'
$invoraShell = New-Object -ComObject WScript.Shell
$invoraDesktop = [Environment]::GetFolderPath('DesktopDirectory')
foreach ($item in @(@('Start Invora', 'Start-Invora.cmd'), @('Stop Invora', 'Stop-Invora.cmd'), @('Check Invora', 'Check-Invora.cmd'))) {
    $shortcut = $invoraShell.CreateShortcut((Join-Path $invoraDesktop ($item[0] + '.lnk')))
    $shortcut.TargetPath = Join-Path $InstallDirectory $item[1]
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.Description = 'Invora local shop application'
    $shortcut.Save()
}
