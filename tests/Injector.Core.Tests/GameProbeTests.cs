using Injector.Core.Models;
using Injector.Core.Probing;
using Xunit;

namespace Injector.Core.Tests;

public class GameProbeTests
{
    private readonly GameProbe _probe = new();

    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));

    [Fact]
    public void Detect_MonoFixture_ReturnsMono()
    {
        string path = Path.Combine(FixturesRoot, "mono-game");
        Assert.True(Directory.Exists(path), $"Missing fixture: {path}");

        GameProbeResult result = _probe.Detect(path);

        Assert.True(result.IsValid);
        Assert.Equal(RuntimeKind.Mono, result.Runtime);
        Assert.Contains("Managed", result.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("2021.3.33f1", result.UnityVersion);
        Assert.True(Directory.Exists(result.GamePath));
    }

    [Fact]
    public void Detect_MonoFixtureExe_UsesParentDirectory()
    {
        string exe = Path.Combine(FixturesRoot, "mono-game", "SampleGame.exe");
        Assert.True(File.Exists(exe), $"Missing fixture exe: {exe}");

        GameProbeResult result = _probe.Detect(exe);

        Assert.True(result.IsValid);
        Assert.Equal(RuntimeKind.Mono, result.Runtime);
        Assert.Equal(Path.GetFullPath(Path.Combine(FixturesRoot, "mono-game")), result.GamePath);
    }

    [Fact]
    public void Detect_Il2CppFixture_ReturnsIl2Cpp()
    {
        string path = Path.Combine(FixturesRoot, "il2cpp-game");
        Assert.True(Directory.Exists(path), $"Missing fixture: {path}");

        GameProbeResult result = _probe.Detect(path);

        Assert.True(result.IsValid);
        Assert.Equal(RuntimeKind.Il2Cpp, result.Runtime);
        Assert.True(
            result.Evidence.Contains("il2cpp_data", StringComparison.OrdinalIgnoreCase) ||
            result.Evidence.Contains("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2022.3.10f1", result.UnityVersion);
    }

    [Fact]
    public void Detect_GarbagePath_ReturnsInvalid()
    {
        string path = Path.Combine(FixturesRoot, "not-a-game");
        Assert.True(Directory.Exists(path), $"Missing fixture: {path}");

        GameProbeResult result = _probe.Detect(path);

        Assert.False(result.IsValid);
        Assert.Equal(InstallErrorCode.NotUnity, result.ErrorCode);
        Assert.Equal(RuntimeKind.Unknown, result.Runtime);
    }

    [Fact]
    public void Detect_MissingPath_ReturnsInvalidPath()
    {
        GameProbeResult result = _probe.Detect(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.False(result.IsValid);
        Assert.Equal(InstallErrorCode.InvalidPath, result.ErrorCode);
    }
}
