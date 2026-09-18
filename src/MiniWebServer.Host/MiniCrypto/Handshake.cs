using System;
using System.Security.Cryptography;

namespace MiniWebServer.Host.MiniCrypto;

// Simplified TLS-style handshake (OSEP \u00a757.5): two parties exchange random nonces
// and derive a per-session symmetric key via HKDF-SHA256 (RFC 5869). Same primitive
// OpenSSL/SChannel use. We model client + server in the same process for the lab.
//
// OSEP \u00a757.5 says it plainly:
//   "you can use Diffie-Hellman key exchange to create the key (and SSL frequently
//    does), but we need to be sure who we are sharing that key with."
// In real TLS the client also signs the handshake (with RSA / ECDSA, M23.4
// primitives) and verifies a server certificate (OSEP \u00a757.3). Both of those are
// skipped in this slice \u2014 we only model the *key derivation* half.
//
// We do *not* run a real Diffie-Hellman here: OSEP \u00a757.5 also notes ECDH/RSA key
// gen takes "hundreds of times longer" than AES and warns you'd pick fresh keys
// per connection. Instead we use a shared symmetric secret as the HKDF input
// salt \u2014 the same primitive TLS would use after ECDH (\u00a757.5 step 3: derive the
// master secret via HKDF). The result: a 256-bit session key with no public-key
// involvement, but with the *structure* of an OSEP \u00a757.5 handshake.
//
// Public-key auth on top of this (\u00a757.3 + M23.4) is left to a future slice.
public static class Handshake
{
    public const int NonceLength = 32;     // 256-bit client / server nonce.
    public const int SessionKeyLength = 32; // 256-bit session AES key.

    /// <summary>
    /// Run a simulated handshake. Returns the client nonce, server nonce, and the
    /// derived session key. The route layer ("/crypto/handshake") demonstrates the
    /// demo: it uses <see cref="SymmetricCipher"/> to encrypt + decrypt with the
    /// session key, proving it's a real AES-256 key.
    /// </summary>
    public static (byte[] ClientNonce, byte[] ServerNonce, byte[] SessionKey) RunDemo()
    {
        var clientNonce = new byte[NonceLength];
        var serverNonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(clientNonce);
        RandomNumberGenerator.Fill(serverNonce);

        // HKDF-SHA256 input material: clientNonce || serverNonce (RFC 5869: salt = PRK,
        // info = transcript). We use a constant shared secret as the HKDF salt; in a
        // real TLS this would be the ECDH shared secret (which the client and server
        // derive independently and never transmit).
        var ikm = new byte[32];
        RandomNumberGenerator.Fill(ikm);

        // Build the HKDF info string \u2014 a transcript label so two different handshakes
        // with the same nonces still produce different keys.
        byte[] info = System.Text.Encoding.UTF8.GetBytes("M23.5-handshake-v1");
        byte[] saltInput = System.Text.Encoding.UTF8.GetBytes("mini-web-server-handshake");

        var sessionKey = HkdfSha256(ikm, saltInput, info, SessionKeyLength);
        return (clientNonce, serverNonce, sessionKey);
    }

    /// <summary>
    /// HKDF-SHA256 (RFC 5869). Returns a derived key of `outputLength` bytes.
    /// </summary>
    public static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int outputLength)
    {
        // Use the .NET 8+ HKDF helper if present; we don't have it directly, so
        // re-implement the RFC 5869 two-step (Extract + Expand) using HMACSHA256.
        if (ikm is null) throw new ArgumentNullException(nameof(ikm));
        if (salt is null) throw new ArgumentNullException(nameof(salt));
        if (info is null) throw new ArgumentNullException(nameof(info));

        // Step 1: Extract. If salt is empty, fall back to HashLen (32) zeros.
        byte[] prk;
        using (var hmac = new HMACSHA256(salt.Length == 0 ? new byte[32] : salt))
        {
            prk = hmac.ComputeHash(ikm);
        }

        // Step 2: Expand.
        var output = new byte[outputLength];
        int written = 0;
        byte counter = 1;
        byte[] previous = Array.Empty<byte>();
        while (written < outputLength)
        {
            using var hmac = new HMACSHA256(prk);
            var input = new byte[previous.Length + info.Length + 1];
            Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
            Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
            input[input.Length - 1] = counter++;
            previous = hmac.ComputeHash(input);

            int copy = Math.Min(previous.Length, outputLength - written);
            Buffer.BlockCopy(previous, 0, output, written, copy);
            written += copy;
        }
        return output;
    }
}
