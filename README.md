# TemanBicara Cold API

.NET 10 API for account registration, one-time registration email verification,
username/password login, persistent bearer authentication, and account-wide logout.
Teacher and Student registration is public. Email verification does not prove school
affiliation. Feature authorization policies and synchronization are not implemented.

## Server setup

`TemanBicaraAPI.MobileAPI/Database/001-auth.sql` records the previously applied authentication
schema change. Do not rerun it on the current database. For a fresh database only, apply it
once to the previously aligned empty Cold DB. The script
aborts when affected records exist; it does not reset data or migrate populated integer IDs.
It converts account IDs and dependent foreign keys to UUID, seeds the two roles, and
adds verification state plus `users.auth_version`. PECS sessions remain activity records.

Configure `TemanBicaraAPI.MobileAPI/appsettings.json` for the database, authentication,
and SMTP settings. This real configuration file is kept locally and ignored by Git.
On a fresh checkout, copy `appsettings.Auth.example.json` in the same directory to
`appsettings.json`, then replace the placeholders. The example is not loaded automatically.

ASP.NET Core loads configuration using its standard defaults. Later sources override
matching keys: appsettings.json, appsettings.{Environment}.json, Development user secrets,
environment variables, then command-line arguments. There is no custom configuration helper.
User secrets under `TemanBicaraAPI-LocalDevelopment` remain unchanged; remove duplicate keys
yourself if you want the appsettings.json values to take effect during Development.
Environment keys use `__`, for example `ConnectionStrings__ColdDb`.

The real appsettings.json is included by normal .NET build/publish behavior. Keep published
artifacts containing credentials private, or supply production values through environment
variables and a suitable deployment settings file. Do not commit credentials.

