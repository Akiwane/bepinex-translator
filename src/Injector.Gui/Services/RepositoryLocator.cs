using BepInExTranslator.Injector.Core;

namespace Injector.Gui.Services;

/// <summary>
/// Gui startup paths so Core can resolve local artifacts/ and config/Translator.cfg.example.
/// </summary>
public sealed class GuiRuntimeOptions
{
    public string? RepositoryRoot { get; init; }

    public string? ConfigExampleSourcePath { get; init; }

    public OverwritePolicy DefaultOverwritePolicy { get; init; } = OverwritePolicy.BackupThenOverwrite;
}

/// <summary>
/// Walks up from the app base directory / cwd to find the repo root (config/ + src/).
/// </summary>
public static class RepositoryLocator
{
    public static string FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? dir = new(start);
            for (int i = 0; i < 10 && dir is not null; i++)
            {
                string config = Path.Combine(dir.FullName, "config", "Translator.cfg.example");
                string src = Path.Combine(dir.FullName, "src");
                if (File.Exists(config) && Directory.Exists(src))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
