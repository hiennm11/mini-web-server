using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MiniWebServer.Host.MiniAuth;

// In-memory user store keyed by username.
// Persists nothing — restarting the server drops every registered user. That's
// fine for a lab; OSEP §54.4 only requires that the *stored* form of the
// password be the hash + salt, and that the original never be persisted.
//
// Thread-safety: ConcurrentDictionary so the 8-worker HTTP handler pool can
// register / verify concurrently without an external lock.
//
// M23.3 extension: every AuthUser carries a Role (User | Admin). OSEP §55.6
// "Role-Based Access Control" by Ferraiolo-Kuhn; we model the simple two-role
// case (admin / user) which the chapter names as "a particularly valuable pattern".
public static class UserStore
{
    public enum Role { User, Admin }

    public sealed record AuthUser(DateTimeOffset CreatedAt, byte[] Salt, byte[] Hash, Role Role);

    private static readonly ConcurrentDictionary<string, AuthUser> _users =
        new(StringComparer.Ordinal);   // case-sensitive: matches OSEP §54.10 "no info to non-authenticated users".

    // Single counter that lets us prove OSEP §54.4 ("shut off access ... after too many wrong
    // guesses") hasn't been implemented — every failed login still increments it.
    public static int FailedLoginAttempts;
    public static int SuccessfulLogins;

    /// <summary>
    /// Register a user. Returns true on success, false if the username is already taken.
    /// The plaintext password is never stored anywhere — only the PBKDF2 hash + salt.
    /// </summary>
    public static bool Register(string username, string password, Role role = Role.User)
    {
        if (string.IsNullOrEmpty(username)) return false;
        if (string.IsNullOrEmpty(password)) return false;

        var result = PasswordHasher.Hash(password);
        var user = new AuthUser(DateTimeOffset.UtcNow, result.Salt, result.Hash, role);

        // AddOrUpdate pattern: if another thread registered the same user concurrently,
        // the dictionary keeps whichever pair was inserted first. The plaintext password
        // was hashed, not stored, so even a race "leak" only reveals the salt + hash pair.
        var existing = _users.GetOrAdd(username, user);
        if (!ReferenceEquals(existing, user))
        {
            return false;   // someone else won the race — username taken
        }
        return true;
    }

    /// <summary>
    /// Grant a new Role to an already-registered user (M23.3 RBAC).
    /// Returns false if the user is unknown.
    /// </summary>
    public static bool GrantRole(string username, Role newRole)
    {
        if (string.IsNullOrEmpty(username)) return false;
        // AddOrUpdate: if the user exists, replace with role-bumped copy; if not,
        // return null. The factory signature gives us current value when present,
        // and the first arg is whether to create if missing (we say no by returning null).
        AuthUser? result = null;
        _users.AddOrUpdate(username,
            addValueFactory: (string _) => null!,
            updateValueFactory: (string _, AuthUser u) =>
            {
                result = u with { Role = newRole };
                return result;
            });
        return result is not null;
    }

    /// <summary>
    /// Verify that the (username, password) pair identifies a user with the required role.
    /// Returns false if the user is unknown, the password is wrong, or the role is insufficient.
    /// </summary>
    public static bool AuthenticateWithRole(string username, string password, Role requiredRole)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
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
        if (!ok)
        {
            Interlocked.Increment(ref FailedLoginAttempts);
            return false;
        }
        // OSEP §55.6 RBAC: "Give a user or a process the minimum privileges required."
        // We enforce least-privilege at this gate.
        if (requiredRole == Role.Admin && user.Role != Role.Admin)
        {
            Interlocked.Increment(ref FailedLoginAttempts);
            return false;
        }
        Interlocked.Increment(ref SuccessfulLogins);
        return true;
    }

    /// <summary>
    /// Read the role of a user. Returns null if unknown.
    /// OSEP §53.4 "fail-safe defaults": this is informational; for any
    /// gate we use AuthenticateWithRole so the response doesn't leak role info.
    /// </summary>
    public static Role? GetRole(string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        return _users.TryGetValue(username, out var u) ? u.Role : null;
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
            lines[i++] = $"  {kv.Key}\tcreated={kv.Value.CreatedAt:O}\trole={kv.Value.Role}\tsalt={PasswordHasher.ToHex(kv.Value.Salt)}\tsha256(pbkdf2)={PasswordHasher.ToHex(kv.Value.Hash)}";
        }
        return (n, lines);
    }

    // Test helper: clear the store. Not exposed via HTTP.
    public static void ClearForTests() => _users.Clear();
}
