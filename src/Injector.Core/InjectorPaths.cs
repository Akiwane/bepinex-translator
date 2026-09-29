namespace Injector.Core;

/// <summary>
/// Shared path constants for the injector (config example, layout markers).
/// </summary>
public static class InjectorPaths
{
    /// <summary>
    /// Repository-relative path of the Translator config example that "copy config" targets.
    /// </summary>
    public const string ConfigExampleRelativePath = "config/Translator.cfg.example";

    /// <summary>
    /// Destination file name under BepInEx/config/ when copying the example.
    /// </summary>
    public const string ConfigExampleDestFileName = "Translator.cfg.example";

    /// <summary>
    /// Marker written by the stub installer under the game root when layout write is enabled.
    /// </summary>
    public const string StubInstallMarkerFileName = ".bepinex-translator-injector-stub";

    /// <summary>
    /// Marker that indicates a prior BepInEx layout (real or stub).
    /// </summary>
    public const string BepInExDirectoryName = "BepInEx";

    public const string PluginsTranslatorRelativePath = "BepInEx/plugins/Translator";

    public const string ConfigRelativeDirectory = "BepInEx/config";
}
