# Milestone 23.4: Public-Key Cryptography (OSEP §56.3)

## Question

How does an asymmetric cipher work? What's the relationship between a private key and a public key, and how do you prove that a message came from the holder of the private key?

## Scope

M23.4 implements the **public-key cryptographic primitive** OSEP §56.3 describes — keypair {private, public}, sign with private, verify with public. Three pieces:

- **`RsaSigner.Generate()`** — produce a fresh 2048-bit RSA keypair. Private kept in process memory; public exported as SPKI/DER bytes.
- **`RsaSigner.Sign(message)`** — SHA-256 + PSS padding. Returns signature bytes.
- **`RsaSigner.Verify(message, signature, publicKeyOverride?)`** — checks signature against either the in-memory key (default) or an externally-distributed one (for cross-process verification).

Exposed via HTTP routes:

| Route | Purpose |
|---|---|
| `/crypto/rsa-keygen` | Generate a fresh RSA-2048 keypair, return public key (SPKI hex). Private stays in process memory. |
| `/crypto/sign?msg=X` | Sign `msg` with the in-memory private key; return hex signature. |
| `/crypto/verify?msg=X&sig=Y[&pubkey=Z]` | Verify `sig` against `msg` using either the in-memory public key or an externally-supplied `pubkey`. |
| `/crypto/import-pubkey` | Switch the active public key — model the "I got your key from somewhere else" flow. |

## OSEP coverage

- **Ch. 56.3** Public-key cryptography: keypair generation, encryption / decryption, **signing / verification**, key distribution.
- **Ch. 56.6** Cryptography + OSes: "THE CRYPTOGRAPHY'S BENEFIT RELIES ENTIRELY ON THE SECRECY OF THE KEY. In this case, the private key. ... Never divulge private keys."
- **Ch. 57.3** Public-key auth in distributed systems (foreshadowing): "this form of cryptography is called public-key cryptography, since one of the two keys can be widely known to the entire public" — we keep the private key in process memory only.

## OSEP §-specific deviations

- **2048-bit keys** (OSEP §56.5 "RSA with 2K or 4K bits" matches "good enough"). We pick 2048 for reasonable smoke-time.
- **PSS padding** for signatures (current best practice); the chapter doesn't pin a padding scheme.
- **No X.509 certificate distribution** (OSEP §57.3): we return raw SPKI bytes. Real systems use signed X.509 chains (browser trust stores, OS root CA bundles).
- **In-memory only**: private key dies with the process. Real systems use HSMs or OS keyrings (which §56.6 explicitly says would defeat the threat model we're modeling).
- **No key rotation policy**: every call to `/crypto/rsa-keygen` discards the previous key (and any signatures made under it). OSEP §56.5 talks about preventing `attacker might try to simply guess each possible key` — key rotation is the practical mitigation. Real systems have periodic rotation.
- **No asymmetric encryption**: we only sign/verify. RSA-OAEP for encryption would be a separate slice.
- **HMAC-based MAC** (OSEP §56.4): could be a cheaper signature alternative for *symmetric* parties, but does not satisfy "anyone can verify with public key" (only the symmetric-holder can). Out of scope.

## .NET mechanism

- `System.Security.Cryptography.RSA.Create(2048)` — managed RSA. Disposed when the next `Generate()` runs.
- `RSA.ExportSubjectPublicKeyInfo()` — returns the SPKI/DER blob for distribution.
- `RSA.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)` — signs.
- `RSA.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)` — verifies.
- For *externally-supplied* verification, a fresh `RSA.Create()` + `ImportSubjectPublicKeyInfo()` so we don't mutate our own keypair.

## Smoke evidence

Captured 2026-09-18:

```
GET /crypto/rsa-keygen
-> public key (hex, SPKI/DER, length=294 bytes):
   30820122300d06092a864886f70d01010105000382010f003082010a02820101...cf370dfa2e24479ca4c3a4d5ede732c5241f1ef53222394bb6f3890203010001
   private key kept in process memory (OSEP §56.6)

GET /crypto/sign?msg=hello
-> message: "hello"
   signature (hex, 256 bytes):
   085c6e7ff7...b60341bd03769f0
   OSEP §56.3: anyone with the public key can verify but only the holder
   of the private key can produce the signature.

GET /crypto/verify?msg=hello&sig=085c...b60341bd03769f0
-> verify result: OK
   OSEP §56.3: signature matches the public key — caller is the signer.

GET /crypto/verify?msg=goodbye&sig=085c...b60341bd03769f0
-> verify result: FAIL
   OSEP §56.3: signature does NOT match — message tampered, wrong key, or wrong message.
```

The same signature hex is `OK` for `hello` and `FAIL` for `goodbye` — the verifier rejects any message other than the one signed. This is the canonical PK authentication guarantee.

## OSEP points worth keeping

§56.3 explains the magic step:
> "If someone hands you a piece of data that has been encrypted with a key K that is known only to you and your buddy Remzi? You know you didn't create it, so if it decrypts properly using key K, you know that Remzi must have created it. After all, he's the only other person who knew key K, so only he could have performed the encryption."

That's authentication via symmetric crypto with shared trust. PK swaps the receiver's "shared key" for the receiver's "public key" — a verifier needs only the public half.

§56.6 hammers the threat-model point:
> "Never divulge private keys. Never share private keys. Take great care in your use of private keys and in how you store them. If you lose a private key, everything you used it for is at risk, and whoever gets hold of it can pose as you and read your secret messages."

Our process-memory storage satisfies *short-term* secrecy; a core dump, swap file, or hibernation file would expose the private key. Real systems use HSMs/TPMs (§53).

## What is *not* in this slice

Per the design rules in `overview.md`:

- **Hybrid encryption** (PK encrypt + symmetric encrypt — `K_decrypt_Symmetric`, then `E(PK_Bob, K)`). Deferred.
- **X.509 certificates** (OSEP §57.3): we'd need a PKI story. Out of scope.
- **HSM / TPM-backed keys**: in-process RSA only.
- **Key revocation lists** (OSEP §57.6): not modeled.
- **Sign-then-encrypt flow** (Alice signs + encrypts for Bob — OSEP §56.3): deferred.

These are documented as future work.
