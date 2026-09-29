using BepInExTranslator.Services;
using UnityEngine;

namespace BepInExTranslator
{
    /// <summary>
    /// 挂在同一插件对象上泵送主线程队列（Awake 后由 Unity 调用 Update）。
    /// </summary>
    public sealed partial class TranslatorPlugin
    {
        private void Update()
        {
            UnityMainThread.Pump();
        }
    }
}
