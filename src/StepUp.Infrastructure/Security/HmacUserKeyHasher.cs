using System.Security.Cryptography;
using System.Text;
using StepUp.Core;

namespace StepUp.Infrastructure.Security;

/// <summary>UserKey = HMAC-SHA256 (server secret) of tid and oid, so it cannot be reversed from known object ids.</summary>
public sealed class HmacUserKeyHasher(byte[] secret) : IUserKeyHasher
{
    public UserKey Hash(string tid, string oid) =>
        new(Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{tid}\n{oid}"))).ToLowerInvariant());
}
