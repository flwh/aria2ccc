# 编译并运行自测（纯逻辑模块 + tests/ParserTests.cs 的 Main）。
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File build\run-tests.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "未找到系统自带的 csc.exe"
    exit 1
}

# 注意：排除 Program.cs（Main 冲突）与 src\UI（WinForms 不可测）；Engine 单独一层递归。
$srcs = @()
$srcs += Get-ChildItem (Join-Path $root "src") -Filter *.cs -File |
    Where-Object { $_.Name -ne "Program.cs" } | ForEach-Object { $_.FullName }
if (Test-Path (Join-Path $root "src\Engine")) {
    $srcs += Get-ChildItem (Join-Path $root "src\Engine") -Filter *.cs -File | ForEach-Object { $_.FullName }
}
$srcs += Get-ChildItem (Join-Path $root "tests") -Filter *.cs -File | ForEach-Object { $_.FullName }

$out = Join-Path $root "build\aria-gui-tests.exe"
# 内嵌 aria2c：Manager 真机 E2E（Embed.Extract）需要
$res = Join-Path $root "assets\aria2c\aria2c.exe"
if (-not (Test-Path $res)) {
    Write-Error "缺少 $res，请先运行 build\get_aria2c.ps1"
    exit 1
}
& $csc /nologo /codepage:65001 /target:exe "/out:$out" "/resource:$res,aria2c.exe" @srcs
if ($LASTEXITCODE -ne 0) {
    Write-Error "测试程序编译失败（csc 退出码 $LASTEXITCODE）"
    exit 1
}

& $out
exit $LASTEXITCODE
