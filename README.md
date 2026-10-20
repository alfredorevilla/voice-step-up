# voice-step-up

Voice step-up for Microsoft Entra ID. An ASP.NET Core API checks a spoken phrase with a self-hosted speaker verifier and asserts `amr: ["vbm"]`. Entra MFA is the fallback. Portfolio project.

Status: foundation only. Architecture and ADRs arrive in M1 (`docs/`).

## Privacy

To be written in M8.

## Limits

To be written in M8.

## Build and test

```bash
dotnet build
dotnet test
```

Requires the .NET SDK pinned in `global.json`.

## Licence

MIT. See `LICENSE`.
