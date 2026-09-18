using System;
using System.Security.Cryptography;
using System.Text;

namespace MiniWebServer.Host.MiniCrypto;

// TOTP = Time-based One-Time Password (RFC 6238), the canonical "what-you-have"
// authentication primitive covered in OSEP \u00a754.5.
//
// OSEP \u00a754.5 describes the "what-you-have" class \u2014 security tokens, smartphone
// authenticator apps, USB YubiKeys \u2014 and notes that without two-factor /
// multi-factor support these are vulnerable if the device is lost:
//
//   "What if you don\u2019t have it? What if you left your smartphone on your dresser
//    bureau this morning? \u2026 You now have a two-fold problem. First, you don\u2019t
//    have the magic item you need to authenticate yourself \u2026 Second, someone
//    else has your magic item, and possibly they can pretend to be you, fooling
//    the operating system \u2026"
//
// Two-factor auth (password + TOTP) overcomes this \u2014 the thief still needs your
// password. We do *not* combine TOTP with the M23.1 password flow in this slice;
// the demo just shows TOTP on its own. A future slice could expose a /auth/login
// route that requires both.
//
// RFC 6238 mechanics:
//   TOTP(K, T) = HOTP(K, T) where T = floor(unix_time / step_seconds)
//   HOTP(K, C) = truncate(HMAC-SHA-1(K, C)) mod 10^digits
//
// We use HMAC-SHA256 instead of HMAC-SHA1 (RFC 6238 §1.2 allows SHA-256); the
// truncation algorithm is the standard "offset = last_nibble & 0xf; 4-byte
// big-endian starting at offset" pattern.
//
// Step size = 30 seconds (RFC 6238 default). Acceptable skew = \u00b11 step.
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>
    /// Compute the 6-digit TOTP for the given secret at the given unix-time.
    /// </summary>
    public static int Compute(byte[] secret, long unixTimeSeconds)
    {
        if (secret is null) throw new ArgumentNullException(nameof(secret));
        long step = unixTimeSeconds / StepSeconds;
        return ComputeHmac(secret, step);
    }

    /// <summary>
    /// Verify a 6-digit code against the current TOTP, with a window of \u00b11 step
    /// (~30s \u00b1 30s) to absorb clock skew.
    /// </summary>
    public static bool Verify(byte[] secret, int code, long unixTimeSeconds)
    {
        if (secret is null) throw new ArgumentNullException(nameof(secret));
        long currentStep = unixTimeSeconds / StepSeconds;
        for (int skew = -1; skew <= 1; skew++)
        {
            int expected = ComputeHmac(secret, currentStep + skew);
            if (expected == code) return true;
        }
        return false;
    }

    /// <summary>
    /// RFC 6238 dynamic truncation: take 4 bytes of HMAC output starting at the offset
    /// byte indicated by the low 4 bits of the LAST byte of the HMAC. Mask the high
    /// bit so we get a 31-bit non-negative number, then mod 10^Digits.
    /// </summary>
    private static int ComputeHmac(byte[] secret, long stepCounter)
    {
        // Encode counter as 8-byte big-endian per RFC 4226.
        var counter = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(stepCounter & 0xff);
            stepCounter >>= 8;
        }

        using var hmac = new HMACSHA256(secret);
        byte[] hash = hmac.ComputeHash(counter);
        int offset = hash[hash.Length - 1] & 0x0f;
        int binary =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        int mod = (int)Math.Pow(10, Digits);
        return binary % mod;
    }

    /// <summary>
    /// Encode raw bytes as base32 (RFC 4648) without padding. Matches what
    /// authenticator apps (Google Authenticator, Authy) accept on enrollment.
    /// </summary>
    public static string ToBase32(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        int buffer = 0;
        int bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(alphabet[(buffer >> bits) & 0x1f]);
            }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 0x1f]);
        return sb.ToString();
    }
}
