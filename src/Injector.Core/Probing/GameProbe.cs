using Injector.Core.Abstractions;
using Injector.Core.Localization;
using Injector.Core.Models;

namespace Injector.Core.Probing;

/// <summary>
/// Filesystem heuristics matching README / REQUIREMENTS §7:
/// - *_Data/Managed without il2cpp_data → Mono
/// - il2cpp_data and/or GameAssembly.dll → IL2CPP
/// </summary>
public sealed class GameProbe : IGameProbe
{
    public GameProbeResult Detect(string path)
    {
        // Normalize: empty / missing → invalid path
        if (string.IsNullOrWhiteSpace(path))
        {
            return GameProbeResult.Invalid(
                path ?? string.Empty,
                InstallErrorCode.InvalidPath,
                UiStrings.ErrInvalidPath);
        }

        string trimmed = path.Trim().Trim('"');
        string gameRoot;

        // If user picked an .exe, use its directory as the game root.
        if (File.Exists(trimmed) &&
            trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            gameRoot = Path.GetDirectoryName(trimmed) ?? trimmed;
        }
        else if (Directory.Exists(trimmed))
        {
            gameRoot = trimmed;
        }
        else
        {
            return GameProbeResult.Invalid(trimmed, InstallErrorCode.InvalidPath, UiStrings.ErrInvalidPath);
        }

        gameRoot = Path.GetFullPath(gameRoot);

        // Collect Unity *_Data directories and IL2CPP markers.
        string[] dataDirs = Directory.GetDirectories(gameRoot, "*_Data");
        bool hasIl2CppData = HasIl2CppData(gameRoot, dataDirs);
        bool hasGameAssembly = File.Exists(Path.Combine(gameRoot, "GameAssembly.dll"));
        string? managedDir = FindManagedDirectory(dataDirs);
        bool hasManaged = managedDir is not null;

        // Not a Unity layout at all.
        if (dataDirs.Length == 0 && !hasIl2CppData && !hasGameAssembly)
        {
            return GameProbeResult.Invalid(gameRoot, InstallErrorCode.NotUnity, UiStrings.ErrNotUnity);
        }

        string? unityVersion = TryReadUnityVersion(gameRoot, dataDirs);

        // Prefer IL2CPP when either marker is present (even if Managed also exists — some hybrid layouts).
        if (hasIl2CppData || hasGameAssembly)
        {
            var evidenceParts = new List<string>();
            if (hasIl2CppData)
            {
                evidenceParts.Add("il2cpp_data");
            }

            if (hasGameAssembly)
            {
                evidenceParts.Add("GameAssembly.dll");
            }

            return new GameProbeResult
            {
                GamePath = gameRoot,
                UnityVersion = unityVersion,
                Runtime = RuntimeKind.Il2Cpp,
                Evidence = string.Join(" + ", evidenceParts),
                IsValid = true,
            };
        }

        // Mono: *_Data/Managed present and no IL2CPP markers.
        if (hasManaged)
        {
            string relativeManaged = Path.GetRelativePath(gameRoot, managedDir!);
            return new GameProbeResult
            {
                GamePath = gameRoot,
                UnityVersion = unityVersion,
                Runtime = RuntimeKind.Mono,
                Evidence = relativeManaged.Replace('\\', '/'),
                IsValid = true,
            };
        }

        // Unity-ish folder but cannot decide Mono vs IL2CPP.
        return new GameProbeResult
        {
            GamePath = gameRoot,
            UnityVersion = unityVersion,
            Runtime = RuntimeKind.Unknown,
            Evidence = dataDirs.Length > 0
                ? Path.GetFileName(dataDirs[0])
                : UiStrings.Unknown,
            IsValid = false,
            ErrorCode = InstallErrorCode.UnknownRuntime,
            ErrorMessage = UiStrings.ErrUnknownRuntime,
        };
    }

    private static bool HasIl2CppData(string gameRoot, string[] dataDirs)
    {
        // Common layouts: <Game>/il2cpp_data or <Game>/*_Data/il2cpp_data
        if (Directory.Exists(Path.Combine(gameRoot, "il2cpp_data")))
        {
            return true;
        }

        foreach (string dataDir in dataDirs)
        {
            if (Directory.Exists(Path.Combine(dataDir, "il2cpp_data")))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindManagedDirectory(string[] dataDirs)
    {
        foreach (string dataDir in dataDirs)
        {
            string managed = Path.Combine(dataDir, "Managed");
            if (Directory.Exists(managed))
            {
                return managed;
            }
        }

        return null;
    }

    /// <summary>
    /// Best-effort Unity version: fixtures may ship unity_version.txt under *_Data;
    /// otherwise try to scrape a version-like string from globalgamemanagers if present.
    /// </summary>
    private static string? TryReadUnityVersion(string gameRoot, string[] dataDirs)
    {
        foreach (string dataDir in dataDirs)
        {
            string marker = Path.Combine(dataDir, "unity_version.txt");
            if (File.Exists(marker))
            {
                string text = File.ReadAllText(marker).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        foreach (string dataDir in dataDirs)
        {
            string ggm = Path.Combine(dataDir, "globalgamemanagers");
            if (!File.Exists(ggm))
            {
                continue;
            }

            string? scraped = TryScrapeVersionFromBinary(ggm);
            if (scraped is not null)
            {
                return scraped;
            }
        }

        // Fallback: look next to UnityPlayer.dll is out of scope on Linux CI; leave null.
        _ = gameRoot;
        return null;
    }

    private static string? TryScrapeVersionFromBinary(string filePath)
    {
        // Unity embeds strings like "2021.3.33f1" near the start of globalgamemanagers.
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            int limit = Math.Min(bytes.Length, 4096);
            var chars = new char[limit];
            for (int i = 0; i < limit; i++)
            {
                byte b = bytes[i];
                chars[i] = b is >= 32 and < 127 ? (char)b : ' ';
            }

            string ascii = new string(chars);
            // Match major.minor.patch + optional letter+digits (Unity style).
            var match = System.Text.RegularExpressions.Regex.Match(
                ascii,
                @"\b(20\d{2}\.\d+\.\d+[a-z]\d+)\b");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
