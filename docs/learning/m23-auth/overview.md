# Milestone 23: Password-based Authentication

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does an operating system verify that a user really is who they claim to be, and how do you store authentication data so that stealing it doesn't let an attacker log in? What does "secure password storage" look like in practice, and why is hashing + salting not enough on its own?

## Scope

A single-process authentication subsystem built with the primitives OSEP Ch. 54 recommends for password-based authentication:

- **Cryptographic hash** (OSEP §54.4): never store plaintext passwords; store only the hash, so a leaked password file doesn't reveal the original password.
- **Salt** (OSEP §54.4, Morris-Thompson): per-user random salt concatenated to the password before hashing. Defeats rainbow-table attacks because every user has a different hash even with the same password.
- **Slow hash** (PBKDF2, also discussed under §54.4 dictionary-attack defence): intentionally expensive hash function (100k iterations of PBKDF2-HMAC-SHA256) so that each dictionary-attack guess takes a meaningful amount of time — making brute force / dictionary attacks infeasible.
- **Timing-safe compare** (Ch. 56 side-channel defence, also implicitly part of safe authentication): verify passwords with constant-time comparison so an attacker can't measure a few nanoseconds difference to leak the prefix of a correct password.
- **Fail-safe defaults** (OSEP §53.4, Saltzer-Schroeder): never echo the supplied password, return the same error message for "no such user" vs "wrong password" (so attackers can't enumerate usernames).

Exposed via HTTP routes:
- `/auth/register` — register a user (POST-like, query params)
- `/auth/login` — verify a user's password
- `/auth/dump` — show what's actually stored (proves it's hash + salt, never plaintext)
- `/auth/run?scenario=...` — smoke scenarios

Plus three demo scenarios:

| Scenario | What it shows | OSEP section |
|---|---|---|
| `register` | User registers; stored entry contains salt + derived hash, never plaintext | §54.4 |
| `login` | Login with correct vs wrong password; identical error message; verifies with timing-safe equal | §53.4 + §54.4 |
| `hashattack` | Two users pick the same password; their stored hashes differ (proves salt works); an attacker who steals the file and tries to match a hash against one user's salt still has to brute-force every user independently | §54.4 (Robert Morris / Ken Thompson) |
| `dictionary` | Top 5 common passwords tried against the file; each PBKDF2 verify costs ~50-100 ms — total attack budget for 100k guesses is hours, not seconds | §54.4 dictionary-attack defence |

## Slice

- **[s1-password-auth.md](./s1-password-auth.md)** — `PasswordHasher` (PBKDF2 + salt + timing-safe), `UserStore` (in-memory map of username → hash + salt), `/auth/*` HTTP routes, four scenarios.

## OSEP coverage

- **Ch. 53 Introduction to OS Security** — §53.4 design principles (economy of mechanism, fail-safe defaults, least privilege) applied to our auth layer; §53.3 CIA goals (confidentiality via salted hash, integrity via auth, availability left to OS).
- **Ch. 54 Authentication** — §54.4 password-based authentication: hash storage, salt, dictionary-attack defence via slow hash, why we don't store plaintext.
- **Ch. 55 Access Control** — only tangentially (login gate is a primitive form of authorization; full ACLs are out of scope for this slice).
- **Ch. 56 Cryptography** — §56.4 cryptographic hash functions (PBKDF2-HMAC-SHA256 is built on SHA-256, currently considered strong; SHA-3 would also be acceptable). §56.6 warns that an OS that has the key can decrypt — for password verification we don't store any key, only the hash, so even if the file leaks we cannot reverse it.
- **Ch. 57 Distributed System Security** — out of scope (we don't transmit over the network; we're a single-process local lab). §57.4 password authentication over the network is the natural follow-up: encrypt the password in transit with TLS, never send plaintext.

OSEP §54.4 sets the bar:
> "Store a hash of the password, not the password itself. ... by their nature, you can't reverse hashing algorithms, so the adversary can't use the stolen hash to obtain the password."

OSEP §54.4 also describes salts:
> "before hashing a new password and storing it in your password file, generate a big random number ... hash the result and store that. ... the attacker can no longer create one translation of passwords in the dictionary to their hashes."

And the warning about timing-safe comparison (implicit under §54.4 + side-channel discussion):
> "run each possible password through the hash once and store the results ... if the salt is 32 bits, that's 2³² different translations for each word in the dictionary"

