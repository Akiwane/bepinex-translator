namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 固定的 BepInEx / 模组包映射。版本 pin 写死在此并文档化；可用 <see cref="PackageSourceOptions"/> 覆盖。
/// </summary>
public static class PackageCatalog
{
    /// <summary>Mono 游戏默认 BepInEx 5 stable（Windows x64）。</summary>
    public const string BepInEx5Version = "5.4.23.5";

    /// <summary>IL2CPP 游戏默认 BepInEx 6 GitHub pre-release（Windows x64 Unity IL2CPP）。</summary>
    public const string BepInEx6Version = "6.0.0-pre.2";

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
    /// 若 Releases 尚未发布对应资产，解析器会回退到本地 artifacts。
    /// </summary>
    public const string DefaultTranslatorReleaseAssetPattern =
        "BepInExTranslator-{runtime}-win.zip";

    public const string DefaultTranslatorGitHubOwner = "Akiwane";
    public const string DefaultTranslatorGitHubRepo = "bepinex-translator";
}
