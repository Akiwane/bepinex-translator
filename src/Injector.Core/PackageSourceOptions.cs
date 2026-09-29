namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 包来源覆盖项。密钥不得写入此类；仅公开 URL / 本地路径。
/// </summary>
public sealed class PackageSourceOptions
{
    /// <summary>覆盖 Mono 用 BepInEx zip URL；空则用 <see cref="PackageCatalog.BepInEx5DownloadUrl"/>。</summary>
    public string? BepInExMonoUrl { get; set; }

    /// <summary>覆盖 IL2CPP 用 BepInEx zip URL；空则用 <see cref="PackageCatalog.BepInEx6DownloadUrl"/>。</summary>
    public string? BepInExIl2CppUrl { get; set; }

    /// <summary>本地 BepInEx zip（若设置则优先于 URL）。</summary>
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
    /// 本模组远程 zip URL。空则尝试 GitHub latest release 资产约定；仍失败则仅允许本地 artifacts。
    /// </summary>
    public string? TranslatorDownloadUrl { get; set; }

    /// <summary>
    /// 用于定位默认 artifacts/ 与 config/ 的仓库根（GUI 启动时传入；测试可指向临时目录）。
    /// </summary>
    public string? RepositoryRoot { get; set; }

    /// <summary>
    /// GitHub Release tag（可选）。若同时提供 Owner/Repo，可拼出默认模组下载 URL。
    /// </summary>
    public string? TranslatorReleaseTag { get; set; }

    public string TranslatorGitHubOwner { get; set; } = PackageCatalog.DefaultTranslatorGitHubOwner;

    public string TranslatorGitHubRepo { get; set; } = PackageCatalog.DefaultTranslatorGitHubRepo;
}
