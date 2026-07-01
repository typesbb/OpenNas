param(
    [int]$Version,
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$csproj = Join-Path $root "OpenNas\OpenNas.csproj"
$passwordFile = Join-Path $root ".keystore-password.txt"
$keystore = Join-Path $root "opennas-release.keystore"
$outDir = Join-Path $root "OpenNas\bin\$Config\net10.0-android"

if (-not (Test-Path $keystore)) {
    Write-Error "Keystore not found: $keystore"
    exit 1
}
if (-not (Test-Path $passwordFile)) {
    Write-Error "Password file not found: $passwordFile"
    exit 1
}
$password = (Get-Content $passwordFile -Raw).Trim()

if ($Version) {
    $lines = Get-Content $csproj
    $lines = $lines -replace '<ApplicationVersion>\d+</ApplicationVersion>', "<ApplicationVersion>$Version</ApplicationVersion>"
    $lines | Set-Content $csproj
    Write-Host "ApplicationVersion -> $Version"
} else {
    $ver = (Select-String -Path $csproj -Pattern '<ApplicationVersion>(\d+)</ApplicationVersion>').Matches[0].Groups[1].Value
    Write-Host "Current ApplicationVersion: $ver"
}

if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
    Write-Host "Cleaned output directory"
}

Write-Host "Publishing Android APK ..."
dotnet publish $csproj -f net10.0-android -c $Config `
    /p:AndroidSigningKeyPass=$password `
    /p:AndroidSigningStorePass=$password

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$apk = Get-ChildItem -Path $outDir -Filter "*-Signed.apk" | Select-Object -First 1
if ($apk) {
    $size = [math]::Round($apk.Length / 1MB, 2)
    Write-Host ""
    Write-Host "========================================"
    Write-Host "  APK: $($apk.FullName)"
    Write-Host "  Size: ${size} MB"
    Write-Host "========================================"
}
