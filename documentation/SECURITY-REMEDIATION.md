# Security review remediation

October 26, 2026. Release version remains 0.2.2; current private source/build set is 2026.26.10.3.
The published 0.2.2 packages and the owner's installed applications are unchanged.

The validation covered every finding in the supplied security/performance review. Some were
confirmed by marker-only probes; others required qualification. For example, normal sync excludes
linked-folder configuration, backup restore endpoints are person-only, and plugin path-filter
bypasses are still blocked by backend authorization. Historical performance estimates are not
current measurements.

## Batch 1: native review and attachment/calendar boundaries

- S02: Windows navigation, permissions and native messages share an exact parsed loopback-origin
  predicate. User-info tricks, different hosts/ports/schemes and malformed sources are denied.
- S03: PowerShell code blocks show the complete script in a selectable, scrollable native window.
  Approval defaults to Cancel. Scripts containing NUL are refused because a native edit control
  cannot display their complete content reliably.
- S09/S45: Attachment collection rejects traversal, alternate-stream and backslash references.
  Serving, trashing, adding and copying attachments also check actual attachment-directory
  containment, excluding junction/symbolic-link paths. A sibling called `attachments-elsewhere`
  never acquires attachment permissions merely from its name.
- S10: Calendar export accepts an existing validated workspace or linked-folder name and checks
  its output path. Registered linked scopes remain exportable.

Regression evidence began with 13 failing assertions against the original source. It covers
calendar traversal/unknown names, attachment sibling serving/trashing, encoded traversal,
Markdown/HTML/site export leakage and exact native-origin decisions, plus normal calendar/export
behavior. Additional tests cover a real Windows junction and the real native review dialog with
long scripts and explicit Run/Cancel actions. Native tests are in `tests/Quantos.Client.Tests`;
the Windows package build runs them alongside the existing backend/frontend suites.

Validation: all **204 backend/core tests passed** with test collections run sequentially. The
default parallel runs passed the new boundary/junction cases but hit existing five-second discovery
and ten-second SSE HTTP timeouts (202/203 and 201/203 passed before the final linked-scope test).
Discovery passed alone, and the full sequential run passed without extending timeouts or weakening
assertions. This establishes a passing controlled run, not a clean default-parallel run on this host.
The calendar subset passed all six cases, including linked scopes. Type checking reported zero
errors and nine warnings in untouched frontend files. Native compilation succeeded with the
WebView2/WindowsBase reference warning; the existing AiTests nullable warning was also emitted.
All **three native dialog tests passed** after the final layout adjustment, exercising 2,000- and
100,000-character scripts, scrolling to their final line, Cancel defaults and an explicit Run click.
Herald validation and whitespace checks passed; Graft was refreshed.

Independent diagnosis agreed on all five causes. Review round 1 found one compatibility issue:
calendar validation initially excluded registered linked folders. A regression test reproduced
HTTP 400, the implementation was corrected, and round 2 returned **no findings**. No findings
were rebutted. The review and tests do not substitute for exercising a real hostile-page navigation
through WebView2 or for acceptance testing the owner's devices.

This batch does not establish that arbitrary same-user processes are isolated, nor does it close
all outside-file paths. In particular the general vault/backup enumeration policies and linked
folder configuration still require their own changes.

## Remaining work in priority order

1. Server-bound agent/native identity and separately authorized approvals (S01), including native
   session minting, permission grants, shell results and actor-bound approval consumption.
2. OutsideFiles checks and migration of linked-folder authority to device data (S04/S05), together
   with no-follow policies for indexing/backups and restore service authorization/integrity.
3. YAML expansion limits and bounded regex/marker parsing (S06/S12); safe standalone HTML output
   and metadata encoding (S07/S08). These remain open even though export attachment copying is fixed.
4. Plugin/automation content trust, scoped device/bridge credentials, sync deletion guards and
   updater authenticity/staging/interruption recovery.
5. FTS integer-rowid mapping, indexed unresolved-link lookup, incremental sync manifests outside
   long write-lock holds, and durable incremental checkpoint/journal progress. Preserve crash
   recovery and conditional sync writes; measure improvements before claiming timings.
6. The remaining endpoint/resource, transport, dependency/CI, frontend and lifecycle hardening
   items from validation, addressed with their individually qualified threat models.

Only the changes explicitly listed under Batch 1 are implemented here. A passed regression suite
is evidence for those cases, not a claim that the full audit has been remediated or that an updated
release has been published, installed, or manually verified on the owner's devices.
