[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    # The target framework of the tool to install and run (net10.0 or net11.0).
    [Parameter(Mandatory)][string]$Framework,
    [Parameter(Mandatory)][string]$Definition
)

# Installs the oe-installer tool from the packed files only (never from nuget.org), checks that the variant for
# $Framework was installed, then builds an MSI and a setup executable with it, the setup without --host.

$ErrorActionPreference = 'Stop'

$package = Get-ChildItem $PackageDirectory -Filter 'OmniEurope.Installer.Tool.*.nupkg' | Where-Object Name -NotLike '*.snupkg' | Select-Object -First 1
if (-not $package) { throw "No tool package in $PackageDirectory." }
$version = $package.BaseName.Substring('OmniEurope.Installer.Tool.'.Length)

$work = Join-Path ([IO.Path]::GetTempPath()) "oe-installer-tool-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # Only the packed files: a version of the same number on nuget.org must not stand in for them.
    $config = Join-Path $work 'nuget.config'
    $source = [Security.SecurityElement]::Escape((Resolve-Path $PackageDirectory).Path)
    Set-Content -Path $config -Encoding utf8 -Value "<configuration><packageSources><clear /><add key=`"packed`" value=`"$source`" /></packageSources></configuration>"

    $toolPath = Join-Path $work 'tool'
    dotnet tool install OmniEurope.Installer.Tool --version $version --tool-path $toolPath --framework $Framework --configfile $config
    if ($LASTEXITCODE -ne 0) { throw "Installing the $Framework tool failed." }

    # The store holds every variant of the package: the command shim names the one it runs.
    $tool = Join-Path $toolPath 'oe-installer.exe'
    $shim = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($tool))
    if (-not $shim.Contains("tools\$Framework\any\oe-installer.dll")) { throw "The oe-installer command does not run the $Framework variant." }
    $variant = Get-ChildItem $toolPath -Recurse -Directory | Where-Object { $_.Name -eq 'any' -and $_.Parent.Name -eq $Framework } | Select-Object -First 1
    if (-not $variant) { throw "The installed tool has no $Framework folder." }
    $runtimeOptions = (Get-Content (Join-Path $variant.FullName 'oe-installer.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
    $major = $Framework.Substring(3).Split('.')[0]
    if ($runtimeOptions.tfm -ne $Framework -or -not $runtimeOptions.framework.version.StartsWith("$major.")) {
        throw "The $Framework variant asks for runtime '$($runtimeOptions.framework.version)' ($($runtimeOptions.tfm))."
    }
    if (-not (Test-Path (Join-Path $variant.FullName 'OmniEurope.Installer.Setup.exe'))) { throw 'The installed tool does not carry the setup host.' }
    $tfm = $runtimeOptions.tfm

    # A published folder with one real executable carrying an icon: Explorer stands in for the product executable.
    $published = Join-Path $work 'published'
    New-Item -ItemType Directory -Path $published | Out-Null
    $executable = Join-Path $published 'SampleApp.exe'
    Copy-Item (Join-Path ([Environment]::GetFolderPath('Windows')) 'explorer.exe') $executable

    $msi =Join-Path $work 'SampleApp-1.2.3.msi'
    & $tool msi --definition $Definition --source $published --version 1.2.3 --output $msi
    if ($LASTEXITCODE -ne 0) { throw "oe-installer msi failed on $Framework (exit $LASTEXITCODE)." }
    if (-not (Test-Path $msi) -or (Get-Item $msi).Length -eq 0) { throw 'oe-installer msi wrote no MSI.' }

    $setup = Join-Path $work 'SampleApp-1.2.3-setup.exe'
    & $tool setup --definition $Definition --msi $msi --icon $executable --output $setup
    if ($LASTEXITCODE -ne 0) { throw "oe-installer setup failed on $Framework (exit $LASTEXITCODE)." }
    if ((Get-Item $setup).Length -le (Get-Item $msi).Length) { throw 'The setup executable does not carry the MSI.' }

    Write-Host "oe-installer $version ($tfm): MSI $((Get-Item $msi).Length) bytes, setup $((Get-Item $setup).Length) bytes."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
