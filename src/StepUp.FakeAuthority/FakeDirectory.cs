namespace StepUp.FakeAuthority;

public sealed record FakeUser(string Slug, string Oid, string Name, string[] Roles);

/// <summary>The demo identities. The ids are made up and belong to no real directory.</summary>
public static class FakeDirectory
{
    public const string FakeTid = "00000000-0000-0000-0000-00000000fa4e";

    public static readonly IReadOnlyDictionary<string, FakeUser> Users = new Dictionary<string, FakeUser>
    {
        ["alice"] = new("alice", "00000000-0000-0000-0000-0000000a11ce", "Alice Demo", []),
        ["bob"] = new("bob", "00000000-0000-0000-0000-000000000b0b", "Bob Demo", []),
        ["analyst"] = new("analyst", "00000000-0000-0000-0000-0000000a0a17", "Ana Analyst", ["Analyst"]),
    };
}
