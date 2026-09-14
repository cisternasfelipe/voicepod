using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Stt;

/// <summary>
/// Downloads the Parakeet model files one by one into the model directory. Partial
/// downloads land on a .part file and are only promoted after the size and, when
/// published upstream, the SHA256 match.
/// </summary>
public sealed class ModelDownloader : IModelDownloader
{
    private const int MaxAttempts = 3;
    private const int BufferSize = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ModelDownloader> _logger;

    public ModelDownloader(HttpClient httpClient, ILogger<ModelDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // The encoder alone is ~650 MB, so the per-request timeout has to be generous.
        _httpClient.Timeout = TimeSpan.FromHours(2);
    }

    public bool IsModelPresent(string modelDirectory)
    {
        if (!Directory.Exists(modelDirectory))
        {
            return false;
        }

        foreach (var file in ModelDownloadDefaults.Files)
        {
            var path = Path.Combine(modelDirectory, file.FileName);
            var info = new FileInfo(path);

            if (!info.Exists || (file.ExpectedSize > 0 && info.Length != file.ExpectedSize))
            {
                return false;
            }
        }

        return true;
    }

    public async Task DownloadAsync(
        string modelDirectory,
        string baseUrl,
        bool verifyHashes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(modelDirectory);

        var files = ModelDownloadDefaults.Files;
        var total = ModelDownloadDefaults.TotalBytes;
        long completedBytes = 0;

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var destination = Path.Combine(modelDirectory, file.FileName);

            if (IsFileComplete(destination, file))
            {
                _logger.LogInformation("{File} already downloaded, skipping", file.FileName);
                completedBytes += file.ExpectedSize;
                Report(progress, file, index, files.Count, completedBytes, total);
                continue;
            }

            var url = $"{baseUrl.TrimEnd('/')}/{file.FileName}";
            var alreadyDone = completedBytes;

            await DownloadFileWithRetriesAsync(
                url,
                destination,
                file,
                verifyHashes,
                received => Report(progress, file, index, files.Count, alreadyDone + received, total),
                cancellationToken).ConfigureAwait(false);

            completedBytes += file.ExpectedSize;
            Report(progress, file, index, files.Count, completedBytes, total);
        }

        _logger.LogInformation("Model download finished in {Directory}", modelDirectory);
    }

    private static bool IsFileComplete(string path, ModelFile file)
    {
        var info = new FileInfo(path);
        return info.Exists && (file.ExpectedSize <= 0 || info.Length == file.ExpectedSize);
    }

    private async Task DownloadFileWithRetriesAsync(
        string url,
        string destination,
        ModelFile file,
        bool verifyHashes,
        Action<long> onBytesReceived,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await DownloadFileAsync(url, destination, file, verifyHashes, onBytesReceived, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Download of {File} failed (attempt {Attempt}/{Max})", file.FileName, attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(
            $"No se pudo descargar {file.FileName} tras {MaxAttempts} intentos: {lastError?.Message}",
            lastError);
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        ModelFile file,
        bool verifyHashes,
        Action<long> onBytesReceived,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".part";

        // A previous attempt may have left bytes behind: continue from where it stopped.
        long resumeFrom = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (resumeFrom > file.ExpectedSize && file.ExpectedSize > 0)
        {
            File.Delete(partial);
            resumeFrom = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (resumeFrom > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // The server ignored the range request: start over.
            resumeFrom = 0;
        }

        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(
            partial,
            resumeFrom > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true))
        {
            var buffer = new byte[BufferSize];
            var received = resumeFrom;
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                onBytesReceived(received);
            }
        }

        await VerifyAsync(partial, file, verifyHashes, cancellationToken).ConfigureAwait(false);

        File.Move(partial, destination, overwrite: true);
        _logger.LogInformation("{File} downloaded and verified", file.FileName);
    }

    private static async Task VerifyAsync(
        string path,
        ModelFile file,
        bool verifyHashes,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);

        if (file.ExpectedSize > 0 && info.Length != file.ExpectedSize)
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"{file.FileName}: tamaño inesperado ({info.Length} bytes, se esperaban {file.ExpectedSize}).");
        }

        if (!verifyHashes || string.IsNullOrEmpty(file.Sha256))
        {
            return;
        }

        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException($"{file.FileName}: el hash SHA256 no coincide.");
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Report(
        IProgress<ModelDownloadProgress>? progress,
        ModelFile file,
        int index,
        int count,
        long received,
        long total) =>
        progress?.Report(new ModelDownloadProgress(file.FileName, index + 1, count, received, total));
}
