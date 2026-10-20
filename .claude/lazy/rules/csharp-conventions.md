---
on:
  tools: [Edit, Write]
  paths: ["**/*.cs", "**/*.csx", "**/appsettings*.json"]
---
# C# Conventions

Adapted from the ECC `rules/csharp` coding-style and security files. House convention, not vendor guidance. The compiler and analyzers (nullable, warnings as errors, `.editorconfig`) enforce the formatting rules; this file holds what they cannot.

## Custom rules

### Required

Nullable reference types MUST be enabled. Blocking calls such as `.Result` and `.Wait()` MUST NOT be used.

Public async APIs MUST accept and pass on a `CancellationToken`.

Secrets and connection strings MUST NOT be hardcoded or kept in `appsettings*.json`.

Data access MUST use parameterized queries (EF Core or parameters), never string concatenation.

Inputs MUST be validated at the API boundary before business logic runs.

Authentication MUST use the framework handlers, not custom token parsing. Authorization MUST be enforced at the endpoint or handler.

Logs MUST NOT contain raw tokens, passwords, personal data or biometric data. API errors MUST NOT expose stack traces, SQL or file paths; use `ProblemDetails`.

A generic repository over EF Core and an `ApiResponse<T>` envelope MUST NOT be introduced. Use `TypedResults` and `ProblemDetails` as the `dotnet-webapi` skill describes.

### Recommended

Immutable value-like models SHOULD be `record` types. Service boundaries SHOULD be interfaces. Configuration SHOULD use the options pattern. DI lifetimes SHOULD be chosen deliberately.

### Advisory

Expression-bodied members MAY be used when they stay readable.
