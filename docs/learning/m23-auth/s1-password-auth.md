# Slice 23.1: Password-based Authentication (OSEP Ch. 54)

## What this slice is

A single-process authentication subsystem built with the primitives that OSEP §54.4 recommends for password-based login:

- **Cryptographic hash** (PBKDF2-HMAC-SHA256, 100k iterations, 32-byte output): the only thing stored about a password. We never persist plaintext.
- **Per-user random salt** (16 bytes from `RandomNumberGenerator`): concatenated before the KDF call, so two users with the same password produce different hashes. Defeats precomputed rainbow tables.
- **Timing-safe constant-time compare** (`CryptographicOperations.FixedTimeEquals`): so an attacker measuring microsecond response differences cannot leak the prefix of a correct hash.
- **Fail-safe defaults** (OSEP §53.4): identical "invalid credentials" message for both "no such user" and "wrong password", plus a dummy PBKDF2 call when the username is unknown so the response time doesn't itself reveal which usernames exist.

All four requirements come straight from OSEP §54.4 and §53.4; the chapter doesn't pick an algorithm for us, so we picked PBKDF2-SHA256 (OWASP 2024 minimum) because it's a standard, .NET ships it in `System.Security.Cryptography`, and unlike a plain SHA-256 it's deliberately slow.

## The four invariants we want to demonstrate

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | Only hash + salt stored; never plaintext | §54.4 (Morris-Thompson) | `/auth/dump` shows the stored form |
| 2 | Per-user salt differs | §54.4 | Dump output shows two 16-byte salt columns differing across users |
| 3 | Same password → different hashes | §54.4 | Register `alice` & `bob` with `hunter2`, dump shows different hashes |
| 4 | Slow hash defeats dictionary attack | §54.4 "drastically slow down" | `/auth/run?scenario=dictionary` times each verify call at 50-100 ms |
| 5 | Identical error for both "no user" and "wrong pass" | §53.4 fail-safe defaults | Two `/auth/login?user=ghost&pass=...` and `/auth/login?user=alice&pass=WRONG` return the same body |

## Files

- `src/MiniWebServer.Host/MiniAuth/PasswordHasher.cs` — PBKDF2 wrap + verify (timing-safe).
- `src/MiniWebServer.Host/MiniAuth/UserStore.cs` — `ConcurrentDictionary<string, AuthUser>`; counts failed vs successful logins.
- `src/MiniWebServer.Host/Program.cs` (lines ~891-1095) — `/auth/*` route block.
- `docs/learning/m23-auth/overview.md` — milestone overview.

## The 5 routes

| Route | Method | Purpose |
|---|---|---|
| `/auth/register?user=X&pass=Y` | GET | Hash + salt store. Returns `200` on success, `409` if username taken, `400` if user/pass empty. |
| `/auth/login?user=X&pass=Y` | GET | Verify. Returns `200` if match, `401` otherwise — and `401` is identical for "no such user" vs "wrong password". |
| `/auth/dump` | GET | Show every stored `{username, createdAt, salt, hash}`. Proves nothing is plaintext. |
| `/auth/run?scenario=...` | GET | Demo scenarios (see below). |
| `/auth/run?scenario=clear` | GET | Test helper: clear the user store. |

## Demo scenarios

### `scenario=hashattack`

Registers two users with identical password `hunter2` and dumps them. Verifies that salts and hashes both differ — the textbook demonstration of why per-user salt matters.

### `scenario=dictionary`

Simulates a dictionary attack against a stolen-but-salted hash file:

1. Registers user `victim` with password `p4ssw0rd`.
2. For each of the top 5 common passwords (`123456`, `password`, `12345`, `qwerty`, `p4ssw0rd`):
   - Calls `PasswordHasher.Verify` and measures wall-clock time.
   - Calls `UserStore.Login` (which also runs a PBKDF2 verification internally, taking the same time).
   - Reports HIT / miss + cost in ms.

What you observe in the output is 5× 50-100 ms PBKDF2 verifies, with only the last one (`p4ssw0rd`) returning HIT. The wall-clock per-guess proves the KDF is doing real work, and the cost is the reason OSEP §54.4 recommends a slow hash for password storage instead of a single SHA-256 (which would be ~5 μs — 10000× faster, making a 100k-guess dictionary attack tractable in seconds instead of hours).

