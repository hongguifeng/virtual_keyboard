using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Windows;

public sealed class KeyboardChordCapturedEventArgs(IEnumerable<WindowsKeyboardKey> keys) : EventArgs
{
    public IReadOnlyList<WindowsKeyboardKey> Keys { get; } = new ReadOnlyCollection<WindowsKeyboardKey>(keys.ToArray());
}

/// <summary>Captures and suppresses one complete global key chord, including Windows shell shortcuts.</summary>
public sealed class KeyboardChordRecorder : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;
    private readonly object _gate = new();
    private readonly HookCallback _callback;
    private readonly List<WindowsKeyboardKey> _keys = [];
    private readonly HashSet<uint> _heldVirtualKeys = [];
    private nint _hook;
    private bool _invalidCombination;
    private bool _disposed;

    public KeyboardChordRecorder() => _callback = OnKeyboardHook;

    public event EventHandler<KeyboardChordCapturedEventArgs>? Captured;

    public event EventHandler? CaptureFailed;

    public bool IsRecording { get { lock (_gate) return _hook != nint.Zero; } }

    public bool Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hook != nint.Zero) return true;
            _keys.Clear();
            _heldVirtualKeys.Clear();
            _invalidCombination = false;
            _hook = SetWindowsHookEx(LowLevelKeyboardHook, _callback, GetModuleHandle(null), 0);
            return _hook != nint.Zero;
        }
    }

    public void Stop()
    {
        nint hook;
        lock (_gate)
        {
            hook = _hook;
            _hook = nint.Zero;
            _keys.Clear();
            _heldVirtualKeys.Clear();
            _invalidCombination = false;
        }
        if (hook != nint.Zero) _ = UnhookWindowsHookEx(hook);
    }

    internal static bool TryMapVirtualKey(uint virtualKey, out WindowsKeyboardKey key)
    {
        virtualKey = virtualKey switch
        {
            0xA0 or 0xA1 => (uint)WindowsKeyboardKey.Shift,
            0xA2 or 0xA3 => (uint)WindowsKeyboardKey.Control,
            0xA4 or 0xA5 => (uint)WindowsKeyboardKey.Alt,
            _ => virtualKey,
        };
        key = (WindowsKeyboardKey)(ushort)virtualKey;
        return virtualKey <= ushort.MaxValue && Enum.IsDefined(key) && LayoutValidator.IsAllowedChordKey(key.ToString());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        GC.SuppressFinalize(this);
    }

    private nint OnKeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !IsRecording) return CallNextHookEx(nint.Zero, code, wParam, lParam);
        int message = unchecked((int)wParam);
        bool isDown = message is KeyDownMessage or SystemKeyDownMessage;
        bool isUp = message is KeyUpMessage or SystemKeyUpMessage;
        if (!isDown && !isUp) return CallNextHookEx(nint.Zero, code, wParam, lParam);

        KeyboardChordCapturedEventArgs? completed = null;
        bool failed = false;
        uint virtualKey = unchecked((uint)Marshal.ReadInt32(lParam));
        lock (_gate)
        {
            if (_hook == nint.Zero) return CallNextHookEx(nint.Zero, code, wParam, lParam);
            if (isDown && _heldVirtualKeys.Add(virtualKey))
            {
                if (!TryMapVirtualKey(virtualKey, out WindowsKeyboardKey key))
                {
                    _invalidCombination = true;
                }
                else if (!_keys.Contains(key))
                {
                    if (_keys.Count < LayoutSchemaLimits.MaximumChordKeys) _keys.Add(key);
                    else _invalidCombination = true;
                }
            }
            if (isUp) _heldVirtualKeys.Remove(virtualKey);
            if (isUp && _heldVirtualKeys.Count == 0 && (_keys.Count > 0 || _invalidCombination))
            {
                if (_invalidCombination) failed = true;
                else completed = new(_keys);
            }
        }
        if (completed is not null || failed)
        {
            Stop();
            if (failed) CaptureFailed?.Invoke(this, EventArgs.Empty);
            else Captured?.Invoke(this, completed!);
        }
        return (nint)1;
    }

    private delegate nint HookCallback(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookCallback callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
