using System.Globalization;
using System.Security.Claims;
using StepUp.Core;

namespace StepUp.Api.Auth;

public static class CallerFactory
{
    public const string RequiredScope = "access_as_user";
    public const string AnalystRole = "Analyst";

    /// <summary>Reads tid, oid, amr and auth_time as the validated token carries them (MapInboundClaims is off).</summary>
    public static Caller? From(ClaimsPrincipal user)
    {
        var tid = user.FindFirstValue("tid");
        var oid = user.FindFirstValue("oid");
        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(oid))
        {
            return null;
        }

        DateTimeOffset? authTime = long.TryParse(user.FindFirstValue("auth_time"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return new Caller(tid, oid, [.. user.FindAll("amr").Select(c => c.Value)], authTime);
    }

    /// <summary>A delegated user token for this API: has the scope, and is not an app-only token.</summary>
    public static bool IsDelegatedUserToken(ClaimsPrincipal user)
    {
        var scopes = (user.FindFirstValue("scp") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Contains(RequiredScope, StringComparer.Ordinal)
            && !string.Equals(user.FindFirstValue("idtyp"), "app", StringComparison.Ordinal)
            && From(user) is not null;
    }
}
