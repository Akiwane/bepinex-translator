# 决策记录 — BepInEx 翻译模组

| 日期 | 决策 | 说明 |
|------|------|------|
| 2026-09-29 | v1：自定义普通翻译 + LLM；产物可读可精翻；已译不重译；自动缩排；三字体源 | 用户给定 |

| 2026-09-29 | v1 文本源：UGUI Text + TMP + 旧版 TextMesh | 用户确认 |
| 2026-09-29 | 翻译触发：按需（遇未缓存再请求） | 用户确认 |
| 2026-09-29 | 默认目标语言：zh-CN | 用户确认 |
| 2026-09-29 | 产物格式：JSON | 用户确认 |
| 2026-09-29 | 运行时：Mono + IL2CPP | 用户确认 |
| 2026-09-29 | 普通 API：通用 HTTP JSON；LLM：OpenAI 兼容 Chat | 用户确认 |
| 2026-09-29 | REQUIREMENTS 升为 v1.0 定稿 | 决策齐备 |
| 2026-09-29 | v1 无配置 UI：仅 BepInEx config + JSON | 前端问，PM 定 |
| 2026-09-29 | 产物 JSON：[{hash,source,translation}] | 可读+稳定键 |
| 2026-09-29 | 前端起草示例配置/字体说明/JSON 样例进仓 | Q3 是 |
| 2026-09-29 | 新增：外挂注入程序，匹配 Unity 与 BepInEx 版本 | 用户新增；细节待拍板 |
| 2026-09-29 | 注入器 v1 仅 Windows | 用户确认 |
| 2026-09-29 | 注入器：自动下载匹配的 BepInEx/模组包 | 用户确认 |
| 2026-09-29 | 注入器形态：独立桌面 GUI | 用户确认 |
| 2026-09-29 | 注入器等 PR #3 合完再交接 | 用户确认 |
| 2026-09-29 | PR #4 合入 main；启动注入器 v1.1 交接 | 用户批准合并 |
| 2026-09-29 | 注入器：Avalonia + Injector.Core 分离；BepInEx 5.4.23.5 (Mono) / 6.0.0-pre.2 IL2CPP win-x64 pin | 实现 v1.1 §7 |
| 2026-09-29 | 模组包默认优先本地 artifacts/mono\|il2cpp，其次可配 zip/URL/Release tag | 实现 v1.1 §7 |
| 2026-09-29 | Core 契约：IGameProbe / IPackageResolver / IInstaller + InjectorError；Gui 仅 stub，完整 UI 归前端 | 与前端分工 |
