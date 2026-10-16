[CmdletBinding()]
param(
    [string] $InstallDirectory,
    [string] $OutputPath,
    [ValidateRange(1, 72)] [int] $HoursBack = 2
)

# Read-only diagnostics. Does not execute Journal, read MDB, change trust, or upload anything.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this diagnostic script on Windows.' }
$warnings = [Collections.Generic.List[string]]::new()
if (-not $InstallDirectory) {
    foreach ($key in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DenisPapusha.Journal',
        'HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\DenisPapusha.Journal'
    )) {
        $registration = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($registration -and $registration.PSObject.Properties['InstallLocation']) {
            $InstallDirectory = [string] $registration.InstallLocation
            break
        }
    }
    if (-not $InstallDirectory) { $InstallDirectory = Join-Path $env:LOCALAPPDATA 'DenisPapusha.Journal' }
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if (-not $OutputPath) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not $desktop) { $desktop = [IO.Path]::GetTempPath() }
    $OutputPath = Join-Path $desktop ('Journal-diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.json')
}
$report = [ordered]@{
    Version = 1
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    InstallDirectory = $InstallDirectory
    Windows = $null
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    SmartAppControlState = $null
    SecurityProducts = @()
    Files = @()
    CodeIntegrityEvents = @()
    Warnings = @()
}
try {
    $os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $report.Windows = [ordered]@{
        ProductName = $os.ProductName
        DisplayVersion = $os.DisplayVersion
        Build = $os.CurrentBuild
        Revision = $os.UBR
        Architecture = $env:PROCESSOR_ARCHITECTURE
    }
}
catch { $warnings.Add('Windows information: ' + $_.Exception.Message) }
try {
    $policy = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop
    if ($policy.PSObject.Properties['VerifiedAndReputablePolicyState']) {
        $report.SmartAppControlState = $policy.VerifiedAndReputablePolicyState
    }
}
catch { $warnings.Add('Smart App Control state unavailable: ' + $_.Exception.Message) }
try {
    $report.SecurityProducts = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName AntiVirusProduct |
        ForEach-Object { [ordered]@{ Name = $_.displayName; State = $_.productState } })
}
catch { $warnings.Add('Security product inventory unavailable: ' + $_.Exception.Message) }
if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
    $binaries = @()
    try {
        $binaries = @(Get-ChildItem -LiteralPath $InstallDirectory -Recurse -File |
            Where-Object { $_.Extension -in @('.exe', '.dll', '.ps1') } | Select-Object -First 1000)
    }
    catch { $warnings.Add('Cannot enumerate installed binaries: ' + $_.Exception.Message) }
    $report.Files = @($binaries | ForEach-Object {
        $file = $_
        try {
            $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
            [ordered]@{
                RelativePath = $file.FullName.Substring($InstallDirectory.Length).TrimStart('\')
                Bytes = $file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                SignatureStatus = [string] $signature.Status
                Signer = $(if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null })
                SignerThumbprint = $(if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null })
                TimestampSigner = $(if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null })
            }
        }
        catch { $warnings.Add('File inspection failed: ' + $file.Name + ': ' + $_.Exception.Message) }
    })
    if ($binaries.Count -eq 1000) { $warnings.Add('File list capped at 1000 binaries.') }
}
else { $warnings.Add('Installation directory not found. Supply -InstallDirectory when a custom folder was chosen.') }
try {
    $events = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Microsoft-Windows-CodeIntegrity/Operational'
        Id = @(3033, 3076, 3077, 3089)
        StartTime = (Get-Date).AddHours(-$HoursBack)
    } -MaxEvents 300)
    $report.CodeIntegrityEvents = @($events | Where-Object { $_.Message -match 'Jornal\.|DenisPapusha\.Journal|Journal-.*Setup' } | ForEach-Object {
        [ordered]@{ TimeCreated = $_.TimeCreated.ToString('o'); Id = $_.Id; Message = $_.Message }
    })
}
catch {
    if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') {
        $warnings.Add('CodeIntegrity log unavailable: ' + $_.Exception.Message)
    }
}
$report.Warnings = @($warnings.ToArray())
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
$json = $report | ConvertTo-Json -Depth 8
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
$stream = [IO.FileStream]::new($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() }
finally { $stream.Dispose() }
Write-Output ('Diagnostic report saved: ' + $OutputPath)
