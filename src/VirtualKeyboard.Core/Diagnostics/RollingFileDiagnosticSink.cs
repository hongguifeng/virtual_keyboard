namespace VirtualKeyboard.Core.Diagnostics;

using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// T0.4 本地滚动 JSONL 汇聚点（FR-DIA-003 / NFR-PRI-001 / NFR-PERF-001 / 设计文档 15.3、16）：
/// 文件数量与总大小都有确定上界（maxFileCount × maxFileBytes）。
/// <para>
/// 线程与轮转模型：活跃文件恒为 <c>diagnostic.0.jsonl</c>（index 0），轮转时其余文件依次下移
/// （删除最旧的 index maxFileCount-1）。整条写入路径——可写状态检查、详细事件过滤、
/// 大小预判（当前文件 stat + 本行字节数）、轮转判定与执行、实际追加、故障降级——
/// 全部在同一把 <c>_gate</c> 锁内执行，因此任何并发下都不会有未串行化的窗口：
/// 单文件（含活跃文件）不会超过 <c>maxFileBytes</c>，总字节量不会超过 <c>maxFileBytes × maxFileCount</c>。
/// </para>
/// <para>
/// 目录创建失败、权限不足或任何 IO/文件级故障时，汇聚点降级为 no-op（后续写入静默丢弃，不抛异常）；
/// 诊断只保留本地文件，无网络上传路径（NFR-PRI-001）。
/// </para>
/// </summary>
public sealed class RollingFileDiagnosticSink : IDiagnosticSink
{
    private const long MinFileBytes = 512;
    private const long MaxFileBytes = 64_000_000;
    private const int MinFileCount = 1;
    private const int MaxFileCount = 50;
    private const string FilePrefix = "diagnostic.";
    private const string Extension = "jsonl";
    // 必须使用 no-BOM UTF-8：Encoding.UTF8 默认输出 EF BB BF preamble，
    // 该 3 字节不计入 lineBytes（GetByteCount），会破坏严格单文件/总量字节上界。
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private bool _detailedEnabled;
    private readonly long _maxFileBytes;
    private readonly int _maxFileCount;
    private string? _root;
    private bool _disposed;

    /// <param name="rootDirectory">日志根目录（不存在则创建）。</param>
    /// <param name="detailedEnabled">是否写入 <see cref="DiagnosticLevel.Detailed"/> 事件（FR-DIA-003 默认 false）。</param>
    /// <param name="maxFileBytes">单文件上限（字节）；超出 [512, 64MB] 时收敛。</param>
    /// <param name="maxFileCount">文件数上限；超出 [1, 50] 时收敛。</param>
    public RollingFileDiagnosticSink(
        string rootDirectory,
        bool detailedEnabled = false,
        long maxFileBytes = 1_048_576,
        int maxFileCount = 5)
    {
        _detailedEnabled = detailedEnabled;
        _maxFileBytes = Math.Clamp(maxFileBytes, MinFileBytes, MaxFileBytes);
        _maxFileCount = Math.Clamp(maxFileCount, MinFileCount, MaxFileCount);

        try
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("rootDirectory is required.", nameof(rootDirectory));
            }

            System.IO.Directory.CreateDirectory(rootDirectory);

            // 写探测：确认目录确实可写（只读卷/权限不足时降级为 no-op）。
            var probe = System.IO.Path.Combine(rootDirectory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            System.IO.File.WriteAllText(probe, string.Empty);
            System.IO.File.Delete(probe);

            _root = System.IO.Path.GetFullPath(rootDirectory);
        }
        catch
        {
            _root = null;
        }
    }

    public bool DetailedEnabled
    {
        get
        {
            lock (_gate)
            {
                return _detailedEnabled;
            }
        }
    }

    public bool CanWrite
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _root is not null;
            }
        }
    }

    public void SetDetailedEnabled(bool enabled)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _detailedEnabled = enabled;
        }
    }

    /// <summary>
    /// 写入单条事件。详细事件在未启用时丢弃（15.3）；任何故障降级为 no-op，绝不抛出。
    /// </summary>
    public void Write(DiagnosticEvent e)
    {
        // JSON 行序列化是纯计算，可在锁外生成；其余文件/状态操作全部在同一锁内串行化。
        var line = DiagnosticSerializer.ToJsonLine(e) + "\n";
        var lineBytes = Utf8.GetByteCount(line);

        try
        {
            lock (_gate)
            {
                if (_disposed || _root is null)
                {
                    return;
                }

                if (e.Level == DiagnosticLevel.Detailed && !_detailedEnabled)
                {
                    return;
                }

                // 大小预判：当前大小（stat）+ 本行字节数将超过单文件上限 → 先滚动。
                // 注意：.NET 10 中 FileInfo.Length 对不存在的文件抛 FileNotFoundException，须先判存在。
                var active = FilePath(0);
                var activeInfo = new FileInfo(active);
                if ((activeInfo.Exists ? activeInfo.Length : 0) + lineBytes > _maxFileBytes)
                {
                    Rotate();
                    active = FilePath(0);
                }

                // 追加：活跃文件恒为 index 0；写入失败由外层 catch 降级。
                System.IO.File.AppendAllText(active, line, Utf8);
            }
        }
        catch
        {
            // 故障隔离（设计文档 16）：标记目录不可写并永久停止使用，
            // 绝不抛出到诊断调用方，绝不影响主流程。
            lock (_gate)
            {
                _root = null;
            }
        }
    }

    /// <summary>
    /// 滚动：删除最旧文件（index <see cref="_maxFileCount"/>-1），其余依次下移，
    /// 活跃文件保持为 index 0（滚动后为空/新建）。调用方必须持有 <see cref="_gate"/>。
    /// </summary>
    private void Rotate()
    {
        var oldest = FilePath(_maxFileCount - 1);
        if (System.IO.File.Exists(oldest))
        {
            System.IO.File.Delete(oldest);
        }

        for (var i = _maxFileCount - 2; i >= 0; i--)
        {
            var src = FilePath(i);
            if (System.IO.File.Exists(src))
            {
                System.IO.File.Move(src, FilePath(i + 1), overwrite: true);
            }
        }
    }

    private string FilePath(int index)
    {
        // _root 在 _disposed 或 IO 故障后被置 null；调用方必须持有 _gate 且已检查。
        return System.IO.Path.Combine(_root!, FilePrefix + index + "." + Extension);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
