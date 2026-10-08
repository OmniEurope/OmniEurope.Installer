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

### Changed

- The MSI is a dual-purpose package: per machine by default (`msiexec`, Group Policy), or for the current user only,
  without administrator rights, with `MSIINSTALLPERUSER=1` (files under `%LOCALAPPDATA%\Programs`, the user's Start
  menu and desktop, HKCU).
- The setup executable starts without administrator rights: the wizard asks "for me only" (preselected) or "for all
  users" for a first installation, `/peruser` and `/permachine` choose on the command line, and `/quiet` alone
  installs for all users. A per-machine operation runs the setup again elevated (UAC prompt); an update, repair or
  uninstall keeps the context of the installed product.
- Registry root `Installation` (HKCU per user, HKLM per machine): used for each shortcut's key path, and required
  for the component that records `setup.settingsKey`. The setup reads the recorded choices from the installed
  context's hive, and under HKCU for an older per-machine installation.
- A repair passes the chosen properties to Windows Installer, before the context, `REINSTALL`, `REINSTALLMODE` and the
  restart suppression.
- The entry left by a former setup is removed only by a per-machine run, which has the rights to do it.

### Fixed

- A product property declared with an empty default (an option unchecked by default) no longer breaks the MSI build
  (Windows Installer error 2210): it is left out of the `Property` table, which leaves it unset.
- A product definition with a JSON `null` for a required field or in a list, or an enum given as a number or an
  unknown name, is refused with an error instead of crashing or falling back to another value.
- A setup executable whose configuration is damaged fails with `1603` instead of an unhandled exception.
- A product name holding a path separator no longer breaks the extraction of the MSI or the log path.
- The wizard can uninstall a newer installed version, as `/quiet /uninstall` already could.
- A product version written with non-ASCII digits is refused as invalid instead of crashing.
