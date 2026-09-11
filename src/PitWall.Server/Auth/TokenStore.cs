using System.Security.Cryptography;
using System.Text;
using PitWall.Core.Auth;

namespace PitWall.Server.Auth;

/// <summary>
/// Holds the F1 TV subscription token between runs without ever writing it in plain text.
/// </summary>
/// <remarks>
/// On Windows the token is sealed with DPAPI for the current user, so the file is useless on
/// any other account or machine. <c>PITWALL_F1TV_TOKEN</c> overrides the file (handy for a
/// headless server); it accepts the raw <c>login-session</c> cookie value or the bare JWT.
/// Nothing in the repository ever holds a token.
/// </remarks>
public sealed class TokenStore
{
    public const string EnvironmentVariable = "PITWALL_F1TV_TOKEN";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PitWall.F1TV.v1");

    private readonly string _path;
    private readonly Lock _gate = new();
    private F1TvToken? _current;

    public TokenStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "f1tv.token");
        _current = F1TvToken.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariable)) ?? ReadFile();
    }

    public event Action<F1TvToken?>? Changed;

    public F1TvToken? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>The token to send, or null when there is none worth sending.</summary>
    public string? UsableToken(DateTimeOffset now)
    {
        var token = Current;
        return token is { IsActive: true } && !token.IsExpired(now) ? token.SubscriptionToken : null;
    }

    /// <summary>Stores a token from the sign-in window. Returns null if the value is not a readable token.</summary>
    public F1TvToken? Save(string cookieOrJwt)
    {
        var token = F1TvToken.TryParse(cookieOrJwt);
        if (token is null)
        {
            return null;
        }

        lock (_gate)
        {
            _current = token;
            WriteFile(token.SubscriptionToken);
        }
        Changed?.Invoke(token);
        return token;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        Changed?.Invoke(null);
    }

    private F1TvToken? ReadFile()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return F1TvToken.TryParse(Encoding.UTF8.GetString(plain));
        }
        catch (CryptographicException)
        {
            // Sealed by another user or machine: treat as signed out.
            return null;
        }
    }

    private void WriteFile(string jwt)
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Elsewhere the token lives in memory (or the environment variable) only.
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(jwt), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, sealedBytes);
    }
}
