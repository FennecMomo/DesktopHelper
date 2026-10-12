param([string]$Output = "$PSScriptRoot\..\artifacts\publish")
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\DesktopHelper\DesktopHelper.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$exe = Join-Path $Output 'DesktopHelper.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if ($test.ExitCode -ne 0) { throw 'Packaged self-test failed' }
Get-Content (Join-Path $Output 'self-test-results.txt')
Get-FileHash $exe -Algorithm SHA256
