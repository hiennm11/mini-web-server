using System;
using System.Collections.Concurrent;

namespace MiniWebServer.Host.MiniCrypto;

// In-memory at-rest encrypted store (OSEP \u00a756.7 "user-level encryption performed
// through an application"). Each named slot holds:
//   - the AES-256-GCM EncryptedBlock (nonce + ciphertext + tag) at rest
//   - the plaintext only when an authorized caller has asked to decrypt it
//
// Thread-safety: ConcurrentDictionary, same pattern as UserStore. Demo helper
// only \u2014 a real key-management system would persist the key (or derive it via
// PBKDF2 from a user passphrase, which is exactly the bridge into MiniAuth).
public static class AtRestStore
{
    public sealed record Slot(string Name, DateTimeOffset CreatedAt, SymmetricCipher.EncryptedBlock Block);

    private static readonly ConcurrentDictionary<string, Slot> _slots =
        new(StringComparer.Ordinal);

    // Single key per process: rotated only when the user asks for it via /crypto/keygen.
    // OSEP \u00a756.6: keys must stay secret. We keep it only in process memory; a real
    // system would derive it from a passphrase via PBKDF2 each session.
    private static byte[] _key = SymmetricCipher.NewKey();
    public static byte[] CurrentKey => _key;

    public static void RotateKey() { _key = SymmetricCipher.NewKey(); }

    /// <summary>Encrypt + store a named slot. Plaintext is wiped from input array (best-effort).</summary>
    public static Slot Put(string name, byte[] plaintext, byte[]? associatedData = null)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));

        var block = SymmetricCipher.Encrypt(plaintext, _key, associatedData);
        var slot = new Slot(name, DateTimeOffset.UtcNow, block);
        _slots[name] = slot;
        CryptographicOperations.ZeroMemory(plaintext);
        return slot;
    }

    /// <summary>Decrypt a named slot. Throws CryptographicException on tamper / wrong key / wrong AAD.</summary>
    public static byte[] Get(string name, byte[]? associatedData = null)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
        if (!_slots.TryGetValue(name, out var slot))
        {
            throw new KeyNotFoundException($"no slot named '{name}'");
        }
        return SymmetricCipher.Decrypt(slot.Block, _key, associatedData);
    }

    public static int Count => _slots.Count;

    /// <summary>Enumerate every slot. For smoke demos + the tamper demo.</summary>
    public static System.Collections.Generic.IEnumerable<Slot> AllSlots()
    {
        foreach (var kv in _slots) yield return kv.Value;
    }

    /// <summary>Snapshot every slot's at-rest form (no plaintext). Demonstrates that storage holds only nonce + ciphertext + tag.</summary>
    public static (int count, string[] summary) DumpSummary()
    {
        int n = _slots.Count;
        var lines = new string[n];
        int i = 0;
        foreach (var kv in _slots)
        {
            lines[i++] = $"  {kv.Key}\tcreated={kv.Value.CreatedAt:O}\t" +
                         $"nonce={SymmetricCipher.ToHex(kv.Value.Block.Nonce)}\t" +
                         $"len={kv.Value.Block.Ciphertext.Length}\t" +
                         $"tag={SymmetricCipher.ToHex(kv.Value.Block.Tag)}";
        }
        return (n, lines);
    }

    public static void ClearForTests() => _slots.Clear();
}

// Polyfill for Span<byte>.Clear we use in a few places.
internal static class CryptographicOperations
{
    public static void ZeroMemory(byte[] bytes)
    {
        if (bytes is null) return;
        Array.Clear(bytes, 0, bytes.Length);
    }
}
