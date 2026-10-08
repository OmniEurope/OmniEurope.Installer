<!-- SPDX-License-Identifier: EUPL-1.2 -->
# PLAN-002: Per-user or per-machine installation

> Status: **in progress** (2026-10-08). Lots 1 and 2 done (unit tests, wizard pages rendered); lot 3 scenarios
> written, not yet run by the CI; the standard-user scenario and the Group Policy test on a test machine remain.

## Objective

One MSI per product version that installs either for the current user, without administrator rights, or for every
user of the machine, with them. The setup wizard asks; a quiet setup and a Group Policy deployment install per
machine. Updates, repairs and uninstalls find the installation in whichever context it lives.

## Current state (2026-10-08)

- The package is per-machine only: `ALLUSERS=1` (`ProductRows.WriteProperties`), files under
  `ProgramFiles64Folder`, all-users Start menu and desktop.
- The setup host requests administrator rights at start (`app.manifest`, `requireAdministrator`).
- The setup finds the installed product through its UpgradeCode in the per-machine context only (`InstalledProduct`).
- Shortcut key paths are written under HKLM, and the component that records `setup.settingsKey` must use
  `LocalMachine` (PLAN-001, 2026-10-08): both become the installation's own hive in lot 1.

## Decisions

- 2026-10-08: dual-purpose package (`ALLUSERS=2`, the context chosen through `MSIINSTALLPERUSER`) rather than two
  MSIs: one build, one UpgradeCode, and Windows Installer itself redirects the folders (64-bit Program Files to the
  user's `Programs` folder, Start menu, desktop) in the per-user context.
- 2026-10-08: default context per machine, so that `msiexec /i` and a Group Policy assignment behave as today.
- 2026-10-08: the wizard preselects "for me only": no UAC prompt unless the user asks for all users.
- 2026-10-08: an update, repair or uninstall keeps the context of the existing installation; changing context means
  uninstalling first. Moving an installation from one context to the other inside the setup would need administrator
  rights whenever the existing installation is per-machine.
- 2026-10-08: no summary information change. The Windows Installer documentation (Single Package Authoring) ties the
  absence of a UAC prompt to the per-user context of a package that follows its guidelines (no elevated custom action,
  no service, no system folder), which this package does.

## Lots

### Lot 1 - Dual-purpose MSI
- [x] `ALLUSERS=2` and no `MSIINSTALLPERUSER` by default (per machine); `MSIINSTALLPERUSER` reserved.
- [x] A registry root meaning "the installation's hive" (HKCU per user, HKLM per machine, `Root` -1 in the
  `Registry` table), new value of `RegistryRoot`; shortcut key paths written with it.
- [x] The component that records `setup.settingsKey` must use that root (replaces the `LocalMachine` rule).
- [x] README, sample definition and CHANGELOG.
Control: unit tests reading the tables back (`ALLUSERS`, `Root` -1 for key paths and settings); the administrative
extraction test still passes.
Done 2026-10-08: `ylaunch -c` green, the administrative extraction test included.

### Lot 2 - Setup host
- [x] Manifest `asInvoker`; a per-machine operation relaunches the setup elevated (UAC), a per-user one does not.
- [x] Wizard choice "For me only / For all users" (French and English), shown for a first installation only.
- [x] The installed product found in both contexts; update, repair and uninstall follow its context.
- [x] Quiet mode: per machine by default, `/peruser` (or `MSIINSTALLPERUSER=1`) for the current user.
- [x] Recorded choices read from the hive of the installed context, with the HKCU fallback for older installations.
- [x] Former setup entry removal only when elevated (it lives under HKLM), logged otherwise.
Control: unit tests of the context decision and of the command lines; the wizard pages rendered in French and
English.
Done 2026-10-08: 84 unit tests, CRAP gate passed; `/render-pages` shows the new page in both languages. The UAC
prompt itself and a real per-user installation are proven only by lot 3.

### Lot 3 - Integration scenarios
- [ ] Per-user install, update to a single entry, uninstall leaving nothing, files under the user's `Programs`
  (written, awaiting the CI).
- [ ] Per-machine install through `msiexec /i ... /qn` without the setup, as a Group Policy assignment does (written,
  awaiting the CI).
- [ ] A setup run while the product is installed in the other context follows the decision on context changes
  (written, awaiting the CI).
- [ ] A per-user install run by a non-administrator account (a local standard user created by the CI job).
Control: the CI integration job green with every scenario, TRX counted.

### Lot 4 - Applications
- [ ] The first application's definition moves its settings to the installation's hive; its startup `Run` value
  stays under HKCU (the application manages it itself). Changed in its repository, not committed yet.
- [ ] Group Policy deployment test on a test machine (PLAN-001, lot 4).
Control: the application's build produces a setup that installs in both contexts on a test machine.

## Order and dependencies

Lot 1, then lot 2, then lot 3. Lot 4 after lot 3. PLAN-001 lot 5 (automatic update) depends on this plan: a
per-machine installation is updated by a service, a per-user one by a task running as the user.

## Closing criterion

Every lot has its proof, the CI integration job covers both contexts, and PLAN-001 refers to this plan for the
installation context.
