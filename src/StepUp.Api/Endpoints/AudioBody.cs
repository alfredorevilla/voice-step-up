using StepUp.Core;

namespace StepUp.Api.Endpoints;

internal static class AudioBody
{
    private static readonly string[] AcceptedTypes = ["audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave"];

    /// <summary>Reads the WAV body and checks its header before anything parses samples: 413 too large, 415 wrong format.</summary>
    public static async Task<(byte[]? Wav, IResult? Error)> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var type = request.ContentType?.Split(';')[0].Trim();
        if (type is null || !AcceptedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Send audio/wav (PCM16, mono, 16 kHz)."));
        }

        if (request.ContentLength > WavInspector.MaxBytes)
        {
            return (null, TooLarge());
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > WavInspector.MaxBytes)
            {
                return (null, TooLarge());
            }
        }

        var wav = buffer.ToArray();
        return WavInspector.Inspect(wav) switch
        {
            WavCheck.Ok => (wav, null),
            WavCheck.TooLarge => (null, TooLarge()),
            _ => (null, Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Audio must be PCM16, mono, 16 kHz WAV.")),
        };
    }

    private static IResult TooLarge() => Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Audio is limited to 10 s and 1 MB.");
}
