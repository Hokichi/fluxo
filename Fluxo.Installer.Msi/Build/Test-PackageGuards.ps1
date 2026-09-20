param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [string]$MsiPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$expected = [Version]"$ExpectedVersion.0"
foreach ($name in @('fluxo.exe', 'fluxo.dll', 'libs/Fluxo.Core.dll', 'libs/Fluxo.Data.dll', 'libs/Fluxo.Services.dll', 'libs/Fluxo.Resources.dll')) {
    $file = Get-Item -LiteralPath (Join-Path $payload $name)
    if ([Version]$file.VersionInfo.FileVersion -ne $expected) {
        throw "Release regression: $name expected $expected; found $($file.VersionInfo.FileVersion)"
    }
}
& "$PSScriptRoot/Verify-Payload.ps1" -PayloadDirectory $payload -ExpectedVersion $ExpectedVersion

function Assert-Rejected([scriptblock]$Action, [string]$Message) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        Write-Host "PASS: rejected $Message"
        return
    }
    throw "Regression: expected rejection containing '$Message'"
}

$scratchParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../obj/guard-tests'))
$scratch = Join-Path $scratchParent ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
try {
    $copy = Join-Path $scratch 'payload'
    Copy-Item -LiteralPath $payload -Destination $copy -Recurse
    $core = Join-Path $copy 'libs/Fluxo.Core.dll'
    Remove-Item -LiteralPath $core
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy $ExpectedVersion } 'Missing payload file'
    Copy-Item -LiteralPath (Join-Path $payload 'libs/Fluxo.Data.dll') -Destination $core
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy $ExpectedVersion } 'Assembly identity mismatch'

    # Build a real old-version assembly; do not depend on the developer's installation.
    $fixture = Join-Path $scratch 'fixture'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Fluxo.Core</AssemblyName><FileVersion>1.0.0.0</FileVersion></PropertyGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $fixture 'OldCore.csproj')
    'namespace Fluxo.Core; public sealed class LegacyMarker { }' | Set-Content -LiteralPath (Join-Path $fixture 'LegacyMarker.cs')
    & dotnet build (Join-Path $fixture 'OldCore.csproj') --nologo -v:q -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
    if ($LASTEXITCODE -ne 0) { throw 'Old-version fixture build failed' }
    Copy-Item -LiteralPath (Join-Path $fixture 'bin/Debug/net10.0/Fluxo.Core.dll') -Destination $core -Force
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy $ExpectedVersion } 'Payload version mismatch'
    Copy-Item -LiteralPath (Join-Path $payload 'libs/Fluxo.Core.dll') -Destination $core -Force
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy '0.0.1' } 'Payload version mismatch'
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy '1.2.3.4' } 'Invalid release version'
    Assert-Rejected { & "$PSScriptRoot/Verify-Payload.ps1" $copy '256.0.0' } 'Invalid release version'

    if ($MsiPath) {
        & "$PSScriptRoot/Verify-Package.ps1" -MsiPath $MsiPath -PayloadDirectory $payload -ExpectedVersion $ExpectedVersion
        # JSON stays valid, so this exercises packaged-byte verification.
        Add-Content -LiteralPath (Join-Path $copy 'fluxo.deps.json') -Value ' '
        Assert-Rejected { & "$PSScriptRoot/Verify-Package.ps1" $MsiPath $copy $ExpectedVersion } 'Packaged hash mismatch'
        Assert-Rejected { & "$PSScriptRoot/Verify-Package.ps1" $MsiPath $payload '0.0.1' } 'MSI product version mismatch'
    }
    Write-Host 'PASS: package guards'
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (-not $resolved.StartsWith($scratchParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe scratch cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
