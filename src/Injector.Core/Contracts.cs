namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 探测游戏路径：Unity 版本 + Runtime（Mono | Il2Cpp | Unknown）+ 证据路径。
/// </summary>
public interface IGameProbe
{
    GameDetectionResult Detect(string path);
}

/// <summary>
/// 按运行时选择 BepInEx 5/6 与翻译模组包（URL 或本地路径）。
/// </summary>
public interface IPackageResolver
{
    PackageResolveResult Resolve(UnityRuntimeKind runtime, PackageSourceOptions options);
}

/// <summary>
/// 下载 / 解压 / 写入 doorstop、winhttp、plugins、配置示例。
/// </summary>
public interface IInstaller
{
    Task<InstallResult> InstallAsync(
        InstallOptions options,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>包解析结果（成功带包列表，失败带类型化错误）。</summary>
public sealed class PackageResolveResult
{
    public bool Success => Error == null;

    public IReadOnlyList<ResolvedPackage> Packages { get; init; } = Array.Empty<ResolvedPackage>();

    public InjectorError? Error { get; init; }

    public static PackageResolveResult Ok(IReadOnlyList<ResolvedPackage> packages) =>
        new() { Packages = packages };

    public static PackageResolveResult Fail(InjectorError error) =>
        new() { Error = error };
}
