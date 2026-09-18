using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MiniWebServer.Host.MiniAuth;

// In-memory user store keyed by username.
// Persists nothing \u2014 restarting the server drops every registered user. That's
// fine for a lab; OSEP \u00a754.4 only requires that the *stored* form of the
// password be the hash + salt, and that the original never be persisted.
//
// Thread-safety: ConcurrentDictionary so the 8-worker HTTP handler pool can
// register / verify concurrently without an external lock.
public static class UserStore
{
    public sealed record AuthUser(DateTimeOffset CreatedAt, byte[] Salt, byte[] Hash);

    private static readonly ConcurrentDictionary<string, AuthUser> _users =
        new(StringComparer.Ordinal);   // case-sensitive: matches OSEP \u00a754.10 "no info to non-authenticated users".

    // Single counter that lets us prove OSEP \u00a754.4 ("shut off access ... after too many wrong
    // guesses") hasn't been implemented \u2014 every failed login still increments it.
    public static int FailedLoginAttempts;
    public static int SuccessfulLogins;

    /// <summary>
    /// Register a user. Returns true on success, false if the username is already taken.
    /// The plaintext password is never stored anywhere \u2014 only the PBKDF2 hash + salt.
    /// </summary>
    public static bool Register(string username, string password)
    {
        if (string.IsNullOrEmpty(username)) return false;
        if (string.IsNullOrEmpty(password)) return false;

        var result = PasswordHasher.Hash(password);
        var user = new AuthUser(DateTimeOffset.UtcNow, result.Salt, result.Hash);

        // AddOrUpdate pattern: if another thread registered the same user concurrently,
        // the dictionary keeps whichever pair was inserted first. The plaintext password
        // was hashed, not stored, so even a race "leak" only reveals the salt + hash pair.
        var existing = _users.GetOrAdd(username, user);
        if (!ReferenceEquals(existing, user))
        {
            return false;   // someone else won the race \u2014 username taken
        }
        return true;
    }

    /// <summary>
    /// Verify a password. Returns true only when the password matches the stored hash.
    /// OSEP \u00a753.4 (fail-safe defaults) note: we return the *same* false for both
    /// "no such user" and "wrong password" so an attacker can't probe usernames.
    /// </summary>
    public static bool Login(string username, string password)
    {
        if (string.IsNullOrEmpty(username))
        {
            Interlocked.Increment(ref FailedLoginAttempts);
            return false;
        }
        if (string.IsNullOrEmpty(password))
        {
            Interlocked.Increment(ref FailedLoginAttempts);
            return false;
        }

        if (!_users.TryGetValue(username, out var user))
        {
            // Burn ~same PBKDF2 work as a real attempt so the response time
            // doesn't reveal whether the username exists (timing side-channel).
            PasswordHasher.Verify(password, new byte[PasswordHasher.SaltLength], new byte[PasswordHasher.HashLength]);
            Interlocked.Increment(ref FailedLoginAttempts);
            return false;
        }

        bool ok = PasswordHasher.Verify(password, user.Salt, user.Hash);
        if (ok) Interlocked.Increment(ref SuccessfulLogins);
        else    Interlocked.Increment(ref FailedLoginAttempts);
        return ok;
    }

    /// <summary>
    /// Snapshot what's stored. Returned to clients via /auth/dump so the
    /// educational point ("we never store plaintext") can be verified visually.
    /// </summary>
    public static (int count, string[] summary) DumpSummary()
    {
        int n = _users.Count;
        var lines = new string[n];
        int i = 0;
        foreach (var kv in _users)
        {
            lines[i++] = $"  {kv.Key}\tcreated={kv.Value.CreatedAt:O}\tsalt={PasswordHasher.ToHex(kv.Value.Salt)}\tsha256(pbkdf2)={PasswordHasher.ToHex(kv.Value.Hash)}";
        }
        return (n, lines);
    }

    // Test helper: clear the store. Not exposed via HTTP.
    public static void ClearForTests() => _users.Clear();
}
