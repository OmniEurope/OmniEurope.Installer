[CmdletBinding()]
param([Parameter(Mandatory)][string]$FixtureDirectory)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:OE_INSTALLER_INTEGRATION -ne '1') {
    throw 'Installer integration requires an opted-in disposable CI runner.'
}
function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Assert-Condition (-not $isAdministrator) 'The fixture token is elevated.'
    Assert-Condition (-not (@($identity.Groups | ForEach-Object Value) -contains 'S-1-5-32-544')) 'The fixture belongs to Administrators, even if its token is filtered.'
    $sid = $identity.User.Value
} finally { $identity.Dispose() }

# Start-Process -Credential passes the controller's environment: its TEMP and TMP are out of this account's reach.
$userTemp = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Temp'
New-Item -ItemType Directory -Force -Path $userTemp | Out-Null
$env:TEMP = $userTemp
$env:TMP = $userTemp

$name = 'OmniEurope Installer Fixture'
$installFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/OmniEurope.Tests/Fixture'
$machineFolder = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'OmniEurope.Tests/Fixture'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($name + '/' + $name + '.lnk')
$settings = 'HKCU:\Software\OmniEurope\InstallerFixture\Installer'
$machineSettings = 'HKLM:\Software\OmniEurope\InstallerFixture\Installer'
# A function unrolls its output and Windows PowerShell has no Count on a single object: callers wrap it in @().
function Get-FixtureEntry {
    Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Get-ItemProperty | Where-Object DisplayName -eq $name
}
function Invoke-Setup([string]$Version, [string]$Operation) {
    $arguments = @('/quiet', '/log', ('"' + (Join-Path $FixtureDirectory ($Version + '-' + $Operation + '.log')) + '"'))
    if ($Operation -eq 'install') { $arguments += '/peruser' }
    if ($Operation -eq 'uninstall') { $arguments += '/uninstall' }
    $process = Start-Process -FilePath (Join-Path $FixtureDirectory ($Version + '.exe')) -ArgumentList $arguments -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'The quiet setup did not finish under the standard user.' }
        Assert-Condition ($process.ExitCode -eq 0) ("Quiet setup failed: " + $process.ExitCode)
    } finally { $process.Dispose() }
}

try {
    Assert-Condition (@(Get-FixtureEntry).Count -eq 0) 'The new account already has a fixture installation.'
    Invoke-Setup 'v1' 'install'
    $entry = @(Get-FixtureEntry)
    Assert-Condition ($entry.Count -eq 1) 'Installation did not create exactly one HKCU entry.'
    $installedVersion = $entry[0].DisplayVersion
    Assert-Condition ($installedVersion -eq '1.0.0') 'Wrong installed version.'
    Assert-Condition ([IO.File]::ReadAllText((Join-Path $installFolder 'data.txt')) -eq 'version 1.0.0') 'Wrong installed data.'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $installFolder 'FixtureApp.exe')) 'The executable is missing.'
    Assert-Condition (Test-Path -LiteralPath $shortcut) 'The user Start menu shortcut is missing.'
    Assert-Condition ((Get-ItemProperty -LiteralPath $settings).InstallFolder.TrimEnd('\') -eq $installFolder.TrimEnd('\')) 'The HKCU settings do not match the install folder.'
    Assert-Condition (-not (Test-Path -LiteralPath $machineFolder)) 'The per-user installation wrote to Program Files.'
    Assert-Condition (-not (Test-Path -LiteralPath $machineSettings)) 'The per-user installation wrote machine settings.'

    # No scope switch: the update must retain the installed per-user context.
    Invoke-Setup 'v2' 'update'
    $entry = @(Get-FixtureEntry)
    Assert-Condition ($entry.Count -eq 1) 'Update did not keep exactly one HKCU entry.'
    $updatedVersion = $entry[0].DisplayVersion
    Assert-Condition ($updatedVersion -eq '2.0.0') 'Wrong updated version.'
    Assert-Condition ([IO.File]::ReadAllText((Join-Path $installFolder 'data.txt')) -eq 'version 2.0.0') 'Wrong updated data.'
    Assert-Condition (-not (Test-Path -LiteralPath $machineFolder)) 'Update switched to the machine context.'
} finally {
    if (@(Get-FixtureEntry).Count -gt 0) { Invoke-Setup 'v2' 'uninstall' }
}

$clean = @(Get-FixtureEntry).Count -eq 0 -and -not (Test-Path -LiteralPath $installFolder) -and
    -not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath $settings)
Assert-Condition $clean 'Uninstall left files, a shortcut, settings or an Installed apps entry.'
@{ UserSid = $sid; IsAdministrator = $isAdministrator; InstalledVersion = $installedVersion;
    UpdatedVersion = $updatedVersion; UninstallClean = $clean } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $FixtureDirectory 'result.json') -Encoding UTF8