## OSEP §-specific deviations

- **PBKDF2 iterations** (OSEP §54.4 "drastically slowing down"): we use 100k iterations of PBKDF2-HMAC-SHA256; matches OWASP 2024 minimum recommendation. This is the "slow hash" the chapter describes.
- **Plaintext over the wire**: the HTTP routes take the password in the query string, which an MITM could read. OSEP §57.4 makes clear that's wrong for real systems; TLS would fix it. Out of scope for this lab (single localhost process).
- **Rate limit / lockout** (OSEP §54.4 "shut off access ... or drastically slow down"): our `dictionary` scenario demonstrates the *defensive* side (each guess is slow due to PBKDF2). A real auth endpoint would also rate-limit by username. Not implemented.
- **Two-factor / multi-factor** (OSEP §54.4 + §54.5): not implemented. Pure password-only.
- **Account enumeration** (OSEP §53.4 fail-safe defaults + §54.10): we return a generic "invalid credentials" message rather than "no such user" vs "wrong password".
- **Persistence**: users are in-memory only. Restarting the server drops all users. A real system persists the hash database on disk; we keep it in a `ConcurrentDictionary`.
- **Group / role ACL** (OSEP §55.4 RBAC): not implemented. We don't distinguish capabilities beyond a registered user.
- **TLS handshake** (OSEP §57.5): not implemented. Single-process localhost.

## .NET mechanism

- `System.Security.Cryptography.Rfc2898DeriveBytes` — implements PBKDF2 with configurable iteration count, salt length, and derived key length. Uses HMAC-SHA256 under the hood.
- `System.Security.Cryptography.RandomNumberGenerator.Fill` — CSPRNG for salt generation (uses the OS's entropy source, which OSEP §56.5 notes is "the best source for operating system purposes").
- `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` — constant-time byte-array comparison. Avoids the short-circuit behaviour of `==` that leaks the longest matching prefix.
- `System.Collections.Concurrent.ConcurrentDictionary<string, AuthUser>` — thread-safe user store; the HTTP handler pool runs 8 threads so we need concurrent map operations.

## Learning sequence

1. Read OSEP §53.3 (CIA goals) and §53.4 (Saltzer-Schroeder principles). These motivate *why* auth matters and *what properties* a good auth layer should have.
2. Read OSEP §54.4 carefully — it's the only section this milestone covers in depth. Pay attention to the saline advice: hash, then salt, then slow-down.
3. Look at `PasswordHasher.cs` to see the PBKDF2 invocation. The parameters matter: 100k iterations, 16-byte salt, 32-byte derived key.
4. Look at `UserStore.cs`. Notice the in-memory `ConcurrentDictionary` and the timing-safe `Verify`.
5. Run the smoke scenarios in order: `register`, `hashattack`, `dictionary`, `login`. The outputs are designed to mirror OSEP §54.4's worked examples.

## Adjacent OSTEP chapters not addressed

- Ch. 53 §53.5 (system calls + access control primitives) — we don't expose auth via syscall; we keep it as an HTTP path.
- Ch. 54 §54.5 (authentication by what you have) and §54.6 (biometrics) — not implemented.
- Ch. 55 in depth (ACLs, capabilities, RBAC, mandatory vs discretionary) — we hint at ACLs via the "registered user" concept but don't build a general authorization layer.
- Ch. 56 in depth (symmetric crypto, PK crypto, full disk encryption) — we touch the cryptographic-hash subsection only.
- Ch. 57 entire (TLS, certificates, MITM, SSH, HTTPS) — out of scope for a single localhost process.

These are documented in `overview.md` as out-of-scope so future readers know what is *not* here.

## Key OSEP quotes

OSEP §54.4 (Morris & Thompson):
> "this concept was introduced in Robert Morris and Ken Thompson's early paper on password security. Why does this help? The attacker can no longer create one translation of passwords in the dictionary to their hashes. What is needed is one translation for every possible salt."

OSEP §53.4 (Saltzer-Schroeder on least privilege):
> "Give a user or a process the minimum privileges required to perform the actions you wish to allow."

OSEP §54.4 (don't store secrets):
> "Storing secrets like plaintext passwords or cryptographic keys is a hazardous business, since the secrets usually leak out. Protect your system by not storing them if you don't need to."
