using System.Text;
using System.Text.RegularExpressions;

namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 尽力从 globalgamemanagers / data.unity3d / exe 旁路径读取 Unity 版本字符串。
/// 不依赖 Windows PE API，以便在 Linux CI 上单测。
/// </summary>
public sealed class UnityVersionReader
{
    private static readonly Regex VersionRegex = new(
        @"\b(20\d{2}\.\d+\.\d+[a-z]\d+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 按常见注入器顺序尝试多个来源；全部失败返回 null。
    /// </summary>
    public string? TryRead(string gameRoot, string dataDirectory, string? exePath)
    {
        // 1) *_Data/globalgamemanagers（二进制中嵌 ASCII 版本）
        var ggm = Path.Combine(dataDirectory, "globalgamemanagers");
        var fromGgm = TryScanFileForVersion(ggm);
        if (fromGgm != null)
        {
            return fromGgm;
        }

        // 2) *_Data/data.unity3d
        var dataUnity3d = Path.Combine(dataDirectory, "data.unity3d");
        var fromData = TryScanFileForVersion(dataUnity3d);
        if (fromData != null)
        {
            return fromData;
        }

        // 3) 仓库旁的 stub：*_Data/unity version.txt（测试桩 / 文档约定）
        var stub = Path.Combine(dataDirectory, "unity version.txt");
        if (File.Exists(stub))
        {
            var text = File.ReadAllText(stub);
            var m = VersionRegex.Match(text);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        // 4) 游戏根目录旁记文件（可选）
        var rootStub = Path.Combine(gameRoot, "unity_version.txt");
        if (File.Exists(rootStub))
        {
            var text = File.ReadAllText(rootStub);
            var m = VersionRegex.Match(text);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        // 5) exe 文件名不含版本；真实环境可用 FileVersionInfo（仅 Windows）。
        // 此处扫描同名 .exe 旁若有 .txt 备注则忽略；返回 null 即可。
        _ = exePath;
        return null;
    }

    /// <summary>
    /// 扫描文件前若干 KB 的可打印 ASCII，匹配 Unity 版本模式。
    /// </summary>
    internal static string? TryScanFileForVersion(string path, int maxBytes = 512 * 1024)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var fs = File.OpenRead(path);
            var len = (int)Math.Min(fs.Length, maxBytes);
            var buffer = new byte[len];
            var read = fs.Read(buffer, 0, len);
            if (read <= 0)
            {
                return null;
            }

            // 抽取可打印 ASCII 片段，降低误匹配
            var sb = new StringBuilder(read);
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                sb.Append(b >= 32 && b < 127 ? (char)b : ' ');
            }

            var m = VersionRegex.Match(sb.ToString());
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
