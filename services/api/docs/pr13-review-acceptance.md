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

**UNVERIFIED:** a combined restore of nonempty real versioned-object references
with transactional remapping, restored API authorization and session revocation,
lease fencing, and exact raw download from the target. The automated
`RestoreAcceptanceTests` exercises these behaviors using disposable PostgreSQL
and S3 containers. On 3 November the test failed during fixture setup because
Testcontainers timed out connecting to `npipe://./pipe/docker_engine`; no
restore assertions ran. The production
certificate and encrypted snapshot have no verified independent recovery copy;
RPO/RTO and revocation reconciliation remain unset. This is partial restore
evidence, not a pass for the complete production recovery gate.

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
enabled. The worker security fix was deployed at commit `c79cf36`; an
authenticated synthetic run completed with 10 sources, no `theverge.com`
sources when that domain was excluded, and the report endpoint returned 404.
The synthetic archive was soft-archived; its test account remains. This confirms
the flag and current single-source flow, not full intended-provider coverage.
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
| PostgreSQL and object restore | UNVERIFIED | Encrypted real DB backup and isolated DB restore passed; synthetic versioned B2 copy passed. Combined end-to-end recovery remains unverified as described above. |
| Provider coverage | UNVERIFIED | Intended provider set and Render-to-provider end-to-end checks remain to be confirmed. |
| Report-disable gate | PASS | Live API setting is false pending provider and recovery gates; a new report request returned 404. |
| Container restore acceptance | FAIL (environment) | The explicit restore test could not initialize its S3 fixture: Docker engine connection timed out. Rerun it with a working Docker engine. |
| Full .NET suite | UNVERIFIED | The complete suite was not run; isolated PostgreSQL and API tests were run separately. |
| Human product/security/release approval | UNVERIFIED | PR review and release decisions remain with the team. |
