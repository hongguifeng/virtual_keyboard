namespace VirtualKeyboard.Core.Diagnostics;

using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 本地滚动文件汇聚点（FR-DIA-003 / 设计文档 15.3）：
/// 仅写本地文件、绝不网络上传（NFR-PRI-001）；
/// 活跃文件达上限字节后滚动为最旧索引，删除最旧文件；
/// 总大小 = 单文件上限 × 文件数上限（有界，NFR-PERF-001）；
/// 目录不可写/IO 故障时降级为 no-op，绝不抛出（设计文档 16）。
/// </summary>
public sealed class RollingFileDiagnosticSink : IDiagnosticSink
{
    private const string FileNamePrefix = "diagnostic.";
    private const string FileExtension = ".jsonl";
    private const long MinMaxFileBytes = 512;
    private const long MaxMaxFileBytes = 64_000_000;
    private const int MaxMaxFileCount = 50;

    private string? _root;
    private readonly long _maxFileBytes;
    private readonly int _maxFileCount;

    public RollingFileDiagnosticSink(
        string rootDirectory,
        bool detailedEnabled = false,
        long maxFileBytes = 1_048_576,
        int maxFileCount = 5)
    {
        DetailedEnabled = detailedEnabled;
        _maxFileBytes = Math.Clamp(maxFileBytes, MinMaxFileBytes, MaxMaxFileBytes);
        _maxFileCount = Math.Clamp(maxFileCount, 1, MaxMaxFileCount);

        try
        {
            var full = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(full);

            // 可写探测：创建并删除探针文件，避免把不可写路径当作可用。
            var probe = Path.Combine(full, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            _root = full;
        }
        catch
        {
            _root = null; // 目录不可用（是文件/被占用/只读等）→ 降级，不崩溃
        }
    }

    public bool CanWrite => _root is not null;

    public bool DetailedEnabled { get; }

    public void Write(DiagnosticEvent e)
    {
        if (_root is null)
        {
            return;
        }

        // 15.3：详细诊断默认关闭，关闭时 Detailed 事件不写入。
        if (e.Level == DiagnosticLevel.Detailed && !DetailedEnabled)
        {
            return;
        }

        try
        {
            var line = DiagnosticSerializer.ToJsonLine(e) + "\n";
            var lineBytes = Encoding.UTF8.GetByteCount(line);
            var active = FilePath(0);
            var size = File.Exists(active) ? new FileInfo(active).Length : 0;
            if (size > 0 && size + lineBytes > _maxFileBytes)
            {
                Rotate();
                active = FilePath(0);
            }

            File.AppendAllText(active, line, Encoding.UTF8);
        }
        catch
        {
            _root = null; // IO 故障 → 降级为 no-op，绝不抛
        }
    }

    public void Dispose()
    {
        // 无持久句柄；滚动/追加均为短生命周期文件 IO。
    }

    /// <summary>diagnostic.{index}.jsonl（0 为活跃）。</summary>
    internal string FilePath(int index) =>
        Path.Combine(_root!, FileNamePrefix + index.ToString(CultureInfo.InvariantCulture) + FileExtension);

    private void Rotate()
    {
        // 删除最旧（_maxFileCount-1），其余向更低索引挪动，活跃文件保持 0。
        var oldest = FilePath(_maxFileCount - 1);
        if (File.Exists(oldest))
        {
            try
            {
                File.Delete(oldest);
            }
            catch
            {
                // 忽略单个删除失败；总量仍受其余文件上限约束
            }
        }

        for (var i = _maxFileCount - 2; i >= 0; i--)
        {
            var src = FilePath(i);
            if (File.Exists(src))
            {
                File.Move(src, FilePath(i + 1), overwrite: true);
            }
        }
    }
}
