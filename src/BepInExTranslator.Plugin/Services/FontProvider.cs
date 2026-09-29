using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace BepInExTranslator.Services
{
    /// <summary>
    /// 字体解析：Default / BuiltIn / System / CustomFile。
    /// Unity 跨版本加载自定义 TTF 的能力并不统一，失败时回退并记日志。
    /// </summary>
    public sealed class FontProvider
    {
        private readonly PluginSettings _settings;
        private readonly ManualLogSource _log;
        private Font? _cached;
        private bool _resolved;

        public FontProvider(PluginSettings settings, ManualLogSource log)
        {
            _settings = settings;
            _log = log;
        }

        public void TryApplyFont(object component)
        {
            var font = Resolve();
            if (font == null)
            {
                return;
            }

            var type = component.GetType();

            // UGUI Text / TextMesh: font property
            var fontProp = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
            if (fontProp != null && fontProp.CanWrite && typeof(Font).IsAssignableFrom(fontProp.PropertyType))
            {
                fontProp.SetValue(component, font, null);
                return;
            }

            // TMP: 需要 TMP_FontAsset，无法从普通 Font 直接构造（版本相关）。
            // 文档说明：CustomFile 对 TMP 需使用 TMP Font Asset；此处仅尝试同名属性。
            var tmpFont = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
            if (tmpFont != null && tmpFont.CanWrite)
            {
                _log.LogDebug("TMP font override requires a TMP_FontAsset; skipping automatic Font assignment.");
            }
        }

        private Font? Resolve()
        {
            if (_resolved)
            {
                return _cached;
            }

            _resolved = true;
            var source = (_settings.FontSource.Value ?? "Default").Trim();

            try
            {
                if (source.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
                {
                    _cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                else if (source.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(_settings.SystemFontName.Value)
                        ? "Arial"
                        : _settings.SystemFontName.Value;
                    _cached = Font.CreateDynamicFontFromOSFont(name, 16);
                }
                else if (source.Equals("CustomFile", StringComparison.OrdinalIgnoreCase))
                {
                    _cached = LoadCustom(_settings.CustomFontPath.Value);
                }
                else
                {
                    _cached = null; // Default：不改字体
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Font resolve failed ({source}): {ex.Message}");
                _cached = null;
            }

            return _cached;
        }

        private Font? LoadCustom(string? pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                _log.LogWarning("Font.Source=CustomFile but CustomFontPath is empty.");
                return null;
            }

            // 1) 若是已安装字体名，走 OS 动态字体
            try
            {
                var os = Font.CreateDynamicFontFromOSFont(pathOrName, 16);
                if (os != null)
                {
                    return os;
                }
            }
            catch
            {
                // continue
            }

            // 2) 文件路径：Unity 没有稳定的跨版本公开 TTF 加载 API。
            // 尝试把文件名（去扩展名）当 OS 字体名；并提示用户安装字体或使用 AssetBundle/TMP。
            if (File.Exists(pathOrName))
            {
                var name = Path.GetFileNameWithoutExtension(pathOrName);
                _log.LogWarning(
                    "Custom TTF/OTF file loading is Unity-version dependent and not guaranteed. " +
                    "Attempting OS font by file name: " + name +
                    ". Prefer installing the font on the system or providing a TMP_FontAsset for TextMeshPro.");
                return Font.CreateDynamicFontFromOSFont(name, 16);
            }

            _log.LogWarning("Custom font path not found: " + pathOrName);
            return null;
        }
    }
}
