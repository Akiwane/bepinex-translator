using Injector.Core.Abstractions;

namespace Injector.Core.Install;

/// <summary>
/// Placeholder for the backend real downloader.
/// StubInstaller never calls this. Do not implement BepInEx GitHub download here —
/// that belongs in the backend Injector.Core PR.
/// </summary>
public sealed class HttpPackageDownloader : IPackageDownloader
{
    public Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        // Intentionally unimplemented in the Gui/stub PR to avoid conflicting with backend Core.
        return Task.FromException(new NotImplementedException(
            "HttpPackageDownloader is a skeleton. Backend owns real BepInEx/plugin downloads " +
            "(official GitHub Releases: Mono→5.x Win x64, IL2CPP→6.x Unity IL2CPP Win; " +
            "plugin prefers artifacts/mono|il2cpp)."));
    }
}
