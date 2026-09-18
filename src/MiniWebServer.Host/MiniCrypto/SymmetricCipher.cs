using System;
using System.Security.Cryptography;
using System.Text;

namespace MiniWebServer.Host.MiniCrypto;

// Symmetric authenticated encryption (AES-256-GCM), per OSEP \u00a756.2 + \u00a756.7.
//
// Three relevant invariants from the chapter:
//   1. "THE CRYPTOGRAPHY'S BENEFIT RELIES ENTIRELY ON THE SECRECY OF THE KEY" \u2014 we
//      generate a fresh 256-bit key per session. At-rest encryption protects data
//      *outside* the OS's domain; the key lives in the process (analogous to OSEP
//      \u00a756.7's "decryption key ... not stored in the machine's stable storage").
//   2. "Symmetric cryptography ... the same key is used to encrypt and decrypt."
//      We use the same key for both Encrypt and Decrypt.
//   3. AES-GCM is an *authenticated* cipher; the 128-bit tag proves both the
//      ciphertext and the optional associated data weren't tampered with
//      (OSEP \u00a756.4 + \u00a756.5). A flipped bit in ciphertext or AAD breaks the tag.
//
// Nonce: we generate a *fresh random 12-byte nonce* per Encrypt call. OSEP \u00a756.5
// warns that reusing a (key, nonce) pair is fatal: with the same keystream XOR
// plaintexts, ciphertexts XOR back to plaintexts XOR'd together. We expose the
// same-nonce scenario explicitly in the smoke demos so the lesson is visible.
public static class SymmetricCipher
{
    public const int KeyLength = 32;       // AES-256 key (256 bits)
    public const int NonceLength = 12;      // AES-GCM standard nonce (96 bits, NIST SP 800-38D)
    public const int TagLength = 16;        // AES-GCM auth tag (128 bits)

    /// <summary>
    /// AES-256-GCM encrypted record: nonce + ciphertext + tag.
    /// All three are needed to attempt decryption; tag failure throws.
    /// </summary>
    public sealed record EncryptedBlock(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

    /// <summary>Generate a fresh 256-bit symmetric key.</summary>
    public static byte[] NewKey()
    {
        var key = new byte[KeyLength];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Encrypt with a freshly-generated random nonce. Returns the nonce + ciphertext + tag.
    /// </summary>
    public static EncryptedBlock Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)
    {
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
        if (key is null || key.Length != KeyLength) throw new ArgumentException($"key must be {KeyLength} bytes", nameof(key));

        var nonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new EncryptedBlock(nonce, ciphertext, tag);
    }

    /// <summary>
    /// Decrypt a previous EncryptedBlock. Throws CryptographicException if the auth
    /// tag check fails (tampering, wrong key, wrong nonce, or wrong AAD).
    /// OSEP \u00a756.4: the auth tag is what stops a flipped bit from producing a
    /// garbage-but-accepted "decryption" \u2014 GCM fails closed.
    /// </summary>
    public static byte[] Decrypt(EncryptedBlock block, byte[] key, byte[]? associatedData = null)
    {
        if (block is null) throw new ArgumentNullException(nameof(block));
        if (key is null || key.Length != KeyLength) throw new ArgumentException($"key must be {KeyLength} bytes", nameof(key));
        if (block.Nonce is null || block.Nonce.Length != NonceLength) throw new ArgumentException("nonce wrong length", nameof(block));
        if (block.Tag is null || block.Tag.Length != TagLength) throw new ArgumentException("tag wrong length", nameof(block));

        var plaintext = new byte[block.Ciphertext.Length];
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(block.Nonce, block.Ciphertext, block.Tag, plaintext, associatedData);
        return plaintext;
    }

    // Hex helpers (same shape as MiniAuth.PasswordHasher).
    public static string ToHex(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static byte[] FromHex(string hex)
    {
        if (hex is null) throw new ArgumentNullException(nameof(hex));
        if (hex.Length % 2 != 0) throw new ArgumentException("hex length must be even", nameof(hex));
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    // Fixed-Nonce variant, ONLY for the smoke demo that proves nonce reuse is
    // catastrophic. Real code never calls this.
    public static EncryptedBlock EncryptWithFixedNonce(byte[] plaintext, byte[] key, byte[] fixedNonce, byte[]? associatedData = null)
    {
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
        if (key is null || key.Length != KeyLength) throw new ArgumentException("bad key", nameof(key));
        if (fixedNonce is null || fixedNonce.Length != NonceLength) throw new ArgumentException("bad nonce", nameof(fixedNonce));

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(fixedNonce, plaintext, ciphertext, tag, associatedData);
        return new EncryptedBlock((byte[])fixedNonce.Clone(), ciphertext, tag);
    }

    /// <summary>XOR two same-length byte arrays. Used by the nonce-reuse demo to prove
    /// that ciphertext XOR equals plaintext XOR when (key, nonce) is reused.</summary>
    public static byte[] Xor(byte[] a, byte[] b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        if (a.Length != b.Length) throw new ArgumentException("lengths must match");
        var output = new byte[a.Length];
        for (int i = 0; i < a.Length; i++) output[i] = (byte)(a[i] ^ b[i]);
        return output;
    }
}
