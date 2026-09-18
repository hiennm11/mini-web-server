# Slice 23.2: At-rest Encryption with AES-256-GCM

## What this slice is

A small symmetric-encryption subsystem built with OSEP §56.2's "use AES, don't design your own" rule and §56.7's "if the device is stolen, the raw blocks are useless without the key" insight. Four pieces:

- **`SymmetricCipher`** wraps `System.Security.Cryptography.AesGcm` with our chosen parameters: 32-byte (256-bit) keys, 12-byte (96-bit) nonces, 16-byte (128-bit) auth tags.
- **`AtRestStore`** is a `ConcurrentDictionary<string, Slot>` that persists only `{nonce, ciphertext, tag}`. Plaintext never lives in storage once the `Put` call returns.
- **HTTP routes** at `/crypto/*` for the four demo scenarios.
- **`SymmetricCipher.EncryptWithFixedNonce`** is the one intentionally-bad primitive exposed solely for the nonce-reuse demo. Real code never calls it.

## The OSEP invariants M23.2 enforces

| # | Invariant | OSEP §  | How we prove it |
|---|---|---|---|
| 1 | Same cipher key encrypts + decrypts | §56.2 | `/crypto/decrypt` after `/crypto/encrypt` returns identical plaintext |
| 2 | Tamper breaks the auth tag | §56.4 | `/crypto/tamper-demo` flips 1 bit, decrypt raises `CryptographicException` |
| 3 | Plaintext not persisted | §56.7 | `/crypto/dump` shows `{nonce, len, tag}` only |
| 4 | Fresh nonce per encrypt | §56.5 | every `Encrypt` call generates a new 96-bit nonce from `RandomNumberGenerator`; the smoke output shows a different nonce each call |
| 5 | Nonce reuse is catastrophic | §56.5 + §56.6 | `/crypto/nonce-reuse-demo` shows `c1 ⊕ c2 == p1 ⊕ p2` is `True` |

## Files

- `src/MiniWebServer.Host/MiniCrypto/SymmetricCipher.cs` — `Encrypt`, `Decrypt`, `NewKey`, `EncryptWithFixedNonce` (demo only), `Xor` (demo only), `ToHex` / `FromHex`.
- `src/MiniWebServer.Host/MiniCrypto/AtRestStore.cs` — `Put(name, plaintext)`, `Get(name)`, `DumpSummary`, `RotateKey`, plus `AllSlots` for the tamper demo.
- `src/MiniWebServer.Host/Program.cs` — `/crypto/*` route block (lines ~1101-1283).
- `docs/learning/m23-at-rest-encryption/overview.md` — milestone overview.

## The 6 routes

| Route | Method | What it does |
|---|---|---|
| `/crypto/keygen` | GET | Rotate AES key (256 bits). Returns old + new hex. |
| `/crypto/encrypt?name=X&msg=Y` | GET | Encrypt + store slot X. Returns `{nonce, ciphertext, auth tag}`. |
| `/crypto/decrypt?name=X` | GET | Decrypt + verify. 401 on tag mismatch (caught `CryptographicException`). |
| `/crypto/tamper-demo?name=X` | GET | If 1 slot exists: flip bit 5 of byte 7 of ciphertext, attempt decrypt with the original tag, report the failure. Otherwise: seed a slot named `tamper-target` with `"transfer $100 to savings"`. |
| `/crypto/nonce-reuse-demo` | GET | Two messages `p1, p2` encrypted with the same `0x42`-padded nonce. Proves `c1 ⊕ c2 == p1 ⊕ p2`. |
| `/crypto/dump` | GET | List every slot's at-rest form. |

## Smoke evidence

Captured 2026-09-18 with AES-256-GCM, 12-byte nonce, 16-byte tag. Key generated per-session by `/crypto/keygen` (default at server start).

### Round-trip

```
GET /crypto/encrypt?name=hello&msg=transfer%20%24100%20to%20savings
-> 200 stored slot 'hello'
   nonce       = de1ccfc574232de6b203023b
   ciphertext  = a8e58f8fbfab74607740b265072117f3ffe15dd2dff0ac76
   auth tag    = ae675900377907c804392f5c9d216a52

GET /crypto/decrypt?name=hello
-> 200 decrypted slot 'hello':
      plaintext = transfer $100 to savings
```