- `Auth:VerificationHashKey`: Base64 of at least 32 cryptographically random bytes.
- `Auth:KeyRingPath`: persistent directory accessible only to the API identity, outside Git.
  Windows encrypts persisted keys with DPAPI. Non-Windows production additionally requires
  `Auth:KeyCertificatePath` (PKCS#12) and `Auth:KeyCertificatePassword` in secrets.
- SMTP requires `StartTls` or `SslOnConnect` and valid sender credentials. For local mail
  capture only, use `localhost`, port `1025`, security `None`, and no SMTP credentials.
- API startup validates database/key settings. Missing SMTP settings produce a recoverable
  pending-verification response, never a fake delivered email or a verification bypass.

Run `dotnet run --project TemanBicaraAPI.MobileAPI`. Use HTTPS with a certificate trusted by test devices. Configure the
mobile app's `EXPO_PUBLIC_API_URL` to the device-reachable API address, not the phone's
own localhost. No database connection string belongs in the mobile app.

## HTTP contract

Password recovery is documented in [PasswordRecovery.md](docs/PasswordRecovery.md).
Before using it, apply the additive `TemanBicaraAPI.MobileAPI/Database/002-password-recovery.sql`
migration. It adds a separate recovery table and preserves existing accounts.

All paths are prefixed `/auth`. JSON uses camelCase.

| Method / path | Input | Success |
| --- | --- | --- |
| POST register | username, password, confirmPassword, email, role (`Teacher`/`Student`) | 202 verificationTicket, masked email, resendAfter |
| POST verify-email | verificationTicket, code | 204 |
| POST resend-verification | verificationTicket | 200 replacement challenge |
| POST login | username, password | 200 credential, account (`id`, `username`, `role`) |
| GET me | Bearer credential | 200 current account |
| POST logout | Bearer credential | 204 |
| POST forgot-password | registered email | 202 recoveryTicket, masked email, resendAfter |
| POST verify-password-reset | recoveryTicket, six-digit code | 200 resetToken, expiresIn |
| POST reset-password | resetToken, password, confirmPassword | 204; prior credentials invalidated |

Errors include `code` and an Indonesian `message`. Validation errors include `errors`.
Unverified login returns 403 `verification_required` and a new verification ticket.
SMTP failure returns 503 `email_delivery_failed` with a ticket for resuming verification.
The pending account remains; sign in with its password to resume after app restart.
Resend failures can include `resendAfter`. Incorrect login uses a generic 401 response.

Codes are six digits, valid for 10 minutes, with five failed attempts and a 60-second
resend cooldown. Tickets expire in 30 minutes. Code hashes are HMAC-SHA256 and passwords
use ASP.NET Core PasswordHasher. Username is 3–32 ASCII letters/digits/underscores;
password is 8–128 characters with no composition rule. Email is required for both roles.

The persistent credential is an opaque Data Protection ticket, not a JWT. Send it as
`Authorization: Bearer <credential>`. It has no fixed expiration by product decision.
Each request checks current email verification, password stamp, role, and auth version
against the database. Logout conditionally advances the credential's version once;
all old account credentials then fail. A copied credential remains usable until
revocation, password change, account removal, or key revocation. Preserve old key-ring
keys across application restarts and deployments. This is not an OAuth/OIDC issuer.

The frontend restores local access offline. Offline logout queues a revocation using
the old account credential; the API can only apply it after reconnection. The app must
drain that queue before a new explicit login. Activity history is not deleted.

## Project structure

The request flow follows STTB: controller -> MediatR validation pipeline -> handler -> EF Core.

| Project | Contents |
| --- | --- |
| MobileAPI | HTTP controllers, authentication handler, error middleware, configuration, Program.cs |
| TemanBicaraAPI | RequestHandlers/Auth, Validators/Auth, Commons authentication/email helpers and pipeline behavior |
| Contracts | RequestModels/Auth, ResponseModels/Auth, DTOs/Auth, public error model |
| Entities | EF CLI-generated entities and TemanBicaraDbContext |

MobileAPI references the base library and Entities; the base library references Contracts
and Entities. Contracts and Entities do not reference the host. Controllers dispatch through
MediatR and obtain current-account/logout identity from authenticated claims.

Program.cs remains in the MobileAPI root. Authorization policies remain empty.
Passwords, credential payloads, and service exceptions are internal library types rather
than public request/response contracts. Existing credentials retain the same Data Protection
application name, purposes, and JSON payload format.

## EF Core CLI scaffolding

From the repository root, restore the pinned 10.0.14 CLI with `dotnet tool restore`.
Configure the server secrets described above before running:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef dbcontext scaffold Name=ConnectionStrings:ColdDb Npgsql.EntityFrameworkCore.PostgreSQL --project TemanBicaraAPI.Entities/TemanBicaraAPI.Entities.csproj --startup-project TemanBicaraAPI.MobileAPI/TemanBicaraAPI.MobileAPI.csproj --context TemanBicaraDbContext --namespace TemanBicaraAPI.Entities --context-namespace TemanBicaraAPI.Entities --output-dir . --context-dir . --schema public --no-onconfiguring
```

Existing generated files cause scaffolding to stop; review changes and back up custom work
before deliberately adding `--force` for regeneration. Do not hand-edit generated files.
Use partial classes or OnModelCreatingPartial for future custom mapping extensions.

Scaffolding reads the database; it does not apply migrations. No connection string is
generated in source. PostgreSQL expression indexes for normalized username/email uniqueness
cannot be scaffolded: preserve them in the database and its SQL schema history. Do not use
EnsureCreated or automatic EF migrations against the Cold DB. Generated provider metadata
also describes Supabase-managed enums/extensions; it is not authorization to recreate them.

Verification uses an explicit transaction and parameterized FOR UPDATE query. Resend and
logout use conditional database updates to preserve their concurrency guarantees.

## Validation

`dotnet build TemanBicaraAPI.slnx --nologo`

The authentication checks require a disposable local PostgreSQL database. They refuse
non-loopback hosts and any database name other than `temanbicara_auth_tests`. They delete
and recreate only that designated test database; never point them at shared account data.

```powershell
docker run --detach --name temanbicara-auth-tests --publish 127.0.0.1:55439:5432 --env POSTGRES_HOST_AUTH_METHOD=trust --env POSTGRES_DB=temanbicara_auth_tests postgres:17
$env:TEMANBICARA_TEST_DB = "Host=127.0.0.1;Port=55439;Database=temanbicara_auth_tests;Username=postgres"
dotnet run --project tests/AuthChecks/AuthChecks.csproj --no-launch-profile
docker stop temanbicara-auth-tests
```

The checks cover PostgreSQL-backed handlers, validation, persisted credentials, legacy
payload compatibility, concurrent verification/resend/logout, authentication HTTP endpoints,
and MailKit delivery to a loopback SMTP capture server. Each handler/HTTP request gets its
own DbContext. The fixture omits Supabase-managed extensions and creates the application
tables plus authentication constraints/indexes in the isolated test database.

To check live mappings without writing data:

```powershell
dotnet run --project tests/AuthChecks/AuthChecks.csproj --no-launch-profile -- --live-schema
```

This uses the standard appsettings files, Development user secrets, environment variables,
and command-line settings in that priority order, and performs read-only queries against all eight public
application tables and authentication columns. It does not replace external SMTP or mobile
device verification. No test credentials are inserted into the shared Cold DB.

`TemanBicaraAPI.MobileAPI/Authorization/Policies/` intentionally contains only `.gitkeep` until feature policies
are implemented. There are no dashboard, PECS, tracking, or sync endpoints in this change.
