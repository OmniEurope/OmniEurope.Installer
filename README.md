<!-- SPDX-License-Identifier: EUPL-1.2 -->
# OmniEurope.Installer

Builds Windows Installer packages (MSI) and setup executables for desktop applications, from a published
application folder and a short JSON product description. Licensed under EUPL-1.2.

## What it produces

- **An MSI** for a per-machine installation under 64-bit `Program Files`: every published file, Start menu and
  desktop shortcuts, registry values, an Add/Remove Programs entry with the product icon, and major upgrades (one
  entry per product, an older version is refused, the same version can be reinstalled). The MSI database is written
  through the Windows Installer API (`msi.dll`); the files are stored in a cabinet embedded in the MSI.
- **A setup executable** that carries the MSI: a French and English wizard (language, license, install folder,
  options, then update, repair or uninstall), closing of the running application before its files are replaced, and
  a quiet mode for scripted deployment.

## Product description

A product is described in JSON (comments allowed, unknown fields rejected):

```jsonc
{
  "name": "Sample App",
  "manufacturer": "OmniEurope",
  "upgradeCode": "3B8E2F14-6C0D-4A7B-9E51-2D4C7A9F0B63",   // never changes between versions
  "mainExecutable": "SampleApp.exe",
  "installDirectory": ["OmniEurope", "Sample App"],         // under Program Files
  "downgradeErrorMessage": "A newer version of Sample App is already installed.",
  "properties": { "INSTALL_DESKTOP_SHORTCUT": "1" },        // public properties and their defaults
  "shortcuts": [
    {
      "id": "DesktopShortcut", "location": "Desktop", "name": "Sample App",
      "condition": "INSTALL_DESKTOP_SHORTCUT",
      "registryKey": "Software\\OmniEurope\\SampleApp", "registryName": "DesktopShortcut"
    }
  ],
  "registryComponents": [
    {
      // Records the choices so that the next setup run (update, repair) starts from them.
      "id": "InstallerSettings", "root": "CurrentUser", "key": "Software\\OmniEurope\\SampleApp\\Installer",
      "values": [
        { "name": "InstallFolder", "type": "String", "value": "[INSTALLFOLDER]" },
        { "name": "DesktopShortcut", "type": "String", "value": "[INSTALL_DESKTOP_SHORTCUT]" }
      ]
    }
  ],
  "setup": {                                                // wizard of the setup executable
    "settingsKey": "Software\\OmniEurope\\SampleApp\\Installer",
    "options": [
      { "property": "INSTALL_DESKTOP_SHORTCUT", "settingName": "DesktopShortcut",
        "labelFr": "Créer un raccourci sur le bureau", "labelEn": "Create desktop shortcut" }
    ]
  }
}
```

A complete example is [tests/OmniEurope.Installer.Tests/Samples/product.json](tests/OmniEurope.Installer.Tests/Samples/product.json).

| Field | Meaning |
|---|---|
| `name`, `manufacturer` | Shown in Installed apps |
| `upgradeCode` | Identity shared by every version; a new version replaces any installed one with the same code |
| `mainExecutable` | Executable relative to the published folder: shortcut target and icon source |
| `installDirectory` | Folder chain under 64-bit Program Files; the last one is `INSTALLFOLDER` |
| `downgradeErrorMessage` | Shown when a newer version is already installed |
| `properties` | Public properties and their defaults; the command line can override them |
| `exclude` | File name patterns (`*`, `?`) left out of the published folder |
| `shortcuts[]` | `id`, `location` (`ProgramMenu` or `Desktop`), `folder` (Start menu sub-folder), `name`, `description`, `condition`, and the HKCU `registryKey`/`registryName` that records the shortcut |
| `registryComponents[]` | `id`, `condition`, `root` (`CurrentUser` or `LocalMachine`), `key`, `values[]` of `name` (omitted for the default value), `type` (`String` or `Integer`) and `value` (may reference properties such as `[INSTALLFOLDER]`) |
| `setup.settingsKey` | HKCU key read to prefill the wizard: `InstallFolder`, `Language` and each option's `settingName`, as written by a registry component |
| `setup.languageProperty` | Public property receiving `fr` or `en`; omitted when the product has no language choice |
| `setup.options[]` | Check boxes: `property` (`"1"` when checked, empty otherwise), `settingName`, `labelFr`, `labelEn` |
| `setup.legacyBundleUpgradeCodes` | BundleUpgradeCodes of former setup bundles whose Installed apps entry is removed after a successful operation |
| `setup.licenseNoticeFr` | French note shown above the license text |

