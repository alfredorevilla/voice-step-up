using System.Runtime.InteropServices;
using Microsoft.AspNetCore.DataProtection;

namespace StepUp.Infrastructure.Security;

/// <summary>Encrypts embeddings and enrollment payloads with ASP.NET Core Data Protection.</summary>
public sealed class EmbeddingProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("StepUp.Embeddings.v1");

    public byte[] Protect(float[] embedding) => _protector.Protect(MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray());

    public float[] Unprotect(byte[] protectedBytes) => MemoryMarshal.Cast<byte, float>(_protector.Unprotect(protectedBytes)).ToArray();

    public byte[] ProtectBytes(byte[] plain) => _protector.Protect(plain);

    public byte[] UnprotectBytes(byte[] protectedBytes) => _protector.Unprotect(protectedBytes);
}
