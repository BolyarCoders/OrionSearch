# PR #13 review: product decision and acceptance evidence

Review response, 3 November 2026. This record is scoped to the reviewed feature
branch and the checks described below; it is not production release approval.

## Product decision: date and language preferences

The project owner explicitly requested removing the saved research date and
language filters on 2 November, then requested AI selection of English sources.
Accordingly, existing `dateFrom`, `dateTo` and `languages` values are retained
and validated for compatibility but are deprecated and do not constrain research
discovery. Discovery uses the providers' 90-day rolling limit, and the model
selects English using titles and available summaries; unknown classifications
are excluded. Full article language and a nonempty result are not guaranteed.
Source-list date and language filters remain independent and continue to apply
to stored metadata. Reverting this decision requires an explicit compatible
product/contract change rather than silently re-enabling old behavior.

Archive create/update OpenAPI descriptions disclose the deprecation. A separate
frontend change, [PR #14](https://github.com/BolyarCoders/OrionSearch/pull/14),
labels these settings as inactive and prevents edits while preserving saved
values. It targets `feature/frontend` and is not yet merged or deployed.
Backend and frontend changes must be released together. Consumers outside this
repository must be notified before the behavior change is considered accepted.

## Security regression checks

- The worker filters excluded source and publisher domains, including uncertain
  Google News wrappers when domain rules are present, before source metadata is
  sent to the language classifier. The same eligibility function is reused at
  ingestion. `test_excluded_domains_never_reach_language_classification`
  captures the actual classification prompt and checks excluded content is not
  present.
- Report request title, description and objective are serialized as a JSON data
  field. The prompt explicitly identifies both request context and article text
  as untrusted and instructs the model not to follow instructions in them.
  `test_report_prompt_serializes_untrusted_request_context` checks the boundary.
  Prompt separation mitigates injection; it cannot guarantee model compliance.

## Restore and storage checks

On 3 November, PostgreSQL 18 `pg_dump` exported the hosted database into memory,
and the snapshot was encrypted with Windows CurrentUser DPAPI. A decryption and
SHA-256 round-trip succeeded. The stored snapshot is under the operator's
`OrionSearch/protected-backups` directory; its key is tied to that Windows user.
The encrypted snapshot was restored with `pg_restore --exit-on-error` into a
new local PostgreSQL 18 database. The restored database contained 12 migrations,
8 archives, 15 runs and 26 documents, with zero orphaned evidence references.
The temporary database was dropped afterward and Render's database IP allowlist
was restored to empty. The production service was not drained; PostgreSQL's
snapshot is database-consistent, but cross-store consistency is not established.

The restored database contained zero version-level storage references and zero
archive raw-storage references. Separately, a synthetic object was written to
the existing private Backblaze bucket, read by its immutable version, re-uploaded
under a distinct reference, and read again; both byte sequences and SHA-256
hashes matched. Two synthetic versions remain in the bucket. The local probe
removed the HTTP `Expect` header to accommodate the operator's network, so it is
not proof of the unmodified Render-to-B2 upload path or an independent backup.

The combined `RestoreAcceptanceTests` was rerun successfully after the operator
started Docker. It used disposable PostgreSQL and versioned S3-compatible
containers with synthetic accounts, jobs, evidence and an object. It exercised
real dump/restore, object export/re-upload and reference remapping, restored
authorization and exact raw download, session revocation and stale lease fencing.
Command: `dotnet test services/api/tests/OrionSearch.Tests/OrionSearch.Tests.csproj
--filter FullyQualifiedName~RestoreAcceptanceTests --no-restore`; 1 passed.
The earlier fixture setup attempt failed because Docker was unavailable; no
assertions ran in that attempt.

**UNVERIFIED:** a combined restore of nonempty real production object references,
which were absent from this snapshot. The production certificate and encrypted
snapshot have no verified independent recovery copy; RPO/RTO and revocation
reconciliation remain unset. The synthetic integrated test and real snapshot
restore are strong evidence but are not complete production recovery sign-off.

## Provider and rollout gates

As of this review, live Python configuration uses Hacker News only. Google News,
Hacker News and arXiv responded with HTTP 200 from the operator's workstation on
3 November, but workstation access is not Render-to-provider verification. The
intended production provider set has not been confirmed and tested end to end;
earlier Render Google News requests returned 503. Do not treat a single-source
run as proof of broad coverage. The project owner stated on 3 November that
another teammate will address provider selection and verification.

The live API setting `ResearchWorker__IncludeReport` was changed to `false` on
3 November and the existing API commit was redeployed. Discovery remains
enabled. The worker security fix was initially deployed at commit `c79cf36`; an
authenticated synthetic run completed with 10 sources, no `theverge.com`
sources when that domain was excluded, and the report endpoint returned 404.
The synthetic archive was soft-archived; its test account remains. This confirms
the flag and current single-source flow, not full intended-provider coverage.
Follow-up commit `c14504d` also rejects excluded publisher domains when a
publisher name is absent, and was deployed after its regression test passed.
An authenticated synthetic request directly to that deployed worker returned
10 sources, no `theverge.com` URLs, and no report when not requested. This
direct test did not create an application run or verify complete provider coverage.
Keep reports disabled until the intended providers and complete recovery
acceptance are verified. Previous report records remain stored; this flag stops
requesting new reports and does not erase existing data. Feature branches and
Render's manually deployed commits can diverge from `main`; releases must be
reconciled before enabling auto-deploy.

## Checks and open gates

| Gate | Status | Evidence / remaining work |
| --- | --- | --- |
| Worker security and contract tests | PASS | Python suite: 150 tests on the security-change working tree. |
| Source-list filters and OpenAPI | PASS | 42 targeted .NET tests passed against an isolated PostgreSQL 18 instance, including date boundaries, unknown publication dates and language filters. |
| Frontend | PASS, not released | TypeScript, 59 tests and the production build passed for PR #14. |
| Synthetic combined DB/object restore | PASS | `RestoreAcceptanceTests`: 1 passed with Docker, including access, session and lease checks. |
| Production recovery readiness | UNVERIFIED | Real DB snapshot restore and synthetic B2 copy passed; independent keys/backups, nonempty production object remapping and RPO/RTO remain unverified. |
| Provider coverage | UNVERIFIED | Intended provider set and Render-to-provider end-to-end checks remain to be confirmed. |
| Report-disable gate | PASS | Live API setting is false pending provider and recovery gates; a new report request returned 404. |
| Full .NET suite | PASS | `dotnet test services/api/OrionSearch.sln --configuration Release --no-restore`: 478 passed, 0 failed, 0 skipped on `c14504d`. |
| Human product/security/release approval | UNVERIFIED | PR review and release decisions remain with the team. |
