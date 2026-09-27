# 获取 aria2c.exe 到 assets/aria2c/ 供 csc /resource: 内嵌使用。
# 用法: powershell -File build\get_aria2c.ps1 [-Version 1.37.0]
param(
    [string]$Version = "1.37.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "assets\aria2c"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$target = Join-Path $dest "aria2c.exe"
if (Test-Path $target) {
    Write-Host "已存在: $target （如需更新请先删除）"
    exit 0
}

$zip = "aria2-$Version-win64.zip"
$url = "https://github.com/aria2/aria2/releases/download/release-$Version/$zip"
$tmp = Join-Path $env:TEMP $zip
Write-Host "下载 $url"
Invoke-WebRequest -Uri $url -OutFile $tmp
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($tmp)
try {
    $entry = $archive.Entries | Where-Object { $_.Name -eq "aria2c.exe" } | Select-Object -First 1
    if (-not $entry) { throw "zip 中未找到 aria2c.exe" }
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
} finally {
    $archive.Dispose()
}
Remove-Item $tmp -Force
Write-Host "完成: $target"
