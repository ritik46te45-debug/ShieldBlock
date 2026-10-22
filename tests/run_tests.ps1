$ErrorActionPreference = "Stop"

$tempDir = Join-Path $env:TEMP "ShieldBlockTests"
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$srcDir = "c:\Users\ritik2\Downloads\Shieldblock\tests\ShieldBlock.Tests\bin\Debug\net8.0"
Copy-Item "$srcDir\*" -Destination $tempDir -Recurse -Force

Get-ChildItem -Path $tempDir | Unblock-File -ErrorAction SilentlyContinue

Write-Host "Running tests from: $tempDir\ShieldBlock.Tests.exe"
& "$tempDir\ShieldBlock.Tests.exe"
exit $LASTEXITCODE
