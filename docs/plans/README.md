<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Implementation plans

Every numbered plan of the repository lives here. The code and the tests remain the structural sources; a plan
describes a piece of work, not the state of the product.

## Naming

- Required format: `PLAN-NNN-short-description.md`.
- `NNN` is dense and follows the order of the registry below.
- A finished plan moves to `archive/` and keeps its number.

## Active plans

| Plan | Remaining work |
|---|---|
| [PLAN-001](PLAN-001-initial-scope.md) | Initial scope: lots 1 to 3 done (MSI, major upgrade, setup executable), lot 4 adopted by a first application (upgrade check with its build and Group Policy test remaining); remaining: automatic update (needs a publication place), other applications, code signing with a certificate-aware payload reader, interactive wizard run, ICE validation, NuGet packaging of the tool |
