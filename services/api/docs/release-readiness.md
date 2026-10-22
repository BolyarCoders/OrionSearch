# Backend foundation: release blockers and acceptance plan

**Status: backend foundation for team integration; not a deployable product release.**

The 2026-10-21 request narrows the hosting goal to making the API reachable on
Railway, while retaining the existing authentication boundary and disabled
research worker. See [Hosting the API on Railway](railway-api.md) for that manual
deployment's configuration, actual verification and current platform blocker.
Hosting the API alone does not close the product release gates below.

This register follows [Velislav's review of PR #6](https://github.com/BolyarCoders/OrionSearch/pull/6#pullrequestreview-5582969412), submitted on 2026-10-20 against `f08a64029b950e5ba67e5505bcae5ca811a254e7`. The review found no specific blocking code defect but did not approve end-to-end product readiness. GitHub records it as COMMENTED, not APPROVED.

The author confirmed the foundation scope on 2026-10-20: preserve explicit follow-up blockers and do not deploy this PR as a functioning authenticated research service. Merging a foundation, if the team later approves it, does not close these release gates. No merge, deployment, worker enablement, CI configuration or other team's implementation is performed by this documentation follow-up.

## Release-blocker register

The RB identifiers below are stable tracking IDs in this document and the PR checklist, not invented Jira tickets. Existing team issues are linked where their ownership is known. An unchecked gate is release-blocking. Passing local backend tests does not close a gate requiring real integration evidence.

| ID | Gate and current evidence | Responsible team / existing tracking | Required closure evidence |
| --- | --- | --- | --- |
| RB-01 | **Required CI: OPEN pending enforcement verification.** The `Backend CI` workflow exists at [`.github/workflows/backend-ci.yml`](https://github.com/BolyarCoders/OrionSearch/blob/feature/ORION-9-backend-foundation/.github/workflows/backend-ci.yml). Run [39404488832](https://github.com/BolyarCoders/OrionSearch/actions/runs/39404488832) completed successfully on commit `9b614548589803ffbb83b54eedd870f23dd9de93`; restore, tool restore, build, automated/PostgreSQL integration tests and TRX artifact upload all passed. The maintainer reports configuring the `Backend build and tests` branch-protection check, but enforcement has not been independently verified here. | Velislav / [ORION-16](https://kviolinov.atlassian.net/browse/ORION-16), currently To Do | Confirm the exact check is required on `main` and demonstrate that a failing check prevents merging. Record the result in the PR/issue. |
| RB-02 | **Real authentication: OPEN.** Normal API startup uses the Unconfigured scheme and protected endpoints return 401. There is no implemented login/provisioning/provider adapter. | Team/PM must select provider and assign integration owner; backend and frontend participate. No dedicated authentication issue was found in the inspected project backlog. | Provider/trust decision, validated external-subject mapping to existing `auth.users.id`, provisioning/disablement behavior, real positive and negative authentication tests, and the security cases below. Do not treat a caller-ID header or test scheme as completion. |
| RB-03 | **Compatible agent and agreed contract: OPEN.** `orion.research.provisional.v1` is still opt-in and unaccepted. The new standalone collector is not a compatible service. | Konstantin / [ORION-8](https://kviolinov.atlassian.net/browse/ORION-8), currently To Do; producer work relates to [ORION-5](https://kviolinov.atlassian.net/browse/ORION-5), currently QA | Agreed request/result schema/version, authenticated HTTP producer, deterministic chunks/quotes, durable raw capture references and verified compatibility fixtures produced by the real Python implementation. |
| RB-04 | **Real end-to-end integration: OPEN / UNVERIFIED.** No real authenticated API → queue → Python agent → PostgreSQL → sources acceptance run has passed. | Cross-team acceptance owner must be named by PM; depends on RB-02/RB-03 and the intended development environment | One controlled real success flow plus failure/retry/isolation cases, using an isolated authorized development archive and actual service credentials. Preserve sanitized run IDs, version/commit identities, statuses and persistence assertions as evidence. A fake-agent test is not this gate. |
| RB-05 | **Integration/security acceptance: OPEN / UNVERIFIED.** Existing local rejection, authorization, transport and fencing tests cover backend boundaries; they do not certify a configured provider or deployed system. | PM must name security/acceptance reviewer; backend, frontend/auth, AI and DevOps supply evidence | Execute and review the separate acceptance pass below against the real configuration. Record findings, fixes, retests and explicit human acceptance. No release sign-off is inferred from this document. |
| RB-06 | **Deployment and hosted operations: OPEN / UNVERIFIED.** API/worker deployment and API-to-Railway readiness are not established by this PR; hosted catalog/history needs secure reinspection. | Velislav / [ORION-14](https://kviolinov.atlassian.net/browse/ORION-14), [ORION-15](https://kviolinov.atlassian.net/browse/ORION-15), currently To Do | Reviewed deployment configuration and secrets, trusted proxy/CORS settings, read-only hosted schema/history inspection, deliberate migration procedure, real readiness and operational checks. Do not run the destructive local database fixture against Railway. |

Release checklist, mirrored in PR #6:

- [ ] **RB-01:** Required CI exists, executes successfully and is enforced.
- [ ] **RB-02:** Real authentication, identity mapping and provisioning are accepted.
- [ ] **RB-03:** Final contract and real compatible agent are accepted.
- [ ] **RB-04:** Controlled real end-to-end integration passes.
- [ ] **RB-05:** Separate integration/security acceptance is signed off.
- [ ] **RB-06:** Deployment, hosted schema/connectivity and operations are verified.

## Evidence checked on 2026-10-20

- PR #6 is open with base `main` and head `feature/ORION-9-backend-foundation`. The implementation head reviewed by Velislav is `f08a6402`.
- The review says not to treat the work as a deployable release until CI and a separate integration/security pass are complete. The user explicitly selected the foundation handoff path for this follow-up.
- The `Backend CI` workflow is now present on the PR branch. Run [39404488832](https://github.com/BolyarCoders/OrionSearch/actions/runs/39404488832) completed successfully for commit `9b614548589803ffbb83b54eedd870f23dd9de93`, including the full automated/PostgreSQL integration-test step. The maintainer reports configuring `Backend build and tests` as a required branch-protection check; this follow-up has not independently read back the saved rule or demonstrated a failing check blocking a merge. Required-check enforcement therefore remains the only open portion of RB-01.
- Jira lists ORION-9 through ORION-12 as Production, the workflow's Done-category status. These are backend task statuses, not evidence that the product is deployed, authenticated or security-accepted. This follow-up does not change any Jira status.
- ORION-8 and ORION-16 remain To Do. ORION-5 is QA; ORION-14 and ORION-15 remain To Do. No other team's issue is modified here.
- New `origin/feature/ai-worker` head `6c97c9e` contains a standalone RSS/API collector and its README. Its own limitations explicitly exclude LangGraph, configurable LLM execution, the topic-input/shared request-result interface, API/PostgreSQL integration and automated tests. `origin/ai-worker` remains placeholders. These branches were inspected with Git reads; neither was merged or rewritten.
- The backend branch still contains its explicitly provisional schemas/fixtures, fail-closed authentication and disabled worker. A local contract fixture passing is distinct from the real Python producer passing it.
- The earlier 2026-10-18 handoff reported 348 full-suite passes on PostgreSQL 18.6. Those historical results remain scoped to that execution; they are not fresh CI or live integration evidence.
- No live provider credentials, compatible agent endpoint or authorized hosted SQL connection is introduced here. Hosted schema/connectivity and complete product security remain UNVERIFIED in this follow-up.

### Local boundary verification in this follow-up

The existing selected boundary suite ran against the unchanged compiled backend implementation at `f08a6402`, using isolated local PostgreSQL 18.6: **89 passed, zero failed or skipped**, in 2m20s. The command used `dotnet test --no-build --filter` with these selections: ArchiveAuthenticationTests; generic-error and environment OpenAPI ApiTests; ReverseProxyTests; ResearchAgentHttpTests; ResearchOutputTests; archive detail membership/source isolation/topic isolation cases; research submission-role/status-membership cases; ResearchCompletionTests; and ResearchVersionProvenanceTests. TRX was saved outside the repository as `review-boundaries.trx`.

These checks exercise test-authenticated positive membership cases, normal-startup rejection, sanitized errors, transport limits/failures, contract rules, real PostgreSQL fencing/rollback and raw/text provenance. They do not use a real identity provider or Python producer, do not connect the API to Railway, and do not close RB-01 through RB-06. The complete 348-test suite was not rerun for this documentation-only follow-up. Both documentation files passed local link checks and all thirteen JSON examples in the API guide parsed successfully.

For the complete implemented behavior, see the [API and Backend Guide](api-and-backend-guide.md), [database design](database.md), and [provisional agent handoff](../../../packages/contracts/research/provisional-v1/README.md).

## CI handoff for RB-01

A baseline workflow has been added to this branch and has a successful GitHub Actions run. Keep ownership and any further hardening coordinated under ORION-16 rather than adding a competing workflow.

- Workflow: [`Backend CI`](https://github.com/BolyarCoders/OrionSearch/blob/feature/ORION-9-backend-foundation/.github/workflows/backend-ci.yml)
- Successful run: [39404488832](https://github.com/BolyarCoders/OrionSearch/actions/runs/39404488832)
- Validated revision: `9b614548589803ffbb83b54eedd870f23dd9de93`
- Successful steps: .NET dependency restore, local tool restore, Release build, automated and PostgreSQL integration tests, and TRX artifact upload.
- The workflow uses .NET 10 as selected by `services/api/global.json` and the PostgreSQL integration fixture's default PostgreSQL 16 container. This run does not establish a PostgreSQL 18 CI matrix.

Remaining CI follow-up:
1. Read back the `main` branch protection rule and confirm the exact required context is `Backend build and tests`.
2. Demonstrate with a controlled failing check (or equivalent repository-approved verification) that the rule blocks merging; do not weaken protection or merge a deliberately failing PR to test it.
3. Consider adding the PostgreSQL 16/18 matrix, `dotnet format OrionSearch.sln --verify-no-changes`, and `dotnet ef migrations has-pending-model-changes --project src/OrionSearch.Infrastructure` as ORION-16 hardening. These checks are not part of the current workflow and should not be described as passing CI.
4. Preserve TRX artifacts and keep deployment credentials, live migration commands and Railway access out of the validation job.

RB-01 remains open until required-check enforcement is confirmed. The passing workflow run is evidence that this candidate builds and its configured automated/PostgreSQL tests pass; it is not proof that branch protection blocks merging.

## Separate integration/security acceptance pass

This is a follow-up execution plan for RB-02 through RB-06, not a claim that those tests already passed. It requires the real provider/agent and an authorized isolated development setup. PM must assign the missing owners and approve the final evidence, rather than silently treating unassigned acceptance as complete.

### Preconditions

- Identify the exact API, producer and contract revisions to test; record environment and configuration identities without secrets.
- Agree identity provider, trusted external-subject mapping, user provisioning and disablement. Provision real owner/editor/viewer and unrelated-user identities for two isolated archives.
- Agree the agent contract/version, service authentication, bounded transport settings, deterministic chunks and durable raw storage. Validate real producer output against the shared contract and backend semantic rules.
- Confirm a secure read-only Railway inspection method. Run [inspect-database.sql](../scripts/inspect-database.sql) and compare all eight expected migrations, citext, seven schemas and fifteen product tables before any separately authorized changes.
- Establish the deployment/migration plan under DevOps ownership. The worker must only be enabled through that deliberate plan; do not add a development identity bypass or production fake agent.

### Identity and authorization cases

1. Valid provider credentials map to the intended existing active internal user and permit that user's authorized operation.
2. Missing, malformed, expired and otherwise invalid credentials fail according to the selected provider's rules; token-provider issuer/audience/signature checks must be exercised where applicable.
3. Public identity headers, arbitrary external subjects, role claims and ambiguous internal identities cannot override server mapping or database membership.
4. Unknown/disabled users cannot act as active application users. Exercise disablement/provisioning behavior with the real mapping/cache policy.
5. Owners/editors/viewers retain their implemented permissions. Viewers cannot submit research; editors cannot archive or change status; unrelated users cannot enumerate archive sources or research statuses.
6. Cross-archive access remains denied even when the same global document is linked to both archives. Client changes to request IDs/settings cannot choose another user's authority or an arbitrary agent endpoint.

### Real research flow and failure cases

1. Create an isolated archive through the real authenticated API, with known date/language/domain preferences.
2. Submit research with a stable idempotency key. Assert 202 only after run/job commit and verify the exact snapshotted request received by the real agent.
3. Observe queued → running → completed through the public status endpoint. Confirm that the real agent was called and that no fake/test assembly supplied the output.
4. Verify committed queries, global document/link identities, retained versions/chunks, findings and exact evidence relationships. Check source listing, null publication dates, filters and counts through the API.
5. Verify raw objects exist and compare their actual bytes/digests. Metadata/reference shape validation alone does not establish retained byte integrity.
6. Replay the same request key and terminal attempt: no duplicate logical submission or replayed result writes. Incompatible key reuse returns conflict.
7. Exercise a temporary real-service failure or controlled equivalent at the deployed boundary: safe error, bounded attempts/backoff and eventual recovery where appropriate.
8. Exercise invalid-version/references/output, authentication failure, archive pause/archive during execution and worker interruption. Verify no partial writes, cross-archive leaks or accepted stale attempt.

### Deployed security and operations cases

1. Confirm secrets are supplied securely, absent from tracked files/public responses and not echoed in operational logs or agent failure bodies.
2. Verify deployed CORS origins, proxy trust, host/port binding and service TLS/auth boundaries. An untrusted forwarded header cannot choose a trusted identity or client address.
3. Verify agent destination configuration, disabled redirects, response/time limits and the selected storage-reference policy with the actual deployment.
4. Confirm Production OpenAPI exposure follows configuration, unexpected exceptions stay generic and health/readiness reveal no credentials.
5. Confirm API readiness against the intended hosted database and independently verify migration compatibility; connectivity readiness alone is insufficient.
6. Confirm restart/recovery, persistent storage and the team's migration/rollback/backup procedures through DevOps evidence before approving service operation.

### Completion record

Close each RB gate only by adding evidence, not by changing its label optimistically. The acceptance record must contain:

| Field | Required content |
| --- | --- |
| Candidate | Exact API/agent/contract commits and environment |
| Owner/reviewer | Named executor and human acceptance reviewer |
| Checks | Concrete cases/commands/jobs, actual outcomes and tested versions |
| Evidence | Sanitized PR/job/test/log links and relevant run/archive IDs |
| Findings | Defects and their fixes/retests, or explicit residual limitations |
| Decision | Accepted/rejected/pending per gate, date and reviewer |

No gate in this register is currently accepted for release. Keep these blockers visible after any foundation merge and before any later deployment or release decision.
