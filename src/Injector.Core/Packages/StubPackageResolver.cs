using Injector.Core.Abstractions;
using Injector.Core.Models;

namespace Injector.Core.Packages;

/// <summary>
/// Stub package resolver: maps Mono → BepInEx 5 / IL2CPP → BepInEx 6 with placeholder URLs.
/// Backend should replace URLs with a real version matrix (Unity version × runtime).
/// </summary>
public sealed class StubPackageResolver : IPackageResolver
{
    // Placeholder URLs — documented for backend. Stub installer does NOT download these by default.
    // Real releases: https://github.com/BepInEx/BepInEx/releases
    public const string PlaceholderBepInEx5Url =
        "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip";

    public const string PlaceholderBepInEx6Il2CppUrl =
        "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-be.735/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.735%2B501bbea.zip";

    public const string PlaceholderPluginMonoUrl =
        "https://example.invalid/bepinex-translator/artifacts/mono/BepInExTranslator-mono.zip";

    public const string PlaceholderPluginIl2CppUrl =
        "https://example.invalid/bepinex-translator/artifacts/il2cpp/BepInExTranslator-il2cpp.zip";

    public PackageIds? Resolve(RuntimeKind runtime) => runtime switch
    {
        RuntimeKind.Mono => new PackageIds
        {
            BepInExPackageId = "BepInEx.Unity.Mono.win-x64@5.x",
            PluginPackageId = "BepInExTranslator.Mono",
            BepInExDownloadUrl = PlaceholderBepInEx5Url,
            PluginDownloadUrl = PlaceholderPluginMonoUrl,
            Description = "BepInEx 5.x (Mono) + Translator Mono plugin",
        },
        RuntimeKind.Il2Cpp => new PackageIds
        {
            BepInExPackageId = "BepInEx.Unity.IL2CPP.win-x64@6.x",
            PluginPackageId = "BepInExTranslator.Il2Cpp",
            BepInExDownloadUrl = PlaceholderBepInEx6Il2CppUrl,
            PluginDownloadUrl = PlaceholderPluginIl2CppUrl,
            Description = "BepInEx 6.x Unity IL2CPP + Translator IL2CPP plugin",
        },
        _ => null,
    };
}
