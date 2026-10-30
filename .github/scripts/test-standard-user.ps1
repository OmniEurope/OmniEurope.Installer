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
$machineOptIn = @{}
try {
    New-Item -ItemType Directory -Path $workspace | Out-Null
    Copy-Item -LiteralPath $SetupV1 -Destination (Join-Path $workspace 'v1.exe')
    Copy-Item -LiteralPath $SetupV2 -Destination (Join-Path $workspace 'v2.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-standard-user-child.ps1') -Destination (Join-Path $workspace 'scenario.ps1')
    $password = ConvertTo-SecureString ('OE-aA9!' + [guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $user = New-LocalUser -Name $accountName -Password $password -Description 'Disposable installer integration fixture'
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $user
    # icacls reads a bare SID as an account name: the leading * marks it as a SID.
    & icacls.exe $workspace /grant ('*' + $user.SID.Value + ':(OI)(CI)M') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the fixture user access to its test files.' }
    # Loading this new user's profile is required to exercise its genuine HKCU and shell folders. Start-Process would
    # pass the controller's environment, which points the shell folders at a profile out of this account's reach;
    # left untouched, ProcessStartInfo.Environment gives the process the environment of the user's own profile.
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'))
    foreach ($argument in '-NoProfile', '-NonInteractive', '-File', (Join-Path $workspace 'scenario.ps1'), '-FixtureDirectory', $workspace) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UserName = $accountName
    $startInfo.Domain = $env:COMPUTERNAME
    $startInfo.Password = $password
    $startInfo.LoadUserProfile = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $workspace
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # The child's own opt-in check reads these two variables: a profile's environment carries only machine-wide ones.
    foreach ($name in 'GITHUB_ACTIONS', 'OE_INSTALLER_INTEGRATION') {
        $machineOptIn[$name] = [Environment]::GetEnvironmentVariable($name, 'Machine')
        [Environment]::SetEnvironmentVariable($name, [Environment]::GetEnvironmentVariable($name), 'Machine')
    }
    $child = [Diagnostics.Process]::Start($startInfo)
    $standardOutput = $child.StandardOutput.ReadToEndAsync()
    $standardError = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(240000)) { throw 'The standard-user scenario exceeded its deadline.' }
    if ($child.ExitCode -ne 0) {
        Write-Host $standardOutput.GetAwaiter().GetResult()
        Write-Host $standardError.GetAwaiter().GetResult()
        foreach ($log in Get-ChildItem -LiteralPath $workspace -Filter '*.log') {
            Write-Host "--- $($log.Name)"
            Get-Content -LiteralPath $log.FullName -Tail 25 | Write-Host
        }
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
    foreach ($name in $machineOptIn.Keys) { [Environment]::SetEnvironmentVariable($name, $machineOptIn[$name], 'Machine') }
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
