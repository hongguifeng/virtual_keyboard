namespace VirtualKeyboard.Core.Diagnostics;

using System.Collections.Generic;

/// <summary>
/// 有界内存诊断队列（NFR-PERF-001 / NFR-REL-001）：
/// 固定容量，满时丢弃最旧事件并计数，绝不允许无界增长。
/// 所有方法与读取（含 Count / DroppedCount）都通过单一锁串行化，
/// 多生产者与多消费者并发使用安全，丢弃/守恒语义在并发下不变。
/// </summary>
// CA1711 抑制：任务规定的类型名 BoundedDiagnosticQueue 以“Queue”结尾（NFR-PERF-001 有界队列语义），
// 且它确为有界队列的包装，不适用“实现 IQueue/IDictionary 等接口”的命名约定例外。
#pragma warning disable CA1711
public sealed class BoundedDiagnosticQueue
{
    /// <summary>容量上限（构造参数会被夹到 1..MaxCapacity）。</summary>
    public const int MaxCapacity = 4096;

    // 单一门闩：保护 _items 与 _dropped 的全部读写（含读取路径），
    // 使 Drain 的批量取出对其它生产者/消费者保持原子。
    private readonly object _gate = new();

    private readonly int _capacity;
    private readonly Queue<DiagnosticEvent> _items;
    private long _dropped;

    public BoundedDiagnosticQueue(int capacity = 256)
    {
        _capacity = Math.Clamp(capacity, 1, MaxCapacity);
        _items = new(_capacity);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>溢出丢弃的事件总数（仅诊断用，不写入日志内容）。</summary>
    public long DroppedCount
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>追加事件；已满时先丢弃最旧事件再入队。不抛异常，容量恒定。</summary>
    public bool TryAdd(DiagnosticEvent e)
    {
        lock (_gate)
        {
            if (_items.Count >= _capacity)
            {
                _items.Dequeue();
                _dropped++;
            }

            _items.Enqueue(e);
        }

        return true;
    }

    public bool TryDequeue(out DiagnosticEvent? e)
    {
        lock (_gate)
        {
            if (_items.TryDequeue(out var item))
            {
                e = item;
                return true;
            }
        }

        e = null;
        return false;
    }

    /// <summary>按序最多取 max 个事件到 buffer；返回实际取出数量（整段取出对并发操作保持原子）。</summary>
    public int Drain(List<DiagnosticEvent> buffer, int max)
    {
        if (max <= 0)
        {
            return 0;
        }

        var n = 0;
        lock (_gate)
        {
            while (n < max && _items.TryDequeue(out var e))
            {
                buffer.Add(e!);
                n++;
            }
        }

        return n;
    }
}
#pragma warning restore CA1711
