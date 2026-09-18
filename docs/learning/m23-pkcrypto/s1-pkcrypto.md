# Slice 23.4: Public-Key Cryptography (Sign + Verify)

## What this slice is

A small PK crypto subsystem built on `System.Security.Cryptography.RSA`. Three pieces:

- **`RsaSigner.Generate()`** — fresh 2048-bit RSA keypair.
- **`RsaSigner.Sign(message)`** — SHA-256 + PSS.
- **`RsaSigner.Verify(message, signature, publicKeyOverride?)`** — checks against the in-memory public key by default, or against an externally-distributed one if supplied.

## The OSEP invariants M23.4 enforces

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | Public-key pair {private, public} | §56.3 | `/crypto/rsa-keygen` returns the public key, keeps the private in process memory |
| 2 | Sign with private → verify with public | §56.3 | `/crypto/sign?msg=hello` then `/crypto/verify?msg=hello&sig=…` → OK |
| 3 | Different message → different signature, same key | §56.3 | `/crypto/verify?msg=goodbye&sig=…` → FAIL |
| 4 | Externally-distributed public keys verifiable | §57.3 | `?pubkey=HEX` parameter on `/crypto/verify` accepts a different SPKI blob |

## Files

- `src/MiniWebServer.Host/MiniCrypto/RsaSigner.cs` — `Generate`, `Sign`, `Verify`, `SetActivePublicKey`.
- `src/MiniWebServer.Host/Program.cs` — `/crypto/rsa-keygen`, `/crypto/sign`, `/crypto/verify`, `/crypto/import-pubkey`.
- `docs/learning/m23-pkcrypto/overview.md` — milestone overview.

## Smoke evidence

Captured 2026-09-18 — see overview.md for full transcript. Key sequence:

```
/crypto/rsa-keygen     -> 294-byte public key (hex), private kept in process memory
/crypto/sign?msg=hello -> 256-byte hex signature
/crypto/verify?msg=hello&sig=<…>     -> OK
/crypto/verify?msg=goodbye&sig=<…>    -> FAIL
```

Same signature, different message: `OK` → `FAIL`. The integrity property of PK signatures is on display.

## OSEP points worth keeping

§56.3's most-quotable line:
> "If someone hands you a piece of data that has been encrypted with a key K that is known only to you and your buddy Remzi? You know you didn't create it, so if it decrypts properly using key K, you know that Remzi must have created it."

PK authentication generalizes this: replace "key known only to you and your buddy" with "key known only to you but your buddy's public key isn't secret". Verifier proves that the holder of the private key — and only the holder — could have produced the signature.

§56.6's storage rule for private keys:
> "Never divulge private keys. Never share private keys. Take great care in your use of private keys and in how you store them."

In-process storage is the bare-minimum. Real systems use TPMs / HSMs; we don't (out of scope for a single-process lab).

## What is *not* in this slice

Per the design rules in `overview.md`:

- **Hybrid encryption** (PK encrypt symmetric key + symmetric encrypt data).
- **Asymmetric encryption** (RSA-OAEP encryption; PKCS#1 v1.5 padding is broken).
- **X.509 certificate chains** (OSEP §57.3).
- **HSM-backed signing keys**.
- **Recovery from key compromise** (key revocation lists — OSEP §57.6).
