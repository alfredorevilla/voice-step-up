---
on:
  tools: [Edit, Write]
  paths: ["**/*.cs", "**/appsettings*.json"]
---
# C# Conventions

Adapted from the ECC `rules/csharp` coding-style and security files. House convention, not vendor guidance. `Directory.Build.props` (nullable, warnings as errors) and the analyzers enforce what they can; `.editorconfig` holds few C# rules and CI runs no format check, so formatting is by convention. This file holds what the tools cannot.

## Custom rules

### Required

Blocking calls such as `.Result` and `.Wait()` MUST NOT be used. Public async APIs MUST accept a `CancellationToken`.

Secrets, connection strings, tenant ids and client ids MUST NOT be hardcoded or kept in `appsettings*.json`.

Data access MUST use parameterized queries (EF Core or parameters), never string concatenation.

WAV uploads MUST be checked before parsing: at most 10 s and 1 MB, PCM16 mono 16 kHz; reject with 413 (too large) or 415 (wrong format). Other inputs MUST be validated at the API boundary before business logic runs.

Authentication MUST use the framework handlers, not custom token parsing. Authorization MUST be enforced at the endpoint or handler.

Logs MUST NOT contain raw tokens, passwords, personal data or biometric data (see `step-up-invariants.md`). API errors MUST NOT expose stack traces, SQL or file paths; use RFC 9457 `ProblemDetails` with a `code` field from the reason-code list in `docs/architecture.md` section 4.

A generic repository over EF Core and an `ApiResponse<T>` envelope MUST NOT be introduced. This overrides `~/.claude/AGENTS.md`, which prescribes both. Use `TypedResults` and `ProblemDetails` as the `dotnet-webapi` skill describes.

### Recommended

Immutable value-like models SHOULD be `record` types. Service boundaries SHOULD be interfaces. Configuration SHOULD use the options pattern.
