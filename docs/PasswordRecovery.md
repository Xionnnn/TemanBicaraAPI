# Password recovery

Implemented November 10, 2026. Recovery is separate from registration email
verification. It does not change account UUIDs, roles, email verification state,
classroom membership, or mobile SQLite data.

## HTTP contract

All three endpoints are anonymous, use the existing `auth` rate limiter, and accept
JSON. The current limit is configured by `Auth:RequestsPerMinute` per client IP.

| Endpoint | Request | Success |
| --- | --- | --- |
| `POST /auth/forgot-password` | `{ "email": "<registered email>" }` | 202 `{ recoveryTicket, email, resendAfter }` |
| `POST /auth/verify-password-reset` | `{ "recoveryTicket": "<challenge UUID>", "code": "<six digits>" }` | 200 `{ resetToken, expiresIn: 600 }` |
| `POST /auth/reset-password` | `{ "resetToken": "<opaque token>", "password": "<new password>", "confirmPassword": "<same password>" }` | 204 |

Calling `forgot-password` again sends a replacement code after the cooldown. The
response email is masked. Unknown emails return 404 `email_not_found`, as explicitly
requested for this application; this discloses whether an email is registered.

Other errors include 400 `invalid_input`, `password_mismatch`, `invalid_reset_code`,
or `reset_expired`; 429 `resend_wait` or `rate_limited`; and 503
`email_delivery_failed`. A cooldown or SMTP failure can include `recoveryTicket`,
`email`, and `resendAfter` so the app can continue and retry without registering
another account. An SMTP failure does not mean a code was delivered.

## Persistence and concurrency

Apply `TemanBicaraAPI.MobileAPI/Database/002-password-recovery.sql` once before
running the feature. It adds `public.password_reset_challenges` without modifying
existing account records. It is safe to run again. It must not be confused with
the old, empty-database-only `001-auth.sql` migration.

The table enables row-level security and revokes PUBLIC, Supabase `anon`, and
`authenticated` access. It is server-owned state; no public client policy is
created. The API uses its existing privileged direct database connection.

There is one recovery row per account. Six-digit codes expire after 10 minutes,
allow five attempts, and have a 60-second resend cooldown. Codes are stored as
purpose-separated HMAC-SHA256 hashes using the existing verification key. A resend
replaces the challenge UUID and invalidates the previous code and reset grant.

A correct code is consumed and replaced by a random 256-bit reset token. Only the
token's SHA-256 hash is stored. The token expires after 10 minutes and can be
consumed once. Code verification and password replacement both check the password
stamp from the request's original account state.

Each recovery mutation locks the account row inside an explicit PostgreSQL
transaction, so parallel requests cannot lose attempt increments, bypass cooldown,
issue multiple reset grants, or consume the same grant twice. Password replacement,
the `auth_version` increment, and challenge removal commit atomically.

The new mapping is in a DbContext partial extension; scaffold-generated files are
preserved. No automatic startup schema migration or `EnsureCreated` is used against
the Cold DB.

The existing README scaffold command covers the entire public schema. A future
scaffold will therefore include this table. Before regenerating, consolidate its
generated entity, mapping, and DbSet with this extension rather than keeping
duplicate definitions.

## Password and account behavior

New passwords use ASP.NET Core `PasswordHasher`. Validation matches the current
registration implementation: nonblank, up to 128 characters, with matching
confirmation. The temporary one-character minimum already used for registration
is preserved; this change does not restore the earlier 15-character policy.

Existing bearer credentials fail after a reset because the password stamp changes
and `auth_version` increments. The user signs in again with the new password;
password recovery does not issue an authentication credential. An unverified
account remains unverified and must still finish registration verification.

Previously restored offline local access remains governed by the existing account
lifecycle. A password reset cannot remotely erase offline device data. On
reconnection, the API rejects old credentials and the client blocks online access.

## Verification

The existing `tests/AuthChecks` executable includes PostgreSQL-backed recovery
handlers and HTTP endpoint tests. It uses only the guarded, disposable local
`temanbicara_auth_tests` database. Coverage includes additive/idempotent migration,
unknown emails, normalization, hashed secrets, cooldown, expiry, five attempts,
resend replacement, new service instances, concurrent verification/resend/reset,
SMTP failure continuation, registration isolation, and old-password/credential
rejection after a successful reset.

The SMTP boundary uses the existing capture fake; the MailKit implementation is
also exercised against the suite's loopback SMTP server. No recovery email is
sent to a real account by these tests. Real SMTP delivery and native screen
interaction require separate device verification.
