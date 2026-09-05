namespace VirtualKeyboard.Core.Diagnostics;

using System.Collections.Generic;

/// <summary>
/// 有界内存诊断队列（NFR-PERF-001 / NFR-REL-001）：
/// 固定容量，满时丢弃最旧事件并计数，绝不允许无界增长。
/// </summary>
// CA1711 抑制：任务规定的类型名 BoundedDiagnosticQueue 以“Queue”结尾（NFR-PERF-001 有界队列语义），
// 且它确为有界队列的包装，不适用“实现 IQueue/IDictionary 等接口”的命名约定例外。
#pragma warning disable CA1711
public sealed class BoundedDiagnosticQueue
{
    /// <summary>容量上限（构造参数会被夹到 1..MaxCapacity）。</summary>
    public const int MaxCapacity = 4096;

    private readonly int _capacity;
    private readonly Queue<DiagnosticEvent> _items;

    public BoundedDiagnosticQueue(int capacity = 256)
    {
        _capacity = Math.Clamp(capacity, 1, MaxCapacity);
        _items = new(_capacity);
    }

    public int Capacity => _capacity;

    public int Count => _items.Count;

    /// <summary>溢出丢弃的事件总数（仅诊断用，不写入日志内容）。</summary>
    public long DroppedCount { get; private set; }

    /// <summary>追加事件；已满时先丢弃最旧事件再入队。不抛异常，容量恒定。</summary>
    public bool TryAdd(DiagnosticEvent e)
    {
        if (_items.Count >= _capacity)
        {
            _items.Dequeue();
            DroppedCount++;
        }

        _items.Enqueue(e);
        return true;
    }

    public bool TryDequeue(out DiagnosticEvent? e)
    {
        if (_items.TryDequeue(out var item))
        {
            e = item;
            return true;
        }

        e = null;
        return false;
    }

    /// <summary>按序最多取 max 个事件到 buffer；返回实际取出数量。</summary>
    public int Drain(List<DiagnosticEvent> buffer, int max)
    {
        if (max <= 0)
        {
            return 0;
        }

        var n = 0;
        while (n < max && _items.TryDequeue(out var e))
        {
            buffer.Add(e!);
            n++;
        }

        return n;
    }
}
#pragma warning restore CA1711