### `scenario=login` / `scenario=register` / `scenario=clear`

Lightweight route summaries used by the smoke tests in `CONTEXT.md`.

## Smoke evidence

Captured 2026-09-18 with PBKDF2-SHA256 / 100k iterations:

```
=== /auth/run?scenario=hashattack ===
scenario: hashattack
Two users, identical plaintext password 'hunter2':
  alice  salt=ed2a6a554fd78cfb474bf3fb769c10fd   hash=304fb8fc463d0a9268b70ac918aab938c129597c1e80e4f6b3206062c600a40f
  bob    salt=8f6d23fabd29c97bbc33761aff24bcba   hash=f46b63119ae68ce7b485e78531d54ee72058ca76759da8bfb45a9113b4fdf468
Observation: salts differ, hashes differ.
OSEP §54.4: per-user salt defeats rainbow-table precomputation.
```

```
=== /auth/run?scenario=dictionary ===
scenario: dictionary
  guess="123456"    -> miss  (101 ms)
  guess="password"  -> miss  ( 92 ms)
  guess="12345"     -> miss  ( 71 ms)
  guess="qwerty"    -> miss  ( 89 ms)
  guess="p4ssw0rd"  -> HIT   ( 71 ms)
```

```
=== /auth/login ===
GET /auth/login?user=alice&pass=hunter2  -> 200 OK
GET /auth/login?user=alice&pass=WRONG    -> 401 Unauthorized, body "invalid credentials\n"
GET /auth/login?user=ghost&pass=anything -> 401 Unauthorized, body "invalid credentials\n"
```

The two 401 responses differ only in their request id — `parsedRequest.Path` is `/auth/login?user=alice&pass=WRONG` vs `/auth/login?user=ghost&pass=anything`, but the bytes returned are identical ("invalid credentials\n" + Content-Length 19). OSEP §53.4 fail-safe defaults: the attacker can't tell from the body whether the username exists.

## OSEP points worth keeping

§54.4 lays out the three pillars clearly:

1. "Store a hash of the password, not the password itself." — we never touch plaintext after the verify call; `UserStore.AuthUser` holds only `Salt + Hash + CreatedAt`.
2. "Before hashing a new password and storing it in your password file, generate a big random number … hash the result and store that." — `RandomNumberGenerator.Fill` does this, 16 bytes per user.
3. "drastically slowing down the process of password checking after a few wrong guesses" — 100k PBKDF2 iterations is the OWASP 2024 minimum; if you really want to slow brute force, bump it to 600k (OWASP recommended) but every login then takes ~500 ms which is annoyingly slow for a localhost demo.

§53.4 adds two more invariants we follow:
- "Fail-safe defaults": we default to refusing access (401) on every error path.
- "Economy of mechanism": the auth layer is ~150 lines total — keep the trusted code base small.

§56.6 warns:
> "any time the key can't be kept secret, you can't effectively use cryptography"

For passwords, the "key" is the password itself. PBKDF2 is a one-way function — even with the salt + hash, the password cannot be recovered. So we don't have a key to keep secret, which is why hash storage is the right pattern instead of encryption.

§57.4 already anticipates this slice's biggest limitation:
> "we must add confidentiality to this cross-network authentication, generally by encrypting at least the password itself"

Real systems put passwords in the POST body over TLS. Our demo uses query-string passwords over plaintext HTTP on localhost, which is wrong for any real system. We document this gap in the scenario output rather than fixing it.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **TLS** (OSEP §57.5): single localhost process, no TLS handshake.
- **RBAC / ACL** (OSEP §55.4): we have "registered user" as a single bit of authorization, nothing finer.
- **Multi-factor** (OSEP §54.4 + §54.5): password-only.
- **Account lockout / rate-limit** (OSEP §54.4): not implemented; the slow hash is the only defence shown.
- **Persistence**: in-memory only; restart wipes users.
- **GET vs POST**: we accept GET for the routes because the rest of this repo's milestones do the same; real systems would use POST and put credentials in the body, never the URL.
