param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($ExpectedVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Invalid release version: $ExpectedVersion"
}
$release = $null
if (-not [Version]::TryParse($ExpectedVersion, [ref]$release) -or $release.Major -gt 255 -or $release.Minor -gt 255 -or $release.Build -gt 65535) {
    throw "Invalid release version: $ExpectedVersion exceeds MSI limits"
}
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
foreach ($name in @('fluxo.exe', 'fluxo.dll', 'fluxo.deps.json', 'fluxo.runtimeconfig.json', 'libs/Fluxo.Core.dll', 'libs/Fluxo.Data.dll', 'libs/Fluxo.Services.dll', 'libs/Fluxo.Resources.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Missing payload file: $name" }
}
$expected = [Version]::new($release.Major, $release.Minor, $release.Build, 0)
$files = @(Get-Item -LiteralPath (Join-Path $payload 'fluxo.exe')) + @(Get-ChildItem -LiteralPath $payload -Filter 'Fluxo*.dll' -Recurse -File)
foreach ($file in $files) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
    $actual = [Version]::new($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
    if ($actual -ne $expected) { throw "Payload version mismatch: $($file.FullName): expected $expected; found $actual" }
    if ($file.Extension -eq '.dll') {
        $identity = [Reflection.AssemblyName]::GetAssemblyName($file.FullName).Name
        if ($identity -cne $file.BaseName) { throw "Assembly identity mismatch: $($file.Name) contains $identity" }
    }
}
$deps = Get-Content -LiteralPath (Join-Path $payload 'fluxo.deps.json') -Raw | ConvertFrom-Json
if (@($deps.libraries.PSObject.Properties.Name) -cnotcontains "fluxo/$ExpectedVersion") {
    throw "Application dependency version mismatch: expected fluxo/$ExpectedVersion"
}
Write-Host "PASS: payload $ExpectedVersion ($($files.Count) first-party binaries)"
