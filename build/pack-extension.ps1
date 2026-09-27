# Pack browser-extension into distribution zip: <root>\aria-gui-extension-v<version>.zip
# Note: Compress-Archive in PS 5.1 writes backslash entry names (non-standard);
# this script uses .NET ZipArchive with forward slashes, then verifies the result.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$src = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\browser-extension"))

$manifestText = [System.IO.File]::ReadAllText((Join-Path $src "manifest.json"), [System.Text.Encoding]::UTF8)
$m = [System.Text.RegularExpressions.Regex]::Match($manifestText, '"version"\s*:\s*"([^"]+)"')
if (-not $m.Success) { throw "version not found in manifest.json" }
$version = $m.Groups[1].Value

$dst = Join-Path $root ("aria-gui-extension-v" + $version + ".zip")
if ([System.IO.File]::Exists($dst)) { [System.IO.File]::Delete($dst) }

$zip = [System.IO.Compression.ZipFile]::Open($dst, [System.IO.Compression.ZipArchiveMode]::Create)
$base = $src.TrimEnd('\') + '\'
Get-ChildItem $src -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($base.Length).Replace('\', '/')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel)
}
$zip.Dispose()

$z = [System.IO.Compression.ZipFile]::OpenRead($dst)
Write-Host "entries:"
foreach ($e in $z.Entries) { Write-Host ("  " + $e.FullName + "  " + $e.Length) }
$bad = @($z.Entries | Where-Object { $_.FullName.Contains('\') })
$z.Dispose()
if ($bad.Count -gt 0) { throw ("backslash entries found: " + $bad.Count) }
Write-Host ("OK: " + $dst)
