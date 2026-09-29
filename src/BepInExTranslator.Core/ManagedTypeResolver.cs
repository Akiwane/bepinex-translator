using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 在已加载程序集中按全名解析托管类型（含 preferred assembly 优先）。
    /// IL2CPP 与 Mono 插件共用同一查找语义；时机（何时有程序集）由调用方负责。
    /// </summary>
    public static class ManagedTypeResolver
    {
        /// <summary>
        /// 在给定程序集集合中查找类型。
        /// </summary>
        /// <param name="assemblies">已加载程序集。</param>
        /// <param name="typeName">如 UnityEngine.UI.Text / TMPro.TMP_Text。</param>
        /// <param name="preferredAssemblyNames">优先程序集简单名；可空或空表示全扫描。</param>
        public static Type? FindType(
            IEnumerable<Assembly> assemblies,
            string typeName,
            IEnumerable<string>? preferredAssemblyNames = null)
        {
            if (assemblies == null || string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            var list = assemblies as IList<Assembly> ?? assemblies.ToList();

            // 先按 preferred 程序集名精确匹配
            if (preferredAssemblyNames != null)
            {
                foreach (var preferred in preferredAssemblyNames)
                {
                    if (string.IsNullOrWhiteSpace(preferred))
                    {
                        continue;
                    }

                    foreach (var asm in list)
                    {
                        if (!AssemblyNameEquals(asm, preferred))
                        {
                            continue;
                        }

                        var hit = TryGetType(asm, typeName);
                        if (hit != null)
                        {
                            return hit;
                        }
                    }
                }
            }

            // 再全量扫描（interop 程序集名因生成器/版本可能不一致）
            foreach (var asm in list)
            {
                var hit = TryGetType(asm, typeName);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>程序集简单名是否与期望相等（忽略大小写）。</summary>
        public static bool AssemblyNameEquals(Assembly assembly, string expectedSimpleName)
        {
            if (assembly == null || string.IsNullOrWhiteSpace(expectedSimpleName))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    assembly.GetName().Name,
                    expectedSimpleName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析属性 setter；若无属性则回退到 set_PropertyName(string) 方法（Il2Cpp 包装常见）。
        /// </summary>
        public static MethodInfo? FindStringSetter(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 标准属性 setter
            var prop = type.GetProperty(propertyName, flags);
            var setter = prop?.GetSetMethod(nonPublic: true);
            if (setter != null)
            {
                return setter;
            }

            // Il2Cpp / 部分生成器仅暴露 set_text(string)
            var methodName = "set_" + propertyName;
            var method = type.GetMethod(methodName, flags, null, new[] { typeof(string) }, null);
            if (method != null)
            {
                return method;
            }

            // 再试任意单参数名为 set_* 的方法（参数可能是 Il2CppSystem.String 包装，运行期再匹配）
            foreach (var candidate in type.GetMethods(flags))
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = candidate.GetParameters();
                if (parameters.Length == 1)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Type? TryGetType(Assembly asm, string typeName)
        {
            try
            {
                return asm.GetType(typeName, throwOnError: false);
            }
            catch
            {
                return null;
            }
        }
    }
}
