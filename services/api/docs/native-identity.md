# Native accounts with ASP.NET Core Identity

P1 implementation of the [backend completion plan](backend-completion-plan.md).
The owner selected ASP.NET Core Identity and email/password-only accounts on
2026-10-28. Registration does not send email, require confirmation or depend on
an external identity provider. No SMTP, issuer, audience or OAuth setup is needed.

## Ownership and schema

`auth.users` remains the account/profile source of truth. Existing UUIDs,
archive owners and memberships stay intact. `IdentityUserStore` implements the
password, email, security-stamp and lockout store interfaces over the existing
domain entity. Identity `UserManager`, `SignInManager` and the standard password
hasher own credential behavior. There is no custom token codec, duplicate
credentials table or global Identity role system. Archive permissions still
come from `auth.archive_members`; the domain has no framework dependency.

Migration `20261028193247_AddNativeIdentity` adds account policy/concurrency
fields to users and `system.data_protection_keys`: nine migrations and sixteen
tables excluding EF history. The original eight migrations are unchanged.
Unique `citext` email and unique normalized email protect concurrent mixed-case
registrations. Existing users keep their credentials, IDs and memberships; a new
registration cannot claim an existing email or replace its password. Legacy
users without credentials need a separate verified enrollment procedure.
Legacy rows without native normalized-login enrollment are denied at login,
including rows with a preserved hash in an unknown format; they do not cause
a password-parser error or get converted implicitly.

The internal `EmailConfirmed` field supports the Identity email store and remains
false on public registration. It is neither required for login nor a claim that
the address belongs to the user. `/me` does not expose a verification flag. Do
not grant membership, link accounts, or authorize recovery based only on a
matching unverified email address.

## Activation settings

Default `NativeIdentity:Enabled=false` preserves the foundation: no account or
`/me` handlers and 401 for protected requests. This change does not enable accounts
or apply migrations to Render. Incomplete enabled configuration fails startup.

| Setting | Purpose |
| --- | --- |
| `NativeIdentity__Enabled` | `true` to activate account routes |
| `NativeIdentity__RequestsPerMinute` | Auth requests per client IP; default 10, range 1–1000 |
| `NativeIdentity__KeyEncryptionCertificatePath` | Absolute mounted private-key PFX path; required outside Development |
| `NativeIdentity__KeyEncryptionCertificatePassword` | PFX password, supplied through secret configuration |

Configure the existing PostgreSQL connection, apply the ninth migration with
the existing EF tooling, mount the certificate, then enable Identity. Development
can run against an isolated database without a certificate. Disabled Identity
does not try to load an unused certificate.

## API contract

JSON bodies reject unknown properties. Registration accepts exactly email and
password; callers cannot choose user IDs, status, roles or a display name. A
default display name is derived from the email's local part, capped at 100
characters. That value is presentation only, never an authorization identity.

| Method / route | Request | Response |
| --- | --- | --- |
| `POST /auth/register` | `email`, `password` | 201 account profile; 400 invalid/duplicate registration |
| `POST /auth/login` | `email`, `password` | 200 token pair; generic 401 for invalid credentials, disabled or locked account |
| `POST /auth/refresh` | `refreshToken` | 200 token pair; 401 invalid, expired or revoked session |
| `POST /auth/logout` | Authenticated, no body | 204, revokes **all** account sessions |
| `POST /auth/change-password` | Authenticated: `currentPassword`, `newPassword` | 204 and session revocation; 400 invalid change |
| `GET /me` | Authenticated | `id`, `email`, `displayName` |

Register, then login immediately using the same body:

```json
{"email":"researcher@example.com","password":"A-long-unique-password1!"}
```

Login returns the framework's response:

```json
{"tokenType":"Bearer","accessToken":"<opaque access token>","expiresIn":900,"refreshToken":"<opaque refresh token>"}
```

Send `Authorization: Bearer <accessToken>` to `/me`, archives and research routes.
Tickets are protected Identity bearer tokens, **not JWTs**; access and refresh
tokens have different protection purposes and are not interchangeable. Access
lifetime is fifteen minutes and refresh lifetime seven days. Built-in refresh
tokens are not single-use: reuse is possible until expiry or stamp revocation.
Per-device session rotation/replay detection is future work.

Every authenticated request checks active account status and the security stamp
against PostgreSQL after framework token validation. Logout-all, password change
and disablement deny subsequent requests with previously issued credentials.
Already-running operations are not retrospectively cancelled. Existing archive
services also check active users and persisted membership independently.

