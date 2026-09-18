# Slice 23.5: TLS-style Handshake + HKDF Session Key

## What this slice is

A single-shot demo of the key-derivation half of a TLS handshake:

1. **ClientHello / ServerHello** — exchange two random 32-byte nonces.
2. **HKDF-SHA256** — derive the session key from a shared IKM + the two nonces.
3. **Encrypt + decrypt** — uses the M23.2 AEAD over the derived key to prove it's a working AES key.

We skip the PK-auth-on-handshake part (no client / server certs, no ECDH); the *structure* matches a TLS handshake minus the auth layer.

## The OSEP invariants M23.5 enforces

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | Two-party key exchange starts with nonces | §57.5 | `/crypto/handshake` prints both nonces |
| 2 | Fresh session key per handshake | §57.5 | Two consecutive handshakes produce different keys (different nonces) |
| 3 | Per-session symmetric key actually usable | §57.5 | step 4 encrypts + decrypts a payload using the derived key |

## Files

- `src/MiniWebServer.Host/MiniCrypto/Handshake.cs` — `RunDemo()` + clean-room `HkdfSha256(ikm, salt, info, outputLength)` (RFC 5869 Extract+Expand).
- `src/MiniWebServer.Host/Program.cs` — `/crypto/handshake` route that runs the demo + encrypt/decrypt.
- `docs/learning/m23-handshake/overview.md` — milestone overview.

## Smoke evidence

Captured 2026-09-18:

```
GET /crypto/handshake
-> OSEP §57.5 handshake demo (simplified TLS-like):
   step 1: client  → ClientHello (random nonce)
     client_nonce = e5f45ce5b71dd714a9e66a6dd75ed111a6675c387e81eed6d6763ec1839e83d1
   step 2: server  → ServerHello (random nonce)
     server_nonce = 667559f67321afc9eba923300aafe5e0703039acdd0c27b95824625e4ae55107
   step 3: both    → HKDF-SHA256(shared_secret, clientNonce || serverNonce)
     session_key  = 0d88e40912837422f60926e207fa265b29c496c08e806cc9e4626ce5dffaf393
   step 4:        → encrypt(payload, session_key) + decrypt (M23.2 AEAD)
     plaintext    = budget meeting 2026 Q3: 5% growth
```

The session key is 32 bytes (256 bits) — a proper AES-256 key. The encrypt+decrypt round-trip confirms it's a real key, not garbage.

## OSEP points worth keeping

§57.5 explains why the structure looks this way:
> "PK cryptography is expensive. We want to stop using it as soon as possible, but we also want to continue to get authentication guarantees. ... symmetric cryptography to encrypt the data once you have authenticated your partner, and you want a fresh key for that."

So the canonical handshake is:
- One-time PK-auth for the *first* message (cheaper if you batch it).
- Both sides derive a fresh symmetric session key via HKDF.
- Subsequent bulk traffic is symmetric.

We skip the PK-auth step but keep the symmetric-key-derivation half, which is the most-educational part.

§56.5 + §56.6's nonce-reuse lesson applies here too: the two nonces *must* be fresh per handshake. Our `RandomNumberGenerator.Fill` ensures this.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **ECDH / X25519** (real TLS key exchange).
- **Server certificate chain verification** (OSEP §57.3).
- **Mutual TLS** (server authenticates the client too).
- **Perfect forward secrecy** (X25519 ephemeral keys).
- **Record-layer MACs** (sequence numbers + HMAC-SHA256 per record).
- **Replay protection** at the record layer.
