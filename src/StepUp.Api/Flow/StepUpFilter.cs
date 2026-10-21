using StepUp.Api.Auth;
using StepUp.Core;

namespace StepUp.Api.Flow;

/// <summary>Marks an endpoint as a high-risk action that needs step-up for <see cref="Action"/>.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresStepUpAttribute(string action) : Attribute
{
    public string Action { get; } = action;
}

/// <summary>An endpoint argument that binds the assertion to its payload (a hash of the fields that matter).</summary>
public interface IStepUpPayload
{
    string PayloadHash { get; }
}

/// <summary>
/// Enforces <see cref="RequiresStepUpAttribute"/>: the request passes on a valid single-use assertion bound to this
/// user, action and payload, OR on fresh MFA through the fallback factor (logged as Mode=fallback).
/// Voice is never the only factor: the bearer token is already validated and its oid and tid are what the assertion must match.
/// </summary>
public sealed class StepUpFilter(
    StepUpAssertionService assertions,
    IStepUpPolicy policy,
    IAttemptLog attempts,
    IVerifier verifier,
    IUserKeyHasher hasher,
    TimeProvider time) : IEndpointFilter
{
    public const string AssertionHeader = "X-StepUp-Assertion";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var required = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequiresStepUpAttribute>();
        if (required is null)
        {
            return await next(context);
        }

        var http = context.HttpContext;
        var caller = CallerFactory.From(http.User);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        var payloadHash = context.Arguments.OfType<IStepUpPayload>().FirstOrDefault()?.PayloadHash;
        var presented = http.Request.Headers[AssertionHeader].ToString();
        var assertionValid = presented.Length > 0
            && (await assertions.ValidateAndConsumeAsync(presented, new AssertionBinding(caller.Oid, caller.Tid, required.Action, payloadHash), http.RequestAborted)).Ok;

        if (!policy.IsSatisfied(caller, assertionValid))
        {
            return Problems.Create(StatusCodes.Status403Forbidden, ReasonCode.StepUpRequired, fallback: true);
        }

        if (!assertionValid)
        {
            await attempts.AppendAsync(
                new Attempt(Guid.NewGuid(), hasher.Hash(caller.Tid, caller.Oid), time.GetUtcNow(), required.Action, null, 0, verifier.ModelId, AttemptResult.Pass, null, AttemptMode.Fallback),
                http.RequestAborted);
        }

        return await next(context);
    }
}
