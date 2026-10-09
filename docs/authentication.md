# Authentication, setup and branch access

This milestone implements a single business/database, one immutable owner account, staff with direct permission grants and authorized branch memberships. Every bearer request resolves current permissions and session state from PostgreSQL. Role templates, email invitations, MFA and forgotten-password recovery are later extensions; no fake provider workflow is present.

## Configuration

Inject these values through the environment or your IDE's private run configuration:

- `ConnectionStrings__Invora`: PostgreSQL connection string for the selected business database.
- `Auth__SigningKey`: cryptographically random signing secret with at least 32 bytes, not a password.
- `Auth__BrowserOrigin`: exact browser origin, e.g. `https://invora.example.com`, without trailing slash. HTTP is allowed only in Development.
- `Auth__BootstrapKey`: independent random setup secret with at least 32 characters. Required only for initial registration; remove it after setup.
- `Auth__Issuer` / `Auth__Audience`: optional overrides; defaults `Invora` / `Invora.Web`.
- `ASPNETCORE_ENVIRONMENT=Development`: local HTTP/Swagger only.

Do not commit real values to appsettings or .env.example. The API fails startup for missing signing/origin configuration. Production uses HTTPS behind the trusted same-origin proxy. Do not expose the database publicly or allow clients to select connection strings.

## Migration and start

```sh
dotnet tool restore
dotnet ef database update --project src/Invora.Infrastructure
dotnet run --project src/Invora.Api
```

The migration command consumes `ConnectionStrings__Invora`. The design-time fallback is for generating models only, not a configured application database. Use the normal .NET 10 installation; for this temporary session substitute `/tmp/invora-dotnet/dotnet` and set `DOTNET_ROOT=/tmp/invora-dotnet`. Startup never auto-migrates or seeds users. The InitialFoundation and AccessAuditDetails migrations include business/branch/identity tables only; stock/financial schemas come after model review.

## Initial setup

`POST /api/v1/auth/register` requires `X-Invora-Bootstrap` matching the private setup key:

```json
{
  "tradeName": "Smart Plaza",
  "branchCode": "SAR",
  "branchName": "Sarangpur Main",
  "ownerLogin": "owner",
  "ownerName": "Business Owner",
  "password": "<unique-strong-password>"
}
```

The server validates details, atomically creates the singleton business, first branch, owner, membership and session, and returns a SessionView. Concurrent/second setup is rejected. Passwords require 12–128 characters including a letter and digit and are salted/hashed using ASP.NET Identity's PasswordHasher with 210,000 PBKDF2 iterations. The owner has the full permission catalog. Owner deactivation/demotion is blocked through staff management; ownership transfer requires a separate future audited workflow.

## Sessions

`POST /api/v1/auth/login` accepts login/password, returns a 10-minute HMAC-SHA256 JWT and sets `invora.refresh`. JWT issuer, audience, algorithm, signature and expiry are validated. A database AuthSession has a fixed 14-day absolute expiry. Refresh credentials are random 48-byte opaque values; only SHA-256 hashes persist. Each refresh consumes the old credential and creates a replacement in a locked transaction. Replay revokes the whole session and invalidates its access JWT and replacement refresh credential.

Refresh cookie is HttpOnly, SameSite Strict, restricted to `/api/v1/auth`, and Secure except in Development. `POST /auth/refresh` and `/auth/logout` require the exact configured Origin and `X-Invora-CSRF: 1`. No cross-origin CORS policy is enabled. The frontend must coordinate refresh calls: concurrently reusing one refresh credential intentionally triggers replay defense. Browser access JWT stays in memory; do not use localStorage.

`GET /auth/me` returns current user/grants/membership without password hashes or tokens. `POST /auth/change-password` requires a bearer JWT and current/new passwords. Success revokes every session and requires login again. Logout revokes the session even if supplied a previously consumed refresh credential. Staff disable or access changes revoke their sessions immediately. Login failures lock a known account for 15 minutes after five failures. Auth operations have an IP-based limit of 20 requests/minute; configure trusted proxy handling at deployment before using forwarded addresses.

Identity mutations use a PostgreSQL transaction advisory lock to serialize setup, password verification, staff access changes and rotation across API instances. This conservative single-business foundation favors correctness; move to session/user-scoped locks if measured login throughput warrants it. No external I/O occurs while holding the lock.

## Business, branches and staff

All routes below have `/api/v1` prefix:

| Endpoint | Authorization |
|---|---|
| GET `/business` | Any authenticated staff |
| PUT `/business` | `business.settings`; tradeName/timeZone/FY month |
| GET `/branches` | Only active branches with current membership |
| GET `/branches/{id}` | Same membership; inaccessible IDs return 404 |
| POST `/branches` | `branches.manage`; creator and active owner receive membership |
| GET `/permissions` | `users.manage`; stable grant codes |
| GET `/users?page=1&pageSize=25` | `users.manage`; paginated, max pageSize 100; delegated manager's branch scope |
| POST `/users` | `users.manage`; login/displayName/password/permissions/branchIds |
| PUT `/users/{id}/access` | `users.manage`; permissions/branchIds/isActive; owner protected |

Staff creation uses an explicit administrator-assigned initial password in this API milestone; staff can immediately change it. No email delivery or default shared password. A delegated manager may grant only permissions they currently hold and active branches in their own memberships. They cannot modify the owner or a user whose existing permissions/branches exceed their own scope. Unknown permissions and nonexistent branch IDs are rejected. Changing access appends an audit event and revokes existing sessions.

`ICurrentUser` exposes verified UserId, current Permissions and AuthorizedBranchIds. Domain/business use cases must check resource BranchId against this set; authentication alone does not authorize an inventory/payment resource. Those checks will be implemented with the resource workflows in the next milestone. No tenant claims or tenant selectors exist for this single-business database.

## Audit and limits

Successful setup, login, branch creation, business edits, staff creation/access changes, password changes, logout and refresh replay append AccessAudit records. Staff grants and business edits retain explicit before/after JSON snapshots excluding all passwords and tokens. Failed known-account login counters/audit are committed despite returning an authentication error. Audit writes share the transaction with the identity change. DbContext guards reject audit updates/deletes; a deployment runtime database role must additionally deny direct audit updates/deletes, since EF guards do not protect raw SQL/DB administrators.

All API responses have `Cache-Control: no-store`. Expected failures return sanitized ProblemDetails: 400 validation, 401 credentials/session, 403 grants/owner protection, 404 inaccessible resource, 409 duplicate/setup conflict, 429 rate limit. Validation does not echo passwords. No recovery email/MFA/role-editor/UI is claimed complete.
