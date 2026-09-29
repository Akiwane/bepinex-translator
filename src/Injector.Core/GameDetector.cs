using System.Text.RegularExpressions;

namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 识别游戏根目录、Mono vs IL2CPP，并尽力读取 Unity 版本。
/// 规则与 README 一致：*_Data/Managed 且无 il2cpp_data → Mono；il2cpp_data / GameAssembly.dll → IL2CPP。
/// </summary>
public sealed class GameDetector : IGameProbe
{
    private static readonly Regex UnityVersionRegex = new(
        @"\b(20\d{2}\.\d+\.\d+[a-z]\d+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly UnityVersionReader _versionReader;

    public GameDetector(UnityVersionReader? versionReader = null)
    {
        _versionReader = versionReader ?? new UnityVersionReader();
    }

    /// <summary>
    /// <paramref name="path"/> 可为游戏根目录或游戏 .exe。
    /// </summary>
    public GameDetectionResult Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid(path ?? string.Empty, null, InjectorError.InvalidPath("路径为空。"));
        }

        var full = Path.GetFullPath(path.Trim());
        var notes = new List<string>();
        var evidence = new List<string>();
        string gameRoot;
        string? exePath = null;

        // —— 解析根目录与 exe ——
        if (File.Exists(full) && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exePath = full;
            gameRoot = Path.GetDirectoryName(full) ?? full;
            notes.Add($"已选择游戏 exe：{Path.GetFileName(full)}");
            evidence.Add(exePath);
        }
        else if (Directory.Exists(full))
        {
            gameRoot = full;
            exePath = FindLikelyGameExe(gameRoot);
            if (exePath != null)
            {
                notes.Add($"在目录中找到候选 exe：{Path.GetFileName(exePath)}");
                evidence.Add(exePath);
            }
        }
        else
        {
            return Invalid(full, null, InjectorError.InvalidPath("路径不存在（既不是目录也不是 .exe）。", full));
        }

        // —— 定位 *_Data ——
        var dataDir = FindDataDirectory(gameRoot, exePath);
        if (dataDir == null)
        {
            return new GameDetectionResult
            {
                GameRoot = gameRoot,
                ExePath = exePath,
                Runtime = UnityRuntimeKind.Unknown,
                UnityVersion = null,
                DataDirectory = null,
                IsValidUnityGame = false,
                EvidencePaths = evidence,
                Error = InjectorError.NotUnityGame(
                    "未找到 Unity *_Data 目录；拒绝安装以免写错路径。",
                    gameRoot),
                Notes = notes.Concat(new[]
                {
                    "未找到 Unity *_Data 目录；拒绝安装以免写错路径。",
                    "请选择包含 Game.exe 与 Game_Data 的游戏根目录，或直接选择游戏 .exe。",
                }).ToArray(),
            };
        }

        notes.Add($"数据目录：{Path.GetFileName(dataDir)}");
        evidence.Add(dataDir);

        // —— Mono vs IL2CPP ——
        var il2cppData = Path.Combine(dataDir, "il2cpp_data");
        var gameAssembly = Path.Combine(gameRoot, "GameAssembly.dll");
        var managed = Path.Combine(dataDir, "Managed");
        var hasIl2CppData = Directory.Exists(il2cppData);
        var hasGameAssembly = File.Exists(gameAssembly);
        var hasManaged = Directory.Exists(managed);

        UnityRuntimeKind runtime;
        InjectorError? error = null;
        if (hasIl2CppData || hasGameAssembly)
        {
            runtime = UnityRuntimeKind.Il2Cpp;
            if (hasIl2CppData)
            {
                notes.Add("检测到 il2cpp_data → IL2CPP。");
                evidence.Add(il2cppData);
            }

            if (hasGameAssembly)
            {
                notes.Add("检测到 GameAssembly.dll → IL2CPP。");
                evidence.Add(gameAssembly);
            }
        }
        else if (hasManaged)
        {
            runtime = UnityRuntimeKind.Mono;
            notes.Add("检测到 *_Data/Managed 且无 il2cpp_data → Mono。");
            evidence.Add(managed);
        }
        else
        {
            runtime = UnityRuntimeKind.Unknown;
            notes.Add("存在 *_Data，但既无 Managed 也无 il2cpp_data / GameAssembly.dll，无法判定运行时。");
            error = InjectorError.UnknownRuntime(
                "无法判定 Mono 或 IL2CPP。",
                dataDir);
        }

        // —— Unity 版本（展示/日志；BepInEx 选型以 Mono/IL2CPP 为主）——
        var unityVersion = _versionReader.TryRead(gameRoot, dataDir, exePath);
        if (unityVersion != null)
        {
            notes.Add($"Unity 版本（尽力解析）：{unityVersion}");
        }
        else
        {
            notes.Add("未能解析 Unity 版本（不影响按 Mono/IL2CPP 选包）。");
        }

        return new GameDetectionResult
        {
            GameRoot = gameRoot,
            ExePath = exePath,
            Runtime = runtime,
            UnityVersion = unityVersion,
            DataDirectory = dataDir,
            IsValidUnityGame = runtime != UnityRuntimeKind.Unknown,
            EvidencePaths = evidence,
            Error = error,
            Notes = notes,
        };
    }

    private static GameDetectionResult Invalid(string gameRoot, string? exe, InjectorError error)
    {
        return new GameDetectionResult
        {
            GameRoot = gameRoot,
            ExePath = exe,
            Runtime = UnityRuntimeKind.Unknown,
            IsValidUnityGame = false,
            Error = error,
            Notes = new[] { error.Message },
        };
    }

    internal static string? FindDataDirectory(string gameRoot, string? exePath)
    {
        if (exePath != null)
        {
            var stem = Path.GetFileNameWithoutExtension(exePath);
            var paired = Path.Combine(gameRoot, stem + "_Data");
            if (Directory.Exists(paired))
            {
                return paired;
            }
        }

        var dirs = Directory.GetDirectories(gameRoot, "*_Data");
        if (dirs.Length == 1)
        {
            return dirs[0];
        }

        if (dirs.Length > 1)
        {
            foreach (var d in dirs)
            {
                if (Directory.Exists(Path.Combine(d, "Managed"))
                    || Directory.Exists(Path.Combine(d, "il2cpp_data")))
                {
                    return d;
                }
            }

            return dirs[0];
        }

        return null;
    }

    internal static string? FindLikelyGameExe(string gameRoot)
    {
        var exes = Directory.GetFiles(gameRoot, "*.exe")
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return !name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)
                       && !name.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase)
                       && !name.Contains("unins", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var exe in exes)
        {
            var stem = Path.GetFileNameWithoutExtension(exe);
            if (Directory.Exists(Path.Combine(gameRoot, stem + "_Data")))
            {
                return exe;
            }
        }

        return exes.FirstOrDefault();
    }

    internal static bool LooksLikeUnityVersion(string text) => UnityVersionRegex.IsMatch(text);

    internal static string? ExtractUnityVersion(string text)
    {
        var m = UnityVersionRegex.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }
}
