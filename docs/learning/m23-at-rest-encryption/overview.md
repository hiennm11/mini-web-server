# Milestone 23.2: At-rest Encryption (AES-256-GCM)

> **Overview** — what this milestone covers and where to start. The slice lives in this folder.

## Question

How does an operating system protect user data when the disk, USB stick, or backup tape physically leaves the OS's control? What does "full disk encryption" actually do, and why does "encrypted files" need more than a cipher — it needs a way to detect tampering too?

## Scope

The second slice of M23 (Part IV Security), built directly on `M23.1` (password authentication) and `M12` (mini file system). Demonstrates four OSEP §56.2 + §56.7 primitives that a real OS uses for at-rest data protection:

- **Symmetric cipher** (OSEP §56.2): AES-256-GCM, `C = E(P, K)` and `P = D(C, K)` with the same 256-bit secret key. We use the BCL's `System.Security.Cryptography.AesGcm` rather than writing our own (OSEP §56.2: "Don't develop your own cipher").
- **Authenticated encryption** (OSEP §56.4 + §56.5): every ciphertext is paired with a 128-bit authentication tag that fails *closed* on tampering. Flipping a single bit of the ciphertext breaks the tag.
- **Fresh random nonce per call** (OSEP §56.5 / §56.6): we never reuse a `(key, nonce)` pair. The `nonce-reuse-demo` route proves that reusing it leaks `c1 ⊕ c2 == p1 ⊕ p2`.
- **At-rest storage** (OSEP §56.7): an in-memory store that persists only `{nonce, ciphertext, tag}` — never the plaintext. A real BitLocker / FileVault does the same thing at the block layer; we do it at the application layer for the slice.

Exposed via HTTP routes:

| Route | Method | Purpose |
|---|---|---|
| `/crypto/keygen` | GET | Rotate the in-memory 256-bit AES key (OSEP §56.7 "well chosen ... symmetry"). |
| `/crypto/encrypt?name=X&msg=Y` | GET | Encrypt + store slot X with plaintext `msg`. Returns nonce + ciphertext + tag. Plaintext is wiped from the working buffer before the route returns. |
| `/crypto/decrypt?name=X` | GET | Decrypt + verify. If the auth tag mismatches, returns `401 Unauthorized` with `CryptographicException` text. |
| `/crypto/tamper-demo` | GET | Encrypt a known plaintext, flip 1 byte of the ciphertext, attempt decrypt. The auth tag fails closed. |
| `/crypto/nonce-reuse-demo` | GET | Encrypt two messages with the *same* key + nonce, prove `c1 ⊕ c2 == p1 ⊕ p2`. Real code never does this. |
| `/crypto/dump` | GET | Show `{nonce, len, tag}` for every slot. Proves plaintext is not in storage. |

## Slice

- **[s1-at-rest.md](./s1-at-rest.md)** — `SymmetricCipher` (AES-256-GCM wrap) + `AtRestStore` (thread-safe in-memory slotted store) + `/crypto/*` HTTP routes + four scenarios.

## OSEP coverage

