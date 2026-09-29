namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 固定的 BepInEx / 模组包映射。版本 pin 写死在此并文档化；可用 <see cref="PackageSourceOptions"/> 覆盖。
/// </summary>
public static class PackageCatalog
{
    /// <summary>Mono 游戏默认 BepInEx 5 stable（Windows x64）。</summary>
    public const string BepInEx5Version = "5.4.23.5";

    /// <summary>
    /// IL2CPP 游戏默认 BepInEx 6 GitHub pre-release（Windows x64 Unity IL2CPP）。
    /// 官方 Releases 可稳定 pin 的 zip；插件 NuGet 另用 6.0.0-be.733 —— 见 docs/injector.md。
    /// </summary>
    public const string BepInEx6Version = "6.0.0-pre.2";

    /// <summary>插件工程引用的 BepInEx.Unity.IL2CPP NuGet（Bleeding Edge）；注入器默认下载仍为 <see cref="BepInEx6Version"/>。</summary>
    public const string BepInEx6PluginNuGetVersion = "6.0.0-be.733";

    public const string BepInEx5Tag = "v5.4.23.5";
    public const string BepInEx6Tag = "v6.0.0-pre.2";

    public const string BepInEx5AssetName = "BepInEx_win_x64_5.4.23.5.zip";
    public const string BepInEx6AssetName = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip";

    public static string BepInEx5DownloadUrl =>
        $"https://github.com/BepInEx/BepInEx/releases/download/{BepInEx5Tag}/{BepInEx5AssetName}";

    public static string BepInEx6DownloadUrl =>
        $"https://github.com/BepInEx/BepInEx/releases/download/{BepInEx6Tag}/{BepInEx6AssetName}";

    /// <summary>
    /// 本翻译模组默认 GitHub Release 资产名约定（可按 runtime 替换 {runtime} = mono|il2cpp）。
    /// </summary>
    public const string DefaultTranslatorReleaseAssetPattern =
        "BepInExTranslator-{runtime}-win.zip";

    public const string DefaultTranslatorGitHubOwner = "Akiwane";
    public const string DefaultTranslatorGitHubRepo = "bepinex-translator";

    /// <summary>
    /// 默认 Release 策略：关键字 <c>latest</c> 表示使用 GitHub
    /// <c>/releases/latest/download/{asset}</c>（当前仓库最新正式 Release 资产）。
    /// 也可设为具体 tag（如 <c>v1.0.0</c>）走 <c>/releases/download/{tag}/{asset}</c>。
    /// 若该资产尚未发布，下载会失败；请构建本地 artifacts/ 或设置 TranslatorLocalZipPath。
    /// </summary>
    public const string DefaultTranslatorReleaseTag = "latest";

    /// <summary>按 runtime 与 tag 拼出默认模组下载 URL。</summary>
    public static string BuildTranslatorReleaseDownloadUrl(
        UnityRuntimeKind runtime,
        string releaseTag,
        string owner = DefaultTranslatorGitHubOwner,
        string repo = DefaultTranslatorGitHubRepo)
    {
        var runtimeKey = runtime == UnityRuntimeKind.Mono ? "mono" : "il2cpp";
        var asset = DefaultTranslatorReleaseAssetPattern
            .Replace("{runtime}", runtimeKey, StringComparison.Ordinal);
        var tag = string.IsNullOrWhiteSpace(releaseTag) ? DefaultTranslatorReleaseTag : releaseTag.Trim();

        // latest → GitHub latest-release 直链；否则按 tag 下载
        if (string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://github.com/{owner}/{repo}/releases/latest/download/{asset}";
        }

        return $"https://github.com/{owner}/{repo}/releases/download/{tag}/{asset}";
    }
}
