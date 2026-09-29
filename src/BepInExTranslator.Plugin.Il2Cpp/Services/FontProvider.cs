using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using BepInExTranslator.Core;

namespace BepInExTranslator.Services
{
    /// <summary>字体解析（反射）：FontSourceType = Unity | System | CustomFile。</summary>
    public sealed class FontProvider
    {
        private readonly PluginSettings _settings;
        private readonly ManualLogSource _log;
        private object? _cached;
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
            var fontProp = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
            if (fontProp != null && fontProp.CanWrite)
            {
                try
                {
                    fontProp.SetValue(component, font);
                }
                catch (Exception ex)
                {
                    _log.LogDebug("Font assign skipped: " + ex.Message);
                }
            }
        }

        private object? Resolve()
        {
            if (_resolved)
            {
                return _cached;
            }

            _resolved = true;
            var sourceType = (_settings.FontSourceType.Value ?? "System").Trim();
            var path = _settings.FontPath.Value ?? string.Empty;

            try
            {
                if (sourceType.Equals("Unity", StringComparison.OrdinalIgnoreCase)
                    || sourceType.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(path) ? "Arial.ttf" : Path.GetFileName(path);
                    var fontType = FindType("UnityEngine.Font");
                    if (fontType != null)
                    {
                        _cached = InvokeGetBuiltin(fontType, name);
                    }
                }
                else if (sourceType.Equals("System", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(path) ? "Arial" : path;
                    _cached = CallCreateDynamicFont(name);
                }
                else if (sourceType.Equals("CustomFile", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var name = File.Exists(path) ? Path.GetFileNameWithoutExtension(path) : path;
                        _log.LogWarning(
                            "IL2CPP custom font file loading is Unity-version dependent; trying OS font name: " + name);
                        _cached = CallCreateDynamicFont(name);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Font resolve failed ({sourceType}): {ex.Message}");
            }

            return _cached;
        }

        private static object? CallCreateDynamicFont(string name)
        {
            var fontType = FindType("UnityEngine.Font");
            var method = fontType?.GetMethod(
                "CreateDynamicFontFromOSFont",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            return method?.Invoke(null, new object[] { name, 16 });
        }

        private static object? InvokeGetBuiltin(Type fontType, string resourceName)
        {
            var resources = FindType("UnityEngine.Resources");
            var method = resources?.GetMethod(
                "GetBuiltinResource",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type), typeof(string) },
                null);
            return method?.Invoke(null, new object[] { fontType, resourceName });
        }

        private static Type? FindType(string fullName) =>
            ManagedTypeResolver.FindType(AppDomain.CurrentDomain.GetAssemblies(), fullName);
    }
}
