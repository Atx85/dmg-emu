$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$romDir = Join-Path $rootDir "gb-test-roms"

$gbTestRepo = "https://github.com/retrio/gb-test-roms.git"
$acid2Url = "https://github.com/mattcurrie/dmg-acid2/releases/download/v1.0/dmg-acid2.gb"
$acid2Sha256 = "464E14B7D42E7FEEA0B7EDE42BE7071DC88913F75B9FFA444299424B63D1DFF1"

Write-Host "fetching gb-test-roms into $romDir"
if (Test-Path (Join-Path $romDir ".git")) {
  & git -C $romDir fetch --depth 1 origin
  & git -C $romDir reset --hard origin/master
} else {
  if (Test-Path $romDir) {
    Remove-Item -Recurse -Force $romDir
  }
  & git clone --depth 1 $gbTestRepo $romDir
}

$acid2Out = Join-Path $romDir "dmg-acid2.gb"
Write-Host "downloading dmg-acid2 ROM"
Invoke-WebRequest -Uri $acid2Url -OutFile $acid2Out

$hash = (Get-FileHash -Algorithm SHA256 $acid2Out).Hash.ToUpperInvariant()
if ($hash -ne $acid2Sha256) {
  throw "checksum mismatch for $acid2Out`nexpected: $acid2Sha256`nactual:   $hash"
}

Write-Host "test ROM setup complete"