Plaintext recovered byte-identical.

### What gets persisted

```
GET /crypto/dump
-> 200 slots: 1
      at-rest form: nonce + ciphertext + 128-bit auth tag (never plaintext)
      ---
        hello  created=2026-09-18T10:24:35  nonce=de1ccfc574232de6b203023b  len=24  tag=ae675900377907c804392f5c9d216a52
```

The plaintext `transfer $100 to savings` is nowhere in the dump — OSEP §56.7 invariant.

### Tamper detection

```
GET /crypto/tamper-demo?name=tamper-target
-> 200 slot 'tamper-target' ciphertext, byte 7 bit 5 flipped.
       now attempting decrypt with the original tag...
       caught CryptographicException: The computed authentication tag did not match the input authentication tag.
       OSEP §56.4: AES-GCM tag mismatch fails closed. The flipped byte broke the auth tag.
```

The decrypted plaintext is never produced. `AesGcm.Decrypt` throws the moment it detects the tag mismatch.

### Nonce reuse = `c1 ⊕ c2 == p1 ⊕ p2`

```
GET /crypto/nonce-reuse-demo
-> 200 OSEP §56.5 / §56.6: nonce-reuse attack
       p1: "budget meeting 2026 income"
       p2: "budget meeting 2026 losses"
       ciphertext XOR  = 00000000000000000000000000000000000000000501101c0816
       plaintext XOR  = 00000000000000000000000000000000000000000501101c0816
       equal?         = True
       If an attacker learns p1, they recover p2 = (p1 ⊕ p2) ⊕ p1 in O(n).
       Real systems generate a fresh nonce per encrypt call (we do by default).
```

The XOR columns are identical because the underlying GCM keystream is the same when `(key, nonce)` repeats. With one known plaintext (or any structure that constrains the second plaintext — both messages start with `"budget meeting 2026 "` here), the second plaintext is recoverable in O(n) operations.

## OSEP points worth keeping

§56.2 makes one thing load-bearing:
> "THE CRYPTOGRAPHY'S BENEFIT RELIES ENTIRELY ON THE SECRECY OF THE KEY"

Our key is in process memory only. If anything dumps process RAM to disk (crash dump, hibernation file, attacker exploit), the key is gone. §56.6 names the same trade-off explicitly:
> "If the legitimate user ever provides the correct password to a compromised OS, all bets are off, alas. The compromised OS will copy the password provided by the user and hand it off to whatever villain is working behind the scenes."

§56.4 stresses why the **auth tag** matters even when the *plaintext* is what we care about — without it, the attacker could substitute a different ciphertext that decrypts to *something* (often junk, but sometimes a believable message). GCM's tag fails closed, so the attacker can't even try to mount a chosen-ciphertext attack without the auth tag flipping first.

§56.7 is the practical rationale for this whole slice:
> "if the data on the device is encrypted via full disk encryption, the new machine will usually be unable to obtain the encryption key. It can access the raw blocks, but they are encrypted and cannot be decrypted without the key."

Our `AtRestStore` is the same idea at application layer: an attacker who steals a process's memory dump or backup of the RAM filesystem sees `{nonce, ciphertext, tag}` — useless without the in-memory key.

## What is *not* in this slice

Per the design rules in `overview.md`:

- **TLS** (OSEP §57): would protect this data in motion; out of scope.
- **PK cryptography** (OSEP §56.3): we never exchange keys with a remote party. A future slice could combine M23.1 + M23.2 + PK auth to encrypt data in transit.
- **Hardware security modules / TPM** (OSEP §53 security enclaves): keys live in normal process memory, not in a separate hardware root of trust.
- **Encrypting minifs.img**: possible follow-up — replace M12's plaintext blocks with `SymmetricCipher.Encrypt` calls. Larger piece of work (per-block metadata + key derivation per inode).
- **Voluntary key rotation by file**: OSEP §56.7 mentions password vaults; we don't have a "vault" UI for per-slot keys.

These are documented in `overview.md` for future readers.
