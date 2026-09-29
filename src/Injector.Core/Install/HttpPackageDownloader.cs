using Injector.Core.Abstractions;

namespace Injector.Core.Install;

/// <summary>
/// Skeleton HTTP downloader for the future real installer.
/// Not used by <see cref="StubInstaller"/> (which skips network by design for CI).
/// TODO(backend): wire this into a real <see cref="IInstaller"/> that downloads BepInEx zips,
/// validates hashes, extracts doorstop/winhttp/BepInEx layout, then copies the plugin package.
/// </summary>
public sealed class HttpPackageDownloader : IPackageDownloader
{
    private readonly HttpClient _httpClient;

    public HttpPackageDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        // Intentionally minimal: backend should add retries, progress, hash checks, and User-Agent.
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using HttpResponseMessage response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream local = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await remote.CopyToAsync(local, cancellationToken).ConfigureAwait(false);
    }
}
