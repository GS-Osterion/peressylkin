# Build Sibir: icons -> compile -> run tests -> copy to out\
param([string[]]$Filter = @(), [switch]$NoTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Test-Path assets\make-icons.ps1) { & .\assets\make-icons.ps1 }
dotnet build Sibir.slnx -c Release -v q -nologo | Out-Host
if ($LASTEXITCODE) { throw "build failed" }
if (-not $NoTests) {
  & .\tests\Sibir.Tests\bin\Release\net48\Sibir.Tests.exe @Filter | Tee-Object -Variable log | Out-Null
  $log | Select-String '^(FAIL|SKIP)|Passed!|Failed!' | Out-Host
  if ($LASTEXITCODE) { throw "tests failed" }
}
New-Item -ItemType Directory -Force out | Out-Null
Copy-Item src\Sibir\bin\Release\net48\Sibir.exe out\ -Force
Get-ChildItem assets\*.ico -EA SilentlyContinue | Copy-Item -Destination out\ -Force
"OK: out\Sibir.exe"
