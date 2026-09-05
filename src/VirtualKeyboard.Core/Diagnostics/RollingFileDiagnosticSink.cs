namespace VirtualKeyboard.Core.Diagnostics;

using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 本地滚动文件汇聚点（FR-DIA-003 / NFR-PRI-001 / NFR-PERF-001 / 设计文档 15.3/16）：
/// 事件序列化为 JSON 行追加到 <c>diagnostic.{N}.jsonl</c>；超过单文件上限即滚动到下一文件，
/// 删除最旧文件，使“文件数 × 单文件上限”构成确定性的总量上界。
/// <para>
/// 降级与故障隔离（设计文档 16）：构造函数先做写探测，失败则 <see cref="CanWrite"/>=false，
/// 后续 <see cref="Write"/> 均为 no-op；运行期任何 IO/磁盘故障把目录标记为不可写并停止使用，
/// 绝不向诊断调用方抛出异常，也绝不影响主流程。
/// </para>
/// </summary>
public sealed class RollingFileDiagnosticSink : IDiagnosticSink
{
    private const string FileNamePrefix = "diagnostic.";
    private const string FileExtension = ".jsonl";
    private const long MinFileBytes = 512;
    private const long MaxFileBytes = 64_000_000;
    private const int MinFileCount = 1;
    private const int MaxFileCount = 50;

    private readonly object _gate = new();
    private readonly bool _detailedEnabled;
    private readonly long _maxFileBytes;
    private readonly int _maxFileCount;
    private string? _root;
    private int _activeIndex;
    private bool _disposed;

    /// <summary>
    /// 创建汇聚点。<paramref name="rootDirectory"/> 不可创建/不可写时 <see cref="CanWrite"/> 为 false（no-op）。
    /// </summary>
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

    /// <summary>
    /// 写入单条事件。详细事件在未启用时丢弃（15.3）；任何故障降级为 no-op，绝不抛出。
    /// </summary>
    public void Write(DiagnosticEvent e)
    {
        string? file;
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

            file = NextFile();
            }
        }
        catch
        {
            return;
        }

        try
        {
            var line = DiagnosticSerializer.ToJsonLine(e) + "\n";
            using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read, 4096);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            sw.Write(line);
        }
        catch
        {
            lock (_gate)
            {
                _root = null;
            }
        }
    }

    /// <summary>计算下一个可写文件路径；必要时滚动。</summary>
    private string NextFile()
    {
        var active = FilePath(_activeIndex);
        var info = new FileInfo(active);
        if (info.Exists && info.Length >= _maxFileBytes)
        {
            Rotate();
            active = FilePath(_activeIndex);
        }

        return active;
    }

    /// <summary>滚动：删除最旧文件，其余下移，activeIndex 前进。</summary>
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

        _activeIndex = Math.Min(_activeIndex + 1, _maxFileCount - 1);
    }

    private string FilePath(int index) =>
        System.IO.Path.Combine(_root!, FileNamePrefix + index.ToString(CultureInfo.InvariantCulture) + FileExtension);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _root = null;
        }
    }
}
