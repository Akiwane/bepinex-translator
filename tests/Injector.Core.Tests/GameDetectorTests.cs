using BepInExTranslator.Injector.Core;

namespace BepInExTranslator.Injector.Core.Tests;

/// <summary>
/// 使用临时目录伪造 Mono / IL2CPP 目录树（文档约定的探测桩）。
/// </summary>
public sealed class GameDetectorTests
{
    [Fact]
    public void Detect_MonoFixture_IdentifiesMono()
    {
        using var fx = GameFixtures.CreateMonoGame("SampleMono");
        IGameProbe detector = new GameDetector();

        var result = detector.Detect(fx.GameRoot);

        Assert.True(result.IsValidUnityGame);
        Assert.Null(result.Error);
        Assert.Equal(UnityRuntimeKind.Mono, result.Runtime);
        Assert.Equal("2021.3.35f1", result.UnityVersion);
        Assert.NotNull(result.DataDirectory);
        Assert.Contains(result.EvidencePaths, p => p.Contains("Managed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Notes, n => n.Contains("Mono", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_Il2CppFixture_IdentifiesIl2Cpp()
    {
        using var fx = GameFixtures.CreateIl2CppGame("SampleIl2Cpp");
        var detector = new GameDetector();

        var result = detector.Detect(fx.GameRoot);

        Assert.True(result.IsValidUnityGame);
        Assert.Equal(UnityRuntimeKind.Il2Cpp, result.Runtime);
        Assert.Equal("2022.3.10f1", result.UnityVersion);
        Assert.Contains(result.Notes, n => n.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_ExePath_ResolvesGameRoot()
    {
        using var fx = GameFixtures.CreateMonoGame("ViaExe");
        var detector = new GameDetector();

        var result = detector.Detect(fx.ExePath);

        Assert.Equal(fx.GameRoot, result.GameRoot);
        Assert.Equal(UnityRuntimeKind.Mono, result.Runtime);
    }

    [Fact]
    public void Detect_NonUnityFolder_Refuses()
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "readme.txt"), "not a game");
            var result = new GameDetector().Detect(root);
            Assert.False(result.IsValidUnityGame);
            Assert.Equal(UnityRuntimeKind.Unknown, result.Runtime);
            Assert.NotNull(result.Error);
            Assert.Equal(InjectorErrorKind.NotUnityGame, result.Error!.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_Il2CppViaGameAssemblyOnly()
    {
        using var fx = GameFixtures.CreateIl2CppGame("GaOnly", includeIl2CppData: false, includeGameAssembly: true);
        var result = new GameDetector().Detect(fx.GameRoot);
        Assert.Equal(UnityRuntimeKind.Il2Cpp, result.Runtime);
    }
}

/// <summary>
/// 文档约定探测桩：伪造文件夹树，无真实游戏二进制。
/// </summary>
internal sealed class GameFixtures : IDisposable
{
    public string GameRoot { get; }
    public string ExePath { get; }
    public string DataDirectory { get; }

    private GameFixtures(string gameRoot, string exePath, string dataDirectory)
    {
        GameRoot = gameRoot;
        ExePath = exePath;
        DataDirectory = dataDirectory;
    }

    public static GameFixtures CreateMonoGame(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-translator-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, name + ".exe");
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A }); // MZ stub
        var data = Path.Combine(root, name + "_Data");
        Directory.CreateDirectory(Path.Combine(data, "Managed"));
        File.WriteAllText(Path.Combine(data, "unity version.txt"), "2021.3.35f1");
        // 放入一小段含版本号的伪 globalgamemanagers
        File.WriteAllBytes(
            Path.Combine(data, "globalgamemanagers"),
            System.Text.Encoding.ASCII.GetBytes("xxxx 2021.3.35f1 yyyy"));
        return new GameFixtures(root, exe, data);
    }

    public static GameFixtures CreateIl2CppGame(
        string name,
        bool includeIl2CppData = true,
        bool includeGameAssembly = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-translator-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, name + ".exe");
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
        var data = Path.Combine(root, name + "_Data");
        Directory.CreateDirectory(data);
        if (includeIl2CppData)
        {
            Directory.CreateDirectory(Path.Combine(data, "il2cpp_data", "Metadata"));
        }

        if (includeGameAssembly)
        {
            File.WriteAllBytes(Path.Combine(root, "GameAssembly.dll"), new byte[] { 0x4D, 0x5A });
        }

        File.WriteAllText(Path.Combine(data, "unity version.txt"), "2022.3.10f1");
        return new GameFixtures(root, exe, data);
    }

    public void Dispose()
    {
        try
        {
            var parent = Directory.GetParent(GameRoot)?.FullName;
            if (parent != null && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
        catch
        {
            // 测试清理失败可忽略
        }
    }
}
