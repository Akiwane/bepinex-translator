# 字体路径说明 / Font path guide

v1.0 无配置 UI：字体通过 BepInEx 配置项 `FontSourceType` + `FontPath` 指定（见 `config/Translator.cfg.example`）。

三种来源各举一例。路径写法以「游戏安装根目录」或「BepInEx 根目录」为参照；后端实现时按相同约定解析。

---

## 1. Unity 内置 / 字体资源引用（`FontSourceType = Unity`）

引用游戏已加载的 Unity `Font` 资源名，或常见内置字体名。

**示例：**

```ini
FontSourceType = Unity
FontPath = Arial.ttf
```

也可使用游戏内已有资源路径风格的引用（由后端按项目约定解析），例如：

```ini
FontPath = Assets/Fonts/GameUIFont
```

说明：具体可用名称取决于目标游戏已打包的字体资源；找不到时插件应回退到安全默认字体并写日志。

---

## 2. 系统字体（`FontSourceType = System`）

使用操作系统已安装的字体族名（或常见文件名）。**各平台注意点：**

| 平台 | 建议示例 | 备注 |
|------|----------|------|
| Windows | `Microsoft YaHei` / `SimSun` | 中文环境常见；也可用 `msyh.ttc` 等文件名（视后端解析而定） |
| macOS | `PingFang SC` / `Hiragino Sans GB` | 系统中文字体族名 |
| Linux | `Noto Sans CJK SC` / `WenQuanYi Micro Hei` | 需已安装对应字体包 |

**示例（Windows）：**

```ini
FontSourceType = System
FontPath = Microsoft YaHei
```

---

## 3. 自定义字体文件（`FontSourceType = CustomFile`）

指向模组或 BepInEx 目录下的字体文件，路径**相对游戏根目录或 BepInEx 根目录**（与 `ProductJsonPath` 同一套解析规则）。

**示例：**

```ini
FontSourceType = CustomFile
FontPath = BepInEx/plugins/Translator/fonts/NotoSansSC-Regular.otf
```

建议将 `.ttf` / `.otf` 放在模组自带的 `fonts/` 目录，便于分发；勿在仓库中提交受许可限制的商业字体二进制（文档仅约定路径字段）。
