# Slice 23.3: Role-Based Access Control + a protected route

## What this slice is

Three small changes that exercise OSEP §55.6 (RBAC):

1. **`UserStore.Role`** enum + a 4th field on `AuthUser`.
2. **`UserStore.AuthenticateWithRole`** — single call combining PBKDF2 verify (from M23.1) + role gate.
3. **`/protected/secret`** — gated route. Returns 200 with a synthesized "secret" body for Admin + correct password; 403 for any failure.

## The OSEP invariants M23.3 enforces

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | Default `User` (least privilege) | §53.4 | Register without `?role=` assigns User |
| 2 | Admin must be explicitly requested | §53.4 | Register with `?role=admin` requires the role=admin token |
| 3 | Wrong role → denied | §55.6 | Bob (User) hits `/protected/secret` → 403 |
| 4 | Wrong password → denied, identical response | §53.4 | Alice with WRONG password → same 403 body as Bob |
| 5 | Role can be granted later | §55.6 | `/auth/grant?user=bob&role=admin` upgrades role in place |

## Files

- `src/MiniWebServer.Host/MiniAuth/UserStore.cs` — `Role` enum, `AuthUser` record gains `Role`, `Register(..., role = User)`, `GrantRole`, `AuthenticateWithRole`, `GetRole`.
- `src/MiniWebServer.Host/Program.cs` — `/auth/grant`, `/auth/role`, `/auth/register?role=admin` query parsing, `/protected/secret` gate.
- `docs/learning/m23-rbac/overview.md` — milestone overview.

## Smoke evidence

Captured 2026-09-18 — see overview.md for the full transcript. Key sequence:

```
POST register alice as Admin   -> 200 OK user=alice role=Admin
POST register bob as User      -> 200 OK user=bob role=User
GET  /protected/secret alice + correct pass  -> 200 OK admin=alice secret="..."
GET  /protected/secret bob   + correct pass  -> 403 denied: admin role required
GET  /protected/secret alice + WRONG pass    -> 403 denied: admin role required
```

The last three responses are byte-identical in body — fail-safe defaults.

## OSEP points worth keeping

§55.6 lists the two motivating cases for RBAC:

1. Single privilege bump: "all programmers should be granted access to a new library" — one role assignment propagates to every user in that role.
2. Mid-session role switching: a Manager who needs to test code can switch to the Programmer role, temporarily gaining library access without taking on Programmer's admin privileges.

Our slice covers (1) — register-with-role + grant — but not (2). The latter would need a session token (M23.1 doesn't issue tokens; future work).

§53.4's fail-safe defaults show up twice in the demo:
- The dummy-PBKDF2 call when the username is missing absorbs timing information about whether the user exists.
- The identical 403 response absorbs information about *which* auth check failed.

Both are why the third 403 in the smoke (alice + WRONG) returns the same as the second (bob + correct): neither leaks whether alice exists or whether her password is "p4ss".

## What is *not* in this slice

Per the design rules in `overview.md`:

- **Mandatory access controls** (OSEP §55.5): we never override an Admin owner-set policy; real MAC systems do.
- **Group inheritance** (OSEP §55.4): a `Group` field would slot in beside `Role`.
- **Capability-based** (OSEP §55.4): a different model entirely — the OS hands the process a list of capabilities rather than checking per-request.
- **`sudo`-style non-human auth** (OSEP §54.7): closer to a process-identity model than a user-identity model; would need a different store.
