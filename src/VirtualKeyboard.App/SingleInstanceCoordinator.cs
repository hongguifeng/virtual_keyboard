using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace VirtualKeyboard.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly bool _ownsMutex;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstanceCoordinator(string scope, Action activationRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(activationRequested);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{scope}.Activate");
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{scope}.Mutex", out bool createdNew);
        _ownsMutex = createdNew;
        IsPrimary = createdNew;
        if (createdNew)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                static (state, _) =>
                {
                    try { ((Action)state!).Invoke(); }
                    catch { /* Activation must not terminate the primary process. */ }
                },
                activationRequested,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator CreateDefault(Action activationRequested)
    {
        string identity = $"{Environment.UserDomainName}\\{Environment.UserName}:{Process.GetCurrentProcess().SessionId}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return new($"VirtualKeyboard.{hash}", activationRequested);
    }

    internal static SingleInstanceCoordinator CreateForTest(string scope, Action activationRequested) => new(scope, activationRequested);

    public void NotifyPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary) _activationEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _registration?.Unregister(null);
        _registration = null;
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activationEvent.Dispose();
        _disposed = true;
    }
}
