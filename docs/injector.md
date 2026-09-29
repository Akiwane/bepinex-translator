# Windows 注入器（REQUIREMENTS §7 / v1.1）

独立桌面 GUI：选择游戏目录或 `.exe` → 识别 Unity 版本与 **Mono / IL2CPP** → 自动下载匹配的 **BepInEx** 与本翻译模组包 → 写入标准 BepInEx 布局（doorstop / `winhttp` 等）。

> 平台：**仅 Windows** 运行 GUI。核心库与单元测试可在 Linux CI 上 `dotnet test`。

## 工程

| 项目 | 说明 |
|------|------|
| `src/BepInExTranslator.Injector.Core` | 探测 / 包解析 / 下载 / 落盘（可单测） |
| `src/BepInExTranslator.Injector.Gui` | Avalonia 桌面 GUI（net8.0） |
| `tests/BepInExTranslator.Injector.Core.Tests` | Mono/IL2CPP 探测桩 + 布局规划 / 安装落盘单测 |

## 构建与运行 GUI

```bash
# 建议先构建对应运行时的插件产物（供本地 artifacts 安装）
dotnet build src/BepInExTranslator.Plugin/BepInExTranslator.Plugin.csproj -c Release
dotnet build src/BepInExTranslator.Plugin.Il2Cpp/BepInExTranslator.Plugin.Il2Cpp.csproj -c Release

dotnet build src/BepInExTranslator.Injector.Gui/BepInExTranslator.Injector.Gui.csproj -c Release
dotnet run --project src/BepInExTranslator.Injector.Gui -c Release
```

Windows 上也可直接运行：

```text
src/BepInExTranslator.Injector.Gui/bin/Release/net8.0/BepInExTranslator.Injector.Gui.exe
```

## 探测规则（与 README 一致）

| 信号 | 判定 | BepInEx |
|------|------|---------|
| 存在 `*_Data/Managed` 且无 `il2cpp_data` | **Mono** | **5.x** |
| 存在 `il2cpp_data` 或 `GameAssembly.dll` | **IL2CPP** | **6.x Unity IL2CPP** |
| 无 `*_Data` | 拒绝安装 | — |

Unity 版本尽力从 `globalgamemanagers` / `data.unity3d` / `*_Data/unity version.txt` 解析，主要用于 UI 展示与日志；**选包以 Mono vs IL2CPP 为准**。

### 探测桩（单测 / 无真机时）

测试会在临时目录伪造：

- Mono：`Game.exe` + `Game_Data/Managed/` + 版本桩文件  
- IL2CPP：`Game.exe` + `Game_Data/il2cpp_data/` 和/或根目录 `GameAssembly.dll`

见 `tests/BepInExTranslator.Injector.Core.Tests/GameDetectorTests.cs`。

## 默认包映射（已 pin）

| 运行时 | BepInEx | 资产 |
|--------|---------|------|
| Mono | **5.4.23.5** | `BepInEx_win_x64_5.4.23.5.zip` |
| IL2CPP | **6.0.0-pre.2** | `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip` |

下载自官方 GitHub Releases：

- `https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip`
- `https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip`

可在 GUI 中用「BepInEx 本地 zip」或将来扩展 URL 覆盖。常量见 `PackageCatalog.cs`。

> 说明：本仓库 IL2CPP 插件 NuGet 引用的是 `BepInEx.Unity.IL2CPP` **6.0.0-be.733**。官方 GitHub 目前有稳定可 pin 的 **6.0.0-pre.2** 发行包。若某款新游戏需要更新的 Bleeding Edge，请在 GUI 中改用本地 BE zip（从 [builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be) 获取 `BepInEx-Unity.IL2CPP-win-x64-…`）。

## 翻译模组包来源（优先级）

1. GUI「翻译模组本地 zip」  
2. GUI「artifacts 目录」，或仓库相对默认：`artifacts/mono` / `artifacts/il2cpp`（需先 `dotnet build` 对应插件）  
3. GUI「下载 URL」  
4. GUI「GitHub Release tag」→ 约定资产名 `BepInExTranslator-{mono|il2cpp}-win.zip`

**默认推荐**：开发机先构建插件，注入器自动从 `artifacts/` 复制。不要把下载的 zip / DLL 提交进 git。

## 安装落盘布局

解压 BepInEx 到游戏根后，期望（相对游戏根）：

```text
winhttp.dll                 # 少数包可能为 version.dll
doorstop_config.ini
BepInEx/core/…
dotnet/                     # 仅 IL2CPP 包
BepInEx/plugins/Translator/
  BepInExTranslator.dll
  BepInExTranslator.Core.dll
BepInEx/config/Translator.cfg.example
```

覆盖策略：默认**备份后覆盖**（写入游戏根 `.injector-backup/<时间戳>/`）；也可跳过已存在或直接覆盖。下载缓存默认在游戏根 `.bepinex-translator-cache/`（勿提交）。

## 如何验证（验收）

1. **探测**：对至少一款 Mono、一款 IL2CPP 真机，或跑 `dotnet test` 中的探测桩，运行时判定正确。  
2. **BepInEx**：安装后启动游戏一次，确认出现 `BepInEx/LogOutput.log`（及 `BepInEx/config` 生成）。若无日志，检查 doorstop / `winhttp.dll` 是否在游戏根、杀毒是否拦截。  
3. **本模组**：确认 `BepInEx/plugins/Translator/` 下有 DLL；日志中应有插件加载相关输出。将 `Translator.cfg.example` 对照填写后重启游戏。

## 安全注意

- 路径必须通过 Unity `*_Data` 探测，否则拒绝写入。  
- 不提交 API 密钥；包 URL 为公开 Releases，无 secrets。  
- 遵守游戏 EULA / ToS 与 BepInEx 许可。
