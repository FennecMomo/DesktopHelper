param([string]$Source = "$PSScriptRoot\..\artifacts\publish", [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\DesktopHelper'
$exe = Join-Path $destination 'DesktopHelper.exe'
$sourceExe = Join-Path $Source 'DesktopHelper.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Run scripts/build.ps1 first.' }
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    $running = @(Get-Process DesktopHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw 'DesktopHelper is still running; close it from the tray before installing.' } }
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
if (Test-Path -LiteralPath $exe) { Copy-Item -LiteralPath $exe -Destination ($exe + '.previous') -Force }
Copy-Item -LiteralPath $sourceExe -Destination $exe -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'DesktopHelper 桌面空间.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()
$sourceHash = (Get-FileHash -LiteralPath $sourceExe).Hash
$installedHash = (Get-FileHash -LiteralPath $exe).Hash
if ($sourceHash -ne $installedHash) { throw 'Installed checksum mismatch' }
Write-Output "Installed: $exe"
Write-Output "SHA256: $installedHash"
if (-not $NoLaunch) { Start-Process -FilePath $exe -WindowStyle Hidden }
