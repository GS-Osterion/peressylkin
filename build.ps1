# Build Sibir: icons -> compile -> run tests -> copy to out\
# Usage: ./build.ps1 [-Filter regex,...] [-NoTests]. Prints only errors, failures and the summary.
param([string[]]$Filter = @(), [switch]$NoTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Test-Path assets\make-icons.ps1) { & .\assets\make-icons.ps1 }
$b = dotnet build Sibir.slnx -c Release -v q -nologo 2>&1
if ($LASTEXITCODE) { $b | Select-String 'error' | Select-Object -Unique -First 10 | ForEach-Object Line; Write-Output 'BUILD FAILED'; exit 1 }
if (-not $NoTests) {
  $log = & .\tests\Sibir.Tests\bin\Release\net48\Sibir.Tests.exe @Filter
  $code = $LASTEXITCODE
  $log | Select-String '^(FAIL|SKIP)|^\s+at |Passed!|Failed!' | Select-Object -First 40 | ForEach-Object Line
  if ($code) { exit 1 }
}
New-Item -ItemType Directory -Force out | Out-Null
Copy-Item src\Sibir\bin\Release\net48\Sibir.exe out\ -Force
Get-ChildItem assets\*.ico -EA SilentlyContinue | Copy-Item -Destination out\ -Force
'OK: out\Sibir.exe'