## Building a package

The `oe-installer` tool (`src/OmniEurope.Installer.Tool`) builds the MSI, then the setup executable:

```powershell
oe-installer msi   --definition product.json --source <published folder> --version 1.2.3 --output App-1.2.3.msi
oe-installer setup --definition product.json --msi App-1.2.3.msi `
                   --host <OmniEurope.Installer.Setup.exe> --icon <published folder>\App.exe `
                   [--license License.rtf] --output App-1.2.3-Setup.exe
```

`--host` is the setup host built from `src/OmniEurope.Installer.Setup` (.NET Framework 4.7.2, part of Windows);
`--icon` is the executable whose icon the setup shows.

## Running the setup executable

Without arguments, the setup shows its wizard. On the command line:

| Argument | Effect |
|---|---|
| `/quiet` | Installs, updates or repairs without any window, with the recorded or default choices |
| `/quiet /uninstall` | Removes the product |
| `PROPERTY=value` | Overrides a choice, e.g. `INSTALL_DESKTOP_SHORTCUT=` to leave the box unchecked |
| `/log <path>` | Writes the log to this file (default: `%TEMP%`) |

Exit codes are those of Windows Installer:

| Code | Meaning |
|---|---|
| `0` | Success |
| `3010` | Success; some files in use are replaced at the next restart. The setup never restarts the computer itself. |
| `1603` | Failure, including a damaged setup file |
| `1605` | Uninstall requested but the product is not installed |
| `1638` | A newer version is already installed |
| `87` | Invalid argument |

Before installing, the setup closes the application started from its install folder. An instance it cannot
identify is left running and reported in the log (`left running`).

## Development

```powershell
dotnet build OmniEurope.Installer.slnx -c Release
dotnet test --project tests/OmniEurope.Installer.Tests/OmniEurope.Installer.Tests.csproj -c Release
```

Warnings are errors. The continuous integration (`.github/workflows/ci.yml`, Windows) builds the solution and runs
the unit tests on every push and pull request to `main` and `develop`.

A second job runs `tests/OmniEurope.Installer.IntegrationTests`: the setup executable is run for real, quietly, on a
throwaway fixture product (install, then uninstall leaving no file, folder, shortcut, entry or setting; update to a
single entry; refused downgrade; running application closed; former setup entry removed; file held by another
program giving `3010` without restart). These tests install on the machine:
they refuse to run without `OE_INSTALLER_INTEGRATION=1` and administrator rights, and are meant for a disposable
machine.

Branches: `main` for releases, `develop` for integration, `feature/*` for work in progress.

## Dependencies

The shipped code uses no third-party package: only .NET, .NET Framework 4.7.2 (setup host) and `msi.dll`
(Windows). Packages used by the tests only:

| Package | License | Purpose |
|---|---|---|
| `xunit.v3` | Apache-2.0 | Test framework |
| `Shouldly` | BSD-3-Clause | Readable assertions |
| `Microsoft.Testing.Extensions.TrxReport` | MIT | TRX test reports |
| `coverlet.MTP` | MIT | Code coverage |

Build only: `Microsoft.NETFramework.ReferenceAssemblies` (MIT), reference assemblies to compile the setup host
without a targeting pack installed; nothing of it is shipped.

## License

EUPL-1.2, see [LICENSE](LICENSE).
