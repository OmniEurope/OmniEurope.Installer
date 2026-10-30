[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupV1,
    [Parameter(Mandatory)][string]$SetupV2
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:OE_INSTALLER_INTEGRATION -ne '1') {
    throw 'This account-creating integration scenario runs only on an opted-in disposable CI runner.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'The controller must be elevated; the installer itself runs as a separate standard user.'
    }
} finally { $identity.Dispose() }

$publicRoot = [IO.Path]::GetFullPath($env:PUBLIC)
$workspace = Join-Path $publicRoot ('OEInstaller-Std-' + [guid]::NewGuid().ToString('N'))
$accountName = 'oe-it-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$user = $null
$child = $null
try {
    New-Item -ItemType Directory -Path $workspace | Out-Null
    Copy-Item -LiteralPath $SetupV1 -Destination (Join-Path $workspace 'v1.exe')
    Copy-Item -LiteralPath $SetupV2 -Destination (Join-Path $workspace 'v2.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-standard-user-child.ps1') -Destination (Join-Path $workspace 'scenario.ps1')
    $password = ConvertTo-SecureString ('OE-aA9!' + [guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $user = New-LocalUser -Name $accountName -Password $password -Description 'Disposable installer integration fixture'
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $user
    & icacls.exe $workspace /grant ($user.SID.Value + ':(OI)(CI)M') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the fixture user access to its test files.' }
    $credential = [pscredential]::new(($env:COMPUTERNAME + '\' + $accountName), $password)
    $powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    # Loading this new user's profile is required to exercise its genuine HKCU and shell folders.
    # https://learn.microsoft.com/powershell/module/microsoft.powershell.management/start-process
    $child = Start-Process -FilePath $powershell -Credential $credential -LoadUserProfile -WindowStyle Hidden -PassThru `
        -WorkingDirectory $workspace -ArgumentList @('-NoProfile', '-NonInteractive', '-File', ('"' + (Join-Path $workspace 'scenario.ps1') + '"'),
        '-FixtureDirectory', ('"' + $workspace + '"')) `
        -RedirectStandardOutput (Join-Path $workspace 'stdout.log') -RedirectStandardError (Join-Path $workspace 'stderr.log')
    if (-not $child.WaitForExit(240000)) { throw 'The standard-user scenario exceeded its deadline.' }
    if ($child.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $workspace 'stdout.log'), (Join-Path $workspace 'stderr.log') | Write-Host
        throw "Standard-user scenario failed with exit code $($child.ExitCode)."
    }
    $report = Join-Path $workspace 'result.json'
    if (-not (Test-Path -LiteralPath $report)) { throw 'The standard-user scenario produced no proof.' }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.UserSid -ne $user.SID.Value -or $result.IsAdministrator -ne $false -or
        $result.InstalledVersion -ne '1.0.0' -or $result.UpdatedVersion -ne '2.0.0' -or $result.UninstallClean -ne $true) {
        throw 'The standard-user proof is incomplete or belongs to another identity.'
    }
    Get-Content -LiteralPath $report | Write-Host
    Write-Host 'Standard-user install, update and uninstall verified.'
} finally {
    if ($null -ne $child) {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    try {
        if ($null -ne $user) {
            try {
                Get-CimInstance Win32_UserProfile | Where-Object SID -eq $user.SID.Value | Remove-CimInstance
            } finally { Remove-LocalUser -SID $user.SID }
        }
    } finally {
        # Verify the precise disposable target before recursively deleting any test file.
        $resolved = [IO.Path]::GetFullPath($workspace)
        if ([IO.Path]::GetDirectoryName($resolved) -ne $publicRoot -or [IO.Path]::GetFileName($resolved) -notlike 'OEInstaller-Std-*') {
            throw 'Refusing cleanup outside the disposable fixture directory.'
        }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
