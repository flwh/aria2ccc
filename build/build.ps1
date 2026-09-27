# 编译 aria-gui：定位系统自带 csc，产出根目录 aria-gui.exe（内嵌 aria2c.exe）。
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "未找到系统自带的 csc.exe（需要 .NET Framework 4.x）"
    exit 1
}

$res = Join-Path $root "assets\aria2c\aria2c.exe"
if (-not (Test-Path $res)) {
    Write-Error "缺少 $res，请先运行 build\get_aria2c.ps1"
    exit 1
}

$icon = Join-Path $root "assets\app.ico"
if (-not (Test-Path $icon)) {
    Write-Error "缺少 $icon，请先运行 build\make-icon.ps1"
    exit 1
}

# @() 强制数组：管道单文件输出会被解包为标量字符串，导致 @srcs splat 逐字符拆分
$srcs = @(Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.cs -File | ForEach-Object { $_.FullName })

$out = Join-Path $root "aria-gui.exe"
& $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$out" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/resource:$res,aria2c.exe" "/win32icon:$icon" @srcs
if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败（csc 退出码 $LASTEXITCODE）"
    exit 1
}

$item = Get-Item $out
Write-Host ("生成 {0}（{1:N0} 字节）" -f $item.FullName, $item.Length)
