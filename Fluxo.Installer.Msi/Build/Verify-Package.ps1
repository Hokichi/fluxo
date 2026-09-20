param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
$view = $null
try {
    $database = $installer.OpenDatabase($msi, 0)
    $view = $database.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductVersion''')
    $view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record -or $record.StringData(1) -ne $ExpectedVersion) { throw "MSI product version mismatch: expected $ExpectedVersion" }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
    $view.Close()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    $view = $database.OpenView('SELECT `FileName`, `Version` FROM `File`')
    $view.Execute()
    $seen = @{}
    while ($null -ne ($record = $view.Fetch())) {
        $name = ($record.StringData(1) -split '\|')[-1]
        if ($name -like 'Fluxo*.dll' -or $name -eq 'fluxo.exe') {
            if ($record.StringData(2) -ne "$ExpectedVersion.0") { throw "MSI file version mismatch: $name" }
            $seen[$name] = $true
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
    }
    foreach ($name in @('fluxo.exe', 'fluxo.dll', 'Fluxo.Core.dll', 'Fluxo.Data.dll', 'Fluxo.Services.dll', 'Fluxo.Resources.dll')) {
        if (-not $seen.ContainsKey($name)) { throw "Missing MSI file: $name" }
    }
}
finally {
    if ($null -ne $view) { $view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
& "$PSScriptRoot/Verify-Payload.ps1" -PayloadDirectory $payload -ExpectedVersion $ExpectedVersion
$scratchParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../obj/package-verification'))
$scratch = Join-Path $scratchParent ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
try {
    $extracted = Join-Path $scratch 'extracted'
    $log = Join-Path $scratch 'extract.log'
    $process = Start-Process -FilePath msiexec.exe -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extracted`"", '/L*v', "`"$log`"") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "MSI extraction failed ($($process.ExitCode)); see $log" }
    $roots = @(Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter 'fluxo.exe')
    if ($roots.Count -ne 1) { throw "Expected one packaged application root; found $($roots.Count)" }
    $root = $roots[0].Directory.FullName
    $sourceFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    $actualFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File)
    if ($sourceFiles.Count -ne $actualFiles.Count) { throw 'Packaged file count mismatch' }
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($payload.TrimEnd('\', '/').Length + 1)
        $destination = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Missing packaged file: $relative" }
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Packaged hash mismatch: $relative" }
    }
    Write-Host "PASS: MSI $ExpectedVersion metadata and $($sourceFiles.Count) packaged file hashes"
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (-not $resolved.StartsWith($scratchParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe scratch cleanup path' }
    # Keep extraction failure evidence; clean successful extraction only.
    if ($null -ne (Get-Variable sourceFiles -ErrorAction SilentlyContinue)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
