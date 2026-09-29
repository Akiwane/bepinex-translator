using System.Threading;
using System.Threading.Tasks;

namespace BepInExTranslator.Core.Backends
{
    /// <summary>翻译后端抽象。</summary>
    public interface ITranslationBackend
    {
        string Name { get; }

        /// <summary>将 source 译为 targetLang；失败应抛异常或返回空（由调用方决定）。</summary>
        Task<string> TranslateAsync(string source, string targetLang, CancellationToken cancellationToken);
    }
}
