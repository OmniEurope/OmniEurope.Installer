[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    # When given, both packages must carry exactly this version (the release tag, without prefix).
    [string]$ExpectedVersion
)

# Checks the content of the two NuGet packages before they are uploaded as an artifact or pushed to nuget.org:
# the library package and the oe-installer tool package (which bundles the setup host).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageEntries([string]$Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try { return @($archive.Entries | ForEach-Object { [uri]::UnescapeDataString($_.FullName) }) }
    finally { $archive.Dispose() }
}

function Get-NuspecMetadata([string]$Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' -and $_.FullName -notlike '*/*' } | Select-Object -First 1
        if (-not $entry) { throw "No .nuspec at the root of $Path." }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        return [pscustomobject]@{ Id = [string]$metadata.id; Version = [string]$metadata.version }
    }
    finally { $archive.Dispose() }
}

function Assert-Entry([string[]]$Entries, [string]$Pattern, [string]$Package) {
    if (-not ($Entries | Where-Object { $_ -like $Pattern })) {
        throw "$Package does not contain an entry matching '$Pattern'."
    }
}

if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) { throw "No package directory: $PackageDirectory." }

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File | Where-Object Name -NotLike '*.snupkg')
$symbols = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.snupkg' -File)

$expected = @(
    @{ Id = 'OmniEurope.Installer'; Entries = @('lib/net10.0/OmniEurope.Installer.dll', 'lib/net10.0/OmniEurope.Installer.xml', 'lib/net11.0/OmniEurope.Installer.dll', 'lib/net11.0/OmniEurope.Installer.xml', 'README.md') },
    @{ Id = 'OmniEurope.Installer.Tool'; Entries = @('tools/net10.0/any/oe-installer.dll', 'tools/net10.0/any/DotnetToolSettings.xml', 'tools/net10.0/any/OmniEurope.Installer.Setup.exe', 'tools/net11.0/any/oe-installer.dll', 'tools/net11.0/any/DotnetToolSettings.xml', 'tools/net11.0/any/OmniEurope.Installer.Setup.exe', 'README.md') }
)

if ($packages.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) packages in $PackageDirectory, found $($packages.Count): $($packages.Name -join ', ')."
}

foreach ($definition in $expected) {
    $id = $definition.Id
    $matching = @($packages | Where-Object { (Get-NuspecMetadata $_.FullName).Id -ceq $id })
    if ($matching.Count -ne 1) { throw "Expected exactly one $id package, found $($matching.Count)." }
    $package = $matching[0]
    $metadata = Get-NuspecMetadata $package.FullName
    $entries = Get-PackageEntries $package.FullName

    foreach ($pattern in $definition.Entries) { Assert-Entry $entries $pattern $package.Name }

    if ($ExpectedVersion -and $metadata.Version -cne $ExpectedVersion) {
        throw "$($package.Name) has version $($metadata.Version), expected $ExpectedVersion."
    }
    if ($package.Name -cne "$id.$($metadata.Version).nupkg") {
        throw "$($package.Name) does not match its metadata ($id $($metadata.Version))."
    }

    $symbolName = "$id.$($metadata.Version).snupkg"
    $symbol = $symbols | Where-Object Name -ceq $symbolName
    if (-not $symbol) { throw "Missing symbol package $symbolName." }
    if (-not (Get-PackageEntries $symbol.FullName | Where-Object { $_ -like '*.pdb' })) {
        throw "$symbolName contains no .pdb."
    }

    Write-Host "$($package.Name): version $($metadata.Version), content and symbols OK."
}
