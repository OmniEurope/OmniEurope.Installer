<!-- SPDX-License-Identifier: EUPL-1.2 -->
# PLAN-001: Initial scope

> Status: **in progress** (2026-10-08). Lots 1 to 3 done: MSI generator, major upgrade, setup executable (covered
> by unit tests and by integration tests run in CI). Lot 4: a first application builds its installer with this
> tool; its upgrade check with its own build and a Group Policy test remain. Remaining: automatic updates (lot 5,
> needs a publication place), adoption by the other applications (lot 6), a complete interactive run of the wizard,
> and ICE validation. The NuGet packaging of `oe-installer` is done (2026-11-11): library and tool packages
> `1.0.0` (.NET 10 and .NET 11) with the setup host bundled, CI pack and content check, and the publish workflow
> (`docs/publishing.md`), whose run for the GitHub release `1.0.0` pushed both packages to NuGet.org on 2026-11-11;
> their listing on nuget.org and an install from it are not verified yet.
> Code signing (lot 7) is deferred.

## Objective

Produce, from this repository and without paid dependencies, an MSI and a setup executable for desktop
applications, then a silent automatic update checked by an in-house signature.

## MSI scope

The published folder under `ProgramFiles64Folder\<Manufacturer>\<App>`, Start menu and desktop shortcuts (optionally
conditional), HKCU or HKLM registry values, public properties (`INSTALLFOLDER` and the product's own), product icon
and `ARPPRODUCTICON`, embedded cabinet, major upgrade (older version refused, same version reinstallable),
per-machine installation. Closing the running application is not done by the MSI: the setup executable and the
updater close it before installing.

## Decisions

- 2026-10-06: the cabinet is written by this repository (MSZIP with .NET deflate) rather than through the FCI API of
  `cabinet.dll`: same format read by Windows, without FCI's native callbacks, and testable with `expand.exe`.
- 2026-10-06: no hosting of published versions exists yet. The updater (lot 5) treats a missing source as a normal
  case: it does nothing and logs it, without error.
- 2026-10-07: an application uses this repository in two steps. First from a sibling checkout, the tool built on
  demand (local machine only); then, once a pipeline must build the installer, through the `oe-installer` .NET tool
  published as a versioned NuGet package (setup host included) and pinned by the application.
- 2026-10-08: HKLM for what the per-machine package records about itself. HKCU is the hive of whichever account runs
  the elevated setup: an administrator elevating for a standard user, a Group Policy deployment or the updater service
  (system account) would record the choices for the wrong account, or where the updater cannot read them. The
  shortcuts' key path values are written under HKLM; the component that records `setup.settingsKey` must use
  `LocalMachine` (refused otherwise); the setup reads the recorded choices under HKLM, then under HKCU so that an
  installation that recorded them per user keeps its choices. Superseded by PLAN-002 (per-user or per-machine
  installation): the settings and key paths then use the installation's own hive.
- 2026-10-08: the startup `Run` value stays a product choice. An application that manages its own startup setting
  under HKCU (without administrator rights) keeps the installer's `Run` value under HKCU too: under HKLM, the
  application could not turn it off and could start twice.
- 2026-10-08: `setup.legacyBundleUpgradeCodes` keeps its name and stays: it names the `BundleUpgradeCode` registry
  value it looks up, and the other applications' former setups (lot 6) leave such an entry too.
- 2026-10-08: `RemoveExistingProducts` stays right after `InstallValidate`: the previous version is removed before
  the new one is installed, so a failed upgrade leaves it removed. Moving it after `InstallInitialize` would roll the
  previous version back on failure, but the old and new components would then coexist in one transaction, which
  requires the component rules to hold strictly between versions (same GUID, same key path and content).

## Lot 1: MSI generator

- [x] MSI database written through P/Invoke on `msi.dll`: tables `Property`, `Directory`, `Component`, `File`,
  `Feature`, `FeatureComponents`, `Registry`, `Shortcut`, `RemoveFile`, `Icon`, `Media`, standard sequences, summary
  information stream.
- [x] Embedded cabinet (MSZIP written by this repository), stored in `_Streams`.
- [x] Component GUIDs stable from one version to the next (derived from the file path).
- Check: tests reading the produced MSI back through `msi.dll`; ICE validation (`darice.cub` from the Windows SDK)
  without errors; a test MSI installs and uninstalls quietly without leaving any file, shortcut or key.
- Done: the tests read the tables back, `expand.exe` restores the cabinet and `msiexec /a` extracts every file
  identically. A real 827-file, 130 MB application package was extracted identically to its published folder, then
  installed, upgraded and uninstalled without leftovers on a real machine. Remaining: ICE validation (Windows SDK
  not available).

## Lot 2: Major upgrade

- [x] Tables `Upgrade`, `FindRelatedProducts`, `RemoveExistingProducts`, downgrade refusal message
  (`LaunchCondition` on `OE_DOWNGRADE_DETECTED`).
- Done: version 1 then version 2 leaves a single entry, an older version is refused with the message, a rebuilt
  package of the same version (other ProductCode) replaces the installed one. Defect found and fixed: an upgrade that
  ships a file older than the installed one lost it (component skipped, then removed with the old product);
  `REINSTALLMODE=amus` now copies every file.
- Check: version 1 then 2 shows a single entry in Installed apps; 2 then 1 is refused with the message; the same
  version can be reinstalled.

## Lot 3: Setup executable

- [x] Single executable carrying the MSI, with a WinForms wizard (language, license, folder, options), closing the
  running application and installing through `MsiInstallProduct` with the chosen properties and a log.
- [x] Quiet mode (`/quiet`, `/uninstall`, `PROPERTY=value`) using the recorded or default choices.
- [x] Removal of the Installed apps entry left by a former setup bundle after a successful operation
  (`legacyBundleUpgradeCodes`).
- Technical choices (2026-10-06): host `src/OmniEurope.Installer.Setup` on .NET Framework 4.7.2 (part of Windows),
  MSI and configuration appended to the executable by `oe-installer setup`, product icon copied into its resources;
  administrator rights requested at start, product restarted at the end through Explorer so that it runs without
  them; `ARPNOREPAIR` and `ARPNOMODIFY` in the MSI, since Windows does not keep the cabinet and a repair started from
  Installed apps would ask for the original MSI.
- Check: interactive installation with each option, quiet installation, running application closed before the copy.
- Done: unit tests for the executable format, icon, configuration, choices, progress, command line and the command
  lines passed to Windows Installer; the six wizard pages rendered and checked in French and English. A quiet install
  on a real machine once restarted the computer by itself (exit code 1641) because a running instance of the
  application was not closed and its files were in use. Fixed: `REBOOT=ReallySuppress` on every operation (3010 and a
  restart message instead), process path read through `QueryFullProcessImageName`, any instance left running logged.
  Integration tests in CI (2026-10-07, 5/5) on a fixture product: install, then uninstall leaving no file, folder,
  shortcut, entry or setting; update to a single entry; downgrade refused (1638); application running before the
  install closed by the setup; former setup entry removed; file held by another program giving 3010 without restart.
  Remaining: a complete interactive run of the wizard.
- Note: check boxes pass `"1"` or an empty string (an MSI condition tests whether the property is set, so `"0"`
  would be true).

## Lot 4: First application

- [x] The application's build script calls this repository (sibling checkout) to produce its MSI and setup
  executable; its previous installer toolchain is removed; its product description lives in its own repository.
  Done 2026-10-07 as reported by the owner: the application's build produces its installer and the application
  works.
- [x] Same `UpgradeCode` as the application's previous MSI, so that the new MSI replaces an existing installation
  (proven on a real machine in lot 1).
- [x] Removal of the Installed apps entry left by the former setup bundle at the first upgrade, by the setup
  executable (lot 3, covered by an integration test). The updater (lot 5) must do the same; the MSI alone cannot
  look the entry up.
- Check: the application's build produces the setup (done); an existing installation moves to the new version with a
  single entry left, using the application's own build (not yet re-run since the adoption); the MSI deployed by
  Group Policy on a test machine (not done).

## Lot 5: Automatic update

- [ ] Windows service (system account) reading a published manifest (version, URL, SHA-256, size) signed with ECDSA
  P-256 by the publisher's private key, checking the signature with the embedded public key, downloading the MSI,
  checking its hash, closing the application and installing with `msiexec /qn`.
- [ ] A per-user installation (PLAN-002) is updated without administrator rights, by a task running as the user
  rather than by the service.
- Check: an application moves from one version to the next without intervention; a badly signed manifest, an altered
  MSI or a lower version are refused and logged.

## Lot 6: Other applications

- [ ] Same adoption as lot 4 for the other desktop applications.
- Check: for each, the checks of lot 4.

## Lot 7: Code signing (deferred)

Deferred on 2026-10-08: not needed for now, kept here for when a certificate is wanted.

- [ ] Authenticode certificate (owner's decision and purchase), signing of the MSI and the setup executable.
- [ ] The setup payload reader skips the Authenticode certificate table appended after the payload trailer
  (`SetupPayload`), with a test on a signed setup: today a signed setup executable cannot read its own payload.
- Check: `signtool verify /pa` passes, the signed setup installs, and SmartScreen no longer shows "unknown
  publisher".
