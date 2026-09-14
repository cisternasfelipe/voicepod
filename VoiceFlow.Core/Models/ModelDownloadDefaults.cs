namespace VoiceFlow.Core.Models;

/// <summary>One file of the STT model, with the metadata used to verify a finished download.</summary>
public sealed record ModelFile(string FileName, long ExpectedSize, string? Sha256);

/// <summary>
/// Default source for sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8. Files are fetched
/// individually (no archive), which keeps the download resumable and verifiable.
/// </summary>
public static class ModelDownloadDefaults
{
    public const string BaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main";

    public static IReadOnlyList<ModelFile> Files { get; } =
    [
        new("encoder.int8.onnx", 652_184_281,
            "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
        new("decoder.int8.onnx", 11_845_275,
            "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
        new("joiner.int8.onnx", 6_355_277,
            "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
        // Not stored with LFS upstream, so there is no published SHA256: size only.
        new("tokens.txt", 93_939, null)
    ];

    public static long TotalBytes => Files.Sum(f => f.ExpectedSize);
}
