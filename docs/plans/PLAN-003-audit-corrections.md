<!-- SPDX-License-Identifier: EUPL-1.2 -->
# PLAN-003 - Audit corrections

Scope: the nine findings of the fresh 2026-10-30 audit. No version change.

## Acceptance

1. NuGet contracts: the same invalid product is rejected by JSON and public C# construction before writing output; malformed payload lengths produce InvalidDataException and a controlled host exit; packaged XML describes the installation hive accurately. Prove with a freshly packed package restored into an isolated consumer.
2. CLI: unknown options are rejected before output, including a misspelled optional license switch; valid MSI and setup commands still work.
3. Validation: CI collects coverage and applies the unchanged coverage floors and canonical CRAP gate; TRX checks reject missing, empty or failed runs; the local CRAP copy rejects incomplete collections. Run the same positive and negative inputs before and after.
4. Conventions: wire GEN008 only and the canonical FileSizeAuditTests guard. Prove rejection of intentional violations without leaving those violations in the product. Internal analyzer project requires explicit structural approval.
5. Standard user: add an isolated CI scenario under a genuine non-administrator token, preserve the administrator-only protections of machine tests, and check install/update/uninstall in that account. Execution requires a disposable Windows environment; never install on the workstation to obtain this proof.

## Batches and evidence

- Batch 1: LIB-001, LIB-002, LIB-003, TOOL-001.
- Batch 2: REPO-001, REPO-002, KIT-001, KIT-002.
- Batch 3: INT-001 and final validation.

Keep exact commands, observed before/after results and unresolved proof limits in the ignored local audit correction ledger. Do not close an item based only on compilation or mock evidence. Preserve the original audit unchanged.

## Current evidence

Batch 1: the original package consumer reproduced both failures. A fresh package in a fresh isolated cache now
rejects the same invalid product and payload, still creates the valid MSI, and contains the corrected XML.
Real CLI tests reject unknown switches, retain the license on the valid path, and the actual net472 host
returns 1603 for the malformed trailer before any installation.

Batch 2: all tests pass (125 after the setup builder validation). The same unnecessary partial type builds at the baseline and fails with GEN008 in
the current production build. The actual size guard rejects a temporary 604-line production fixture, removed
after the check. Actual TRX, coverage and CRAP scripts accept the current Release collection and reject
zero/failed tests, below-floor coverage and an incomplete collection. No guard exception or threshold changed.

Batch 3: the standard-account scenario is implemented and compiled. Its workstation refusal and PowerShell
7/5.1 syntax have been checked; the account creation and install/update/uninstall path have not run. The user
confirmed that no disposable environment is accessible. INT-001 remains open for runtime proof. The modified
workflow has not run on GitHub; its unit-job commands have run locally in Release without publishing code.
A review found three defects that would have stopped the scenario; each fix is proved on the real statement, not
on the whole scenario: the `icacls` SID without `*` (exit 1332, now 0), a missing count on a single entry under
Windows PowerShell 5.1, and the controller's `TEMP` and `TMP` inherited through `Start-Process -Credential`.
