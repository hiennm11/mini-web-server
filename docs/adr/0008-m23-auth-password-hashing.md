# ADR 0008: M23 Password Authentication (PBKDF2 + Salt + Timing-Safe)

## Status

Accepted

## Date

2026-09-18

## Context

The current `MiniWebServer.Host` repo covers OSTEP Part I (Virtualization), Part II (Concurrency), and Part III (Persistence) — every chapter is touched except a few that don't fit a single-process .NET HTTP server (Ch. 7 Process API, Ch. 14-17 segmentation).

The one OSTEP piece the roadmap has been missing is **Part IV: Security** (Ch. 53-57). Until M23, there was no auth, no access control, and no cryptographic primitives anywhere in the codebase. Every request to every route was anonymous and either accepted or rejected on uniform logic.

This ADR covers the M23 slice: a single, focused password-authentication subsystem backed by the OS-level primitives OSEP §54.4 describes. It does not cover TLS, certificates, full-disk encryption, capability systems, or distributed auth — those are explicitly future work (see `docs/learning/m23-auth/overview.md`).

## Decision

We implement **password-based authentication** with three classical OS primitives, and only those three:

### 1. PBKDF2-HMAC-SHA256, 100k iterations, 32-byte output

**Rationale**: OSEP §54.4 requires "drastically slowing down" the password verifier so dictionary attacks become infeasible. PBKDF2 is the most boring choice that meets the requirement:

- Ships in `System.Security.Cryptography` (no external dependency, no build-time cost).
- ITER value (100k) is the OWASP 2024 minimum and matches what bcrypt/scrypt/Argon2 would want for an analogous "interactive" tier.
- HMAC-SHA256 is itself a recommended cryptographic hash per OSEP §56.4 ("SHA-3 is a standard"). SHA-256 has no known cryptanalytic break.

**Alternatives considered**:

- Argon2id — strongest KDF per OWASP, but `System.Security.Cryptography` does not implement it. Adding `Konscious.Security.Cryptography.Argon2` would introduce a new dependency just for one milestone. Rejected.
- Scrypt — same situation, not in BCL. Rejected for the same reason.
- Plain SHA-256, salted — single SHA-256 is ~5 µs per call. A 100k-guess dictionary attack then takes ~500 ms total — tractable. Fails OSEP §54.4 "drastically slow down". Rejected.
- BCrypt — BCL doesn't ship it. Same dependency concern.

### 2. 16-byte random salt per user

