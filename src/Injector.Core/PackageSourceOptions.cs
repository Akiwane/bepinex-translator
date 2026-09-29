namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 包来源覆盖项。密钥不得写入此类；仅公开 URL / 本地路径。
/// </summary>
public sealed class PackageSourceOptions
{
    /// <summary>覆盖 Mono 用 BepInEx zip URL；空则用 <see cref="PackageCatalog.BepInEx5DownloadUrl"/>。</summary>
    public string? BepInExMonoUrl { get; set; }

    /// <summary>
    /// 覆盖 IL2CPP 用 BepInEx zip URL；空则用默认 pin <see cref="PackageCatalog.BepInEx6DownloadUrl"/>（6.0.0-pre.2）。
    /// 可指向 builds.bepinex.dev 的 BE zip（须 HTTPS）。
    /// </summary>
    public string? BepInExIl2CppUrl { get; set; }

    /// <summary>
    /// 本地 BepInEx zip（若设置则优先于 URL）。
    /// IL2CPP 可用 builds.bepinex.dev 下载的 <c>BepInEx-Unity.IL2CPP-win-x64-…</c>（含 BE.733）覆盖默认 pre.2。
    /// </summary>
    public string? BepInExLocalZipPath { get; set; }

    /// <summary>
    /// 本模组包：本地 zip。优先于目录与远程。
    /// </summary>
    public string? TranslatorLocalZipPath { get; set; }

    /// <summary>
    /// 本模组本地构建目录覆盖。空则按 runtime 使用仓库相对 artifacts/mono 或 artifacts/il2cpp。
    /// </summary>
    public string? TranslatorLocalArtifactsDirectory { get; set; }

    /// <summary>
    /// 本模组远程 zip URL（须 HTTPS + 允许主机）。空则按 <see cref="TranslatorReleaseTag"/> 拼 GitHub Release。
    /// </summary>
    public string? TranslatorDownloadUrl { get; set; }

    /// <summary>
    /// 用于定位默认 artifacts/ 与 config/ 的仓库根（GUI 启动时传入；测试可指向临时目录）。
    /// </summary>
    public string? RepositoryRoot { get; set; }

    /// <summary>
    /// GitHub Release tag。空则默认 <see cref="PackageCatalog.DefaultTranslatorReleaseTag"/>（<c>latest</c>）。
    /// <c>latest</c> → <c>/releases/latest/download/{asset}</c>；其它 tag → <c>/releases/download/{tag}/{asset}</c>。
    /// </summary>
    public string? TranslatorReleaseTag { get; set; }

    public string TranslatorGitHubOwner { get; set; } = PackageCatalog.DefaultTranslatorGitHubOwner;

    public string TranslatorGitHubRepo { get; set; } = PackageCatalog.DefaultTranslatorGitHubRepo;
}
