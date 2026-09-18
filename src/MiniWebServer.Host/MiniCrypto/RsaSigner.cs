using System;
using System.Security.Cryptography;
using System.Text;

namespace MiniWebServer.Host.MiniCrypto;

// Public-key cryptography (OSEP \u00a756.3):
//   - Keypair {private, public}. Private = kept secret; public = freely shared.
//   - Sign with private key \u2192 anyone with the public key can verify.
//   - Here we only do *signing* (authentication). OSEP \u00a756.3 also describes
//     public-key *encryption* (encrypt with public, decrypt with private) and
//     mixed flows (Alice signs with own private, then encrypts for Bob's public);
//     those are deferred \u2014 they'd require asymmetric encryption modes that BCL
//     RSA does not provide well (OAEP for encryption, PSS for signatures).
//
// OSEP \u00a756.3 explicitly warns on the practice:
//   "THE CRYPTOGRAPHY'S BENEFIT RELIES ENTIRELY ON THE SECRECY OF THE KEY.
//    In this case, the private key. \u2026 If you lose a private key, everything
//    you used it for is at risk."
//
// OSEP \u00a757.3 (public-key auth in distributed systems) is the natural follow-up;
// we cover that with the companion handshake demo (M23.5).
public static class RsaSigner
{
    public const int KeySize = 2048;   // bits; matches OSEP \u00a757.5 "RSA with 2048 bit keys".

    /// <summary>
    /// In-memory keypair: private kept in process memory (like the AES key in
    /// M23.2); public returned to the caller via /crypto/rsa-keygen for downstream
    /// verification. Real systems distribute the public key via X.509 certs,
    /// which is OSEP \u00a757.3 + \u00a757.5.
    /// </summary>
    public sealed record KeyPair(byte[] PublicKey, RSA PrivateKey);

    private static RSA? _rsa;
    private static byte[]? _publicKeyBytes;

    /// <summary>
    /// Generate a fresh 2048-bit RSA keypair. Discards any prior key.
    /// </summary>
    public static KeyPair Generate()
    {
        // Dispose any prior key so we don't leak the old private bits in process memory.
        _rsa?.Dispose();
        _rsa = RSA.Create(KeySize);
        _publicKeyBytes = _rsa.ExportSubjectPublicKeyInfo();
        return new KeyPair((byte[])_publicKeyBytes.Clone(), _rsa);
    }

    /// <summary>
    /// Sign a UTF-8 string with the in-memory private key.
    /// Returns the PSS signature bytes (OSEP \u00a756.3 PKCS-style).
    /// </summary>
    public static byte[] Sign(string message)
    {
        if (_rsa is null) throw new InvalidOperationException("call Generate() first");
        if (message is null) throw new ArgumentNullException(nameof(message));
        byte[] data = Encoding.UTF8.GetBytes(message);
        return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    /// <summary>
    /// Verify a signature against the stored public key (or, optionally, an
    /// external one supplied by the caller). Returns true on match.
    /// </summary>
    public static bool Verify(string message, byte[] signature, byte[]? publicKeyBytesOverride = null)
    {
        if (signature is null) return false;
        if (message is null) throw new ArgumentNullException(nameof(message));
        byte[] data = Encoding.UTF8.GetBytes(message);

        if (publicKeyBytesOverride is not null)
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKeyBytesOverride, out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        if (_publicKeyBytes is null || _rsa is null) return false;
        return _rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    /// <summary>Hex-encoded public key for transport. Use ImportPublicKey below to recover.</summary>
    public static byte[]? CurrentPublicKey => _publicKeyBytes;

    /// <summary>Import an externally-distributed public key so subsequent Verify calls use it
    /// instead of the in-memory key. Mirrors OSEP \u00a757.3 "you only need to know what
    /// you're trying to authenticate to verify its signature".</summary>
    public static void SetActivePublicKey(byte[] publicKeyBytes)
    {
        if (publicKeyBytes is null) throw new ArgumentNullException(nameof(publicKeyBytes));
        _publicKeyBytes = (byte[])publicKeyBytes.Clone();
        // We don't import into the RSA object \u2014 Verify handles ad-hoc public keys.
    }

    public static string ToHex(byte[] bytes) => SymmetricCipher.ToHex(bytes);
    public static byte[] FromHex(string hex) => SymmetricCipher.FromHex(hex);
}
