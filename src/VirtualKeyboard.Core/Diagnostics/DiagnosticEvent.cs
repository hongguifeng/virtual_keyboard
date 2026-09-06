namespace VirtualKeyboard.Core.Diagnostics;

/// <summary>结构化诊断事件类型（设计文档 15.1 事件模型；封闭枚举，不含自由文本）。</summary>
public enum DiagnosticType
{
    FocusObserved,
    ClassificationCompleted,
    TargetSessionCreated,
    TargetSessionInvalidated,
    OverlayShown,
    OverlayHidden,
    OverlayMoved,
    InputBatchStarted,
    InputBatchSucceeded,
    InputBatchFailed,
    InputFailureClassified,
    ConfigLoaded,
    ConfigRecovered,
    ConfigSaveFailed,
    LayoutLoaded,
    LayoutRejected,
    UnhandledBoundaryException,
}

/// <summary>事件来源模块（封闭枚举，避免以模块名夹带任意文本）。</summary>
public enum DiagnosticModule
{
    Core,
    Focus,
    Classification,
    State,
    Positioning,
    Overlay,
    Input,
    Configuration,
    Layout,
}

/// <summary>诊断级别。Detailed（详细 UIA 元数据等）默认关闭（FR-DIA-003 / 15.3）。</summary>
public enum DiagnosticLevel
{
    Info,
    Detailed,
}

/// <summary>控件分类（仅类型类别，不含控件 Name/Value 等任何文本）。</summary>
public enum ControlKind
{
    Editable,
    NotEditable,
    Password,
    Unknown,
}

/// <summary>分类判定结果（封闭枚举；密码等结论只以枚举表达，不携带目标名称或值）。</summary>
public enum Verdict
{
    None,
    Editable,
    NotEditable,
    Password,
    Unknown,
}

/// <summary>封闭原因码（15.1 ReasonCode）。</summary>
public enum ReasonCode
{
    None,
    ElementInvalid,
    PatternMissing,
    StaleVersion,
    Timeout,
    ValidationFailed,
    PermissionBoundary,
    IoError,
    Cancelled,
    Unknown,
}

/// <summary>
/// 结构化诊断事件（FR-DIA-001 / FR-DIA-002，NFR-PRI-001，设计文档 15.1/15.2）。
/// 类型级隐私约束：仅包含时间、版本号、事件 ID、模块、事件类型、数字身份信息
/// （目标进程 ID、序列号、错误码）、耗时与封闭枚举；不含也不能承接
/// InputAction 文本、密码 Value、剪贴板内容、自定义短语、AutomationElement Name/Value。
/// </summary>
public sealed record DiagnosticEvent
{
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>应用版本号（如 "0.1.0"）；仅版本号，永不承载用户内容。</summary>
    public AppVersion? AppVersion { get; init; }

    public Guid EventId { get; init; }

    public DiagnosticType Type { get; init; }

    public DiagnosticModule Module { get; init; }

    public DiagnosticLevel Level { get; init; } = DiagnosticLevel.Info;

    /// <summary>目标进程 ID；-1 表示无目标（仅数字，不记录进程名/窗口标题）。</summary>
    public int TargetProcessId { get; init; } = -1;

    public ControlKind? ControlKind { get; init; }

    public Verdict? Verdict { get; init; }

    public ReasonCode? Reason { get; init; }

    /// <summary>非负 OS/内部错误码（仅数字）。</summary>
    public int ErrorCode { get; init; }

    /// <summary>耗时（毫秒，非负）。</summary>
    public long DurationMs { get; init; }

    /// <summary>Requested native operations in a batch; zero when not applicable.</summary>
    public int RequestedCount { get; init; }

    /// <summary>Completed native operations in a batch; zero when not applicable.</summary>
    public int CompletedCount { get; init; }

    /// <summary>单调递增序号（评估版本/批次等，仅数字）。</summary>
    public long Sequence { get; init; }
}
