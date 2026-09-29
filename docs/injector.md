# 注入器（Injector）— Windows GUI

v1.1 范围见 [`REQUIREMENTS.md`](../REQUIREMENTS.md) §7：独立 **Windows** 桌面 GUI，选择游戏目录/exe → 探测 Unity / Mono·IL2CPP → 安装匹配的 BepInEx + 本模组。

本 PR 交付 **Avalonia GUI + 共享契约 + 可运行 stub**，便于演示完整 UX。真实下载/解压/doorstop·`winhttp` 落盘由**后端**另开 PR 替换 `IInstaller` / `IPackageResolver` 实现；合并时以共享接口名为准。**不要**声称端到端 BepInEx 安装已可用。

## 如何运行 GUI

```bash
dotnet run --project src/Injector.Gui
```

- 产品定位：**Windows only**（DECISIONS / §7）。
- 工程 TFM 为 `net8.0`（非 `net8.0-windows`），以便 Linux CI 能 `dotnet build` Avalonia 工程；运行时 GUI 仍面向 Windows。
- DI 默认：`GameProbe`（真实文件系统启发式）+ `StubInstaller`（仅模拟进度，**不**下载真实 BepInEx）。

## Stub vs 真实安装

| 组件 | 本 PR（Gui 侧 stub） | 后端职责 |
|------|----------------------|----------|
| `IGameProbe` / `GameProbe` | `*_Data/Managed` → Mono；`il2cpp_data` / `GameAssembly.dll` → IL2CPP；fixtures 单测 | 可增强 Unity 版本解析；接口名保持对齐 |
| `IPackageResolver` | stub：Mono→BepInEx 5、IL2CPP→BepInEx 6 占位 URL | 按 Unity×runtime 选精确 release；插件优先本地 `artifacts/` |
| `IInstaller` | `StubInstaller`：短延迟 + 进度；fixture / `INJECTOR_STUB_WRITE=1` 时写标记 | 真实安装：下载、解压、doorstop/`winhttp`/`BepInEx/plugins` |
| `IPackageDownloader` | `HttpPackageDownloader` 骨架（stub **不调用**） | 挂到真实安装器；CI 默认跳过大包 |

配置示例复制目标：仓库 [`config/Translator.cfg.example`](../config/Translator.cfg.example)（常量 `InjectorPaths.ConfigExampleRelativePath`）。

## 下载来源约定（后端默认，无密钥）

- **BepInEx**：官方 [GitHub Releases](https://github.com/BepInEx/BepInEx/releases)
  - Mono → **BepInEx 5.x** Windows x64
  - IL2CPP → **BepInEx 6.x** Unity IL2CPP Windows x64
  - 具体稳定版本号由后端在 resolver 中钉死并写入文档
- **本模组插件**：优先使用本地构建产物 `artifacts/mono/` 或 `artifacts/il2cpp/`；否则可配置 Release asset URL 或本地 zip（勿提交密钥）

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
| 下载并落盘 BepInEx，启动后有日志 | **未做**（stub 跳过下载；待后端真实安装器） |
| 模组包装入 plugins；配置示例可复制 | stub write 模式创建 plugins 槽位 + 复制 cfg 示例；真实包待后端 |

## 相关工程

- `src/Injector.Gui` — **主交付**：Avalonia 11 MVVM 中文 UI
- `src/Injector.Core` — 契约 + 探测启发式 + stub（后端可替换安装/解析实现）
- `tests/Injector.Core.Tests` — 探测与 stub 安装测试
