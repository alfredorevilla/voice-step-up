namespace StepUp.Infrastructure.Persistence;

// Row types for docs/architecture.md section 3. Times are unix milliseconds (SQLite cannot order DateTimeOffset).

public sealed class ConsentRow
{
    public Guid Id { get; set; }
    public string UserKey { get; set; } = "";
    public string TextVersion { get; set; } = "";
    public long GivenAt { get; set; }
    public long? WithdrawnAt { get; set; }
}

public sealed class VoiceprintRow
{
    public Guid Id { get; set; }
    public string UserKey { get; set; } = "";
    public string ModelId { get; set; } = "";
    public byte[] EmbeddingProtected { get; set; } = [];
    public long CreatedAt { get; set; }
}

public sealed class EnrollmentRow
{
    public Guid Id { get; set; }
    public string UserKey { get; set; } = "";
    public byte[] SamplesProtected { get; set; } = [];
    public long ExpiresAt { get; set; }
}

public sealed class AttemptRow
{
    public Guid Id { get; set; }
    public string UserKey { get; set; } = "";
    public long At { get; set; }
    public string Action { get; set; } = "";
    public double? Score { get; set; }
    public double Threshold { get; set; }
    public string ModelId { get; set; } = "";
    public int Result { get; set; }
    public int? ReasonCode { get; set; }
    public int Mode { get; set; }
}

public sealed class ChallengeRow
{
    public Guid Id { get; set; }
    public string UserKey { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Phrase { get; set; }
    public long ExpiresAt { get; set; }
    public long? ConsumedAt { get; set; }
}

public sealed class UsedAssertionRow
{
    public string Jti { get; set; } = "";
    public long ExpiresAt { get; set; }
}

public sealed class ThresholdRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ModelId { get; set; } = "";
    public double Value { get; set; }
    public long UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
