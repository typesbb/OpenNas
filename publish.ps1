param(
    [int]$Version,
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$csproj = Join-Path $root "OpenNas" "OpenNas.csproj"
$passwordFile = Join-Path $root ".keystore-password.txt"
$keystore = Join-Path $root "opennas-release.keystore"
$outDir = Join-Path $root "OpenNas" "bin" $Config "net10.0-android"

# ---------- check ----------
if (-not (Test-Path $keystore)) {
    Write-Error "密钥库不存在: $keystore"
    Write-Error "请先运行 keytool 生成密钥库"
    exit 1
}
if (-not (Test-Path $passwordFile)) {
    Write-Error "密码文件不存在: $passwordFile"
    exit 1
}
$password = (Get-Content $passwordFile -Raw).Trim()

# ---------- bump version ----------
if ($Version) {
    $lines = Get-Content $csproj
    $lines = $lines -replace '<ApplicationVersion>\d+</ApplicationVersion>', "<ApplicationVersion>$Version</ApplicationVersion>"
    $lines | Set-Content $csproj
    Write-Host "→ 版本升级到 $Version"
} else {
    $ver = (Select-String -Path $csproj -Pattern '<ApplicationVersion>(\d+)</ApplicationVersion>').Matches[0].Groups[1].Value
    Write-Host "→ 当前版本 $ver (指定 -Version 参数可升级)"
}

# ---------- clean ----------
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
    Write-Host "→ 已清理旧构建"
}

# ---------- publish ----------
Write-Host "→ 开始构建 APK ..."
dotnet publish $csproj -f net10.0-android -c $Config `
    /p:AndroidSigningKeyPass=$password `
    /p:AndroidSigningStorePass=$password

if ($LASTEXITCODE -ne 0) {
    Write-Error "构建失败，退出码 $LASTEXITCODE"
    exit $LASTEXITCODE
}

# ---------- result ----------
$apk = Get-ChildItem -Path $outDir -Filter "*-Signed.apk" | Select-Object -First 1
if ($apk) {
    $size = [math]::Round($apk.Length / 1MB, 2)
    Write-Host "`n========================================"
    Write-Host "  APK: $($apk.FullName)"
    Write-Host "  大小: ${size}MB"
    Write-Host "========================================"
}
