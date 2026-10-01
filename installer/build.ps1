# 生成 Windows 安装包（MSI）
#
#   1. 需要 .NET SDK 8 与 WiX：dotnet tool install --global wix
#   2. 在仓库根目录执行：powershell -ExecutionPolicy Bypass -File installer\build.ps1
#
# 产物：dist\BukaMusicDesktop-<版本>-x64.msi

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = "1.0.3"

Write-Host "==> 发布自包含程序（自带 .NET 与 Windows App SDK）"
dotnet publish "$root\BukaMusicDesktop.csproj" -c Release -p:Platform=x64 -p:SelfContained=true -o "$root\publish"

$wix = (Get-Command wix -ErrorAction SilentlyContinue)
if (-not $wix) {
    $fallback = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
    if (Test-Path $fallback) { $wix = $fallback } else {
        throw "未找到 wix，请先执行：dotnet tool install --global wix"
    }
}

New-Item -ItemType Directory -Force -Path "$root\dist" | Out-Null
Write-Host "==> 生成 MSI"
& $wix build "$PSScriptRoot\BukaMusicDesktop.wxs" -arch x64 `
    -o "$root\dist\BukaMusicDesktop-$version-x64.msi"

Write-Host "==> 完成：dist\BukaMusicDesktop-$version-x64.msi"
