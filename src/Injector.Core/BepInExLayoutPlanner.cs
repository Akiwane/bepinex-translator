namespace BepInExTranslator.Injector.Core;

/// <summary>
/// BepInEx 标准落盘布局规划（相对游戏根目录的路径约定）。
/// 与官方 zip 解压内容对齐：doorstop / winhttp / BepInEx/…，IL2CPP 另含 dotnet/。
/// </summary>
public static class BepInExLayoutPlanner
{
    public const string PluginsTranslatorRelative = "BepInEx/plugins/Translator";
    public const string ConfigRelative = "BepInEx/config";
    public const string ConfigExampleFileName = "Translator.cfg.example";
    public const string LogOutputRelative = "BepInEx/LogOutput.log";

    /// <summary>
    /// Mono（BepInEx 5）解压后期望出现在游戏根的关键文件/目录（相对路径）。
    /// </summary>
    public static IReadOnlyList<string> ExpectedMonoRootEntries { get; } = new[]
    {
        "winhttp.dll",
        "doorstop_config.ini",
        "BepInEx",
        "BepInEx/core",
    };

    /// <summary>
    /// IL2CPP（BepInEx 6 Unity IL2CPP）解压后期望条目；除 Mono 共有项外含 dotnet/。
    /// </summary>
    public static IReadOnlyList<string> ExpectedIl2CppRootEntries { get; } = new[]
    {
        "winhttp.dll",
        "doorstop_config.ini",
        "BepInEx",
        "BepInEx/core",
        "dotnet",
    };

    public static IReadOnlyList<string> ExpectedRootEntries(UnityRuntimeKind runtime) =>
        runtime == UnityRuntimeKind.Il2Cpp ? ExpectedIl2CppRootEntries : ExpectedMonoRootEntries;

    /// <summary>
    /// 翻译插件应复制到的相对目录，以及建议的配置示例目标路径。
    /// </summary>
    public static PluginLayoutPlan PlanTranslatorLayout(string gameRoot)
    {
        var plugins = Path.Combine(gameRoot, "BepInEx", "plugins", "Translator");
        var configDir = Path.Combine(gameRoot, "BepInEx", "config");
        return new PluginLayoutPlan(
            PluginDirectory: plugins,
            ConfigDirectory: configDir,
            ConfigExampleDestination: Path.Combine(configDir, ConfigExampleFileName),
            RelativePluginDirectory: PluginsTranslatorRelative,
            RelativeConfigDirectory: ConfigRelative);
    }

    /// <summary>
    /// 规划：zip 内条目应落到游戏根的相对路径列表（用于单测断言，不执行 IO）。
    /// zip 相对路径若以 BepInEx/ 或 doorstop/winhttp 开头则原样保留。
    /// </summary>
    public static IReadOnlyList<string> PlanZipExtractRelativePaths(
        IEnumerable<string> zipEntryNames,
        UnityRuntimeKind runtime)
    {
        var expected = new HashSet<string>(ExpectedRootEntries(runtime), StringComparer.OrdinalIgnoreCase);
        var planned = new List<string>();

        foreach (var raw in zipEntryNames)
        {
            var name = NormalizeZipEntry(raw);
            if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
            {
                continue;
            }

            planned.Add(name);

            // 记录是否覆盖了期望根条目的前缀
            foreach (var exp in expected.ToList())
            {
                if (name.Equals(exp, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(exp.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase))
                {
                    expected.Remove(exp);
                }
            }
        }

        return planned;
    }

    /// <summary>
    /// 检查解压后游戏根是否具备关键 doorstop / BepInEx 布局。
    /// </summary>
    public static IReadOnlyList<string> MissingExpectedEntries(string gameRoot, UnityRuntimeKind runtime)
    {
        var missing = new List<string>();
        foreach (var rel in ExpectedRootEntries(runtime))
        {
            var full = Path.Combine(gameRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                missing.Add(rel);
            }
        }

        return missing;
    }

    internal static string NormalizeZipEntry(string entry)
    {
        var n = entry.Replace('\\', '/').TrimStart('/');
        // 部分 zip 可能多一层顶层目录；若首段不是已知根文件/目录则剥一层
        var parts = n.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var first = parts[0];
            var known = first is "BepInEx" or "dotnet" or "winhttp.dll" or "doorstop_config.ini"
                or ".doorstop_version" or "changelog.txt";
            if (!known
                && !first.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !first.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                n = string.Join('/', parts.Skip(1));
            }
        }

        return n;
    }
}

public sealed record PluginLayoutPlan(
    string PluginDirectory,
    string ConfigDirectory,
    string ConfigExampleDestination,
    string RelativePluginDirectory,
    string RelativeConfigDirectory);
