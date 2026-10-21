using System.Security.Cryptography;
using StepUp.Verifier.Sherpa;

namespace StepUp.Verifier.Tests;

public sealed class ModelLockTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("modellock").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private string WriteLock(params string[] lines)
    {
        var path = Path.Combine(dir, "models.lock");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void Load_ReturnsSpeakerHash_WhenEveryRuntimeFileMatches()
    {
        var speaker = Write("wespeaker_en_voxceleb_resnet34_LM.onnx", "speaker");
        var asr = Write("whisper-base.en/base.en-encoder.int8.onnx", "enc");
        var lockFile = WriteLock(
            "# comment",
            $"runtime verify {speaker} wespeaker_en_voxceleb_resnet34_LM.onnx",
            $"runtime verify {asr} whisper-base.en/base.en-encoder.int8.onnx");

        var info = ModelLock.Load(lockFile, dir);

        Assert.Equal(speaker, info.SpeakerSha256);
    }

    [Fact]
    public void Load_Throws_WhenAFileHashDiffers()
    {
        Write("wespeaker_en_voxceleb_resnet34_LM.onnx", "speaker");
        var lockFile = WriteLock($"runtime verify {new string('0', 64)} wespeaker_en_voxceleb_resnet34_LM.onnx");

        var error = Assert.Throws<InvalidOperationException>(() => ModelLock.Load(lockFile, dir));

        Assert.Contains("wespeaker_en_voxceleb_resnet34_LM.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Throws_WhenAFileIsMissing()
    {
        var lockFile = WriteLock($"runtime verify {new string('a', 64)} wespeaker_en_voxceleb_resnet34_LM.onnx");

        Assert.Throws<InvalidOperationException>(() => ModelLock.Load(lockFile, dir));
    }

    [Fact]
    public void Load_IgnoresTestScopeFiles()
    {
        var speaker = Write("wespeaker_en_voxceleb_resnet34_LM.onnx", "speaker");
        var lockFile = WriteLock(
            $"runtime verify {speaker} wespeaker_en_voxceleb_resnet34_LM.onnx",
            $"test verify {new string('b', 64)} tts/missing.onnx");

        var info = ModelLock.Load(lockFile, dir);

        Assert.Equal(speaker, info.SpeakerSha256);
    }

    [Fact]
    public void Load_Throws_WhenThereIsNoSpeakerModelEntry()
    {
        var asr = Write("whisper-base.en/base.en-encoder.int8.onnx", "enc");
        var lockFile = WriteLock($"runtime verify {asr} whisper-base.en/base.en-encoder.int8.onnx");

        Assert.Throws<InvalidOperationException>(() => ModelLock.Load(lockFile, dir));
    }

    [Fact]
    public void Load_Throws_WhenThePathEscapesTheModelsDirectory()
    {
        var lockFile = WriteLock($"runtime verify {new string('a', 64)} ../outside.onnx");

        Assert.Throws<InvalidOperationException>(() => ModelLock.Load(lockFile, dir));
    }

    [Fact]
    public void ModelId_ContainsTheSpeakerHashPrefixTheSherpaVersionAndTheFeatureVersion()
    {
        var id = SherpaVerifier.BuildModelId(new string('c', 64), "1.13.8");

        Assert.Equal("wespeaker-resnet34-lm:cccccccccccc+sherpa-1.13.8+feat-v1", id);
    }
}
