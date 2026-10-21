# ADR-0006: SQLite storage with encrypted embeddings

Status: Accepted
Date: 2026-10-20
Decisions: D7

## Context
The service stores consent, voiceprints, enrollments, attempts, challenges, used assertions and thresholds. Voiceprints are biometric data.

## Decision
SQLite through EF Core; the tables are in `docs/architecture.md` section 3.
- Embeddings are encrypted with ASP.NET Core Data Protection and stored with the `ModelId`. No raw audio is stored or logged.
- `UserKey` is an HMAC-SHA256 (server secret) of `tid` + `oid`, so it cannot be reversed from known object ids.
- `PRAGMA secure_delete = ON`; deleting a voiceprint deletes the row. A test checks that no embedding bytes remain in the DB file.
- After consent withdrawal, attempts are pseudonymised, not deleted. The consent text says so.
- No adaptive template update: a user re-enrolls (delete plus enroll with fresh MFA).

## Alternatives considered
- PostgreSQL: more moving parts for a demonstrator.
- Files per user: no transactions, weaker deletion guarantees.
- Store only a hash of the embedding: a hash cannot be compared by similarity.
- Azure Key Vault for keys: adds a cloud dependency to the offline demo.
- Unsalted hash of `tid` + `oid` as `UserKey`: linkable from known object ids.

## Consequences
- Losing the Data Protection key ring makes embeddings unreadable; users re-enroll.
- Changing the `ModelId` forces re-enrollment.
- SQLite limits this to one instance; acceptable for the demo.
