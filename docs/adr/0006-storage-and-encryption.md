# ADR-0006: SQLite storage with encrypted embeddings

Status: Proposed
Date: 2026-10-20
Decisions: D7

## Context
The service stores consent, voiceprints, attempts, challenges and thresholds. Voiceprints are biometric data.

## Decision
SQLite through EF Core. Embeddings are encrypted with ASP.NET Core Data Protection and stored with the model id. No raw audio is stored or logged. Deleting a voiceprint deletes the row.

## Alternatives considered
- PostgreSQL: more moving parts for a demonstrator.
- Files per user: no transactions, weaker deletion guarantees.
- Store only a hash of the embedding: a hash cannot be compared by similarity.
- Azure Key Vault for keys: adds a cloud dependency to the offline demo.

## Consequences
- Losing the Data Protection key ring makes embeddings unreadable; users re-enroll.
- Changing the model id forces re-enrollment.
- SQLite limits this to one instance; acceptable for the demo.
