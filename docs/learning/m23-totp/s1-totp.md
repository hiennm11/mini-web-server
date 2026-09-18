# Slice 23.6: TOTP (RFC 6238) — Auth by What You Have

## What this slice is

A clean-room **TOTP** (RFC 6238) implementation in ~70 lines. Three pieces:

- **`Totp.Compute(secret, unixTime)`** — 6-digit code for the given 30-second window.
- **`Totp.Verify(secret, code, unixTime)`** — ±1-step clock-skew tolerance.
- **`Totp.ToBase32(secret)`** — for enrollment QR-code generation in a future slice.

HMAC-SHA256 instead of HMAC-SHA1 (RFC 6238 §1.2 allows either; SHA-1 is the historical default).

## The OSEP invariants M23.6 enforces

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | 6-digit code matches at current time | §54.5 | `/crypto/totp-demo` reports `verify(code, now) = True` |
| 2 | ±1 step window absorbs clock skew | §54.5 | reports `verify(code, now-30s) = True` |
| 3 | ±2 steps rejected | §54.5 | reports `verify(code, now-60s) = False` |
| 4 | Wrong code rejected | §54.5 | reports `verify(code+1, now) = False` |

## Files

- `src/MiniWebServer.Host/MiniCrypto/Totp.cs` — `Compute`, `Verify`, `ToBase32`, plus the RFC 6238 private `ComputeHmac` (truncation step).
- `src/MiniWebServer.Host/Program.cs` — `/crypto/totp-demo?msg=HEXSECRET` route.
- `docs/learning/m23-totp/overview.md` — milestone overview.

## Smoke evidence

Captured 2026-09-18 — see overview.md for full transcript. Key outputs:

```
code    = 275647  (6 digits, HMAC-SHA256 truncated)
verify(code, now)         = True
verify(code, now-30s)     = True  (window of ±1 step, accept)
verify(code, now-60s)     = False (±2 steps, outside window, reject)
verify(code+1, now) wrong = False
```

## OSEP points worth keeping

§54.5 motivates the second factor:
> "If the thief stole your security token, but doesn't know your password, the thief will still have to guess that before they can pose as you."

§54.4's *password-storage* PBKDF2 work can derive the same TOTP secret from the user's master password at enrollment. The piece doc + ADR flag the composition as one-line future work.

§56.5's key-selection lesson: the TOTP secret must be **randomly chosen**. Real services use `RandomNumberGenerator.Fill` for 32 bytes then `ToBase32` to produce the enrollment string.

§54.5 describes a key defense we don't implement:
> "If you're smart in setting up your system, an attacker really should not be able to run a dictionary attack on a login process remotely. ... there's no good reason your system should allow a remote user to make 15,000 guesses."

Rate-limiting the TOTP verify endpoint is the natural M23.6 extension. We don't add it; we count failed verify attempts in the existing counter.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **MFA composition** with M23.1 password (one-line addition to `/auth/login`).
- **Replay tracking** (which (code, step) pairs have already been used).
- **HOTP** (counter-based, RFC 4226) — predecessor.
- **U2F / FIDO** (challenge-response hardware tokens).
- **QR enrollment** (RFC 6238 §5.1.1 `otpauth://` URI + QR).
- **Rate-limiting** on the verify endpoint.
