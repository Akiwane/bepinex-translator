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
    /// 含 <c>..</c> / 绝对路径等不安全条目会被跳过。
    /// </summary>
    public static IReadOnlyList<string> PlanZipExtractRelativePaths(
        IEnumerable<string> zipEntryNames,
        UnityRuntimeKind runtime)
    {
        var expected = new HashSet<string>(ExpectedRootEntries(runtime), StringComparer.OrdinalIgnoreCase);
        var planned = new List<string>();

        foreach (var raw in zipEntryNames)
        {
            // 不安全条目（zip-slip）不进入规划列表
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

    /// <summary>
    /// 规范化 zip 条目相对路径。含 <c>..</c>、空段或盘符/绝对路径时返回 <c>null</c>（调用方应拒绝解压）。
    /// 若原条目以 <c>/</c>（或 <c>\</c>）结尾，结果保留尾斜杠，以保留目录语义。
    /// </summary>
    internal static string? NormalizeZipEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        var raw = entry.Replace('\\', '/');

        // —— 拒绝绝对路径 / UNC / Windows 盘符 ——
        if (raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (raw.Length >= 2 && char.IsLetter(raw[0]) && raw[1] == ':')
        {
            return null;
        }

        // Split(RemoveEmptyEntries) 会丢掉尾斜杠；先记下目录语义
        var isDirectoryMarker = raw.EndsWith('/');

        var n = raw.TrimStart('/');
        var parts = n.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // —— 在剥顶层目录之前先拒绝 ".." ——
        foreach (var segment in parts)
        {
            if (segment == "..")
            {
                return null;
            }
        }

        // 部分 zip 可能多一层顶层目录；若首段不是已知根文件/目录则剥一层
        if (parts.Length >= 2)
        {
            var first = parts[0];
            var known = first is "BepInEx" or "dotnet" or "winhttp.dll" or "doorstop_config.ini"
                or ".doorstop_version" or "changelog.txt";
            if (!known
                && !first.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !first.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                && first != ".")
            {
                parts = parts.Skip(1).ToArray();
            }
        }

        // —— 清理 "." 并再次确认无 ".." ——
        var cleaned = new List<string>();
        foreach (var segment in parts)
        {
            if (segment == "..")
            {
                return null;
            }

            if (segment == ".")
            {
                continue;
            }

            cleaned.Add(segment);
        }

        n = string.Join('/', cleaned);
        if (string.IsNullOrEmpty(n))
        {
            return null;
        }

        return isDirectoryMarker ? n + "/" : n;
    }

    /// <summary>
    /// 断言 <paramref name="candidatePath"/> 解析后的完整路径严格位于 <paramref name="destinationRoot"/> 之下
    ///（或等于根本身）。使用带尾部分隔符的前缀比较，防止 <c>/games/foo</c> 误匹配 <c>/games/foobar</c>。
    /// </summary>
    internal static bool IsStrictlyUnderDestination(string destinationRoot, string candidatePath)
    {
        var rootFull = Path.GetFullPath(destinationRoot);
        var candidateFull = Path.GetFullPath(candidatePath);

        // 允许目标恰好为根目录本身（创建目录条目时）
        if (string.Equals(rootFull, candidateFull, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record PluginLayoutPlan(
    string PluginDirectory,
    string ConfigDirectory,
    string ConfigExampleDestination,
    string RelativePluginDirectory,
    string RelativeConfigDirectory);
