$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$outPath = Join-Path $env:TEMP ("cpu-tests-" + [Guid]::NewGuid().ToString("N") + ".exe")

try {
  $coreSources = Get-ChildItem -Recurse (Join-Path $rootDir "src/DmgEmu.Core") -Filter *.cs | ForEach-Object { $_.FullName }
  $testSource = Join-Path $rootDir "tools/cpu-tests.cs"

  Write-Host "compiling cpu tests..."
  & mcs -r:System.Drawing $coreSources $testSource -out:$outPath

  & mono $outPath @args
  exit $LASTEXITCODE
}
finally {
  if (Test-Path $outPath) {
    Remove-Item -Force $outPath
  }
}
