# Milestone 23.5: TLS-style Handshake (OSEP §57.5)

## Question

How does an HTTPS / TLS connection set up a fresh symmetric key for each session, when the two parties have never met and can't share a secret ahead of time?

## Scope

M23.5 implements a **simplified handshake** that demonstrates the key-derivation half of an OSEP §57.5 TLS handshake. Two pieces:

- **`Handshake.RunDemo()`** — emits `clientNonce`, `serverNonce`, derives a per-session `sessionKey` via **HKDF-SHA256** (RFC 5869).
- **Demonstration via the M23.2 AEAD**: the route encrypts + decrypts a sample payload using the freshly-derived `sessionKey`, proving the key produced is a real working AES-256 key.

Exposed via HTTP route:

| Route | Purpose |
|---|---|
| `/crypto/handshake` | One-shot demo: 4-step walkthrough (ClientHello, ServerHello, HKDF key derivation, encrypt + decrypt) |

## OSEP coverage

- **Ch. 57.5** SSL/TLS — especially:
  > "you need to use public/private keys for authentication many times ... but you need to use symmetric cryptography to encrypt the data once you have authenticated your partner, and you want a fresh key for that."
- **Ch. 57.2** Public key in authentication (foreshadowing): the handshake would normally wrap with a server-cert check; we skip that part.
- **Ch. 56.6** Symmetric key secrecy: the session key is a fresh AES-256 key derived per handshake. Destruction at connection-end would erase the key from memory; in a single-process demo we just drop the variable.

## OSEP §-specific deviations

- **HKDF-SHA256 instead of ECDH**: real TLS uses Diffie-Hellman (RFC 8446 §4.1) for forward-secret key exchange; BCL doesn't ship ECDH directly. We use HKDF with a randomly-generated symmetric input-keying-material + the two nonces, mirroring the **HKDF-based derivation stage** in any TLS handshake.
- **No certificate verification** (OSEP §57.3): we skip the CA-chain check. A real TLS handshake would verify the server certificate at this point.
- **No client authentication**: §57.5 says "client is pretty sure who the server is, but the server has no clue about the client." We model only server-side for now.
- **In-process client + server**: the route models both parties in one process. In a real handshake they're separated by a network.
- **No replay protection** (nonce reuse tags §57.6): TLS records include sequence numbers + MACs; we don't.

## .NET mechanism

- `System.Security.Cryptography.RandomNumberGenerator.Fill` — nonces.
- A clean-room **HKDF-SHA256** (~25 lines, RFC 5869 Extract+Expand) built on `HMACSHA256`.
- Reuse M23.2's `SymmetricCipher.Encrypt` / `Decrypt` for the encrypt/decrypt proof.

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

The session key derives fresh on every call (nonces regenerate). Two consecutive handshakes produce different keys.

## OSEP points worth keeping

§57.5 makes one observation we mirror exactly:
> "PK cryptography is expensive. We want to stop using it as soon as possible, but we also want to continue to get authentication guarantees."

In a real TLS handshake:
1. Client + server exchange nonces (cheap).
2. Server certificate + ECDH public key (PK, expensive — done once per connection).
3. Both sides derive a fresh session key via HKDF on the ECDH shared secret + nonces.
4. Subsequent traffic uses the symmetric cipher (cheap, fast).

Our demo collapses steps 2-3 by using a randomly-generated symmetric IKM instead of ECDH, but the *structure* is identical to TLS: exchange nonces → derive → encrypt.

§56.6's symmetric-key secrecy rule applies to the session key: destroying it ends the encrypted session. The OS does this by zeroing the buffer when the connection closes.

§56.5 + §56.6's nonce-reuse lesson also applies — the M23.2 demo proves why we need *fresh* nonces per handshake.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **ECDH / X25519** (real TLS): we use a symmetric IKM as a stand-in.
- **X.509 certificate verification** (OSEP §57.3).
- **Perfect forward secrecy** (OSEP §56.5 — "true randomness"): requires true ECDH ephemeral keys, which we couldn't include anyway.
- **Record-layer MACs** (TLS record sequence numbers + HMAC).
- **Mutual TLS (mTLS)** — server authenticates the client too.

These are documented as future work.
