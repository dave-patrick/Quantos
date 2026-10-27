# Security review remediation

October 26, 2026. Release version remains 0.2.2; current private source/build set is 2026.26.10.4.
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

## Batch 2: separate native, personal CLI and agent credentials

- S01: discovery tokens now allow only ping and requesting/polling pairing. Header changes cannot
  choose a CLI, agent or native identity. Each paired connection has a server-bound actor and a
  separately revocable credential; the server persists only its hash in device settings.
- Native credentials stay in memory. Windows/Android hosts create them in process; Windows
  reconnect uses a same-user named pipe with kernel peer PID and matching executable-path checks
  on both sides. No HTTP endpoint issues native credentials. Update client/backend together.
- Pairing requires native confirmation of a matching code. Personal CLI consent explicitly
  includes content edits and browser sign-in; named agents cannot mint any browser/app session.
  Tool/MCP calls without a name default to the agent `agent`, never the personal CLI.
- Approvals bind actor, credential, instance and exact action. Agent names that resemble native
  clients cannot approve actions or submit native shell results. An invalid bearer never falls
  back to a browser cookie. The Settings MCP page lists/revokes paired connections without secrets.
- Windows CLI caches use DPAPI CurrentUser and private directory ACLs; Unix caches use private
  directory/file modes. The isolated development harness provisions a personal CLI only through
  an explicit Debug-only startup flag, rejected in Release builds.
- S04/S05 prerequisite: registering/removing linked roots and writing their configuration require
  OutsideFiles as well as CreateEdit. Linked-file serving rejects nested junctions and refuses
  device-data paths. Migrating imported linked-root authority out of vault content remains open.

The initial credential regression set failed all ten cases against the old implementation.
Additional coverage exercises real Windows pipe peer IDs, wrong peers/PIDs, native startup/page
sign-in, CLI first pairing/cache reuse, credential persistence/revocation, header spoofing, approval
replay across actors/instances/credentials, protected caches and outside-path grants.
Same-user debugging, executable replacement and DPAPI decryption by an already-compromised OS
account remain outside this application-level role boundary. Revoking a personal CLI credential
does not revoke previously minted browser sessions.

## Remaining work in priority order

1. Finish migration of linked-folder authority to device data (S04/S05), together
   with no-follow policies for indexing/backups and restore service authorization/integrity.
2. YAML expansion limits and bounded regex/marker parsing (S06/S12); safe standalone HTML output
   and metadata encoding (S07/S08). These remain open even though export attachment copying is fixed.
3. Plugin/automation content trust, scoped device/bridge credentials, sync deletion guards and
   updater authenticity/staging/interruption recovery.
4. FTS integer-rowid mapping, indexed unresolved-link lookup, incremental sync manifests outside
   long write-lock holds, and durable incremental checkpoint/journal progress. Preserve crash
   recovery and conditional sync writes; measure improvements before claiming timings.
5. The remaining endpoint/resource, transport, dependency/CI, frontend and lifecycle hardening
   items from validation, addressed with their individually qualified threat models.

Only the changes explicitly listed under Batches 1 and 2 are implemented here. A passed regression suite
is evidence for those cases, not a claim that the full audit has been remediated or that an updated
release has been published, installed, or manually verified on the owner's devices.