There are no confirmation, resend, forgot-password or reset-password routes.
Changing a password requires the current password and a valid session. A user
who has forgotten it cannot use self-service recovery in this increment. A
future recovery method must prove account ownership; knowing an email is not
sufficient, and no unauthenticated administrative reset shortcut is provided.

## Security and browser integration

Passwords are 12–128 characters with the default Identity uppercase/lowercase/
digit/nonalphanumeric rules. Emails are capped at 254 characters. Five failed
password checks lock an account for five minutes. All `/auth` routes share the
per-IP request limit (429 on exhaustion) and a 32 KiB body cap. Verify proxy trust
and effective client IP on hosting. The limiter is per process; additional
replicas need a coordinated budget before scaling.

Auth and profile responses set `Cache-Control: no-store` and `Referrer-Policy:
no-referrer`. Passwords/tickets/hashes never appear in public profiles or logs.
Do not enable EF sensitive-data logging or authentication request-body logging.

API bearer mode issues no browser cookies. Prefer a frontend BFF with an
HttpOnly session cookie and server-held API tokens for the browser product;
direct browser integration should hold tokens in memory and handle reload/
re-authentication rather than defaulting to localStorage. BFF/cookie integration,
MFA, invitations, profile/email changes and account deletion are separate work.

## Keys, backups and release

Data Protection keys live in PostgreSQL, not an ephemeral container filesystem.
Outside Development the configured certificate encrypts them using the
framework's `ProtectKeysWithCertificate`, including restart decryption support.
Never promote a development database containing plaintext keys into Production.
Back up the DB and private-key certificate/password through separate secure
storage: a DB backup alone cannot restore encrypted sessions. Losing keys
requires deliberate global session invalidation.

Certificate rotation must retain old decryption certificates through the life
of retained keys/tokens. A multi-certificate rotation procedure is not automated
here. Test rotation before changing the live certificate. The Down migration
refuses to discard enrolled account fields or persistent keys; production
rollback uses compatible application code and preserves the forward schema.

Before activation verify schema/history, mounted certificate, frontend forms,
CORS/proxy settings, registration/login, token expiry/revocation, restart and
cross-archive isolation. Record exact deploy identities. Public signup abuse
controls and a recovery policy remain product acceptance concerns; no email
service is an activation prerequisite under the owner's selected scope.

## Verification: 2026-10-28

Candidate: the email/password-only implementation on `codex/native-identity`.
No hosted migrations, deployment, main-branch changes or CI/CD changes were made.

| Gate | Result / evidence |
| --- | --- |
| Full backend suite, PostgreSQL 16 | **PASS:** `dotnet test services/api/OrionSearch.sln --configuration Release --no-build --no-restore`, 374 passed, zero failed/skipped, 6m05s; `identity-final-accepted-scope.trx` |
| Identity, migration and OpenAPI checks, PostgreSQL 18 | **PASS:** `ORIONSEARCH_TEST_POSTGRES_IMAGE=postgres:18-alpine`, test filter `FullyQualifiedName~NativeIdentity\|FullyQualifiedName~ArchiveOpenApi`, 27 passed, zero failed/skipped; `email-password-postgres18.trx` |
| Migration compatibility | **PASS:** fresh initialization, upgrade preserving legacy user/archive/membership, idempotent reapplication, constraints and guarded rollback exercised in the suites |
| Encrypted key continuity | **PASS:** actual PFX configuration encrypts stored keys and a new provider decrypts them; HTTP refresh also survives host restart |
| Release build and publish | **PASS:** Release compilation during test run; `dotnet publish --configuration Release --no-build --no-restore` into a fresh external output directory |
| Formatting / model | **PASS:** `dotnet format --verify-no-changes --no-restore`; `dotnet ef migrations has-pending-model-changes` reports no changes |
| Dependency audit | **PASS:** `dotnet list services/api/OrionSearch.sln package --vulnerable --include-transitive` reports no known vulnerable packages from the configured sources |
| New container / hosted account acceptance | **UNVERIFIED:** no new container build, Render activation, frontend integration or real hosted registration was performed in this implementation task |

TRX files are retained outside the repository in the operator's task-results
directory. Historical checks for the superseded email-confirmation design do
not establish readiness of this final scope. The legacy-hash regression first
reproduced a 500 response, then passed with 401 after the enrollment guard was
added; no legacy password hash is overwritten or interpreted as a new account.

## References

- [Microsoft: Identity API authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
- [Microsoft: Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
