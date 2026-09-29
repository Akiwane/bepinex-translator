namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 下载远程包到本地缓存目录。下载产物不得提交进 git。
/// </summary>
public sealed class PackageDownloader
{
    private readonly HttpClient _http;

    public PackageDownloader(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            // GitHub releases 可能较慢
            Timeout = TimeSpan.FromMinutes(10),
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("BepInExTranslator-Injector/1.1");
        }
    }

    /// <summary>
    /// 将 <see cref="ResolvedPackage"/> 物化为本地 zip 或目录路径。
    /// </summary>
    public async Task<string> MaterializeAsync(
        ResolvedPackage package,
        string cacheDirectory,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);

        switch (package.SourceKind)
        {
            case PackageSourceKind.LocalDirectory:
            case PackageSourceKind.LocalZip:
                if (string.IsNullOrWhiteSpace(package.LocalPath))
                {
                    throw new InvalidOperationException("本地包缺少 LocalPath。");
                }

                log?.Report($"使用本地包：{package.LocalPath}");
                return package.LocalPath;

            case PackageSourceKind.RemoteUrl:
                if (string.IsNullOrWhiteSpace(package.DownloadUrl))
                {
                    throw new InvalidOperationException("远程包缺少 DownloadUrl。");
                }

                return await DownloadUrlAsync(package.DownloadUrl, cacheDirectory, log, cancellationToken)
                    .ConfigureAwait(false);

            default:
                throw new ArgumentOutOfRangeException(nameof(package), package.SourceKind, null);
        }
    }

    public async Task<string> DownloadUrlAsync(
        string url,
        string cacheDirectory,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var fileName = GuessFileName(url);
        var dest = Path.Combine(cacheDirectory, fileName);

        // 已缓存则复用
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
        {
            log?.Report($"缓存命中：{dest}");
            return dest;
        }

        log?.Report($"下载中：{url}");
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tmp = dest + ".partial";
        await using (var fs = File.Create(tmp))
        {
            await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(dest))
        {
            File.Delete(dest);
        }

        File.Move(tmp, dest);
        log?.Report($"已保存：{dest}");
        return dest;
    }

    internal static string GuessFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (UriFormatException)
        {
            // fall through
        }

        return "download-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url))).Substring(0, 16) + ".zip";
    }
}
