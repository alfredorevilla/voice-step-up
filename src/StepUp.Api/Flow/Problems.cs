using StepUp.Core;

namespace StepUp.Api.Flow;

public static class Problems
{
    /// <summary>RFC 9457 problem details with a <c>code</c> from the reason-code list and, when relevant, the fallback instruction.</summary>
    public static IResult Create(int status, ReasonCode code, bool fallback = false, TimeSpan? retryAfter = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code.ToString() };
        if (fallback)
        {
            extensions["fallback"] = new { type = "entra-mfa", instruction = "Re-authenticate with multi-factor authentication and retry." };
        }

        if (retryAfter is { } wait)
        {
            extensions["retryAfterSeconds"] = (int)Math.Ceiling(wait.TotalSeconds);
        }

        return Results.Problem(statusCode: status, title: code.ToString(), extensions: extensions);
    }
}
