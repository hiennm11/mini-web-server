# Milestone 23.3: Role-Based Access Control (OSEP Ch. 55)

## Question

Once we know who a user is, how does the OS decide what that user is allowed to do? What is the difference between ACLs, capabilities, and RBAC?

## Scope

M23.3 implements the **Role-Based Access Control (RBAC)** pattern OSEP §55.6 calls out as particularly valuable for organizations with multiple user types. Five pieces:

- **`UserStore.Role`** — enum `User | Admin`. Each `AuthUser` carries a role.
- **`UserStore.Register(username, password, role = User)`** — caller can ask for `?role=admin` at registration. Default is `User` (Saltzer-Schroeder §53.4 *least privilege*).
- **`UserStore.GrantRole(username, role)`** — promote / demote an existing user.
- **`UserStore.AuthenticateWithRole(username, password, requiredRole)`** — combined password-verify + role-gate. Returns false if either the password is wrong OR the role is insufficient (no info-leak).
- **`/protected/secret`** — gated route that requires Admin role + correct password. Returns 403 (`forbidden`) for any failure; 200 + the "secret" body for Admin + correct password.

Exposed via HTTP routes:

| Route | Purpose |
|---|---|
| `/auth/register?user=X&pass=Y&role=admin` | register with optional role (default User) |
| `/auth/grant?user=X&role=admin` | promote / demote existing user |
| `/auth/role?user=X` | read a user's role (returns "unknown" rather than "no such user" for fail-safe defaults) |
| `/protected/secret?user=X&pass=Y` | requires Admin role; returns 403 on any failure, 200 with the secret body on Admin pass |

## OSEP coverage

- **Ch. 55** Access control, especially:
  - **§55.4** capabilities — we *don't* implement pure capability-based, but we *do* practice OS-side gates (the role check inside `AuthenticateWithRole` is a capability in spirit).
  - **§55.6** RBAC by Ferraiolo & Kuhn [FK92] — our `Role` enum + per-route gate is the simplest form OSEP mentions as "particularly valuable if certain users are permitted to switch roles depending on the task".
  - **§55.5** Mandatory vs Discretionary — our ACL-like table is discretionary (root owner-style auth decides who is Admin). Real RBAC has both flavors.
- **Ch. 53.4** Saltzer-Schroeder:
  - **Least privilege**: default `User` role; Admin must be requested explicitly.
  - **Fail-safe defaults**: identical 403 for "no such user" and "wrong password" and "wrong role".

## OSEP §-specific deviations

- **Two roles only** (User | Admin). OSEP §55.6 talks about "particular roles" in the plural; we model the smallest non-trivial case.
- **Grant isn't auth-gated** (anyone can grant). A real system requires an already-Admin caller to grant. We leave the gate unimplemented for the slice; the smoke demo runs before any grant check exists.
- **No groups / hierarchy**: OSEP §55.4 describes *groups* ("all your salespersons can read inventory"). We don't add groups.
- **In-memory only**: roles die with the server. Restart requires re-registration.
- **No `sudo` / `setuid` analog**: §54.7's `sudo -u webserver apache2` example doesn't translate to .NET (no setuid syscall equivalent). The closest is per-process impersonation, which is out of scope for a single-process lab.

## .NET mechanism

- `enum MiniWebServer.Host.MiniAuth.UserStore.Role { User, Admin }` — minimal role representation.
- `record AuthUser(DateTimeOffset CreatedAt, byte[] Salt, byte[] Hash, Role Role)` — existing user gets a 4th field; role defaults to `User` at registration.
- `ConcurrentDictionary.AddOrUpdate` with the two-factory overload (`addValueFactory`, `updateValueFactory`). Used by `GrantRole` to do a single atomic replace; the slot is either upgraded to the new role or kept as-is when the user is missing.
- `ConcurrentDictionary.GetOrAdd` for register (existing pattern from M23.1).
- The `/protected/*` gate composes the existing M23.1 PBKDF2 verify with the new role check — single round-trip, same dummy-PBKDF2 cost when the user is missing.

## Smoke evidence

Captured 2026-09-18:

```
GET /auth/register?user=alice&pass=p4ss&role=admin
-> 200 OK  user=alice  role=Admin

GET /auth/register?user=bob&pass=p4ss
-> 200 OK  user=bob  role=User

GET /protected/secret?user=alice&pass=p4ss
-> 200 OK admin=alice secret="the cake is a lie"

GET /protected/secret?user=bob&pass=p4ss
-> 403 Forbidden  denied: admin role required

GET /protected/secret?user=alice&pass=WRONG
-> 403 Forbidden  denied: admin role required
```

The third request (alice + WRONG) returns the **same** 403 response as the second (bob + correct). OSEP §53.4 fail-safe defaults: an attacker scanning usernames cannot tell from the response whether the username exists or the password is wrong or the role is wrong.

## OSEP points worth keeping

§55.6 makes the most sense:

> "if a company determines that all programmers should be granted access to a new library that has been developed, but accountants should not, RBAC would achieve this effect with a single operation that assigns the necessary privilege to the *Programmer* role."

Our slice implements the simplified two-role case. A multi-role RBAC (Programmer / Manager / Account / Auditor / Sysadmin …) would extend the enum and the `AuthenticateWithRole` check.

§53.4's least-privilege line is the most important for what we *didn't* build:
> "Give a user or a process the minimum privileges required to perform the actions you wish to allow."

We default to `User` (read-only-equivalent) and require an opt-in `?role=admin` query. Anything else would treat Admin as the global default and violate least-privilege.

§54.7's `sudo` example shows what *would* be the next step for a non-human principal. Real systems use `setuid` + capability dropping; we don't.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **Full ACL per file/object** (OSEP §55.3): we have one global role gate at one endpoint, not per-file permissions.
- **Capability-based systems** (OSEP §55.4 in depth): not implemented.
- **Mandatory access control** (OSEP §55.5): not implemented; everything is discretionary.
- **Multi-role switching mid-session** (OSEP §55.6 Manager/Programmer switch): not implemented.
- **Group inheritance** (OSEP §55.4 "members of a particular group"): not implemented.

These are documented as future work.
