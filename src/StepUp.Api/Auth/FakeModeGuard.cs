namespace StepUp.Api.Auth;

/// <summary>
/// Fake auth and the fake verifier start only with an explicit demo flag AND a Development or Demo environment
/// (the compose demo sets Demo). Anything else is a startup failure, so a misconfigured real deployment cannot
/// trust the fake authority (threat model TM-013).
/// </summary>
public static class FakeModeGuard
{
    public static void Require(string what, bool demoFlag, string environmentName)
    {
        var allowedEnvironment = environmentName is "Development" or "Demo";
        if (!demoFlag || !allowedEnvironment)
        {
            throw new InvalidOperationException(
                $"{what} is fake and may run only with Demo:Enabled=true in the Development or Demo environment (environment is '{environmentName}', Demo:Enabled={demoFlag}).");
        }
    }
}
