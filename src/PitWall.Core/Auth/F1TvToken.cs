using System.Text;
using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Core.Auth;

/// <summary>
/// The F1 TV subscription token the live-timing feed accepts as a bearer token.
/// </summary>
/// <remarks>
/// Signing in at account.formula1.com sets a <c>login-session</c> cookie holding URL-encoded
/// JSON; <c>data.subscriptionToken</c> inside it is a JWT whose payload names the subscription
/// status and expiry. Nothing here validates the signature: the token is only ever handed back
/// to Formula 1, which does that itself. We read the payload to tell the user whether it will work.
/// </remarks>
public sealed record F1TvToken(string SubscriptionToken, string SubscriptionStatus, string? Product, DateTimeOffset Expires)
{
    public bool IsActive => string.Equals(SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase);

    public bool IsExpired(DateTimeOffset now) => Expires <= now;

    /// <summary>Accepts the raw <c>login-session</c> cookie value, its decoded JSON, or a bare JWT.</summary>
    public static F1TvToken? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var jwt = ExtractJwt(input.Trim());
        if (jwt is null)
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = JsonNode.Parse(Base64UrlDecode(parts[1]));
            var status = payload?["SubscriptionStatus"].AsString() ?? payload?["subscriptionStatus"].AsString();
            var exp = payload?["exp"].AsInt() ?? 0;
            if (status is null || exp <= 0)
            {
                return null;
            }
            var product = payload?["SubscribedProduct"].AsString() ?? payload?["subscribedProduct"].AsString();
            return new F1TvToken(jwt, status, product, DateTimeOffset.FromUnixTimeSeconds(exp));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ExtractJwt(string input)
    {
        if (input.StartsWith("ey", StringComparison.Ordinal) && input.Count(c => c == '.') == 2)
        {
            return input;
        }

        var json = input.StartsWith('{') ? input : Uri.UnescapeDataString(input);
        try
        {
            return JsonNode.Parse(json)?["data"]?["subscriptionToken"].AsString();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    /// <summary>Never print the token itself.</summary>
    public override string ToString() => $"F1TvToken {{ Status = {SubscriptionStatus}, Product = {Product}, Expires = {Expires:u} }}";
}
