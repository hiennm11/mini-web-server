# Milestone 23.6: Time-based One-Time Passwords (OSEP §54.5)

## Question

What's the canonical "auth by what-you-have" primitive that bypasses typing, and how does it defend against replay attacks?

## Scope

M23.6 implements **TOTP** (Time-based One-Time Password) per RFC 6238 — the canonical "what-you-have" authentication primitive covered in OSEP §54.5. Four pieces:

- **`Totp.Compute(secret, unixTime)`** — returns the 6-digit code for the given secret at the given time.
- **`Totp.Verify(secret, code, unixTime)`** — checks the code against current time ±1 step (~30s window) for clock skew.
- **Truncated output** — RFC 6238 dynamic truncation (offset = last_nibble & 0xf; 4 bytes; high bit masked; mod 10^6).
- **`Totp.ToBase32(secret)`** — encodes the shared secret in the format authenticator apps expect at enrollment (Google Authenticator / Authy / 1Password).

Exposed via HTTP route:

| Route | Purpose |
|---|---|
| `/crypto/totp-demo?msg=HEXSECRET` | Compute current 6-digit code; verify it for (a) now, (b) now-30s (in window), (c) now-60s (outside window), (d) wrong code |

## OSEP coverage

- **Ch. 54.5** "Authentication by What You Have":
  - Hardware tokens (USB dongles, smart cards): out of scope (no USB hardware in the lab).
  - SMS codes: out of scope (no SMS provider).
  - Smartphone apps (Google Authenticator, Authy, 1Password): **this is what TOTP implements**, and we model the cryptographic primitive exactly.
  - The chapter warns about the *loss* problem; we don't combine with password (MFA), but we demo the algorithm.
- **Ch. 54.4** connection to password storage — a real system chains M23.1 password verify + M23.6 TOTP verify. The OSEP §54.4 paragraph "two-factor authentication" lines up exactly.

## OSEP §-specific deviations

- **HMAC-SHA256 instead of HMAC-SHA1**: OSEP §56.4 + RFC 6238 §1.2 allow this. SHA-256 is the standard today.
- **30-second steps**: RFC 6238 default.
- **±1 step window**: typical server tolerance for clock skew.
- **6 digits** (1,000,000 possible codes): RFC 6238 default. Bigger digits → less brute-force surface, more typing.
- **No server-side replay protection**: real TOTP services track "what codes have already been used" to prevent a second use of a single code. We accept the same code repeatedly within the window — fine for a demo, would be a real bug.
- **No enrollment flow**: real TOTP enrollments exchange the base32 secret + scan a QR code. We just accept hex secrets in the demo query.

## .NET mechanism

- `System.Security.Cryptography.HMACSHA256` — RFC 6238 step 1 (HMAC + counter).
- Big-endian counter encoding (`((step >> (8*i)) & 0xff) for i in 7..0`).
- RFC 6238 dynamic truncation: 4 bytes starting at `(hmac[hmac.Length - 1] & 0x0f)`, mask the high bit to get a 31-bit unsigned integer, then `% 10^digits`.
- Step derivation: `step = unix_seconds / 30`.

## Smoke evidence

Captured 2026-09-18:

```
GET /crypto/totp-demo?msg=68656c6c6f20776f726c642031323334
-> OSEP §54.5 — TOTP (auth by what-you-have):
     now     = 1789727958 (unix seconds)
     step    = 59657598 (TOTP step = 30 seconds, RFC 6238)
     code    = 275647  (6 digits, HMAC-SHA256 truncated)
     verify(code, now)         = True
     verify(code, now-30s)     = True  (window of ±1 step, accept)
     verify(code, now-60s)     = False (±2 steps, outside window, reject)
     verify(code+1, now) wrong = False
```

Four invariants demonstrated:
1. The current code verifies at the current time.
2. The same code verifies ±1 step (clock-skew window).
3. The same code is rejected ±2 steps (defends against indefinite replay).
4. A wrong code (off-by-one) is rejected.

## OSEP points worth keeping

§54.5 warns about the **loss** problem — what happens when someone loses the hardware token. The chapter points out that **multi-factor** resolves this:
> "If the thief stole your security token, but doesn't know your password, the thief will still have to guess that before they can pose as you."

We don't compose TOTP with the M23.1 password flow — adding `?totp=CODE` to `/auth/login` is a one-line change for a future slice — but the structure is already there (the auth block keeps the username + password threading intact).

§54.4's PBKDF2 work can derive the *TOTP secret* from the same master: in a real system, the user's master password unlocks the TOTP secret stored on disk. That straddles M23.1 + M23.6 and is also future work.

§56.5's key-selection lesson applies: the TOTP secret must be **randomly chosen** (32 bytes from `RandomNumberGenerator`, base32-encoded). Real services also enforce this at enrollment.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **MFA composition** (TOTP + M23.1 PBKDF2 password). One-line addition to `/auth/login`.
- **Replay tracking**: servers should remember which (code, step) pairs have already been used; we don't.
- **HOTP** (counter-based, RFC 4226) — predecessor to TOTP; out of scope.
- **U2F / FIDO** (challenge-response hardware tokens): out of scope, requires USB + browser API.
- **QR code enrollment** (RFC 6238 §5.1.1 — `otpauth://` URI + QR): would be a tiny add-on.
