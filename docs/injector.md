# 注入器（Injector）— Windows GUI

v1.1 范围见 [`REQUIREMENTS.md`](../REQUIREMENTS.md) §7：独立 **Windows** 桌面 GUI，选择游戏目录/exe → 探测 Unity / Mono·IL2CPP → 安装匹配的 BepInEx + 本模组。

本 PR 交付 **UI + 共享契约 + 可运行 stub**。真实下载/解压/doorstop·`winhttp` 落盘由后端替换 stub 安装器后完成；**不要**声称端到端 BepInEx 安装已可用。

## 如何运行 GUI

```bash
dotnet run --project src/Injector.Gui
```

- 产品定位：**Windows only**（DECISIONS / §7）。
- 工程 TFM 为 `net8.0`（非 `net8.0-windows`），以便 Linux CI 能 `dotnet build` Avalonia 工程；运行时 GUI 仍面向 Windows。
- DI 默认：`GameProbe`（真实文件系统启发式）+ `StubInstaller`（模拟进度，默认不下载）。

## Stub vs 真实安装

| 组件 | 当前 | 后端下一步 |
|------|------|------------|
| `IGameProbe` / `GameProbe` | 已实现：`*_Data/Managed` → Mono；`il2cpp_data` / `GameAssembly.dll` → IL2CPP | 可增强 Unity 版本解析（PE 资源 / `globalgamemanagers`） |
| `IPackageResolver` | stub：Mono→BepInEx 5、IL2CPP→BepInEx 6 占位 URL | 按 Unity 版本选精确 release |
| `IInstaller` | `StubInstaller`：短延迟 + 进度；fixture 或 `INJECTOR_STUB_WRITE=1` 时写标记布局 | 实现真实 `HttpInstaller`：调用 `IPackageDownloader`、解压、写 doorstop/`winhttp`/`BepInEx/plugins` |
| `IPackageDownloader` | `HttpPackageDownloader` 骨架（stub 不调用） | 挂到真实安装器；CI 默认仍应跳过大包下载 |

配置示例复制目标：仓库 [`config/Translator.cfg.example`](../config/Translator.cfg.example)（常量 `InjectorPaths.ConfigExampleRelativePath`）。

## 探测 fixtures（单元测试）

```text
tests/fixtures/mono-game/     → RuntimeKind.Mono
tests/fixtures/il2cpp-game/   → RuntimeKind.Il2Cpp
tests/fixtures/not-a-game/    → invalid / NotUnity
```

```bash
dotnet test tests/Injector.Core.Tests
```

## §7 验收映射

| §7 验收 | 本 PR |
|---------|--------|
| 识别 Mono / IL2CPP 样例（或探测桩） | fixtures + `GameProbe` 单测 |
| 下载并落盘 BepInEx，启动后有日志 | **未做**（stub 跳过下载；待 `HttpInstaller`） |
| 模组包装入 plugins；配置示例可复制 | stub 在 write 模式下创建 plugins/Translator 槽位 + 复制 cfg 示例；真实包待后端 |

## 相关工程

- `src/Injector.Core` — 契约、探测、stub、`UiStrings`
- `src/Injector.Gui` — Avalonia 11 MVVM GUI
- `tests/Injector.Core.Tests` — 探测与 stub 安装测试
