using System;
using System.Security.Cryptography;

namespace MiniWebServer.Host.MiniAuth;

// Password-based authentication primitives per OSEP \u00a754.4.
//
// Three properties enforced:
//   1. Never store plaintext passwords \u2014 only the derived hash.
//   2. Per-call random salt \u2014 same password yields different hashes for different users.
//   3. Slow KDF \u2014 each verify costs ~50\u2013100 ms of PBKDF2 work, making dictionary attacks
//      (OSEP \u00a754.4 "drastically slowing down") measurably expensive instead of microseconds.
//
// Verification uses CryptographicOperations.FixedTimeEquals to defend against
// timing-side-channel attacks: a naive `==` comparison short-circuits on the
// first differing byte, leaking the longest prefix of a correct hash.
public static class PasswordHasher
{
    // OWASP 2024 minimum PBKDF2-HMAC-SHA256 iteration count.
    // OSEP \u00a754.4 does not mandate a specific number; it just observes
    // that each guess must cost meaningful work.
    public const int Iterations = 100_000;

    public const int SaltLength = 16;        // 128 bits \u2014 matches OSEP \u00a754.4 "32 or 64 bits" minimum with margin.
    public const int HashLength = 32;        // 256 bits \u2014 output length of SHA-256.

    // PBKDF2 expects the underlying HMAC; SHA-256 (default for Rfc2898DeriveBytes) is fine.
    public static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Hash a password using PBKDF2-HMAC-SHA256 with a fresh random salt.
    /// Returns the salt alongside the derived hash so the caller can store both.
    /// </summary>
    public static HashResult Hash(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));

        Span<byte> salt = stackalloc byte[SaltLength];
        RandomNumberGenerator.Fill(salt);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithm,
            outputLength: HashLength);
        return new HashResult(salt.ToArray(), hash);
    }

    /// <summary>
    /// Verify a password against a stored salt + hash using constant-time compare.
    /// </summary>
    public static bool Verify(string password, byte[] salt, byte[] expectedHash)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        if (salt is null || salt.Length != SaltLength) return false;
        if (expectedHash is null || expectedHash.Length != HashLength) return false;

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithm,
            outputLength: HashLength);

        bool ok = CryptographicOperations.FixedTimeEquals(actual, expectedHash);

        // Wipe the candidate hash off the stack. Treats the local copy like any other
        // short-lived secret. Best-effort; JIT may keep a copy.
        CryptographicOperations.ZeroMemory(actual);
        return ok;
    }

    public readonly record struct HashResult(byte[] Salt, byte[] Hash);

    // Hex helpers used in HTTP responses (which are text/plain).
    public static string ToHex(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
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
}