**Rationale**: OSEP §54.4 lays out the salt concept and notes "32 or 64 bits" as the minimum. We pick **16 bytes = 128 bits**, which is far above what the chapter calls for and is the OWASP minimum as well. `RandomNumberGenerator.Fill` pulls from the OS entropy pool (OSEP §56.5's "gathering entropy").

**Why not a global salt**: a global salt would still let an attacker precompute a rainbow table once per application deployment. Per-user salt forces the attacker to recompute per user (2¹²⁸ work even if they steal the entire hash database).

### 3. `CryptographicOperations.FixedTimeEquals` for the verify path

**Rationale**: OSEP §54.4 doesn't explicitly call out timing side-channels, but the canonical password-equality bug (`if (storedHash.SequenceEqual(input))`) leaks the longest matching prefix via short-circuit byte-by-byte compare. The .NET BCL provides the constant-time primitive; we use it.

**Bonus — dummy PBKDF2 when the username is unknown**: OSEP §53.4 fail-safe defaults. We must not leak through *response time* whether a username exists either. So `UserStore.Login` runs a verify against all-zero salt+hash when the user is missing, burning the same ~70 ms as a real attempt. The "no such user" and "wrong password" code paths both consume ~70 ms of CPU.

### 4. Identical error message for both failure modes

**Rationale**: OSEP §53.4 fail-safe defaults, again. `Login` returns `false` and the `/auth/login` route always emits `body = "invalid credentials\n"` regardless of which branch was hit. An attacker can't enumerate valid usernames by scanning responses.

## Scope decisions

### In scope (M23 slice)

- `MiniAuth/PasswordHasher.cs`: PBKDF2 wrap + timing-safe Verify.
- `MiniAuth/UserStore.cs`: `ConcurrentDictionary<string, AuthUser>` store; counters for failed/successful logins.
- HTTP routes `register`, `login`, `dump`, `run?scenario=...`.
- Four demo scenarios: `register`, `login`, `hashattack`, `dictionary`, `clear`.

### Out of scope (deferred)

- **TLS / HTTPS** (OSEP §57.5): our requests go in plaintext. The slice doc calls this out: OSEP §57.4 says encrypting the password in transit is mandatory for real systems. Adding TLS would require cert management + `SslStream` wrapping + a one-time cert generation story — too broad for M23.
- **RBAC / ACL** (OSEP §55.4 / Ch. 55 §55.6): only the "registered user" bit exists. No roles, no privileges beyond authentication.
- **Multi-factor** (OSEP §54.4 + §54.5): password only. No TOTP / hardware token.
- **Account lockout / rate limiting**: not implemented. Slow hash is the only speed bump.
- **Persistence**: in-memory only. Restart drops users.
- **General access control**: ACLs/capabilities from Ch. 55 not built; the routes we have are protected by nothing more than "this is a /auth/* URL".

### Storage choice

In-memory `ConcurrentDictionary`. OSEP §54.4 doesn't require persistence; it just requires that what's stored be a hash + salt. We chose to keep the data in RAM rather than write to minifs.img because:

1. minifs is for general file-system teaching. Adding an `auth.db` would couple M23 to M12's filesystem semantics.
2. The authn invariant we want to demonstrate is "hash, not plaintext" — that holds equally well in RAM as on disk.
3. Persistence to disk would require a serialization format choice (JSON? protobuf?) which is out of scope.

## Consequences

### Positive

- **Closing the OSEP coverage gap**: M23 takes us from ~55% of OSTEP to ~57% (added 1 of 5 security chapters via Ch. 54 + supporting touch from Ch. 53).
- **Reusable primitives**: `PasswordHasher.Hash` / `Verify` could be re-purposed for protecting the minifs.superblock or any future "write secret to disk" feature.
- **Sets up M24+ Part IV slices**: RBAC, full-disk encryption, TLS all become incremental add-ons now that the user database + hash infrastructure exists.

### Negative

- **No tests**: M23 follows the pattern of M13-M22 (no custom unit tests for the new class). Coverage is exercised by the smoke scripts. If we wanted test coverage, we'd add to `tests/MiniWebServer.Host.Tests/Program.cs` — adding `Run("PasswordHasher.Hash never returns plaintext", ...)` etc. Deferred.
- **No real security**: the password is in the query string, visible in process logs. OSEP §57.4 explicitly forbids this for real systems.
- **Single-process only**: there's no concept of federated login (OAuth, Kerberos). M23 only models a single local password database.
- **No auditing**: failed logins are counted but not logged with timestamps or IPs.

## Verification

- 14/14 existing tests still pass (no test changes).
- Smoke script `/auth/run?scenario=hashattack` produces two users with different salts + different hashes for the same plaintext password (mirrors `overview.md` and `s1-password-auth.md`).
- Smoke script `/auth/run?scenario=dictionary` measures 71-101 ms per PBKDF2 verify call (matches the expected ~50-100 ms range for 100k iterations of HMAC-SHA256 on this hardware).
- Smoke script `/auth/login?user=ghost&pass=...` and `/auth/login?user=alice&pass=WRONG` produce byte-identical responses.

## Source Documents

- `docs/learning/m23-auth/overview.md` — milestone scope + OSEP § coverage.
- `docs/learning/m23-auth/s1-password-auth.md` — slice doc with smoke evidence + invariants.
- OSTEP §53.4 (Saltzer-Schroeder design principles).
- OSTEP §54.4 (password storage: hash + salt + slow down).
- OSTEP §56.4 + §56.5 (cryptographic hash + entropy gathering).
- OSTEP §57.4 (why this design is *not* sufficient for real distributed systems).
