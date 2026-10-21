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

## Run the real verifier

The speaker and speech models are not in the repo. Fetch them (about 450 MB, every file checked against `models/models.lock`) and run the model tests:

```bash
scripts/fetch-models.sh
dotnet test -- --filter-trait "Category=Model"
```

Or run the API with the models in a container: `docker compose up --build --wait` (health check on `http://localhost:8080/healthz`). Design and measured latency: `docs/design/verifier.md`. Licences of the models: `NOTICE`.

## Licence

MIT. See `LICENSE`.
