namespace VirtualKeyboard.Core.Diagnostics;

/// <summary>
/// 诊断事件汇聚点（仅本地，绝不网络上传——NFR-PRI-001）。
/// 实现必须吞掉写入故障并降级为 no-op（日志目录不可写不得阻止程序运行——设计文档 16）。
/// </summary>
public interface IDiagnosticSink : IDisposable
{
    /// <summary>当前是否持有可写目标；false 时 <see cref="Write"/> 应为 no-op。</summary>
    bool CanWrite { get; }

    /// <summary>详细诊断是否启用（FR-DIA-003 / 15.3：默认关闭）。</summary>
    bool DetailedEnabled { get; }

    /// <summary>写入单条事件（JSON 行）。必须不抛异常。</summary>
    void Write(DiagnosticEvent e);
}
