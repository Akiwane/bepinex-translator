namespace Injector.Core.Abstractions;

/// <summary>
/// Downloads remote package bytes. Real HTTP implementation lives in the backend;
/// the stub installer never calls this unless wired to <see cref="Install.HttpPackageDownloader"/>.
/// </summary>
public interface IPackageDownloader
{
    /// <summary>
    /// Download a package from <paramref name="url"/> to <paramref name="destinationPath"/>.
    /// </summary>
    Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default);
}
