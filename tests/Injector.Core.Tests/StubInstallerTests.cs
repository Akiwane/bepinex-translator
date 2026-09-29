using Injector.Core.Install;
using Injector.Core.Models;
using Injector.Core.Packages;
using Injector.Core.Probing;
using Xunit;

namespace Injector.Core.Tests;

public class StubInstallerTests
{
    private readonly GameProbe _probe = new();
    private readonly StubInstaller _installer = new(new StubPackageResolver(), stageDelay: TimeSpan.Zero);

    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));

    [Fact]
    public async Task Install_MonoFixture_ReportsStagesAndWritesMarker()
    {
        string source = Path.Combine(FixturesRoot, "mono-game");
        string work = CreateTempCopy(source);
        try
        {
            GameProbeResult probe = _probe.Detect(work);
            Assert.True(probe.IsValid);

            var stages = new List<InstallStage>();
            // Use a synchronous IProgress — System.Progress<T> may post via SyncContext
            // and drop early reports before the test asserts.
            IProgress<InstallProgress> progress = new SyncProgress(p => stages.Add(p.Stage));

            InstallResult result = await _installer.InstallAsync(
                new InstallRequest { GamePath = work, Probe = probe, Overwrite = false },
                progress);

            Assert.True(result.Success, result.Message);
            Assert.Contains(InstallStage.Resolving, stages);
            Assert.Contains(InstallStage.DownloadingBepInEx, stages);
            Assert.Contains(InstallStage.CopyingConfig, stages);
            Assert.Contains(InstallStage.Done, stages);
            Assert.True(File.Exists(Path.Combine(work, InjectorPaths.StubInstallMarkerFileName)));
            Assert.True(Directory.Exists(Path.Combine(work, InjectorPaths.PluginsTranslatorRelativePath)));
            Assert.True(File.Exists(Path.Combine(
                work,
                InjectorPaths.ConfigRelativeDirectory,
                InjectorPaths.ConfigExampleDestFileName)));
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public async Task Install_AlreadyInstalled_WithoutOverwrite_Fails()
    {
        string source = Path.Combine(FixturesRoot, "mono-game");
        string work = CreateTempCopy(source);
        try
        {
            GameProbeResult probe = _probe.Detect(work);
            InstallResult first = await _installer.InstallAsync(
                new InstallRequest { GamePath = work, Probe = probe, Overwrite = false });
            Assert.True(first.Success, first.Message);

            InstallResult second = await _installer.InstallAsync(
                new InstallRequest { GamePath = work, Probe = probe, Overwrite = false });

            Assert.False(second.Success);
            Assert.Equal(InstallErrorCode.AlreadyInstalled, second.ErrorCode);
            Assert.True(second.AlreadyInstalled);
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public async Task Install_AlreadyInstalled_WithOverwrite_Succeeds()
    {
        string source = Path.Combine(FixturesRoot, "il2cpp-game");
        string work = CreateTempCopy(source);
        try
        {
            GameProbeResult probe = _probe.Detect(work);
            Assert.Equal(RuntimeKind.Il2Cpp, probe.Runtime);

            Assert.True((await _installer.InstallAsync(
                new InstallRequest { GamePath = work, Probe = probe, Overwrite = false })).Success);

            InstallResult overwrite = await _installer.InstallAsync(
                new InstallRequest { GamePath = work, Probe = probe, Overwrite = true });

            Assert.True(overwrite.Success, overwrite.Message);
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public void PackageResolver_MapsRuntimes()
    {
        var resolver = new StubPackageResolver();
        Assert.NotNull(resolver.Resolve(RuntimeKind.Mono));
        Assert.NotNull(resolver.Resolve(RuntimeKind.Il2Cpp));
        Assert.Null(resolver.Resolve(RuntimeKind.Unknown));
    }

    private static string CreateTempCopy(string source)
    {
        // Path contains "/fixtures/" so StubInstaller.ShouldWriteLayout enables disk writes
        // without mutating process-wide INJECTOR_STUB_WRITE.
        string dest = Path.Combine(
            Path.GetTempPath(),
            "fixtures",
            "injector-work-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);
        return dest;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = file.Replace(source, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private sealed class SyncProgress : IProgress<InstallProgress>
    {
        private readonly Action<InstallProgress> _handler;

        public SyncProgress(Action<InstallProgress> handler) => _handler = handler;

        public void Report(InstallProgress value) => _handler(value);
    }
}
