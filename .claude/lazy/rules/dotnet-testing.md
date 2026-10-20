---
on:
  tools: [Edit, Write]
  paths: ["tests/**/*", "**/*.csproj", "Directory.Packages.props", "global.json"]
---
# .NET Testing

## Custom rules

### Required

Tests MUST use xUnit v3 on Microsoft.Testing.Platform, set up with the `scaffold-dotnet-test-project` and `run-tests` skills.

Assertions MUST use xUnit's built-in `Assert`. A fluent assertion library MUST NOT be added: FluentAssertions 8 and later needs a paid Xceed licence for commercial use, and this repo is shown to clients (decision of 2026-10-20; AwesomeAssertions, Apache-2.0, is the fallback if a fluent style is wanted later).

API tests MUST go through HTTP with `WebApplicationFactory` and the fake authority. They MUST NOT bypass authentication or authorization middleware.

Every `IVerifier` implementation MUST pass the shared contract suite.

Tests that need a model file MUST be tagged `Category=Model`. Unit tests MUST NOT use the network.

Test names MUST describe behavior, not implementation.

### Recommended

Line coverage on Core and Api SHOULD be at least 80%.

The `test-anti-patterns` and `assertion-quality` skills SHOULD be run over the suite before review.

## Sources

Licence fact: Xceed Fluent Assertions FAQ, https://xceed.com/fluent-assertions-faq/ , and the repository LICENSE (Xceed Community License, non-commercial use). Tier: authoritative (the vendor's own terms). Retrieved 2026-10-20.
