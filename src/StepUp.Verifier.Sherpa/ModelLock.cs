using System.Security.Cryptography;

namespace StepUp.Verifier.Sherpa;

public sealed record ModelLockInfo(string SpeakerSha256);

/// <summary>
/// Reads <c>models.lock</c> and checks that every runtime file is present with the pinned SHA-256
/// (docs/design/verifier.md section 8). A missing or changed model stops the service at startup.
/// </summary>
public static class ModelLock
{
    public const string SpeakerModelFile = "wespeaker_en_voxceleb_resnet34_LM.onnx";

    public static ModelLockInfo Load(string lockFile, string modelsDir)
    {
        if (!File.Exists(lockFile))
        {
            throw new InvalidOperationException($"Model lock file not found: {lockFile}. Run scripts/fetch-models.sh.");
        }

        var root = Path.GetFullPath(modelsDir) + Path.DirectorySeparatorChar;
        string? speaker = null;
        foreach (var line in File.ReadLines(lockFile))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || fields[0] != "runtime" || fields[1] != "verify")
            {
                continue;
            }

            var expected = fields[2];
            var relative = fields[3];
            var path = Path.GetFullPath(Path.Combine(modelsDir, relative));
            if (!path.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Model lock path escapes the models directory: {relative}");
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Model file missing: {relative}. Run scripts/fetch-models.sh.");
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Model file {relative} does not match models.lock. Run scripts/fetch-models.sh.");
            }

            if (relative == SpeakerModelFile)
            {
                speaker = expected.ToLowerInvariant();
            }
        }

        return speaker is null
            ? throw new InvalidOperationException($"models.lock has no runtime entry for {SpeakerModelFile}.")
            : new ModelLockInfo(speaker);
    }
}
