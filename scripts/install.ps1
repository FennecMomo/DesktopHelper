param([string]$Source = "$PSScriptRoot\..\artifacts\publish", [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\DesktopHelper'
$exe = Join-Path $destination 'DesktopHelper.exe'
$sourceExe = Join-Path $Source 'DesktopHelper.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Run scripts/build.ps1 first.' }
$dataDirectory = Join-Path $env:LOCALAPPDATA 'DesktopHelper'
$layoutPath = Join-Path $dataDirectory 'layout.json'
$upgradeBackup = Join-Path $dataDirectory ('recovery\upgrade-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
function Backup-Layout([string]$stage) {
    $stagePath = Join-Path $upgradeBackup $stage
    New-Item -ItemType Directory -Force -Path $stagePath | Out-Null
    foreach ($name in @('layout.json', 'layout.json.bak', 'status.json')) {
        $path = Join-Path $dataDirectory $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $stagePath }
    }
}
Backup-Layout 'before-exit'
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    $running = @(Get-Process DesktopHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    foreach ($process in $running) { if (-not $process.WaitForExit(10000)) { throw 'DesktopHelper is still running; close it from the tray before installing.' } }
}
if (Get-Process DesktopHelper -ErrorAction SilentlyContinue) { throw 'Another DesktopHelper process is still running. Update cancelled to protect layout data.' }
Backup-Layout 'after-exit'
$expectedGroups = $null
if (Test-Path -LiteralPath $layoutPath) {
    $savedLayout = Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $savedLayout.Groups) { throw 'Layout is invalid; update cancelled. Original data has been backed up.' }
    $expectedGroups = ConvertTo-Json -InputObject @($savedLayout.Groups) -Depth 50 -Compress
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
if ($expectedGroups -and (ConvertTo-Json -InputObject @((Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json).Groups) -Depth 50 -Compress) -cne $expectedGroups) { throw 'Layout changed during update; launch cancelled.' }
if (-not $NoLaunch) {
    $started = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        Start-Sleep -Milliseconds 100
        try {
            $status = Get-Content (Join-Path $dataDirectory 'status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($status.ProcessId -eq $started.Id) { $ready = $true; break }
        } catch { }
        if ($started.HasExited) { break }
    }
    if (-not $ready) { throw 'New instance did not confirm readiness; inspect the preserved upgrade backup.' }
    if ($expectedGroups -and (ConvertTo-Json -InputObject @((Get-Content -LiteralPath $layoutPath -Raw -Encoding UTF8 | ConvertFrom-Json).Groups) -Depth 50 -Compress) -cne $expectedGroups) { throw 'Layout verification failed after restart; preserved upgrade backup is available.' }
    Write-Output ('Layout verified after restart: ' + $status.ItemCount + ' items')
}
Write-Output "Layout backup: $upgradeBackup"
