using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using StepUp.Core;

namespace StepUp.Infrastructure.Security;

/// <summary>
/// The ES256 assertion signing key and the UserKey HMAC secret. Both are generated at first start,
/// protected with Data Protection and stored in the key directory (a volume, never the repo or the image).
/// Rotation is manual: delete the files and restart (a demo limit, docs/architecture.md section 4).
/// </summary>
public sealed class FileKeyStore : IAssertionKeyProvider
{
    private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FileKeyStore(string directory, IDataProtectionProvider provider)
    {
        Directory.CreateDirectory(directory);
        var protector = provider.CreateProtector("StepUp.Keys.v1");

        var signingPath = Path.Combine(directory, "assertion-signing.key");
        if (File.Exists(signingPath))
        {
            _ecdsa.ImportPkcs8PrivateKey(protector.Unprotect(File.ReadAllBytes(signingPath)), out _);
        }
        else
        {
            WriteSecret(signingPath, protector.Protect(_ecdsa.ExportPkcs8PrivateKey()));
        }

        var secretPath = Path.Combine(directory, "userkey.secret");
        if (File.Exists(secretPath))
        {
            UserKeySecret = protector.Unprotect(File.ReadAllBytes(secretPath));
        }
        else
        {
            UserKeySecret = RandomNumberGenerator.GetBytes(32);
            WriteSecret(secretPath, protector.Protect(UserKeySecret));
        }

        var kid = Base64UrlEncoder.Encode(SHA256.HashData(_ecdsa.ExportSubjectPublicKeyInfo())[..8]);
        SigningKey = new ECDsaSecurityKey(_ecdsa) { KeyId = kid };
    }

    public SecurityKey SigningKey { get; }

    public IReadOnlyCollection<SecurityKey> ValidationKeys => [SigningKey];

    public byte[] UserKeySecret { get; }

    private static void WriteSecret(string path, byte[] content)
    {
        File.WriteAllBytes(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
