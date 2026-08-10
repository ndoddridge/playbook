using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Playbook.Infrastructure.Replay.Nflverse;

/// <summary>Downloads and caches nflverse release assets under the app data directory.</summary>
public sealed class NflverseCsvCache
{
    public const string HttpClientName = "NflverseHistoricalReplay";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NflverseCsvCache> _logger;
    private readonly string _cacheRoot;

    public NflverseCsvCache(IHttpClientFactory httpClientFactory, ILogger<NflverseCsvCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheRoot = NflverseReleaseCatalog.CacheRoot;
        Directory.CreateDirectory(_cacheRoot);
    }

    public async Task<string> EnsureFileAsync(
        string url,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_cacheRoot, fileName);
        if (File.Exists(path) && new FileInfo(path).Length > 100)
        {
            return path;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        _logger.LogInformation("Downloading nflverse asset {File} from {Url}", fileName, url);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"nflverse asset unavailable ({(int)response.StatusCode}) for {url}");
        }

        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, path, overwrite: true);
        return path;
    }

    public Task<StreamReader> OpenTextAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            stream = new GZipStream(stream, CompressionMode.Decompress);
        }

        return Task.FromResult(new StreamReader(stream));
    }
}
