namespace BepInExTranslator.Injector.Core;

/// <summary>
/// 目标路径已有同名文件时的处理策略。
/// </summary>
public enum OverwritePolicy
{
    /// <summary>跳过已存在文件，保留用户现有内容。</summary>
    SkipExisting = 0,

    /// <summary>覆盖前将已有文件复制到同目录 `.injector-backup/` 时间戳子目录。</summary>
    BackupThenOverwrite = 1,

    /// <summary>直接覆盖（危险；仅在用户明确确认后使用）。</summary>
    Overwrite = 2,
}
