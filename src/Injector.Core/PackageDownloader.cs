namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 下载远程包到本地缓存目录。下载产物不得提交进 git。
/// 仅允许 HTTPS，且主机须在允许列表内。
/// </summary>
public sealed class PackageDownloader
{
    /// <summary>
    /// 允许下载的主机（大小写不敏感）。
    /// 额外主机：<c>builds.bepinex.dev</c>（Bleeding Edge zip）、GitHub release CDN。
    /// </summary>
    public static IReadOnlyList<string> AllowedDownloadHosts { get; } = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "builds.bepinex.dev",
    };

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
    /// 校验下载 URL：必须 HTTPS，且 Host 在 <see cref="AllowedDownloadHosts"/> 中。
    /// 合法返回 <c>null</c>；否则返回类型化 <see cref="InjectorError"/>。
    /// </summary>
    public static InjectorError? ValidateDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return InjectorError.DownloadFailed("下载 URL 为空。");
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return InjectorError.DownloadFailed("下载 URL 格式无效。", url);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return InjectorError.DownloadFailed(
                "仅允许 HTTPS 下载。",
                $"scheme={uri.Scheme}; url={url}");
        }

        var host = uri.Host;
        // 仅精确匹配允许列表主机（github.com / objects.githubusercontent.com 等）
        var allowed = AllowedDownloadHosts.Any(h =>
            string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            return InjectorError.DownloadFailed(
                "下载主机不在允许列表中。允许：github.com、objects.githubusercontent.com、release-assets.githubusercontent.com、builds.bepinex.dev。",
                host);
        }

        return null;
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
        // —— URL 信任：HTTPS + 主机允许列表 ——
        var trustError = ValidateDownloadUrl(url);
        if (trustError != null)
        {
            throw new InvalidOperationException(trustError.Message +
                (string.IsNullOrEmpty(trustError.Detail) ? string.Empty : " (" + trustError.Detail + ")"));
        }

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
