# Changelog

Notable changes to this project are documented here, following the Keep a Changelog format.

## [Unreleased]

### Added

- MSI generator: database written through `msi.dll`, embedded MSZIP cabinet, per-machine installation, shortcuts,
  registry values, major upgrade with downgrade refusal; `oe-installer` tool (`msi`, `setup`).
- Setup executable (.NET Framework 4.7.2 host) built by `oe-installer setup`: French and English wizard, quiet mode
  (`/quiet`, `/uninstall`, `/log`, `PROPERTY=value`), closing of the application started from its install folder,
  removal of the entry left by a former setup bundle.
- Windows Installer exit codes (`0`, `3010`, `1603`, `1605`, `1638`, `87`); the computer is never restarted
  automatically (`REBOOT=ReallySuppress` on every operation, `3010` when a restart is still needed).
- Unit tests (`tests/OmniEurope.Installer.Tests`) and integration tests (`tests/OmniEurope.Installer.IntegrationTests`)
  running the setup executable for real, quietly, on a fixture product in five scenarios: install then uninstall
  leaving nothing behind, update to a single entry with a refused downgrade, running application closed, former
  setup entry removed, file held by another program giving `3010` without restart. They run only with
  `OE_INSTALLER_INTEGRATION=1` and administrator rights.
- Continuous integration on Windows (`.github/workflows/ci.yml`): Release build without warnings, unit tests,
  integration tests, mandatory TRX reports.