- **Ch. 56** Cryptography — §56.2 symmetric cryptography (AES standard, same-key encryption); §56.4 cryptographic hashes + integrity (authenticated cipher mode); §56.5 brute force + WEP (the password file line that connects to M23.1 — we never store secrets unprotected); §56.6 cryptography + operating systems (keys kept in process memory, never on stable storage); §56.7 at-rest encryption (full-disk FileVault pattern, password vaults, why it's the right defence when the device is stolen).
- **Ch. 53.4** Saltzer-Schroeder — fail-safe defaults (auth-tag mismatches return 401, not "garbage plaintext"); least privilege (key rotates per `keygen`, never persisted).
- **Ch. 54.4** — bridge from M23.1: the same PBKDF2-derived master key could unlock this encryption (we don't do that in the slice but the connection is documented).

## OSEP §-specific deviations

- **Key rotation is manual** (OSEP §56.6 talks about long-term secrets). We keep one in-memory key per process; `/crypto/keygen` rotates it. A real OS derives the key from a passphrase via PBKDF2 (M23.1's primitives) at login time, then keeps it only in RAM until logout — matching OSEP §56.7's "compromise between usability and security ... remembering the key after first entry for a significant period of time, but only keeping it in RAM".
- **No hardware security module / TPM** (OSEP §53 security enclaves aside). The key is in the process address space; if the OS is compromised, so is the key.
- **No file-attribute decisions** (OSEP §56.7 talks about "records, data blocks, individual files, entire file systems, by different system components" — application vs library vs device driver vs file system). This slice is application-level.
- **Nonce reuse demo is intentionally bad** (OSEP §56.5). The slice exposes the bad behavior so the lesson is visible — the *real* `Encrypt` always picks a fresh nonce.
- **GCM is one of many AEAD modes** (OSEP §56.4 lists several primitives; GCM is the "good default" today). ChaCha20-Poly1305, AES-CCM, etc. would be defensible alternatives; .NET's BCL ships AesGcm so we use it.

## .NET mechanism

- `System.Security.Cryptography.AesGcm` — .NET's AES-GCM implementation, GCM-standard (NIST SP 800-38D), 96-bit nonce + 128-bit tag + variable-length ciphertext (= plaintext length). Supports optional AAD (associated authenticated data) so the same key+nonce can authenticate extra context.
- `System.Security.Cryptography.RandomNumberGenerator` — CSPRNG for both key and nonce generation. Pulls from the OS entropy pool (OSEP §56.5 "the best source for operating system purposes").
- `System.Security.Cryptography.CryptographicException` — thrown by `AesGcm.Decrypt` when the auth tag fails. Caught at the HTTP route boundary and translated to 401.
- `System.Collections.Concurrent.ConcurrentDictionary<string, Slot>` — same pattern as M23's `UserStore` (HTTP handler pool runs 8 threads).

## Learning sequence

1. Re-read OSEP §56.2 (caesar cipher → AES standard) and §56.7 (full-disk + password vaults).
2. Run `/crypto/encrypt` then `/crypto/decrypt` to see the round-trip.
3. Run `/crypto/dump` to confirm storage holds only `nonce + ciphertext + tag`.
4. Run `/crypto/tamper-demo` to see AES-GCM's tag fail closed.
5. Run `/crypto/nonce-reuse-demo` to see why nonce reuse is catastrophic — `c1 ⊕ c2 == p1 ⊕ p2`.

## Adjacent OSEP not addressed

- **Ch. 56.3** Public-key cryptography: not implemented. We use *symmetric* AES-GCM only; PK would let us exchange a key with a remote partner without a pre-shared secret.
- **Ch. 57** Distributed-system security (TLS, certificates, MITM): covered by a future milestone. Connecting M23.1 + M23.2 to a server *and* a remote client would mean TLS over TCP, which is `SslStream` + cert handling.
- **Ch. 55** Access control / RBAC / ACLs: not implemented. M23.2 protects data from tampering, not from *unauthorized reads* (the key is in the process — anyone with code execution can decrypt).
- **M12 connection**: we'd love to encrypt `minifs.img` itself, but the FUSE-style integration is a larger piece of work. The cryptographic primitives here are reusable: a future milestone could call `SymmetricCipher.Encrypt` from `MiniFs.Write` to encrypt blocks at rest.

## Key OSEP quotes

OSEP §56.2 (don't develop your own cipher):
> "Don't. ... pretty much anyone who puts their mind to it can create a cipher they can't break themselves. ... if you actually use it for something important, you will unfortunately draw their attention. Following which your secrets will be revealed, following which you will look foolish for designing your own cipher instead of using something standard like AES, which is easier to do, anyway. So, don't."

OSEP §56.4 (integrity + auth tag):
> "if we want to be really careful, we can't use just any hash function, since hash functions, by their very nature, have hash collisions, where two different bit patterns hash to the same thing. If an attacker can change the bit pattern we intended to send to some other bit pattern that hashes to the same thing, we would lose our integrity property. So to be particularly careful, we can use a cryptographic hash."

OSEP §56.6 (cryptography + OS):
> "Either you trust your operating system or you don't. If you don't, life is going to be unpleasant anyway, but one implication is that the untrusted operating system, having access at one time to your secret key, can copy it and re-use it whenever it wants to."

OSEP §56.7 (at-rest benefits):
> "if the data on the device is encrypted via full disk encryption, the new machine will usually be unable to obtain the encryption key. It can access the raw blocks, but they are encrypted and cannot be decrypted without the key. This benefit would be useful if the hardware in question was stolen and moved to another machine."
